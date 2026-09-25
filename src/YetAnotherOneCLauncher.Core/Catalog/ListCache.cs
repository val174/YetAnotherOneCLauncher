using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YetAnotherOneCLauncher.Core.IO;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>Сведения о сохранённой копии списка.</summary>
public sealed record CachedListInfo
{
    public ListSourceKind Kind { get; init; }

    /// <summary>Путь или адрес источника.</summary>
    public string Location { get; init; } = string.Empty;

    public DateTimeOffset SavedAt { get; init; }

    /// <summary>Веб-сервис: идентификатор клиента, выданный сервисом.</summary>
    public string? ClientId { get; init; }

    /// <summary>Веб-сервис: код версии списка — по нему сервис отвечает, изменился ли список.</summary>
    public string? CheckCode { get; init; }
}

/// <summary>Копия списка из кэша.</summary>
public sealed record CachedList(V8iDocument Document, CachedListInfo Info);

/// <summary>
/// Последние успешно прочитанные копии общих списков и списков с веб-сервиса — в каталоге настроек лаунчера.
/// Если источник недоступен, показываются базы из копии. На каждый источник — два файла:
/// <c>&lt;ключ&gt;.v8i</c> (текст списка) и <c>&lt;ключ&gt;.json</c> (откуда и когда).
/// </summary>
public sealed class ListCache
{
    public ListCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>Сохраняет копию. Ошибки записи не мешают работе — это только кэш.</summary>
    /// <returns><c>false</c>, если сохранить не удалось.</returns>
    public async Task<bool> SaveAsync(ListSource source, string text, CachedListInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(info);
        var key = KeyOf(source);
        info = info with { Kind = source.Kind, Location = source.Location };
        try
        {
            await AtomicFileWriter.WriteAllBytesAsync(
                    Path.Combine(Directory, key + ".v8i"), TextFileCodec.Encode(text, TextFormat.V8iDefault), backupPath: null, cancellationToken)
                .ConfigureAwait(false);
            await AtomicFileWriter.WriteAllBytesAsync(
                    Path.Combine(Directory, key + ".json"), JsonSerializer.SerializeToUtf8Bytes(info, ListCacheJsonContext.Default.CachedListInfo), backupPath: null, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <returns>Копия или <c>null</c>, если её нет или она повреждена.</returns>
    public async Task<CachedList?> LoadAsync(ListSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = KeyOf(source);
        var infoPath = Path.Combine(Directory, key + ".json");
        var listPath = Path.Combine(Directory, key + ".v8i");
        try
        {
            if (!File.Exists(infoPath) || !File.Exists(listPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(infoPath);
            var info = await JsonSerializer.DeserializeAsync(stream, ListCacheJsonContext.Default.CachedListInfo, cancellationToken)
                .ConfigureAwait(false);
            if (info is null || info.Kind != source.Kind || !string.Equals(info.Location, source.Location, StringComparison.Ordinal))
            {
                return null; // совпадение ключа у разных источников практически невозможно, но проверяем
            }

            var document = V8iDocument.Parse(await File.ReadAllBytesAsync(listPath, cancellationToken).ConfigureAwait(false));
            return new CachedList(document, info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Имя файлов копии: SHA-256 от вида и адреса источника.</summary>
    public static string KeyOf(ListSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var location = OperatingSystem.IsWindows() && source.Kind == ListSourceKind.Common
            ? source.Location.ToUpperInvariant() // пути Windows не различают регистр
            : source.Location;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Kind}\n{location}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CachedListInfo))]
internal sealed partial class ListCacheJsonContext : JsonSerializerContext;
