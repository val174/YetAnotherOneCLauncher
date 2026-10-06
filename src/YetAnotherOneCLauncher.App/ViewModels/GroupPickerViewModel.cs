using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Окно «Выбор группы» из формы базы: дерево существующих групп (папок списка) и создание новой.
/// Новая группа только добавляется в дерево — в списке баз она появится вместе с сохранённой базой.
/// </summary>
public sealed partial class GroupPickerViewModel : ObservableObject
{
    /// <summary>Подпись «группа не выбрана» — база в корне списка.</summary>
    public const string NoGroupText = "Не выбрана";

    private readonly Func<string, Task<string?>>? _askName;

    /// <param name="folders">Пути существующих групп («/Рабочие/Отчёты»); родительские группы добавляются сами.</param>
    /// <param name="selected">Текущая группа базы.</param>
    /// <param name="askName">Запрос имени новой группы (получает путь родительской); <c>null</c> — создавать нельзя.</param>
    public GroupPickerViewModel(IEnumerable<string> folders, string? selected, Func<string, Task<string?>>? askName = null)
    {
        _askName = askName;
        NoGroup = new GroupNodeViewModel(NoGroupText, FolderPaths.Root);
        Groups = [NoGroup];
        foreach (var path in folders)
        {
            Ensure(path);
        }

        var current = FolderPaths.Normalize(selected);
        SelectedGroup = current == FolderPaths.Root ? NoGroup : Ensure(current);
    }

    /// <summary>Верхний уровень: «Не выбрана» и группы в корне списка.</summary>
    public ObservableCollection<GroupNodeViewModel> Groups { get; }

    public GroupNodeViewModel NoGroup { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPath))]
    public partial GroupNodeViewModel? SelectedGroup { get; set; }

    /// <summary>Выбранная группа; ничего не выделено — корень.</summary>
    public string SelectedPath => SelectedGroup?.Path ?? FolderPaths.Root;

    public bool CanCreate => _askName is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = string.Empty;

    public bool HasError => Error.Length > 0;

    /// <summary>Новая группа — внутри выделенной (или в корне); созданная выделяется.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateGroupAsync()
    {
        var parent = SelectedPath;
        if (await _askName!(parent) is not { } input)
        {
            return;
        }

        var name = input.Trim();
        Error = name.Length == 0 ? "Имя группы не может быть пустым."
            : name.Contains('/', StringComparison.Ordinal) ? "Имя группы не может содержать «/»."
            : string.Empty;
        if (!HasError)
        {
            SelectedGroup = Ensure(FolderPaths.Combine(parent, name));
        }
    }

    /// <summary>Узел группы по пути: найденный или созданный вместе с родительскими; родители раскрываются.</summary>
    private GroupNodeViewModel Ensure(string path)
    {
        GroupNodeViewModel? parent = null;
        var current = FolderPaths.Root;
        foreach (var segment in FolderPaths.Split(path))
        {
            current = FolderPaths.Combine(current, segment);
            var siblings = parent?.Children ?? Groups;
            var node = siblings.FirstOrDefault(g => g != NoGroup && string.Equals(g.Path, current, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                node = new GroupNodeViewModel(segment, current);
                // По имени; «Не выбрана» всегда первая.
                var index = siblings.TakeWhile(g => g == NoGroup || StringComparer.CurrentCultureIgnoreCase.Compare(g.Name, segment) < 0).Count();
                siblings.Insert(index, node);
            }

            parent?.IsExpanded = true;
            parent = node;
        }

        return parent ?? NoGroup;
    }
}

/// <summary>Группа в окне выбора группы.</summary>
public sealed partial class GroupNodeViewModel(string name, string path) : ObservableObject
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public bool IsNoGroup => Path == FolderPaths.Root;

    public ObservableCollection<GroupNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
