using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Updates;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Итог установки обновления.</summary>
public abstract record UpdateInstallResult
{
    private UpdateInstallResult()
    {
    }

    /// <summary>Файл программы заменён; новая версия запустится после перезапуска.</summary>
    public sealed record Installed(ReleaseVersion Version) : UpdateInstallResult;

    public sealed record Failed(string Reason) : UpdateInstallResult;
}

/// <summary>Проверка и установка обновлений лаунчера из релизов GitHub.</summary>
public interface IUpdateService
{
    /// <summary>Установленная версия.</summary>
    ReleaseVersion CurrentVersion { get; }

    /// <summary>Можно ли заменить файл программы (опубликованный один файл, есть права на запись).</summary>
    bool CanInstall { get; }

    /// <summary>Почему установить обновление нельзя — тогда предлагается скачать его вручную.</summary>
    string? CannotInstallReason { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Скачивает файл новой версии, сверяет размер и SHA-256 и заменяет им файл программы.</summary>
    Task<UpdateInstallResult> InstallAsync(UpdateCheckResult.Available update, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Запускает новую версию; она дождётся выхода этого процесса. Лаунчер после вызова закрывается.</summary>
    bool StartUpdatedVersion();
}

/// <summary>
/// Обновление одного файла программы. Новый файл скачивается рядом с программой (<c>&lt;exe&gt;.download</c>), затем:
/// в Windows запущенный exe переименовывается в <c>&lt;exe&gt;.old</c> (удалить его нельзя, переименовать — можно),
/// а скачанный встаёт на его место; в Linux скачанный файл просто заменяет программу и получает право на выполнение.
/// Остатки (<c>.old</c>, <c>.download</c>) удаляются при следующем запуске — <see cref="CleanupLeftovers"/>.
/// </summary>
public sealed partial class UpdateService : IUpdateService
{
    /// <summary>Аргумент новой версии: дождаться выхода прежнего процесса (по PID), прежде чем открываться.</summary>
    public const string AfterUpdateArgument = "--after-update";

    private const string OldSuffix = ".old";
    private const string DownloadSuffix = ".download";

    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _releases;
    private readonly string? _executablePath;
    private readonly string? _assetName;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(HttpClient http, ILogger<UpdateService> logger)
        : this(http, logger, CurrentVersionOf(typeof(UpdateService).Assembly), PublishedExecutable(), UpdateChecker.AssetNameForCurrentOs())
    {
    }

    /// <param name="executablePath">Файл программы, который заменяется; <c>null</c> — не опубликованная сборка (отладка, тесты).</param>
    internal UpdateService(HttpClient http, ILogger<UpdateService> logger, ReleaseVersion currentVersion, string? executablePath, string? assetName)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger;
        _releases = new GitHubReleaseClient(http);
        CurrentVersion = currentVersion;
        _executablePath = executablePath;
        _assetName = assetName;
    }

    public ReleaseVersion CurrentVersion { get; }

    public bool CanInstall => CannotInstallReason is null;

    public string? CannotInstallReason =>
        _assetName is null ? "Для этой ОС сборки лаунчера не публикуются."
        : _executablePath is null ? "Лаунчер запущен не из опубликованного файла — обновление ставится только в опубликованную сборку."
        : !CanWriteNextTo(_executablePath) ? $"Нет прав на запись в каталог {Path.GetDirectoryName(_executablePath)}."
        : null;

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        _assetName is null
            ? Task.FromResult<UpdateCheckResult>(new UpdateCheckResult.Failed("Для этой ОС сборки лаунчера не публикуются."))
            : UpdateChecker.CheckAsync(_releases, CurrentVersion, _assetName, cancellationToken);

    public async Task<UpdateInstallResult> InstallAsync(
        UpdateCheckResult.Available update, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (CannotInstallReason is { } reason)
        {
            return new UpdateInstallResult.Failed(reason);
        }

        var target = _executablePath!;
        var download = target + DownloadSuffix;
        try
        {
            if (await DownloadAsync(update.Asset, download, progress, cancellationToken).ConfigureAwait(false) is { } error)
            {
                TryDelete(download);
                return new UpdateInstallResult.Failed(error);
            }

            Replace(target, download);
            LogInstalled(_logger, update.Version, target);
            return new UpdateInstallResult.Installed(update.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(download);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            LogInstallFailed(_logger, ex);
            TryDelete(download);
            return new UpdateInstallResult.Failed("Не удалось установить обновление: " + ex.Message);
        }
    }

    public bool StartUpdatedVersion()
    {
        if (_executablePath is null)
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(_executablePath) { UseShellExecute = false };
            start.ArgumentList.Add(AfterUpdateArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            LogRestartFailed(_logger, ex);
            return false;
        }
    }

    /// <summary>Удаляет остатки прошлого обновления рядом с программой. Вызывается при запуске.</summary>
    public static void CleanupLeftovers(string? executablePath = null)
    {
        var path = executablePath ?? Environment.ProcessPath;
        if (path is null)
        {
            return;
        }

        TryDelete(path + OldSuffix);
        TryDelete(path + DownloadSuffix);
    }

    /// <summary>
    /// Новая версия запущена с <c>--after-update &lt;pid&gt;</c>: ждём, пока прежний процесс закроется (не дольше
    /// <paramref name="timeout"/>), — иначе сработал бы запрет повторного запуска.
    /// </summary>
    public static void WaitForPreviousInstance(IReadOnlyList<string> args, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(args);
        var index = args.ToList().IndexOf(AfterUpdateArgument);
        if (index < 0 || index + 1 >= args.Count
            || !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            // Процесс уже закрылся.
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>Версия программы: «0.1.0+хеш» из атрибутов сборки (Directory.Build.props, тег релиза при сборке).</summary>
    internal static ReleaseVersion CurrentVersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString();
        return ReleaseVersion.TryParse(informational, out var version) ? version : ReleaseVersion.Parse("0.0.0");
    }

    /// <summary>Файл программы, если лаунчер опубликован одним файлом; иначе (dotnet …dll, тесты) — <c>null</c>.</summary>
    private static string? PublishedExecutable()
    {
        var path = Environment.ProcessPath;
        var expected = UpdateChecker.AssetNameForCurrentOs();
        // У опубликованного одним файлом приложения сборки внутри файла; рядом с exe из bin\Debug лежит YetAnotherOneCLauncher.dll.
        var singleFile = !File.Exists(Path.Combine(AppContext.BaseDirectory, typeof(UpdateService).Assembly.GetName().Name + ".dll"));
        return singleFile && path is not null && expected is not null
               && string.Equals(Path.GetFileName(path), expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? path
            : null;
    }

    /// <returns>Причина отказа или <c>null</c>, если файл скачан и совпал по размеру и хешу.</returns>
    private async Task<string?> DownloadAsync(GitHubReleaseAsset asset, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return $"Не удалось скачать обновление: {(int)response.StatusCode} {response.ReasonPhrase}.";
        }

        var total = asset.Size > 0 ? asset.Size : response.Content.Headers.ContentLength ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                received += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1, (double)received / total));
                }
            }
        }

        if (asset.Size > 0 && received != asset.Size)
        {
            return $"Файл обновления скачан не полностью: {received} из {asset.Size} байт.";
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (asset.Sha256 is { } expected && !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return "Файл обновления повреждён: контрольная сумма SHA-256 не совпала с указанной в релизе.";
        }

        return null;
    }

    private static void Replace(string target, string download)
    {
        if (OperatingSystem.IsWindows())
        {
            var old = target + OldSuffix;
            TryDelete(old);
            File.Move(target, old);
            try
            {
                File.Move(download, target);
            }
            catch
            {
                // Не встал новый файл — возвращаем прежний, программа должна запускаться.
                File.Move(old, target);
                throw;
            }
        }
        else
        {
            var mode = File.GetUnixFileMode(target);
            File.Move(download, target, overwrite: true);
            File.SetUnixFileMode(target, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    private static bool CanWriteNextTo(string path)
    {
        var probe = path + ".write-test";
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Занят или нет прав — удалим в другой раз.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Установлено обновление {Version}: {Path}")]
    private static partial void LogInstalled(ILogger logger, ReleaseVersion version, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось установить обновление")]
    private static partial void LogInstallFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось запустить обновлённую версию")]
    private static partial void LogRestartFailed(ILogger logger, Exception exception);
}
