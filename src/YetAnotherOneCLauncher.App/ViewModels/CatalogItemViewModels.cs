using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Узел дерева: папка или база.</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    public abstract string Name { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Чётная видимая строка дерева (вторая, четвёртая…) — на подложке, если чередование включено.</summary>
    [ObservableProperty]
    public partial bool IsStripe { get; set; }
}

public enum FolderKind
{
    Regular,
    Favorites,

    /// <summary>«Проекты 1C:EDT»: рабочие области из EDT Start.</summary>
    EdtProjects,
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

    // Значок папки выбирается в шаблоне дерева: обычная папка или звезда «Избранного».
    public bool IsRegularFolder => Kind == FolderKind.Regular;

    public bool IsFavorites => Kind == FolderKind.Favorites;

    public bool IsEdtProjects => Kind == FolderKind.EdtProjects;

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

/// <summary>Строка плоского списка и результатов поиска: база или проект 1C:EDT.</summary>
public abstract class CatalogListItemViewModel(IReadOnlyList<TextSegment> nameSegments, bool isStripe)
{
    /// <summary>Чётная строка списка (вторая, четвёртая…) — на подложке, если чередование включено.</summary>
    public bool IsStripe { get; } = isStripe;

    public IReadOnlyList<TextSegment> NameSegments { get; } = nameSegments;
}

/// <summary>База в плоском списке.</summary>
public sealed class BaseListItemViewModel(InfoBaseViewModel infoBase, IReadOnlyList<TextSegment> nameSegments, bool isStripe = false)
    : CatalogListItemViewModel(nameSegments, isStripe)
{
    public InfoBaseViewModel Base { get; } = infoBase;
}

/// <summary>Проект 1C:EDT в плоском списке (поиск, режим «Проекты 1C:EDT»).</summary>
public sealed class EdtListItemViewModel(EdtProjectNodeViewModel project, IReadOnlyList<TextSegment> nameSegments, bool isStripe = false)
    : CatalogListItemViewModel(nameSegments, isStripe)
{
    public EdtProjectNodeViewModel Project { get; } = project;
}

/// <summary>Проект 1C:EDT (рабочая область из EDT Start) в группе «Проекты 1C:EDT».</summary>
public sealed partial class EdtProjectNodeViewModel : TreeNodeViewModel
{
    public EdtProjectNodeViewModel(Core.Edt.EdtProject project, Core.Edt.EdtInstallation? installation, bool isVersionMissing, string? javaPath)
    {
        Project = project;
        Installation = installation;
        IsVersionMissing = isVersionMissing;
        JavaPath = javaPath;
    }

    public Core.Edt.EdtProject Project { get; }

    /// <summary>Чем открывать: версия проекта, выбранная пользователем замена или самая новая.</summary>
    public Core.Edt.EdtInstallation? Installation { get; }

    /// <summary>Версии проекта на компьютере нет — откроется в <see cref="Installation"/> (с конвертацией).</summary>
    public bool IsVersionMissing { get; }

    public string? JavaPath { get; }

    public override string Name => Project.Name;

    /// <summary>Колонка «Платформа»: «EDT 2025.1».</summary>
    public string VersionText => Installation is null ? "EDT не найден" : "EDT " + Installation.Version;

    public string VersionToolTip => IsVersionMissing
        ? $"Версии EDT, в которой создан проект, на компьютере нет — откроется в {Installation?.Version ?? "?"} (проект будет сконвертирован)."
        : $"Открывается в 1C:EDT {Installation?.Version}";

    /// <summary>Java в свойствах проекта: каталог JDK («axiom-jdk-full-17.0.16+12-x86_64»), полный путь — в подсказке.</summary>
    public string JavaDisplay => JavaPath is null ? "EDT найдёт сам" : JdkDirectory(JavaPath);

    /// <summary>Колонка «Режим»: «Java 17» — какая Java передаётся EDT.</summary>
    public string JavaText => JavaPath is null ? "Java —" : "Java " + JavaMajor(JavaPath);

    /// <summary>Рабочая область открыта в EDT — зелёная точка, как у запущенной базы.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsOpen { get; set; }

    private static string JavaMajor(string javaPath)
    {
        // …\axiom-jdk-full-17.0.16+12-x86_64\bin\javaw.exe → 17
        var match = System.Text.RegularExpressions.Regex.Match(JdkDirectory(javaPath), @"jdk(-full)?-(\d+)");
        return match.Success ? match.Groups[2].Value : "?";
    }

    /// <summary>
    /// Каталог JDK — на два уровня выше <c>bin\javaw.exe</c>. Разделители обоих видов: путь мог прийти из Windows,
    /// а Path.GetDirectoryName в Linux «\» за разделитель не считает.
    /// </summary>
    private static string JdkDirectory(string javaPath)
    {
        var parts = javaPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[^3] : javaPath;
    }
}
