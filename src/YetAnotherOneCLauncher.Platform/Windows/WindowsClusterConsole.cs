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
        File.Exists(LibraryPath(platform)) && FindSnapIn(platform) is not null;

    /// <summary>
    /// Зарегистрированные компоненты: 64-разрядный раздел реестра и 32-разрядный (WOW6432Node) читаются оба —
    /// в каждом может быть своя консоль от своей платформы.
    /// </summary>
    public IReadOnlyList<ClusterConsoleRegistration> FindRegistered() =>
    [
        .. new[]
        {
            ReadRegistration(RegistryView.Registry64, PlatformArchitecture.X64),
            ReadRegistration(RegistryView.Registry32, PlatformArchitecture.X86),
        }.OfType<ClusterConsoleRegistration>(),
    ];

    private static ClusterConsoleRegistration? ReadRegistration(RegistryView view, PlatformArchitecture architecture)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var server = root.OpenSubKey($@"CLSID\{SnapInClassId}\InprocServer32");
            return server?.GetValue(null) is string { Length: > 0 } path ? new ClusterConsoleRegistration(path, architecture) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public async Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        // 32-разрядную библиотеку регистрирует 32-разрядный regsvr32.
        var regsvr = Path.Combine(
            Environment.GetFolderPath(platform.Architecture == PlatformArchitecture.X86 ? Environment.SpecialFolder.SystemX86 : Environment.SpecialFolder.System),
            "regsvr32.exe");
        var startInfo = new ProcessStartInfo(regsvr, $"/s \"{LibraryPath(platform)}\"")
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
                    $"regsvr32 не смог зарегистрировать {LibraryPath(platform)} (код {process.ExitCode}).");
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

    public string AdminLibraryPath(PlatformInstallation platform) => LibraryPath(platform);

    public static string LibraryPath(PlatformInstallation platform)
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

        // Сначала — common своей установки; если там файла нет (32-разрядная платформа ставится без него,
        // когда консоль уже есть у 64-разрядной), — общие каталоги 1cv8 в Program Files. Файл оснастки ссылается
        // только на COM-класс, а какую radmin.dll он загрузит, решают регистрация и разрядность MMC.
        string?[] directories =
        [
            root is null ? null : Path.Combine(root, "common"),
            CommonDirectory(Environment.SpecialFolder.ProgramFilesX86),
            CommonDirectory(Environment.SpecialFolder.ProgramFiles),
        ];
        var candidates = directories
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "1CV8 Servers*.msc"))
            .ToList();
        var wide = candidates.FirstOrDefault(f => f.Contains("x86-64", StringComparison.OrdinalIgnoreCase));
        var narrow = candidates.FirstOrDefault(f => !f.Contains("x86-64", StringComparison.OrdinalIgnoreCase));
        return platform.Architecture == PlatformArchitecture.X86 ? narrow ?? wide : wide ?? narrow;

        static string? CommonDirectory(Environment.SpecialFolder folder) =>
            Environment.GetFolderPath(folder) is { Length: > 0 } programFiles ? Path.Combine(programFiles, "1cv8", "common") : null;
    }
}
