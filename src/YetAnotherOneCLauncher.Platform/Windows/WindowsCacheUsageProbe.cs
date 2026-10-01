using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// В Windows каталог нельзя переименовать, пока внутри открыт файл без разрешения на удаление, —
/// так и проверяется, работает ли база: каталог переименовывается и сразу возвращается обратно.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCacheUsageProbe : ICacheUsageProbe
{
    public IReadOnlyList<PlatformProcess> CurrentUserPlatformProcesses() => PlatformProcesses.CurrentUser();

    public bool IsDirectoryInUse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var probe = trimmed + ".yaocl-check-" + Environment.ProcessId;
        try
        {
            Directory.Move(trimmed, probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        try
        {
            Directory.Move(probe, trimmed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Каталог кэша остался переименованным: {probe}. Верните ему имя {Path.GetFileName(trimmed)}.", ex);
        }

        return false;
    }
}

