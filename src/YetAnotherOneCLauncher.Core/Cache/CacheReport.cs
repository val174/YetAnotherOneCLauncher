using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.Core.Cache;

/// <summary>Кэш одной базы (или кэш без хозяина) во всех корнях.</summary>
/// <param name="Id">GUID базы.</param>
/// <param name="InfoBase">База из списков; <c>null</c> — такого <c>ID</c> нет ни в одном списке.</param>
/// <param name="Directories">Каталоги кэша: локальный и (в Windows) Roaming.</param>
public sealed record CacheOwner(string Id, InfoBase? InfoBase, IReadOnlyList<CacheDirectory> Directories)
{
    public bool IsOrphan => InfoBase is null;

    public long LocalBytes => Directories.Where(d => d.Location == CacheLocation.Local).Sum(d => d.SizeBytes);

    public long RoamingBytes => Directories.Where(d => d.Location == CacheLocation.Roaming).Sum(d => d.SizeBytes);

    public long TotalBytes => LocalBytes + RoamingBytes;

    public DateTimeOffset LastWriteTime => Directories.Max(d => d.LastWriteTime);

    /// <summary>Каталоги указанных видов.</summary>
    public IEnumerable<CacheDirectory> In(bool local, bool roaming) =>
        Directories.Where(d => d.Location == CacheLocation.Local ? local : roaming);
}

/// <summary>Кэш, сопоставленный с базами из списков.</summary>
public sealed class CacheReport
{
    private readonly Dictionary<string, CacheOwner> _byId;

    private CacheReport(IReadOnlyList<CacheOwner> owners, IReadOnlyList<string> warnings)
    {
        Owners = owners;
        Warnings = warnings;
        _byId = owners.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static CacheReport Empty { get; } = new([], []);

    /// <summary>Сначала самые большие.</summary>
    public IReadOnlyList<CacheOwner> Owners { get; }

    public IReadOnlyList<string> Warnings { get; }

    public long TotalBytes => Owners.Sum(o => o.TotalBytes);

    public long OrphanBytes => Owners.Where(o => o.IsOrphan).Sum(o => o.TotalBytes);

    public static CacheReport Build(CacheScanResult scan, IEnumerable<InfoBase> infoBases)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(infoBases);
        var basesById = new Dictionary<string, InfoBase>(StringComparer.OrdinalIgnoreCase);
        foreach (var infoBase in infoBases)
        {
            if (CacheScanner.TryParseId(infoBase.Id?.Trim().Trim('{', '}')) is { } id)
            {
                basesById.TryAdd(id, infoBase); // первая — как в каталоге: личный список важнее общих
            }
        }

        var owners = scan.Directories
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CacheOwner(g.Key, basesById.GetValueOrDefault(g.Key), g.OrderBy(d => d.Location).ToList()))
            .OrderByDescending(o => o.TotalBytes)
            .ToList();
        return new CacheReport(owners, scan.Warnings);
    }

    public CacheOwner? For(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        return CacheScanner.TryParseId(infoBase.Id?.Trim().Trim('{', '}')) is { } id ? _byId.GetValueOrDefault(id) : null;
    }
}
