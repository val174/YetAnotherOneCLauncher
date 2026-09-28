using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Linux;

/// <summary>В Linux открытые файлы переименованию не мешают, поэтому смотрим дескрипторы процессов в <c>/proc</c>.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCacheUsageProbe : ICacheUsageProbe
{
    public IReadOnlyList<string> RunningPlatformProcesses() => PlatformProcesses.List();

    public bool IsDirectoryInUse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + "/";
        foreach (var process in SafeEnumerate("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(process), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            foreach (var descriptor in SafeEnumerate(Path.Combine(process, "fd")))
            {
                string? target;
                try
                {
                    target = new FileInfo(descriptor).LinkTarget;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (target is not null && target.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] SafeEnumerate(string directory)
    {
        try
        {
            return Directory.GetFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // чужие процессы и завершившиеся между шагами
        }
    }
}
