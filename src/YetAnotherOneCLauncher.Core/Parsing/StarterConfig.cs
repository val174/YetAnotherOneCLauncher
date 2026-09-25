using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Настройки стартера 1С (1cestart.cfg): строки «ключ=значение» без секций,
/// ключи могут повторяться (например, несколько <c>CommonInfoBases</c>).
/// </summary>
public sealed class StarterConfig
{
    public const string CommonInfoBasesKey = "CommonInfoBases";
    public const string InstalledLocationKey = "InstalledLocation";
    public const string DefaultVersionKey = "DefaultVersion";
    public const string InternetServiceKey = "InternetService";

    public StarterConfig(IEnumerable<KeyValuePair<string, string>> entries, string? sourcePath = null)
    {
        Entries = entries.ToList();
        SourcePath = sourcePath;
    }

    public static StarterConfig Empty { get; } = new([]);

    /// <summary>Файл, из которого прочитаны настройки (для объединённых — <c>null</c>).</summary>
    public string? SourcePath { get; }

    /// <summary>Все пары в порядке следования в файле(ах).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries { get; }

    /// <summary>Пути к общим спискам баз (.v8i).</summary>
    public IReadOnlyList<string> CommonInfoBases => GetAll(CommonInfoBasesKey);

    /// <summary>Каталоги, где установлены платформы 1С.</summary>
    public IReadOnlyList<string> InstalledLocations => GetAll(InstalledLocationKey);

    /// <summary>Адреса веб-сервисов списков баз.</summary>
    public IReadOnlyList<string> InternetServices => GetAll(InternetServiceKey);

    /// <summary>Версия платформы по умолчанию (последнее значение побеждает).</summary>
    public string? DefaultVersion => GetLast(DefaultVersionKey);

    /// <summary>Все непустые значения ключа (регистр ключа не важен).</summary>
    public IReadOnlyList<string> GetAll(string key) =>
        Entries
            .Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase) && e.Value.Length > 0)
            .Select(e => e.Value)
            .ToList();

    public string? GetLast(string key) => GetAll(key) is { Count: > 0 } values ? values[^1] : null;

    public static StarterConfig Parse(string text, string? sourcePath = null)
    {
        var lines = V8iParser.SplitLines(text, out _);
        var entries = new List<KeyValuePair<string, string>>();
        foreach (var raw in lines)
        {
            var line = IniLine.Parse(raw);
            if (line.IsKeyValue)
            {
                entries.Add(new KeyValuePair<string, string>(line.Key!, line.Value ?? string.Empty));
            }
        }

        return new StarterConfig(entries, sourcePath);
    }

    public static async Task<StarterConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var decoded = await TextFileCodec.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(decoded.Text, path);
    }

    /// <summary>
    /// Объединяет настройки в указанном порядке (обычно: общие для компьютера, затем пользовательские).
    /// Для многозначных ключей значения накапливаются, для однозначных побеждает последнее.
    /// </summary>
    public static StarterConfig Merge(IEnumerable<StarterConfig> configs) =>
        new(configs.SelectMany(c => c.Entries));
}
