using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YetAnotherOneCLauncher.Core.Edt;

/// <summary>Установленная версия 1C:EDT (из <c>products.json</c> EDT Start).</summary>
/// <param name="Id">Идентификатор версии в EDT Start — на него ссылаются проекты.</param>
/// <param name="Version">Номер версии для показа: «2025.2».</param>
/// <param name="ExecutablePath">Путь к <c>1cedt.exe</c> (Linux — <c>1cedt</c>).</param>
public sealed record EdtInstallation(string Id, string Version, string ExecutablePath);

/// <summary>Проект EDT Start — рабочая область (workspace) 1C:EDT.</summary>
/// <param name="Id">Идентификатор проекта в EDT Start.</param>
/// <param name="Workspace">Каталог рабочей области: передаётся в <c>-data</c>.</param>
/// <param name="InstallationId">Версия EDT, в которой открывается проект; её может уже не быть на компьютере.</param>
public sealed record EdtProject(string Id, string Name, string Workspace, string? InstallationId);

/// <summary>Проекты и версии 1C:EDT из данных EDT Start.</summary>
public sealed record EdtCatalog(IReadOnlyList<EdtProject> Projects, IReadOnlyList<EdtInstallation> Installations)
{
    public static readonly EdtCatalog Empty = new([], []);

    public bool IsEmpty => Projects.Count == 0;

    /// <summary>Версия EDT проекта; <c>null</c> — её нет на компьютере (удалена из EDT Start).</summary>
    public EdtInstallation? InstallationOf(EdtProject project) =>
        Installations.FirstOrDefault(i => string.Equals(i.Id, project?.InstallationId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Самая новая установленная версия — по умолчанию, если версии проекта нет.</summary>
    public EdtInstallation? Newest => Installations.OrderByDescending(i => EdtVersions.SortKey(i.Version)).FirstOrDefault();
}

/// <summary>
/// Чтение данных 1C:EDT Start: <c>projects.json</c> (проекты — рабочие области) и <c>products.json</c> (версии EDT).
/// Формат не документирован (поле <c>version</c> = «1.1»): читаются только нужные поля, незнакомый или повреждённый
/// файл — пустой каталог, без исключений.
/// </summary>
public static class EdtStartReader
{
    public const string ProjectsFile = "projects.json";
    public const string ProductsFile = "products.json";

    /// <summary>Каталог данных EDT Start: Windows — <c>%LOCALAPPDATA%\1C\1cedtstart</c>, Linux — <c>~/.local/share/1C/1cedtstart</c>.</summary>
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1C", "1cedtstart");

    /// <summary>
    /// Программа 1C:EDT Start — самая новая из каталогов компонентов 1С
    /// (<c>components\1c-edt-start-0.10.0+448-x86_64\1cedtstart.exe</c>); <c>null</c> — не установлена.
    /// </summary>
    public static string? FindStarter(IEnumerable<string> componentRoots)
    {
        var executable = OperatingSystem.IsWindows() ? "1cedtstart.exe" : "1cedtstart";
        var found = new List<(string Path, Version Version)>();
        foreach (var root in componentRoots)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var directory in Directory.GetDirectories(root, "1c-edt-start-*"))
                {
                    var path = Path.Combine(directory, executable);
                    if (File.Exists(path))
                    {
                        found.Add((path, EdtVersions.SortKey(Path.GetFileName(directory)["1c-edt-start-".Length..])));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found.OrderByDescending(f => f.Version).Select(f => f.Path).FirstOrDefault();
    }

    public static EdtCatalog Load(string dataDirectory)
    {
        try
        {
            var projects = Path.Combine(dataDirectory, ProjectsFile);
            var products = Path.Combine(dataDirectory, ProductsFile);
            return File.Exists(projects) && File.Exists(products)
                ? Parse(File.ReadAllBytes(projects), File.ReadAllBytes(products))
                : EdtCatalog.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return EdtCatalog.Empty;
        }
    }

    public static EdtCatalog Parse(byte[] projectsJson, byte[] productsJson)
    {
        var installations = new List<EdtInstallation>();
        foreach (var item in Items(productsJson))
        {
            if (String(item, "id") is { Length: > 0 } id && String(item, "location") is { Length: > 0 } location)
            {
                var version = item.TryGetProperty("installedVersion", out var installed) && installed.ValueKind == JsonValueKind.Object
                    ? String(installed, "label") ?? String(installed, "name")
                    : null;
                installations.Add(new EdtInstallation(id, version ?? String(item, "label") ?? "?", location));
            }
        }

        var projects = new List<EdtProject>();
        foreach (var item in Items(projectsJson))
        {
            if (String(item, "id") is { Length: > 0 } id && String(item, "location") is { Length: > 0 } workspace)
            {
                var name = String(item, "label") is { Length: > 0 } label ? label : Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace));
                projects.Add(new EdtProject(id, name, workspace, String(item, "productId")));
            }
        }

        return new EdtCatalog(projects, installations);
    }

    /// <summary>Элементы массива <c>data</c>; неверный JSON — пусто.</summary>
    private static List<JsonElement> Items(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? [.. data.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => e.Clone())]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Версии EDT («2024.2», «2025.2.6») — для сортировки.</summary>
public static class EdtVersions
{
    public static Version SortKey(string? version)
    {
        var numbers = Regex.Match(version ?? string.Empty, @"^\d+(\.\d+){0,3}").Value;
        // «2024» — тоже версия: Version требует хотя бы два числа.
        var text = numbers.Contains('.', StringComparison.Ordinal) ? numbers : numbers + ".0";
        return System.Version.TryParse(text, out var parsed) ? parsed : new Version(0, 0);
    }
}

/// <summary>
/// Java для 1C:EDT. EDT Start передаёт её через <c>-vm</c> (в <c>1cedt.ini</c> её нет): нужная версия — в
/// <c>-Dosgi.requiredJavaVersion</c> из <c>1cedt.ini</c>, сами JDK — в каталоге компонентов 1С
/// (<c>C:\Program Files\1C\1CE\components\axiom-jdk-full-17.0.16+12-x86_64</c>).
/// </summary>
public static partial class EdtJava
{
    /// <summary>Каталоги компонентов 1С с JDK.</summary>
    public static IReadOnlyList<string> DefaultComponentRoots =>
        OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "1C", "1CE", "components")]
            : ["/opt/1C/1CE/components"];

    /// <summary>Нужная основная версия Java: из <c>1cedt.ini</c> рядом с <c>1cedt.exe</c>; <c>null</c> — не указана.</summary>
    public static int? RequiredVersion(string executablePath)
    {
        var ini = Path.Combine(Path.GetDirectoryName(executablePath) ?? string.Empty, "1cedt.ini");
        try
        {
            return File.Exists(ini) && RequiredJavaRegex().Match(File.ReadAllText(ini)) is { Success: true } match
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>javaw.exe</c> (Linux — <c>java</c>) подходящего JDK: основная версия совпадает с нужной, из нескольких — самый
    /// новый (сначала Axiom — его ставит EDT Start). Не нашли или версия не указана — <c>null</c>: EDT ищет Java сам.
    /// </summary>
    public static string? Find(IEnumerable<string> componentRoots, int? requiredVersion)
    {
        if (requiredVersion is not { } required)
        {
            return null;
        }

        var executable = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        var candidates = new List<(string Path, Version Version, bool Axiom)>();
        foreach (var root in componentRoots)
        {
            string[] directories;
            try
            {
                directories = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (JdkDirectoryRegex().Match(Path.GetFileName(directory)) is { Success: true } match
                    && Version.TryParse(match.Groups["version"].Value, out var version) && version.Major == required)
                {
                    var java = Path.Combine(directory, "bin", executable);
                    if (File.Exists(java))
                    {
                        candidates.Add((java, version, match.Groups["vendor"].Value.Equals("axiom", StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
        }

        return candidates.OrderByDescending(c => c.Axiom).ThenByDescending(c => c.Version).Select(c => c.Path).FirstOrDefault();
    }

    [GeneratedRegex(@"-Dosgi\.requiredJavaVersion=(\d+)")]
    private static partial Regex RequiredJavaRegex();

    // axiom-jdk-full-17.0.16+12-x86_64, azul-jdk-full-17.0.13+11-x86_64
    [GeneratedRegex(@"^(?<vendor>[a-z]+)-jdk(-full)?-(?<version>\d+(\.\d+){0,3})", RegexOptions.IgnoreCase)]
    private static partial Regex JdkDirectoryRegex();
}

/// <summary>Запуск 1C:EDT с рабочей областью проекта.</summary>
public static class EdtLaunch
{
    /// <summary>Аргументы: <c>-data "&lt;рабочая область&gt;" -vm "&lt;javaw&gt;"</c> (<c>-vm</c> — если Java найдена).</summary>
    public static string Arguments(string workspace, string? javaPath)
    {
        var arguments = "-data " + Quote(workspace);
        return javaPath is null ? arguments : arguments + " -vm " + Quote(javaPath);
    }

    /// <summary>Строка запуска целиком — для подсказки и «Копировать строку запуска».</summary>
    public static string CommandLine(string executablePath, string workspace, string? javaPath) =>
        Quote(executablePath) + " " + Arguments(workspace, javaPath);

    /// <summary>
    /// Рабочая область уже открыта в EDT: Eclipse держит файл <c>.metadata\.lock</c> занятым, пока она открыта
    /// (после закрытия файл остаётся, но свободен).
    /// </summary>
    public static bool IsWorkspaceOpen(string workspace)
    {
        var lockFile = Path.Combine(workspace, ".metadata", ".lock");
        if (!File.Exists(lockFile))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>
/// С какими информационными базами EDT связал проекты рабочей области: файлы
/// <c>.metadata\.plugins\org.eclipse.core.resources\.projects\&lt;проект&gt;\com._1c.g5.v8.dt.platform.services.core\refs\heads\&lt;ветка&gt;\AssociationData.properties</c>
/// со строкой <c>Infobases=&lt;ID базы&gt;,…</c> — это ID из списка баз (ключ <c>ID</c> в ibases.v8i).
/// </summary>
public static class EdtWorkspaceBindings
{
    public static IReadOnlySet<string> InfobaseIds(string workspace)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = Path.Combine(workspace, ".metadata", ".plugins", "org.eclipse.core.resources", ".projects");
        try
        {
            if (!Directory.Exists(projects))
            {
                return ids;
            }

            foreach (var project in Directory.EnumerateDirectories(projects))
            {
                var heads = Path.Combine(project, "com._1c.g5.v8.dt.platform.services.core", "refs", "heads");
                if (!Directory.Exists(heads))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(heads, "AssociationData.properties", SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (line.StartsWith("Infobases=", StringComparison.Ordinal))
                        {
                            ids.UnionWith(line["Infobases=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Нет доступа к рабочей области — привязок не знаем.
        }

        return ids;
    }
}
