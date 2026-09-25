using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>
/// Загружает все списки баз: личный ibases.v8i и общие списки из <c>CommonInfoBases</c>
/// (из всех 1cestart.cfg), объединяет их и убирает дубликаты.
/// </summary>
/// <remarks>
/// Правила:
/// <list type="bullet">
/// <item>Отсутствующий файл — не ошибка. Отсутствующий личный список считается пустым.</item>
/// <item>Недоступный общий список (сеть, права, таймаут) попадает в предупреждения, загрузка продолжается.</item>
/// <item>Дубликаты определяются по <see cref="InfoBase.IdentityKey"/> (ID или нормализованная строка подключения);
/// побеждает первая запись в порядке: личный список, затем общие в порядке следования в cfg.</item>
/// <item>Веб-сервис списков (<c>InternetService</c>) пока не читается — выдаётся информационное сообщение.</item>
/// </list>
/// </remarks>
public sealed partial class InfoBaseCatalogLoader
{
    private readonly CatalogLoadOptions _options;
    private readonly ILogger _logger;

    public InfoBaseCatalogLoader(CatalogLoadOptions? options = null, ILogger<InfoBaseCatalogLoader>? logger = null)
    {
        _options = options ?? new CatalogLoadOptions();
        _logger = logger ?? NullLogger<InfoBaseCatalogLoader>.Instance;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async Task<InfoBaseCatalog> LoadAsync(CatalogSources sources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var warnings = new List<CatalogWarning>();
        var startedAt = Stopwatch.GetTimestamp();

        // 1. Настройки стартера.
        var configs = new List<StarterConfig>();
        foreach (var cfgPath in sources.StarterConfigPaths)
        {
            var result = await ReadFileAsync(cfgPath, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case ReadStatus.Ok:
                    configs.Add(StarterConfig.Parse(result.Text!.Text, cfgPath));
                    break;
                case ReadStatus.Failed:
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Warning,
                        $"Не удалось прочитать настройки стартера: {result.Error}",
                        cfgPath));
                    break;
            }
        }

        var starterConfig = StarterConfig.Merge(configs);

        // 2. Перечень списков.
        var listSources = new List<ListSource> { new(ListSourceKind.Personal, sources.PersonalListPath) };
        var seenPaths = new HashSet<string>(PathComparer) { NormalizeFileSystemPath(sources.PersonalListPath) };
        foreach (var common in starterConfig.CommonInfoBases)
        {
            var expanded = ExpandPath(common);
            if (expanded.Length > 0 && seenPaths.Add(NormalizeFileSystemPath(expanded)))
            {
                listSources.Add(new ListSource(ListSourceKind.Common, expanded));
            }
        }

        foreach (var url in starterConfig.InternetServices)
        {
            warnings.Add(new CatalogWarning(
                CatalogWarningLevel.Info,
                "Получение списка баз с веб-сервиса пока не поддерживается.",
                url));
        }

        // 3. Чтение списков параллельно (сетевые пути не ждут друг друга).
        var lists = await Task.WhenAll(listSources.Select(s => LoadListAsync(s, cancellationToken)))
            .ConfigureAwait(false);

        foreach (var list in lists.Where(l => l.Error is not null))
        {
            warnings.Add(new CatalogWarning(
                CatalogWarningLevel.Warning,
                $"Список баз недоступен: {list.Error}",
                list.Source.Location));
        }

        // 4. Базы и папки без дубликатов.
        var infoBases = new List<InfoBase>();
        var folders = new List<InfoBaseFolder>();
        var seenBases = new Dictionary<string, InfoBase>(StringComparer.Ordinal);
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in lists)
        {
            if (list.Document is null)
            {
                continue;
            }

            foreach (var section in list.Document.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Name))
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Warning,
                        "Секция без названия пропущена.",
                        list.Source.Location));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(section.Get(V8iKeys.Connect)))
                {
                    var folder = new InfoBaseFolder(section, list.Source);
                    if (seenFolders.Add(folder.FullPath))
                    {
                        folders.Add(folder);
                    }

                    continue;
                }

                var infoBase = new InfoBase(section, list.Source);
                if (infoBase.Connection.HasErrors)
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Warning,
                        $"«{infoBase.Name}»: {string.Join(" ", infoBase.Connection.Errors)}",
                        list.Source.Location));
                }

                if (infoBase.ConnectionKind == ConnectionKind.Unknown)
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Info,
                        $"«{infoBase.Name}»: тип подключения не распознан ({infoBase.Connection}).",
                        list.Source.Location));
                }

                if (seenBases.TryGetValue(infoBase.IdentityKey, out var existing))
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Info,
                        $"Дубликат «{infoBase.Name}» пропущен: база уже есть в списке «{existing.Source.Location}».",
                        list.Source.Location));
                    continue;
                }

                seenBases.Add(infoBase.IdentityKey, infoBase);
                infoBases.Add(infoBase);
            }
        }

        foreach (var warning in warnings)
        {
            var level = ToLogLevel(warning.Level);
            LogCatalogWarning(_logger, level, warning.Message, warning.Location);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var availableLists = lists.Count(l => l.IsAvailable);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogCatalogLoaded(_logger, infoBases.Count, folders.Count, availableLists, lists.Length, elapsedMs);
        }

        return new InfoBaseCatalog(lists, infoBases, folders, starterConfig, warnings);
    }

    private async Task<LoadedList> LoadListAsync(ListSource source, CancellationToken cancellationToken)
    {
        var result = await ReadFileAsync(source.Location, cancellationToken).ConfigureAwait(false);
        LogListRead(_logger, source.Kind, source.Location, result.Status);
        return result.Status switch
        {
            ReadStatus.Ok => new LoadedList(
                source,
                V8iDocument.Parse(result.Text!.Text, result.Text!.Format),
                null),

            // Личного списка ещё нет — это нормально, он будет создан при первом добавлении базы.
            ReadStatus.Missing when source.Kind == ListSourceKind.Personal =>
                new LoadedList(source, new V8iDocument(), null),

            ReadStatus.Missing => new LoadedList(source, null, "файл не найден"),
            _ => new LoadedList(source, null, result.Error),
        };
    }

    private async Task<ReadResult> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // File.Exists на недоступном сетевом пути может висеть долго, поэтому всё — в фоне и с таймаутом.
            var work = Task.Run(
                async () =>
                {
                    if (!File.Exists(path))
                    {
                        return new ReadResult(ReadStatus.Missing, null, null);
                    }

                    var decoded = await TextFileCodec.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
                    return new ReadResult(ReadStatus.Ok, decoded, null);
                },
                cancellationToken);

            return await work.WaitAsync(_options.FileTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ReadResult(
                ReadStatus.Failed,
                null,
                $"превышено время ожидания ({_options.FileTimeout.TotalSeconds:0} с)");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException or NotSupportedException
                                       or ArgumentException)
        {
            return new ReadResult(ReadStatus.Failed, null, ex.Message);
        }
    }

    private static string ExpandPath(string path) =>
        Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    private static string NormalizeFileSystemPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static LogLevel ToLogLevel(CatalogWarningLevel level) => level switch
    {
        CatalogWarningLevel.Error => LogLevel.Error,
        CatalogWarningLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    [LoggerMessage(Level = LogLevel.Debug, Message = "Список {Kind} {Location}: {Status}")]
    private static partial void LogListRead(ILogger logger, ListSourceKind kind, string location, ReadStatus status);

    [LoggerMessage(Message = "Каталог: {Message} ({Location})")]
    private static partial void LogCatalogWarning(ILogger logger, LogLevel level, string message, string? location);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Каталог загружен: баз {InfoBaseCount}, папок {FolderCount}, списков {AvailableLists} из {TotalLists}, {ElapsedMs} мс")]
    private static partial void LogCatalogLoaded(
        ILogger logger,
        int infoBaseCount,
        int folderCount,
        int availableLists,
        int totalLists,
        long elapsedMs);

    private enum ReadStatus
    {
        Ok,
        Missing,
        Failed,
    }

    private sealed record ReadResult(ReadStatus Status, DecodedText? Text, string? Error);
}
