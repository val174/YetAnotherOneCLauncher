using System.Text.Json;
using System.Text.Json.Serialization;
using YetAnotherOneCLauncher.Core.IO;

namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Шаблон в кэше — поля <see cref="ConfigurationTemplate"/>.</summary>
public sealed record CachedTemplate
{
    public string Catalog { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string? ConfigurationPath { get; init; }

    public string? DumpPath { get; init; }
}

/// <summary>Найденные шаблоны и где их искали.</summary>
public sealed record TemplateCacheData
{
    /// <summary>Когда список обновляли.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Каталоги шаблонов, в которых искали: изменились — список стоит обновить.</summary>
    public List<string> Roots { get; init; } = [];

    public List<CachedTemplate> Templates { get; init; } = [];

    public IReadOnlyList<ConfigurationTemplate> ToTemplates() =>
        [.. Templates.Where(t => t is not null).Select(t => new ConfigurationTemplate(t.Catalog ?? string.Empty, t.Version ?? string.Empty, t.ConfigurationPath, t.DumpPath))
            .Where(t => t.Path.Length > 0)];

    public static TemplateCacheData From(IEnumerable<ConfigurationTemplate> templates, IEnumerable<string> roots, DateTimeOffset updatedAt) => new()
    {
        UpdatedAt = updatedAt,
        Roots = [.. roots],
        Templates = [.. templates.Select(t => new CachedTemplate
        {
            Catalog = t.Catalog, Version = t.Version, ConfigurationPath = t.ConfigurationPath, DumpPath = t.DumpPath,
        })],
    };

    /// <summary>Искали в тех же каталогах (без учёта регистра и порядка).</summary>
    public bool SameRoots(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        static HashSet<string> Set(IEnumerable<string> paths) =>
            paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Set(Roots).SetEquals(Set(roots));
    }
}

/// <summary>
/// Кэш шаблонов конфигураций (<c>templates-cache.json</c> в данных лаунчера): каталоги шаблонов просматриваются только
/// по кнопке «Обновить» на шаге выбора шаблона, в остальное время список берётся отсюда.
/// </summary>
public sealed class TemplateCache(string filePath)
{
    public const string FileName = "templates-cache.json";

    public string FilePath { get; } = filePath;

    /// <returns><c>null</c> — кэша нет или он повреждён (список ещё не обновляли).</returns>
    public async Task<TemplateCacheData?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync(stream, TemplateCacheJsonContext.Default.TemplateCacheData, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(TemplateCacheData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, TemplateCacheJsonContext.Default.TemplateCacheData);
        return AtomicFileWriter.WriteAllBytesAsync(FilePath, bytes, backupPath: null, cancellationToken);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TemplateCacheData))]
internal sealed partial class TemplateCacheJsonContext : JsonSerializerContext;
