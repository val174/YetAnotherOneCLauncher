namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Имена исполняемых файлов платформы на конкретной ОС.</summary>
/// <param name="ThickClientFileName"><c>1cv8.exe</c> в Windows, <c>1cv8</c> в Linux.</param>
/// <param name="ThinClientFileName"><c>1cv8c.exe</c> в Windows, <c>1cv8c</c> в Linux.</param>
public sealed record PlatformExecutableNames(string ThickClientFileName, string ThinClientFileName)
{
    public static PlatformExecutableNames Windows { get; } = new("1cv8.exe", "1cv8c.exe");

    public static PlatformExecutableNames Linux { get; } = new("1cv8", "1cv8c");
}

/// <summary>Результат поиска платформ.</summary>
/// <param name="Installations">Найденные платформы: сначала новые, при равной версии — в порядке корней.</param>
/// <param name="Warnings">Каталоги, которые не удалось просмотреть.</param>
public sealed record PlatformScanResult(IReadOnlyList<PlatformInstallation> Installations, IReadOnlyList<string> Warnings);

/// <summary>
/// Ищет установленные платформы в корневых каталогах.
/// </summary>
/// <remarks>
/// Платформой считается каталог с именем-версией (8.3.24.1548), в котором лежит <c>1cv8</c> или <c>1cv8c</c> —
/// в подкаталоге <c>bin</c> (Windows) или прямо в нём (Linux). Такой каталог ищется не глубже двух уровней от корня,
/// поэтому подходят все известные раскладки:
/// <list type="bullet">
/// <item><c>C:\Program Files\1cv8\8.3.24.1548\bin\1cv8.exe</c> — корень <c>C:\Program Files\1cv8</c>;</item>
/// <item><c>/opt/1cv8/x86_64/8.3.24.1548/1cv8</c> — корень <c>/opt/1cv8</c>;</item>
/// <item>корень, который сам является каталогом версии.</item>
/// </list>
/// </remarks>
public static class PlatformScanner
{
    private const int MaxDepth = 2;

    public static PlatformScanResult Scan(IEnumerable<string> roots, PlatformExecutableNames names)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(names);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenRoots = new HashSet<string>(comparer);
        var seenBins = new HashSet<string>(comparer);
        var found = new List<PlatformInstallation>();
        var warnings = new List<string>();

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim().Trim('"')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings.Add($"Некорректный путь к платформам «{root}»: {ex.Message}");
                continue;
            }

            if (!seenRoots.Add(fullRoot) || !Directory.Exists(fullRoot))
            {
                continue;
            }

            Visit(fullRoot, depth: 0, names, seenBins, found, warnings);
        }

        // Стабильная сортировка: при равных версиях сохраняется порядок корней.
        var ordered = found
            .Select((installation, index) => (installation, index))
            .OrderByDescending(x => x.installation.Version)
            .ThenBy(x => x.index)
            .Select(x => x.installation)
            .ToList();

        return new PlatformScanResult(ordered, warnings);
    }

    private static void Visit(
        string directory,
        int depth,
        PlatformExecutableNames names,
        HashSet<string> seenBins,
        List<PlatformInstallation> found,
        List<string> warnings)
    {
        if (PlatformVersion.TryParse(Path.GetFileName(directory), out var version))
        {
            var installation = TryCreate(version, directory, names);
            if (installation is not null && seenBins.Add(installation.BinDirectory))
            {
                found.Add(installation);
            }

            // Внутри каталога версии других платформ не бывает.
            return;
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add($"Не удалось просмотреть «{directory}»: {ex.Message}");
            return;
        }

        foreach (var child in children)
        {
            Visit(child, depth + 1, names, seenBins, found, warnings);
        }
    }

    private static PlatformInstallation? TryCreate(PlatformVersion version, string versionDirectory, PlatformExecutableNames names)
    {
        foreach (var bin in new[] { Path.Combine(versionDirectory, "bin"), versionDirectory })
        {
            var thick = ExistingFile(bin, names.ThickClientFileName);
            var thin = ExistingFile(bin, names.ThinClientFileName);
            if (thick is null && thin is null)
            {
                continue;
            }

            var architecture = ExecutableHeader.ReadArchitecture(thick ?? thin!);
            return new PlatformInstallation(version, architecture, bin, thick, thin);
        }

        return null;
    }

    private static string? ExistingFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }
}
