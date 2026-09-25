using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>
/// Загружает все списки баз: личный ibases.v8i, общие списки из <c>CommonInfoBases</c> и списки с веб-сервисов
/// из <c>InternetService</c> (из всех 1cestart.cfg), объединяет их и убирает дубликаты.
/// </summary>
/// <remarks>
/// Правила:
/// <list type="bullet">
/// <item>Отсутствующий файл — не ошибка. Отсутствующий личный список считается пустым.</item>
/// <item>Недоступный общий список (сеть, права, таймаут) попадает в предупреждения, загрузка продолжается.</item>
/// <item>Дубликаты определяются по <see cref="InfoBase.IdentityKey"/> (ID или нормализованная строка подключения);
/// побеждает первая запись в порядке: личный список, затем общие в порядке следования в cfg, затем веб-сервисы.</item>
/// <item>Последняя удачная копия каждого общего списка и списка с веб-сервиса хранится в <see cref="ListCache"/>;
/// если источник недоступен, показываются базы из копии с пометкой даты.</item>
/// <item>С <c>progress</c> каталог выдаётся постепенно: сначала личный список и копии из кэша, затем — по мере
/// чтения каждого общего списка.</item>
/// </list>
/// </remarks>
public sealed partial class InfoBaseCatalogLoader
{
    private readonly CatalogLoadOptions _options;
    private readonly ILogger _logger;
    private readonly ListCache? _cache;
    private readonly WebInfoBaseListClient? _web;
    private readonly TimeProvider _time;

    public InfoBaseCatalogLoader(
        CatalogLoadOptions? options = null,
        ILogger<InfoBaseCatalogLoader>? logger = null,
        ListCache? cache = null,
        WebInfoBaseListClient? web = null,
        TimeProvider? time = null)
    {
        _options = options ?? new CatalogLoadOptions();
        _logger = logger ?? NullLogger<InfoBaseCatalogLoader>.Instance;
        _cache = cache;
        _web = web;
        _time = time ?? TimeProvider.System;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <param name="sources">Откуда читать.</param>
    /// <param name="progress">Промежуточные каталоги: личный список и копии, затем по мере чтения общих списков.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task<InfoBaseCatalog> LoadAsync(
        CatalogSources sources,
        IProgress<InfoBaseCatalog>? progress = null,
        CancellationToken cancellationToken = default)
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

        var seenServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in starterConfig.InternetServices)
        {
            var trimmed = url.Trim().Trim('"');
            if (trimmed.Length == 0 || !seenServices.Add(trimmed.TrimEnd('/')))
            {
                continue;
            }

            if (_web is null)
            {
                warnings.Add(new CatalogWarning(CatalogWarningLevel.Info, "Получение списка баз с веб-сервиса отключено.", trimmed));
                continue;
            }

            listSources.Add(new ListSource(ListSourceKind.InternetService, trimmed));
        }

        // 3. Сразу — личный список и сохранённые копии остальных, пока настоящие читаются.
        var personalTask = LoadListAsync(listSources[0], null, cancellationToken);
        var cachedTasks = listSources.Skip(1).Select(s => LoadCachedAsync(s, cancellationToken)).ToList();
        var lists = new LoadedList[listSources.Count];
        lists[0] = await personalTask.ConfigureAwait(false);
        var cached = await Task.WhenAll(cachedTasks).ConfigureAwait(false);
        for (var i = 1; i < lists.Length; i++)
        {
            lists[i] = cached[i - 1] is { } copy
                ? new LoadedList(listSources[i] with { CachedAt = copy.Info.SavedAt }, copy.Document, null) { IsPending = true }
                : new LoadedList(listSources[i], null, null) { IsPending = true };
        }

        var gate = new Lock();
        void Report()
        {
            if (progress is null)
            {
                return;
            }

            LoadedList[] snapshot;
            lock (gate)
            {
                snapshot = [.. lists];
            }

            progress.Report(InfoBaseCatalog.Build(snapshot, starterConfig, warnings));
        }

        if (lists.Length > 1)
        {
            Report();
        }

        // 4. Чтение общих списков и веб-сервисов параллельно (сетевые пути не ждут друг друга).
        await Task.WhenAll(Enumerable.Range(1, lists.Length - 1).Select(async i =>
        {
            var loaded = await LoadListAsync(listSources[i], cached[i - 1], cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                lists[i] = loaded;
            }

            Report();
        })).ConfigureAwait(false);

        foreach (var list in lists.Where(l => l.Error is not null))
        {
            warnings.Add(new CatalogWarning(
                CatalogWarningLevel.Warning,
                list.Source.CachedAt is { } savedAt
                    ? string.Create(
                        CultureInfo.CurrentCulture,
                        $"Список баз недоступен: {list.Error}. Показана сохранённая копия на {savedAt.ToLocalTime():dd.MM.yyyy HH:mm}.")
                    : $"Список баз недоступен: {list.Error}",
                list.Source.Location));
        }

        // 5. Базы и папки без дубликатов.
        var catalog = InfoBaseCatalog.Build(lists, starterConfig, warnings);

        foreach (var warning in catalog.Warnings)
        {
            var level = ToLogLevel(warning.Level);
            LogCatalogWarning(_logger, level, warning.Message, warning.Location);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var availableLists = lists.Count(l => l.IsAvailable);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogCatalogLoaded(_logger, catalog.InfoBases.Count, catalog.Folders.Count, availableLists, lists.Length, elapsedMs);
        }

        return catalog;
    }

    private async Task<CachedList?> LoadCachedAsync(ListSource source, CancellationToken cancellationToken) =>
        _cache is null ? null : await _cache.LoadAsync(source, cancellationToken).ConfigureAwait(false);

    /// <param name="source">Источник.</param>
    /// <param name="cached">Сохранённая копия: подставляется, если источник недоступен.</param>
    /// <param name="cancellationToken">Отмена.</param>
    private async Task<LoadedList> LoadListAsync(ListSource source, CachedList? cached, CancellationToken cancellationToken)
    {
        if (source.Kind == ListSourceKind.InternetService)
        {
            return await LoadWebListAsync(source, cached, cancellationToken).ConfigureAwait(false);
        }

        var result = await ReadFileAsync(source.Location, cancellationToken).ConfigureAwait(false);
        LogListRead(_logger, source.Kind, source.Location, result.Status);
        switch (result.Status)
        {
            case ReadStatus.Ok:
                if (source.Kind != ListSourceKind.Personal && _cache is not null)
                {
                    await _cache.SaveAsync(source, result.Text!.Text, new CachedListInfo { SavedAt = _time.GetUtcNow() }, cancellationToken)
                        .ConfigureAwait(false);
                }

                return new LoadedList(source, V8iDocument.Parse(result.Text!.Text, result.Text!.Format), null);

            // Личного списка ещё нет — это нормально, он будет создан при первом добавлении базы.
            case ReadStatus.Missing when source.Kind == ListSourceKind.Personal:
                return new LoadedList(source, new V8iDocument(), null);

            case ReadStatus.Missing:
                return FromCache(source, cached, "файл не найден");

            default:
                return FromCache(source, cached, result.Error ?? "ошибка чтения");
        }
    }

    private async Task<LoadedList> LoadWebListAsync(ListSource source, CachedList? cached, CancellationToken cancellationToken)
    {
        var state = cached?.Info is { ClientId: { } clientId, CheckCode: { } checkCode } ? new WebListState(clientId, checkCode) : null;
        WebListResult result;
        try
        {
            result = await _web!.FetchAsync(source.Location, state, cached is not null, cancellationToken)
                .WaitAsync(_options.WebServiceTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            result = new WebListResult.Failed(
                string.Create(CultureInfo.InvariantCulture, $"веб-сервис не ответил за {_options.WebServiceTimeout.TotalSeconds:0} с"));
        }

        LogWebList(_logger, source.Location, result.GetType().Name);
        switch (result)
        {
            // Сервис подтвердил, что копия актуальна, — это не «данные из кэша», а текущий список.
            case WebListResult.NotChanged when cached is not null:
                return new LoadedList(source, cached.Document, null);

            case WebListResult.Changed changed:
                if (_cache is not null)
                {
                    var info = new CachedListInfo { SavedAt = _time.GetUtcNow(), ClientId = changed.State.ClientId, CheckCode = changed.State.CheckCode };
                    await _cache.SaveAsync(source, changed.V8iText, info, cancellationToken).ConfigureAwait(false);
                }

                return new LoadedList(source, V8iDocument.Parse(changed.V8iText), null);

            case WebListResult.Failed failed:
                return FromCache(source, cached, failed.Message);

            default:
                return FromCache(source, cached, "веб-сервис не вернул список");
        }
    }

    private static LoadedList FromCache(ListSource source, CachedList? cached, string error) =>
        cached is null
            ? new LoadedList(source, null, error)
            : new LoadedList(source with { CachedAt = cached.Info.SavedAt }, cached.Document, error);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Веб-сервис списков {Url}: {Result}")]
    private static partial void LogWebList(ILogger logger, string url, string result);

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
