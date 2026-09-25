using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>Прочитанный список: источник, документ (если прочитан) и ошибка (если нет).</summary>
public sealed record LoadedList(ListSource Source, V8iDocument? Document, string? Error)
{
    public bool IsAvailable => Document is not null;
}

/// <summary>Объединённый каталог баз и папок из всех источников.</summary>
public sealed class InfoBaseCatalog
{
    public InfoBaseCatalog(
        IReadOnlyList<LoadedList> lists,
        IReadOnlyList<InfoBase> infoBases,
        IReadOnlyList<InfoBaseFolder> folders,
        StarterConfig starterConfig,
        IReadOnlyList<CatalogWarning> warnings)
    {
        Lists = lists;
        InfoBases = infoBases;
        Folders = folders;
        StarterConfig = starterConfig;
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

    public CatalogFolderNode BuildTree() => CatalogTreeBuilder.Build(Folders, InfoBases);
}
