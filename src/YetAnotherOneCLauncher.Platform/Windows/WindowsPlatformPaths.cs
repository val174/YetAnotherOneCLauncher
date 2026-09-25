using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Core.Cache;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Пути 1С в Windows:
/// <list type="bullet">
/// <item>%APPDATA%\1C\1CEStart\ibases.v8i и 1cestart.cfg — пользовательские;</item>
/// <item>%ALLUSERSPROFILE%\1C\1CEStart\1cestart.cfg — общий для компьютера;</item>
/// <item>%APPDATA%\1C\1cv8 и %LOCALAPPDATA%\1C\1cv8 — кэш баз;</item>
/// <item>Program Files (x86 и x64)\1cv8 — установленные платформы.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformPaths : IPlatformPaths
{
    public WindowsPlatformPaths()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        PersonalInfoBaseListPath = Path.Combine(roaming, "1C", "1CEStart", "ibases.v8i");
        StarterConfigPaths =
        [
            Path.Combine(programData, "1C", "1CEStart", "1cestart.cfg"),
            Path.Combine(roaming, "1C", "1CEStart", "1cestart.cfg"),
        ];

        DefaultPlatformInstallRoots = new[] { programFiles, programFilesX86 }
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => Path.Combine(p, "1cv8"))
            .ToList();

        InfoBaseCacheRoots =
        [
            new CacheRoot(Path.Combine(local, "1C", "1cv8"), CacheLocation.Local),
            new CacheRoot(Path.Combine(roaming, "1C", "1cv8"), CacheLocation.Roaming),
        ];

        AppDataDirectory = Path.Combine(roaming, PlatformServices.AppFolderName);
    }

    public string PersonalInfoBaseListPath { get; }

    public IReadOnlyList<string> StarterConfigPaths { get; }

    public IReadOnlyList<string> DefaultPlatformInstallRoots { get; }

    public PlatformExecutableNames PlatformExecutableNames => PlatformExecutableNames.Windows;

    public IReadOnlyList<CacheRoot> InfoBaseCacheRoots { get; }

    public string AppDataDirectory { get; }
}
