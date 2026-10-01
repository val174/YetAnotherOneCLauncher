namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>
/// Где лежит стандартный стартер 1С (1cestart): в подкаталоге <c>common</c> каталога установки платформ
/// (<c>C:\Program Files\1cv8\common\1cestart.exe</c>, <c>/opt/1cv8/common/1cestart</c>).
/// </summary>
public static class StarterLocator
{
    /// <summary>Каталоги, в которых ищется стартер, в порядке проверки.</summary>
    /// <param name="installRoots">
    /// Каталоги установки: сначала из 1cestart.cfg (InstalledLocation), затем стандартные для ОС.
    /// Если указан каталог версии или <c>bin</c>, берётся каталог установки над ним.
    /// </param>
    public static IReadOnlyList<string> Candidates(IEnumerable<string> installRoots, PlatformExecutableNames names)
    {
        ArgumentNullException.ThrowIfNull(installRoots);
        ArgumentNullException.ThrowIfNull(names);

        var result = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in installRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            // Каталог установки — «…\1cv8»; из «…\1cv8\8.3.27.1936» и «…\1cv8\8.3.27.1936\bin» поднимаемся к нему.
            var directory = Path.TrimEndingDirectorySeparator(root.Trim());
            while (Path.GetFileName(directory) is { Length: > 0 } name
                   && (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || Version.TryParse(name, out _))
                   && Path.GetDirectoryName(directory) is { Length: > 0 } parent)
            {
                directory = parent;
            }

            var candidate = string.Equals(Path.GetFileName(directory), "common", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(directory, names.StarterFileName)
                : Path.Combine(directory, "common", names.StarterFileName);
            if (seen.Add(candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>Все существующие стартеры в порядке каталогов (в Windows — 64- и 32-разрядный каталог установки).</summary>
    public static IReadOnlyList<string> FindAll(IEnumerable<string> installRoots, PlatformExecutableNames names, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return [.. Candidates(installRoots, names).Where(fileExists)];
    }
}
