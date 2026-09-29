using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Консоль кластера в Windows. Оснастка — общий файл <c>&lt;каталог 1cv8&gt;\common\1CV8 Servers*.msc</c>
/// (64-разрядная — «(x86-64)» в Program Files, 32-разрядная — в Program Files (x86)); она вызывает COM-класс
/// из <c>&lt;версия&gt;\bin\radmin.dll</c>. Какая версия ответит, решает регистрация класса в реестре.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClusterConsole : IClusterConsole
{
    /// <summary>COM-класс оснастки из <c>radmin.dll</c> (он же указан в файле .msc).</summary>
    public const string SnapInClassId = "{A42674D4-2D97-4988-A81D-2C113CC42A95}";

    public const string AdminLibraryName = "radmin.dll";

    private const int ErrorCancelled = 1223;

    public bool IsSupported => true;

    public bool IsAvailable(PlatformInstallation platform) =>
        File.Exists(AdminLibraryPath(platform)) && FindSnapIn(platform) is not null;

    public bool IsRegistered(PlatformInstallation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var view = platform.Architecture == PlatformArchitecture.X86 ? RegistryView.Registry32 : RegistryView.Registry64;
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var server = root.OpenSubKey($@"CLSID\{SnapInClassId}\InprocServer32");
            return server?.GetValue(null) is string registered && SamePath(registered, AdminLibraryPath(platform));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public async Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        // 32-разрядную библиотеку регистрирует 32-разрядный regsvr32.
        var regsvr = Path.Combine(
            Environment.GetFolderPath(platform.Architecture == PlatformArchitecture.X86 ? Environment.SpecialFolder.SystemX86 : Environment.SpecialFolder.System),
            "regsvr32.exe");
        var startInfo = new ProcessStartInfo(regsvr, $"/s \"{AdminLibraryPath(platform)}\"")
        {
            UseShellExecute = true,
            Verb = "runas", // запрос прав администратора (UAC)
        };

        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new LaunchFailedException("Не удалось запустить regsvr32.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new LaunchFailedException(
                    $"regsvr32 не смог зарегистрировать {AdminLibraryPath(platform)} (код {process.ExitCode}).");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new LaunchFailedException("Регистрация отменена: без прав администратора консоль этой версии не открыть.", ex);
        }
        catch (Win32Exception ex)
        {
            throw new LaunchFailedException("Не удалось зарегистрировать компонент администрирования: " + ex.Message, ex);
        }
    }

    public void Open(PlatformInstallation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var snapIn = FindSnapIn(platform)
                     ?? throw new LaunchFailedException("Не найден файл консоли 1CV8 Servers*.msc в каталоге common платформы.");

        // Через оболочку: mmc.exe может запросить повышение прав. 32-разрядной оснастке нужен 32-разрядный MMC (-32).
        var arguments = platform.Architecture == PlatformArchitecture.X86 ? $"\"{snapIn}\" -32" : $"\"{snapIn}\"";
        var startInfo = new ProcessStartInfo("mmc.exe", arguments) { UseShellExecute = true };
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new LaunchFailedException("Открытие консоли отменено.", ex);
        }
        catch (Win32Exception ex)
        {
            throw new LaunchFailedException("Не удалось открыть консоль кластера: " + ex.Message, ex);
        }
    }

    public static string AdminLibraryPath(PlatformInstallation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return Path.Combine(platform.BinDirectory, AdminLibraryName);
    }

    /// <summary>
    /// Файл оснастки: <c>common</c> рядом с каталогами версий (<c>1cv8\8.3.25.1633\bin</c> → <c>1cv8\common</c>).
    /// У 64-разрядной — «1CV8 Servers (x86-64).msc», у 32-разрядной — «1CV8 Servers.msc».
    /// </summary>
    public static string? FindSnapIn(PlatformInstallation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var versionDirectory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(platform.BinDirectory));
        var root = versionDirectory is null ? null : Path.GetDirectoryName(versionDirectory);
        if (root is null)
        {
            return null;
        }

        var common = Path.Combine(root, "common");
        if (!Directory.Exists(common))
        {
            return null;
        }

        var candidates = Directory.GetFiles(common, "1CV8 Servers*.msc");
        var wide = candidates.FirstOrDefault(f => f.Contains("x86-64", StringComparison.OrdinalIgnoreCase));
        var narrow = candidates.FirstOrDefault(f => !f.Contains("x86-64", StringComparison.OrdinalIgnoreCase));
        return platform.Architecture == PlatformArchitecture.X86 ? narrow ?? wide : wide ?? narrow;
    }

    private static bool SamePath(string registered, string expected)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"'));
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
