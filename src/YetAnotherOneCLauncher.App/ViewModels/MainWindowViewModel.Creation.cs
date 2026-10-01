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

    /// <summary>Шаблоны из каталога шаблонов по умолчанию и из <c>ConfigurationTemplatesLocation</c> в 1cestart.cfg.</summary>
    private async Task<IReadOnlyList<ConfigurationTemplate>> FindTemplatesAsync()
    {
        if (_paths is null)
        {
            return [];
        }

        var roots = new List<string> { _paths.DefaultTemplatesDirectory };
        roots.AddRange(_catalog?.StarterConfig.TemplateLocations ?? []);
        return await Task.Run(() => TemplateScanner.Scan(roots));
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
}
