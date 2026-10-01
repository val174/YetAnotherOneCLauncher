namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>
/// Шаблон информационной базы — одна версия конфигурации из каталога шаблонов 1С. В каталоге версии может быть
/// конфигурация (.cf), выгрузка демонстрационной базы (.dt) или оба файла.
/// </summary>
/// <param name="Catalog">Имя конфигурации — путь в дереве шаблонов через «/» (ключ <c>Catalog</c> манифеста) или путь каталога.</param>
/// <param name="Version">Версия конфигурации из манифеста; пусто — неизвестна.</param>
/// <param name="ConfigurationPath">Файл конфигурации (.cf): база создаётся без данных.</param>
/// <param name="DumpPath">Выгрузка (.dt): база создаётся с данными (демонстрационная).</param>
public sealed record ConfigurationTemplate(string Catalog, string Version, string? ConfigurationPath, string? DumpPath)
{
    /// <summary>Есть и конфигурация, и выгрузка — нужно выбрать, что из них создавать.</summary>
    public bool HasBoth => ConfigurationPath is not null && DumpPath is not null;

    /// <summary>Файл по умолчанию: конфигурация, если есть, иначе выгрузка.</summary>
    public string Path => ConfigurationPath ?? DumpPath ?? string.Empty;

    /// <summary>Шаблон из одного выбранного файла (.cf или .dt).</summary>
    public static ConfigurationTemplate FromFile(string path) =>
        path.EndsWith(".dt", StringComparison.OrdinalIgnoreCase)
            ? new(System.IO.Path.GetFileName(path), string.Empty, null, path)
            : new(System.IO.Path.GetFileName(path), string.Empty, path, null);

    /// <summary>Что в шаблоне: «конфигурация (.cf)», «демо-база (.dt)» или оба.</summary>
    public string Contents => HasBoth
        ? "конфигурация (.cf) и демонстрационная база (.dt)"
        : ConfigurationPath is not null ? "конфигурация (.cf)" : "выгрузка базы (.dt)";

    public string VersionText => Version.Length > 0 ? Version : "версия не указана";

    public override string ToString() => Version.Length > 0 ? $"{Catalog} ({Version})" : Catalog;
}

/// <summary>
/// Ищет шаблоны так же, как штатный стартер: в каталогах шаблонов (по умолчанию — <c>%APPDATA%\1C\1cv8\tmplts</c>,
/// плюс <c>ConfigurationTemplatesLocation</c> из 1cestart.cfg) — подкаталоги с манифестом <c>1cv8.mft</c>.
/// Манифест: <c>Version=</c> и секции <c>[ConfigN]</c> с <c>Catalog=</c> (путь в дереве) и <c>Source=</c> (файл).
/// Каталог без манифеста, но с <c>1cv8.cf</c> или <c>1cv8.dt</c>, тоже считается шаблоном.
/// Конфигурация и выгрузка из одного каталога — один шаблон (имя — по конфигурации).
/// </summary>
public static class TemplateScanner
{
    private const int MaxDepth = 6;
    private const string ManifestName = "1cv8.mft";

    public static IReadOnlyList<ConfigurationTemplate> Scan(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var result = new List<ConfigurationTemplate>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var full = System.IO.Path.TrimEndingDirectorySeparator(root.Trim());
            if (seen.Add(full) && Directory.Exists(full))
            {
                Walk(full, full, 0, result);
            }
        }

        return [.. result
            .DistinctBy(t => t.Path, PathComparer)
            .OrderBy(t => t.Catalog, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(t => System.Version.TryParse(t.Version, out var v) ? v : new Version())];
    }

    /// <summary>Шаблоны, описанные манифестом каталога <paramref name="directory"/>.</summary>
    public static IReadOnlyList<ConfigurationTemplate> ParseManifest(string directory, IEnumerable<string> manifestLines)
    {
        ArgumentNullException.ThrowIfNull(manifestLines);
        var version = string.Empty;
        var configs = new List<(string Catalog, string File)>();
        string? catalog = null;
        string? source = null;
        var inConfig = false;

        void Flush()
        {
            if (inConfig && !string.IsNullOrWhiteSpace(source))
            {
                configs.Add((catalog ?? string.Empty, System.IO.Path.Combine(directory, source)));
            }

            catalog = null;
            source = null;
        }

        foreach (var raw in manifestLines)
        {
            var line = raw.Trim().TrimStart('﻿');
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
        var fallback = System.IO.Path.GetFileName(directory);
        return Merge([.. configs.Select(c => (c.Catalog.Length > 0 ? c.Catalog : fallback, c.File))], version);
    }

    /// <summary>
    /// Одна конфигурация (.cf) и одна выгрузка (.dt) в каталоге — один шаблон с выбором; иначе — по шаблону на файл.
    /// </summary>
    private static List<ConfigurationTemplate> Merge(List<(string Catalog, string File)> files, string version)
    {
        var cf = files.Where(f => f.File.EndsWith(".cf", StringComparison.OrdinalIgnoreCase)).ToList();
        var dt = files.Where(f => f.File.EndsWith(".dt", StringComparison.OrdinalIgnoreCase)).ToList();
        if (cf.Count == 1 && dt.Count == 1)
        {
            return [new ConfigurationTemplate(cf[0].Catalog, version, cf[0].File, dt[0].File)];
        }

        return [.. files.Select(f => f.File.EndsWith(".dt", StringComparison.OrdinalIgnoreCase)
            ? new ConfigurationTemplate(f.Catalog, version, null, f.File)
            : new ConfigurationTemplate(f.Catalog, version, f.File, null))];
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void Walk(string root, string directory, int depth, List<ConfigurationTemplate> result)
    {
        try
        {
            var manifest = System.IO.Path.Combine(directory, ManifestName);
            var found = File.Exists(manifest)
                ? ParseManifest(directory, File.ReadAllLines(manifest))
                    .Select(t => t with
                    {
                        ConfigurationPath = File.Exists(t.ConfigurationPath) ? t.ConfigurationPath : null,
                        DumpPath = File.Exists(t.DumpPath) ? t.DumpPath : null,
                    })
                    .Where(t => t.Path.Length > 0)
                    .ToList()
                : [];
            if (found.Count == 0)
            {
                // Без манифеста: 1cv8.cf и (или) 1cv8.dt прямо в каталоге; имя — относительный путь каталога.
                var files = Directory.EnumerateFiles(directory)
                    .Where(f => System.IO.Path.GetFileName(f) is var name
                                && (name.Equals("1cv8.cf", StringComparison.OrdinalIgnoreCase) || name.Equals("1cv8.dt", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (files.Count > 0)
                {
                    var relative = System.IO.Path.GetRelativePath(root, directory).Replace('\\', '/');
                    var catalog = relative == "." ? System.IO.Path.GetFileName(root) : relative;
                    found.AddRange(Merge([.. files.Select(f => (catalog, f))], string.Empty));
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
