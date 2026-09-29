using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>Элемент дерева: папка или база.</summary>
public abstract class CatalogTreeItem
{
    public abstract string Name { get; }

    /// <summary>Порядок из <c>OrderInTree</c>; записи без порядка идут в конце.</summary>
    public abstract double? SortOrder { get; }
}

/// <summary>Узел-папка. Корень дерева — папка с путём "/".</summary>
public sealed class CatalogFolderNode : CatalogTreeItem
{
    private readonly string _name;

    internal CatalogFolderNode(string name, string path)
    {
        _name = name;
        Path = path;
    }

    public override string Name => _name;

    /// <summary>Полный путь папки: "/", "/Бухгалтерия", "/Бухгалтерия/Архив".</summary>
    public string Path { get; }

    /// <summary>Запись папки из списка. <c>null</c>, если папка выведена только из пути <c>Folder</c> у баз.</summary>
    public InfoBaseFolder? Folder { get; internal set; }

    public bool IsRoot => Path == FolderPaths.Root;

    public override double? SortOrder => Folder?.OrderInTree;

    /// <summary>Вложенные папки и базы вперемешку, отсортированные как в штатном стартере.</summary>
    public List<CatalogTreeItem> Items { get; } = [];

    public IEnumerable<CatalogFolderNode> SubFolders => Items.OfType<CatalogFolderNode>();

    public IEnumerable<InfoBase> InfoBases => Items.OfType<CatalogInfoBaseItem>().Select(i => i.InfoBase);

    /// <summary>Все базы в этой папке и во вложенных.</summary>
    public IEnumerable<InfoBase> DescendantInfoBases =>
        InfoBases.Concat(SubFolders.SelectMany(f => f.DescendantInfoBases));
}

/// <summary>Лист дерева — база.</summary>
public sealed class CatalogInfoBaseItem : CatalogTreeItem
{
    internal CatalogInfoBaseItem(InfoBase infoBase)
    {
        InfoBase = infoBase;
    }

    public InfoBase InfoBase { get; }

    public override string Name => InfoBase.Name;

    public override double? SortOrder => InfoBase.OrderInTree;
}

/// <summary>Строит дерево по путям <c>Folder</c>; недостающие промежуточные папки создаются автоматически.</summary>
/// <summary>Порядок записей в дереве.</summary>
public enum CatalogSortMode
{
    /// <summary>По наименованию: в каждой папке сначала вложенные папки, затем базы, по алфавиту.</summary>
    Name,

    /// <summary>Свой порядок из <c>OrderInTree</c> (его же показывает штатный стартер); записи без порядка — в конце по алфавиту.</summary>
    Custom,
}

public static class CatalogTreeBuilder
{
    public static CatalogFolderNode Build(
        IEnumerable<InfoBaseFolder> folders,
        IEnumerable<InfoBase> infoBases,
        CatalogSortMode sortMode = CatalogSortMode.Custom)
    {
        var root = new CatalogFolderNode(string.Empty, FolderPaths.Root);
        var nodes = new Dictionary<string, CatalogFolderNode>(StringComparer.OrdinalIgnoreCase)
        {
            [FolderPaths.Root] = root,
        };

        foreach (var folder in folders)
        {
            var node = EnsureNode(nodes, folder.FullPath);
            node.Folder ??= folder;
        }

        foreach (var infoBase in infoBases)
        {
            EnsureNode(nodes, infoBase.FolderPath).Items.Add(new CatalogInfoBaseItem(infoBase));
        }

        Sort(root, sortMode == CatalogSortMode.Name ? CompareByName : CompareItems);
        return root;
    }

    private static CatalogFolderNode EnsureNode(Dictionary<string, CatalogFolderNode> nodes, string path)
    {
        var normalized = FolderPaths.Normalize(path);
        if (nodes.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        var current = nodes[FolderPaths.Root];
        var currentPath = FolderPaths.Root;
        foreach (var segment in FolderPaths.Split(normalized))
        {
            currentPath = FolderPaths.Combine(currentPath, segment);
            if (!nodes.TryGetValue(currentPath, out var child))
            {
                child = new CatalogFolderNode(segment, currentPath);
                nodes[currentPath] = child;
                current.Items.Add(child);
            }

            current = child;
        }

        return current;
    }

    private static void Sort(CatalogFolderNode node, Comparison<CatalogTreeItem> comparison)
    {
        node.Items.Sort(comparison);
        foreach (var child in node.SubFolders)
        {
            Sort(child, comparison);
        }
    }

    /// <summary>Сначала папки, затем базы; внутри — по алфавиту. Так же нумерует <see cref="Editing.PersonalListEditor.SortByName"/>.</summary>
    public static int CompareByName(CatalogTreeItem a, CatalogTreeItem b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var byKind = (a is CatalogFolderNode ? 0 : 1).CompareTo(b is CatalogFolderNode ? 0 : 1);
        if (byKind != 0)
        {
            return byKind;
        }

        var byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
        return byName != 0 ? byName : CompareItems(a, b); // одинаковые имена — в своём порядке, стабильно
    }

    private static int CompareItems(CatalogTreeItem a, CatalogTreeItem b)
    {
        var byOrder = (a.SortOrder ?? double.MaxValue).CompareTo(b.SortOrder ?? double.MaxValue);
        return byOrder != 0
            ? byOrder
            : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
    }
}
