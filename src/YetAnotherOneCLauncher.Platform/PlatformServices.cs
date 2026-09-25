using YetAnotherOneCLauncher.Platform.Abstractions;
using YetAnotherOneCLauncher.Platform.Linux;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>Выбор реализаций под текущую ОС. Поддерживаются Windows и Linux.</summary>
public static class PlatformServices
{
    public const string AppFolderName = "YetAnotherOneCLauncher";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static IPlatformPaths CreatePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPlatformPaths();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxPlatformPaths();
        }

        throw new PlatformNotSupportedException(
            "Поддерживаются только Windows и Linux. Поддержка macOS запланирована на будущее.");
    }
}
