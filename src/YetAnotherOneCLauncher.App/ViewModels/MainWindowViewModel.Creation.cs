using System.Text;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Создание новой информационной базы (<c>1cv8 CREATEINFOBASE</c>) из формы добавления базы.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Платформы, которыми можно создать базу (есть 1cv8): сначала — версия по умолчанию из 1cestart.cfg,
    /// затем предпочитаемая разрядность, затем новые.
    /// </summary>
    private List<PlatformInstallation> CreationPlatforms()
    {
        var defaultMask = VersionMask.TryParse(_starterDefaultVersion, out var mask) ? mask : null;
        var preferred = _settings.Settings.Launch.ToLaunchOptions().PreferredArchitecture;
        return [.. _installations
            .Where(i => i.ThickClientPath is not null)
            .OrderByDescending(i => defaultMask?.Matches(i.Version) == true)
            .ThenByDescending(i => i.Version)
            .ThenByDescending(i => i.Architecture == preferred)];
    }

    /// <summary>Кэш шаблонов (<c>templates-cache.json</c> в данных лаунчера).</summary>
    internal TemplateCache? TemplateCache =>
        field ??= _paths is null ? null : new TemplateCache(Path.Combine(_paths.AppDataDirectory, TemplateCache.FileName));

    /// <summary>Каталоги шаблонов: по умолчанию и <c>ConfigurationTemplatesLocation</c> из 1cestart.cfg.</summary>
    private List<string> TemplateRoots() =>
        _paths is null ? [] : [_paths.DefaultTemplatesDirectory, .. _catalog?.StarterConfig.TemplateLocations ?? []];

    /// <summary>
    /// Шаблоны для формы — из кэша, без просмотра каталогов (их просматривает только «Обновить» на шаге выбора шаблона).
    /// <c>null</c> — список ещё не обновляли.
    /// </summary>
    private async Task<TemplateCacheData?> LoadCachedTemplatesAsync() =>
        TemplateCache is { } cache ? await cache.LoadAsync() : null;

    /// <summary>«Обновить» на шаге выбора шаблона: просмотр каталогов шаблонов в фоне и запись в кэш.</summary>
    private async Task<TemplateCacheData> RefreshTemplatesAsync()
    {
        var roots = TemplateRoots();
        var found = await Task.Run(() => TemplateScanner.Scan(roots));
        var data = TemplateCacheData.From(found, roots, DateTimeOffset.Now);
        if (TemplateCache is { } cache)
        {
            try
            {
                await cache.SaveAsync(data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Кэш не записался — список всё равно показан; в следующий раз его снова нужно будет обновить.
                LogTemplateCacheFailed(_logger, cache.FilePath, ex);
            }
        }

        LogTemplatesRefreshed(_logger, found.Count, roots);
        return data;
    }

    /// <summary>
    /// Создаёт базу и ждёт платформу. Результат платформа пишет в файл (<c>/Out</c>): при ошибке его текст
    /// возвращается в форму. <c>null</c> — база создана.
    /// </summary>
    private async Task<string?> CreateInfoBaseAsync(InfoBaseCreation creation, PlatformInstallation platform)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"yaocl-create-{Guid.NewGuid():N}.txt");
        var command = creation.BuildCommand(platform, outFile);
        var display = creation.BuildCommand(platform, outFile, maskPasswords: true).ToDisplayString();
        LogCreating(_logger, display);
        StatusText = "Создание информационной базы…";
        try
        {
            var exitCode = await _processLauncher.RunAsync(command);
            var output = ReadOutput(outFile);
            if (exitCode == 0)
            {
                LogCreated(_logger, output);
                return null;
            }

            LogCreateFailed(_logger, exitCode, output);
            StatusText = "Информационная база не создана.";
            return "Платформа не создала информационную базу"
                   + (output.Length > 0 ? ":" + Environment.NewLine + output : $" (код завершения {exitCode}).");
        }
        catch (LaunchFailedException ex)
        {
            StatusText = "Информационная база не создана.";
            return ex.Message;
        }
        finally
        {
            try
            {
                File.Delete(outFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Временный файл останется — не страшно.
            }
        }
    }

    /// <summary>Текст из файла /Out (UTF-8 с BOM или без); нет файла — пусто.</summary>
    private static string ReadOutput(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Создание информационной базы: {Command}")]
    private static partial void LogCreating(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Информационная база создана: {Output}")]
    private static partial void LogCreated(ILogger logger, string output);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Информационная база не создана (код {ExitCode}): {Output}")]
    private static partial void LogCreateFailed(ILogger logger, int exitCode, string output);

    [LoggerMessage(Level = LogLevel.Information, Message = "Список шаблонов обновлён: найдено {Count} в {Roots}")]
    private static partial void LogTemplatesRefreshed(ILogger logger, int count, IReadOnlyList<string> roots);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось записать кэш шаблонов {Path}")]
    private static partial void LogTemplateCacheFailed(ILogger logger, string path, Exception exception);
}
