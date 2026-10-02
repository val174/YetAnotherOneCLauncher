using System.Text.RegularExpressions;

namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>
/// Средство администрирования (настройки, вкладка «Средства администрирования»): веб-сервис или программа.
/// </summary>
public sealed record AdminTool
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Строка запуска: адрес веб-страницы сервиса (http, https) или путь к программе с параметрами.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Значок: <c>null</c> — автоматически (значок программы, значок сайта); <see cref="AdminToolIcon.BuiltInPrefix"/> +
    /// имя — значок из лаунчера; <see cref="AdminToolIcon.FilePrefix"/> + имя файла — свой, в каталоге значков лаунчера.
    /// </summary>
    public string? Icon { get; init; }
}

/// <summary>Что запускает инструмент.</summary>
public enum AdminToolKind
{
    /// <summary>Программа (путь к файлу).</summary>
    Application,

    /// <summary>Веб-сервис (адрес http или https).</summary>
    WebService,
}

/// <summary>Значение <see cref="AdminTool.Icon"/>.</summary>
public static class AdminToolIcon
{
    public const string BuiltInPrefix = "builtin:";

    public const string FilePrefix = "file:";

    public static string BuiltIn(string id) => BuiltInPrefix + id;

    public static string File(string fileName) => FilePrefix + fileName;

    /// <summary>Имя встроенного значка; <c>null</c> — значок не встроенный.</summary>
    public static string? BuiltInId(string? icon) =>
        icon is not null && icon.StartsWith(BuiltInPrefix, StringComparison.Ordinal) ? icon[BuiltInPrefix.Length..] : null;

    /// <summary>Имя файла своего значка; <c>null</c> — значок не свой. Только имя: путь в каталог не выпускается.</summary>
    public static string? FileName(string? icon) =>
        icon is not null && icon.StartsWith(FilePrefix, StringComparison.Ordinal)
        && icon[FilePrefix.Length..] is { Length: > 0 } name && Path.GetFileName(name) == name
            ? name
            : null;
}

/// <summary>Программа инструмента: существующий файл и параметры командной строки.</summary>
public sealed record ResolvedProgram(string Path, string Arguments);

/// <summary>Разбор строки запуска инструмента.</summary>
public static partial class AdminToolTarget
{
    /// <summary>Адрес веб-сервиса; <c>null</c> — строка запуска не адрес http или https.</summary>
    public static Uri? WebUrl(string? target) => NetworkSettings.ParseWebUrl(target);

    public static AdminToolKind KindOf(string? target) => WebUrl(target) is null ? AdminToolKind.Application : AdminToolKind.WebService;

    /// <summary>
    /// Программа и её параметры: путь в кавычках и параметры после него (<c>"C:\Program Files\x.exe" -a</c>), иначе
    /// вся строка — путь (в нём могут быть пробелы). Переменные окружения (<c>%windir%</c>) раскрываются.
    /// </summary>
    public static (string Program, string Arguments) SplitProgram(string? target)
    {
        var text = (target ?? string.Empty).Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 0)
            {
                return (Environment.ExpandEnvironmentVariables(text[1..end]), text[(end + 1)..].Trim());
            }

            text = text.Trim('"');
        }

        return (Environment.ExpandEnvironmentVariables(text), string.Empty);
    }

    /// <summary>
    /// Программа из строки запуска: полный путь к существующему файлу и параметры. Путь без кавычек может быть и целой
    /// строкой (с пробелами в пути), и её началом до пробела (<c>mmc.exe compmgmt.msc</c>) — берётся первый существующий
    /// файл: вся строка, затем начала от короткого к длинному, как у командной строки Windows. Имя без каталога
    /// (<c>mmc.exe</c>, <c>notepad</c>) ищется по каталогам PATH. <c>null</c> — файл не найден.
    /// </summary>
    public static ResolvedProgram? ResolveProgram(string? target)
    {
        var (program, arguments) = SplitProgram(target);
        if (program.Length == 0)
        {
            return null;
        }

        var candidates = new List<(string Program, string Arguments)> { (program, arguments) };
        if (arguments.Length == 0)
        {
            for (var space = program.IndexOf(' ', StringComparison.Ordinal); space > 0; space = program.IndexOf(' ', space + 1))
            {
                candidates.Add((program[..space], program[(space + 1)..].Trim()));
            }
        }

        return candidates
            .Select(c => FindProgram(c.Program) is { } path ? new ResolvedProgram(path, c.Arguments) : null)
            .FirstOrDefault(p => p is not null);
    }

    private static string? FindProgram(string program)
    {
        if (File.Exists(program))
        {
            return Path.GetFullPath(program);
        }

        if (Path.GetFileName(program) != program)
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(program)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => extensions.Select(ext => Path.Combine(dir.Trim('"'), program + ext)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Значки сайта из разметки страницы (<c>&lt;link rel="icon" href="…"&gt;</c>, <c>shortcut icon</c>,
    /// <c>apple-touch-icon</c>) в порядке предпочтения, адреса — абсолютные; в конце — <c>/favicon.ico</c>.
    /// Значки SVG пропускаются: их не нарисовать как картинку.
    /// </summary>
    public static IReadOnlyList<Uri> FaviconCandidates(string? html, Uri page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var found = new List<(int Rank, Uri Url)>();
        foreach (Match link in LinkTag().Matches(html ?? string.Empty))
        {
            var rel = Attribute(link.Value, "rel")?.ToLowerInvariant();
            var href = Attribute(link.Value, "href");
            if (rel is null || href is null || !rel.Contains("icon", StringComparison.Ordinal)
                || rel.Contains("mask-icon", StringComparison.Ordinal)
                || (Attribute(link.Value, "type") ?? string.Empty).Contains("svg", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(page, System.Net.WebUtility.HtmlDecode(href), out var url)
                || url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps && url.Scheme != "data"))
            {
                continue;
            }

            found.Add((rel.Contains("apple", StringComparison.Ordinal) ? 1 : 0, url));
        }

        var result = found.OrderBy(f => f.Rank).Select(f => f.Url).Distinct().ToList();
        var fallback = new Uri(page, "/favicon.ico");
        if (!result.Contains(fallback))
        {
            result.Add(fallback);
        }

        return result;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value.Trim() : null;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();
}
