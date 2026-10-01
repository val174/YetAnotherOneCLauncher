namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Шаблон информационной базы из каталога шаблонов 1С.</summary>
/// <param name="Path">Файл конфигурации (.cf) или выгрузки (.dt).</param>
/// <param name="Catalog">Путь в дереве шаблонов через «/» (ключ <c>Catalog</c> манифеста) или путь каталога.</param>
/// <param name="Version">Версия конфигурации из манифеста; пусто — неизвестна.</param>
public sealed record ConfigurationTemplate(string Path, string Catalog, string Version)
{
    /// <summary>Выгрузка информационной базы (.dt) — база создаётся сразу с данными.</summary>
    public bool IsDump => Path.EndsWith(".dt", StringComparison.OrdinalIgnoreCase);

    public string Details => (Version.Length > 0 ? $"версия {Version}, " : string.Empty)
                             + (IsDump ? "выгрузка базы (.dt)" : "конфигурация (.cf)");

    public override string ToString() => Version.Length > 0 ? $"{Catalog} ({Version})" : Catalog;
}

/// <summary>
/// Ищет шаблоны так же, как штатный стартер: в каталогах шаблонов (по умолчанию — <c>%APPDATA%\1C\1cv8\tmplts</c>,
/// плюс <c>ConfigurationTemplatesLocation</c> из 1cestart.cfg) — подкаталоги с манифестом <c>1cv8.mft</c>.
/// Манифест: <c>Version=</c> и секции <c>[ConfigN]</c> с <c>Catalog=</c> (путь в дереве) и <c>Source=</c> (файл).
/// Каталог без манифеста, но с <c>1cv8.cf</c> или <c>1cv8.dt</c>, тоже считается шаблоном.
/// </summary>
public static class TemplateScanner
{
    private const int MaxDepth = 6;
    private const string ManifestName = "1cv8.mft";

    public static IReadOnlyList<ConfigurationTemplate> Scan(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var result = new List<ConfigurationTemplate>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var full = System.IO.Path.TrimEndingDirectorySeparator(root.Trim());
            if (seen.Add(full) && Directory.Exists(full))
            {
                Walk(full, full, 0, result);
            }
        }

        return [.. result
            .DistinctBy(t => t.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(t => t.Catalog, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(t => Version.TryParse(t.Version, out var v) ? v : new Version())];
    }

    /// <summary>Шаблоны, описанные манифестом каталога <paramref name="directory"/>.</summary>
    public static IReadOnlyList<ConfigurationTemplate> ParseManifest(string directory, IEnumerable<string> manifestLines)
    {
        ArgumentNullException.ThrowIfNull(manifestLines);
        var version = string.Empty;
        var configs = new List<(string Catalog, string Source)>();
        string? catalog = null;
        string? source = null;
        var inConfig = false;

        void Flush()
        {
            if (inConfig && !string.IsNullOrWhiteSpace(source))
            {
                configs.Add((catalog ?? string.Empty, source));
            }

            catalog = null;
            source = null;
        }

        foreach (var raw in manifestLines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                inConfig = line.StartsWith("[Config", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (!inConfig && key.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                version = value;
            }
            else if (inConfig && key.Equals("Catalog", StringComparison.OrdinalIgnoreCase))
            {
                catalog = value;
            }
            else if (inConfig && key.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                source = value;
            }
        }

        Flush();
        return [.. configs.Select(c => new ConfigurationTemplate(
            System.IO.Path.Combine(directory, c.Source),
            c.Catalog.Length > 0 ? c.Catalog : System.IO.Path.GetFileName(directory),
            version))];
    }

    private static void Walk(string root, string directory, int depth, List<ConfigurationTemplate> result)
    {
        try
        {
            var manifest = System.IO.Path.Combine(directory, ManifestName);
            var found = File.Exists(manifest)
                ? ParseManifest(directory, File.ReadAllLines(manifest)).Where(t => File.Exists(t.Path)).ToList()
                : [];
            if (found.Count == 0)
            {
                // Без манифеста: 1cv8.cf или 1cv8.dt прямо в каталоге; путь в дереве — относительный путь каталога.
                foreach (var name in new[] { "1cv8.cf", "1cv8.dt" })
                {
                    var file = Directory.EnumerateFiles(directory)
                        .FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                    {
                        var relative = System.IO.Path.GetRelativePath(root, directory).Replace('\\', '/');
                        found.Add(new ConfigurationTemplate(file, relative == "." ? System.IO.Path.GetFileName(root) : relative, string.Empty));
                    }
                }
            }

            result.AddRange(found);
            if (depth >= MaxDepth)
            {
                return;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                Walk(root, child, depth + 1, result);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Недоступный каталог шаблонов не мешает остальным.
        }
    }
}
