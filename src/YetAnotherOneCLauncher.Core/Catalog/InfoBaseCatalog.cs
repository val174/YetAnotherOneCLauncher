using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>Прочитанный список: источник, документ (если прочитан) и ошибка (если нет).</summary>
public sealed record LoadedList(ListSource Source, V8iDocument? Document, string? Error)
{
    public bool IsAvailable => Document is not null;

    /// <summary>Список ещё читается (при постепенной загрузке).</summary>
    public bool IsPending { get; init; }
}

/// <summary>Объединённый каталог баз и папок из всех источников.</summary>
public sealed class InfoBaseCatalog
{
    private readonly IReadOnlyList<CatalogWarning> _loadWarnings;

    private InfoBaseCatalog(
        IReadOnlyList<LoadedList> lists,
        IReadOnlyList<InfoBase> infoBases,
        IReadOnlyList<InfoBaseFolder> folders,
        StarterConfig starterConfig,
        IReadOnlyList<CatalogWarning> loadWarnings,
        IReadOnlyList<CatalogWarning> warnings)
    {
        Lists = lists;
        InfoBases = infoBases;
        Folders = folders;
        StarterConfig = starterConfig;
        _loadWarnings = loadWarnings;
        Warnings = warnings;
    }

    /// <summary>Все источники, включая недоступные.</summary>
    public IReadOnlyList<LoadedList> Lists { get; }

    /// <summary>Базы без дубликатов, в порядке источников и файлов.</summary>
    public IReadOnlyList<InfoBase> InfoBases { get; }

    public IReadOnlyList<InfoBaseFolder> Folders { get; }

    /// <summary>Объединённые настройки стартера.</summary>
    public StarterConfig StarterConfig { get; }

    public IReadOnlyList<CatalogWarning> Warnings { get; }

    /// <summary>Личный список (для добавления и изменения записей). <c>null</c>, если его не удалось прочитать.</summary>
    public LoadedList? PersonalList => Lists.FirstOrDefault(l => l.Source.Kind == ListSourceKind.Personal);

    public CatalogFolderNode BuildTree(CatalogSortMode sortMode = CatalogSortMode.Custom) =>
        CatalogTreeBuilder.Build(Folders, InfoBases, sortMode);

    /// <summary>
    /// Объединяет прочитанные списки: базы и папки без дубликатов.
    /// Дубликаты определяются по <see cref="InfoBase.IdentityKey"/>; побеждает первая запись в порядке списков.
    /// </summary>
    /// <param name="lists">Списки в порядке приоритета: личный, затем общие.</param>
    /// <param name="starterConfig">Объединённые настройки стартера.</param>
    /// <param name="loadWarnings">Замечания, собранные при чтении файлов (недоступные списки и т. п.).</param>
    public static InfoBaseCatalog Build(
        IReadOnlyList<LoadedList> lists,
        StarterConfig starterConfig,
        IReadOnlyList<CatalogWarning> loadWarnings)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(starterConfig);
        ArgumentNullException.ThrowIfNull(loadWarnings);

        var warnings = new List<CatalogWarning>(loadWarnings);
        var infoBases = new List<InfoBase>();
        var folders = new List<InfoBaseFolder>();
        var seenBases = new Dictionary<string, InfoBase>(StringComparer.Ordinal);
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in lists)
        {
            if (list.Document is null)
            {
                continue;
            }

            foreach (var section in list.Document.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Name))
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Warning,
                        "Секция без названия пропущена.",
                        list.Source.Location));
                    continue;
                }

                if (V8iSections.IsFolder(section))
                {
                    var folder = new InfoBaseFolder(section, list.Source);
                    if (seenFolders.Add(folder.FullPath))
                    {
                        folders.Add(folder);
                    }

                    continue;
                }

                var infoBase = new InfoBase(section, list.Source);
                if (infoBase.Connection.HasErrors)
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Warning,
                        $"«{infoBase.Name}»: {string.Join(" ", infoBase.Connection.Errors)}",
                        list.Source.Location));
                }

                if (infoBase.ConnectionKind == ConnectionKind.Unknown)
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Info,
                        $"«{infoBase.Name}»: тип подключения не распознан ({infoBase.Connection}).",
                        list.Source.Location));
                }

                if (seenBases.TryGetValue(infoBase.IdentityKey, out var existing))
                {
                    warnings.Add(new CatalogWarning(
                        CatalogWarningLevel.Info,
                        $"Дубликат «{infoBase.Name}» пропущен: база уже есть в списке «{existing.Source.Location}».",
                        list.Source.Location));
                    continue;
                }

                seenBases.Add(infoBase.IdentityKey, infoBase);
                infoBases.Add(infoBase);
            }
        }

        return new InfoBaseCatalog(lists, infoBases, folders, starterConfig, loadWarnings, warnings);
    }

    /// <summary>
    /// Тот же каталог с новым содержимым личного списка — после правки или внешнего изменения файла.
    /// Общие списки не перечитываются: на сетевых дисках это долго.
    /// </summary>
    public InfoBaseCatalog WithPersonalDocument(V8iDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var personal = PersonalList ?? throw new InvalidOperationException("В каталоге нет личного списка.");
        var lists = Lists.Select(l => ReferenceEquals(l, personal) ? l with { Document = document, Error = null } : l).ToList();
        var loadWarnings = _loadWarnings.Where(w => w.Location != personal.Source.Location).ToList();
        return Build(lists, StarterConfig, loadWarnings);
    }
}
