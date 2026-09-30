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
    /// <summary>COM-класс 64-разрядной оснастки из radmin.dll (он же указан в «1CV8 Servers (x86-64).msc»); у 32-разрядной — другой.</summary>
    public const string SnapInClassId = "{A42674D4-2D97-4988-A81D-2C113CC42A95}";

    public const string AdminLibraryName = "radmin.dll";

    private const int ErrorCancelled = 1223;

    public bool IsSupported => true;

    public bool IsAvailable(PlatformInstallation platform) =>
        File.Exists(LibraryPath(platform)) && FindSnapIn(platform) is not null;

    /// <summary>
    /// Зарегистрированные консоли. У 64- и 32-разрядной оснастки разные COM-классы и разные разделы реестра,
    /// поэтому класс не угадывается: перебираются оснастки MMC каждой разрядности
    /// (<c>HKLM\SOFTWARE\Microsoft\MMC\SnapIns</c>, для 32-разрядных — WOW6432Node), и берутся те, чей
    /// компонент — <c>radmin.dll</c>.
    /// </summary>
    public IReadOnlyList<ClusterConsoleRegistration> FindRegistered() =>
    [
        .. ReadRegistrations(RegistryView.Registry64, PlatformArchitecture.X64),
        .. ReadRegistrations(RegistryView.Registry32, PlatformArchitecture.X86),
    ];

    private static List<ClusterConsoleRegistration> ReadRegistrations(RegistryView view, PlatformArchitecture architecture)
    {
        var found = new List<ClusterConsoleRegistration>();
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var snapIns = machine.OpenSubKey(@"SOFTWARE\Microsoft\MMC\SnapIns");

            // Известный класс 64-разрядной оснастки — и без записи в MMC\SnapIns.
            var classIds = (snapIns?.GetSubKeyNames() ?? []).Append(SnapInClassId).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var classId in classIds)
            {
                using var server = classes.OpenSubKey($@"CLSID\{classId}\InprocServer32");
                if (server?.GetValue(null) is string { Length: > 0 } path
                    && string.Equals(Path.GetFileName(path.Trim().Trim('"')), AdminLibraryName, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(new ClusterConsoleRegistration(path, architecture, classId));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Реестр не прочитался — считаем, что консоль этой разрядности не зарегистрирована.
        }

        return found;
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
        if (platform.Architecture == PlatformArchitecture.X86 && IsWideSnapIn(snapIn))
        {
            snapIn = NarrowCopyOf(snapIn, platform);
        }

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

    /// <summary>
    /// Файл 32-разрядной оснастки из 64-разрядного: тот же файл, но с COM-классом 32-разрядной оснастки
    /// (его берём из регистрации). Нужен, когда у 32-разрядной платформы своего «1CV8 Servers.msc» нет.
    /// </summary>
    public static string PatchSnapInClass(string mscText, string fromClassId, string toClassId) =>
        mscText.Replace(fromClassId.Trim('{', '}'), toClassId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);

    private static bool IsWideSnapIn(string path) => Path.GetFileName(path).Contains("x86-64", StringComparison.OrdinalIgnoreCase);

    private string NarrowCopyOf(string wideSnapIn, PlatformInstallation platform)
    {
        var classId = FindRegistered()
            .FirstOrDefault(r => r.Architecture == PlatformArchitecture.X86 && r.Matches(LibraryPath(platform)))?.SnapInClassId;
        if (classId is null || string.Equals(classId, SnapInClassId, StringComparison.OrdinalIgnoreCase))
        {
            return wideSnapIn; // класс 32-разрядной оснастки неизвестен или тот же — как есть
        }

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "YetAnotherOneCLauncher");
            Directory.CreateDirectory(directory);
            var copy = Path.Combine(directory, "1CV8 Servers (x86).msc");
            File.WriteAllText(copy, PatchSnapInClass(File.ReadAllText(wideSnapIn), SnapInClassId, classId));
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LaunchFailedException("Не удалось подготовить файл 32-разрядной консоли: " + ex.Message, ex);
        }
    }

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
