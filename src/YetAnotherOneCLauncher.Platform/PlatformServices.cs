using YetAnotherOneCLauncher.Platform.Abstractions;
using YetAnotherOneCLauncher.Platform.Linux;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>Выбор реализаций под текущую ОС. Поддерживаются Windows и Linux.</summary>
public static class PlatformServices
{
    public const string AppFolderName = "YetAnotherOneCLauncher";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Список переходов у значка на панели задач (только Windows).</summary>
    public static IJumpList CreateJumpList() =>
        OperatingSystem.IsWindows() ? new WindowsJumpList() : new NoJumpList();

    /// <summary>Консоль кластера серверов 1С (только Windows).</summary>
    public static IClusterConsole CreateClusterConsole() =>
        OperatingSystem.IsWindows() ? new WindowsClusterConsole() : new NoClusterConsole();

    /// <summary>Значки файлов (только Windows; на Linux у программ — значок по умолчанию).</summary>
    public static IFileIconReader CreateFileIconReader() =>
        OperatingSystem.IsWindows() ? new WindowsFileIconReader() : new NoFileIconReader();

    public static ICacheUsageProbe CreateCacheUsageProbe() =>
        OperatingSystem.IsWindows() ? new WindowsCacheUsageProbe()
        : OperatingSystem.IsLinux() ? new LinuxCacheUsageProbe()
        : throw new PlatformNotSupportedException("Проверка кэша есть только для Windows и Linux.");

    /// <summary>Хранилище паролей ОС.</summary>
    public static ICredentialStore CreateCredentialStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new SecretToolCredentialStore();
        }

        throw new PlatformNotSupportedException("Хранилище паролей есть только для Windows и Linux.");
    }

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
