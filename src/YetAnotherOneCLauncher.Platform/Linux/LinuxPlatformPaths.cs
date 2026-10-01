using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Core.Cache;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Linux;

/// <summary>
/// Пути 1С в Linux:
/// <list type="bullet">
/// <item>~/.1C/1cestart/ibases.v8i и 1cestart.cfg;</item>
/// <item>~/.1cv8/1C/1cv8 — кэш баз;</item>
/// <item>/opt/1cv8/&lt;arch&gt;/&lt;версия&gt; и старый вариант /opt/1C/v8.3/&lt;arch&gt; — платформы.</item>
/// </list>
/// Пути нужно сверить на реальных установках разных версий платформы.
/// Старую раскладку /opt/1C/v8.3/&lt;arch&gt; поиск пока не находит: в пути нет каталога с версией,
/// и нужно выяснить, откуда её брать.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPlatformPaths : IPlatformPaths
{
    public LinuxPlatformPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var starterDir = Path.Combine(home, ".1C", "1cestart");

        PersonalInfoBaseListPath = Path.Combine(starterDir, "ibases.v8i");
        StarterConfigPaths = [Path.Combine(starterDir, "1cestart.cfg")];
        DefaultPlatformInstallRoots = ["/opt/1cv8", "/opt/1C/v8.3"];
        // В Linux кэш и локальные настройки базы лежат в одном каталоге.
        InfoBaseCacheRoots = [new CacheRoot(Path.Combine(home, ".1cv8", "1C", "1cv8"), CacheLocation.Local)];
        DefaultTemplatesDirectory = Path.Combine(home, ".1cv8", "1C", "1cv8", "tmplts");

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            configHome = Path.Combine(home, ".config");
        }

        AppDataDirectory = Path.Combine(configHome, PlatformServices.AppFolderName);
    }

    public string PersonalInfoBaseListPath { get; }

    public IReadOnlyList<string> StarterConfigPaths { get; }

    public IReadOnlyList<string> DefaultPlatformInstallRoots { get; }

    public PlatformExecutableNames PlatformExecutableNames => PlatformExecutableNames.Linux;

    public IReadOnlyList<CacheRoot> InfoBaseCacheRoots { get; }

    public string DefaultTemplatesDirectory { get; }

    public string AppDataDirectory { get; }
}
