using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Узел дерева: папка или база.</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    public abstract string Name { get; }

    public virtual string Icon => string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

public enum FolderKind
{
    Regular,
    Favorites,
    Recent,
}

public sealed class FolderNodeViewModel : TreeNodeViewModel
{
    private readonly Action<FolderNodeViewModel>? _expansionChanged;

    public FolderNodeViewModel(string name, string path, FolderKind kind, bool isExpanded, Action<FolderNodeViewModel>? expansionChanged = null)
    {
        Name = name;
        Path = path;
        Kind = kind;
        IsExpanded = isExpanded;
        _expansionChanged = expansionChanged;
    }

    public override string Name { get; }

    /// <summary>Полный путь папки из списка баз; у специальных папок — условный.</summary>
    public string Path { get; }

    public FolderKind Kind { get; }

    /// <summary>Запись папки в списке; <c>null</c> — папка есть только в путях баз (или специальная).</summary>
    public InfoBaseFolder? Record { get; init; }

    /// <summary>Папку можно переименовать, удалить, перемещать: она из личного списка.</summary>
    public bool IsEditable { get; init; }

    public override string Icon => Kind switch
    {
        FolderKind.Favorites => "★",
        FolderKind.Recent => "🕘",
        _ => "📁",
    };

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsExpanded))
        {
            _expansionChanged?.Invoke(this);
        }
    }
}

/// <summary>База в дереве. Одна и та же база может быть и в папке, и в «Избранном».</summary>
public sealed class BaseNodeViewModel : TreeNodeViewModel
{
    public BaseNodeViewModel(InfoBaseViewModel infoBase)
    {
        Base = infoBase;
    }

    public InfoBaseViewModel Base { get; }

    public override string Name => Base.Name;
}

/// <summary>Кусок текста для подсветки совпадений.</summary>
public sealed record TextSegment(string Text, bool IsMatch);

/// <summary>Строка плоского списка и результатов поиска.</summary>
public sealed class BaseListItemViewModel
{
    public BaseListItemViewModel(InfoBaseViewModel infoBase, IReadOnlyList<TextSegment> nameSegments)
    {
        Base = infoBase;
        NameSegments = nameSegments;
    }

    public InfoBaseViewModel Base { get; }

    public IReadOnlyList<TextSegment> NameSegments { get; }
}
