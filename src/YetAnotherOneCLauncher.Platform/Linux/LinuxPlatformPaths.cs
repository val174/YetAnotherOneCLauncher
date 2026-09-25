using System.Runtime.Versioning;
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
        InfoBaseCacheRoots = [Path.Combine(home, ".1cv8", "1C", "1cv8")];

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

    public IReadOnlyList<string> InfoBaseCacheRoots { get; }

    public string AppDataDirectory { get; }
}
