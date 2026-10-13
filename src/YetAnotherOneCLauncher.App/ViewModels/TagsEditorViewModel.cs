using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка окна «Метки»: метка из каталога или новая.</summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    public TagRowViewModel(string? originalName, string name, TagColor color, bool showInRow, int count)
    {
        OriginalName = originalName;
        Name = name;
        Color = color;
        ShowInRow = showInRow;
        Count = count;
    }

    /// <summary>Имя в каталоге; <c>null</c> — метка создана в этом окне.</summary>
    public string? OriginalName { get; }

    /// <summary>Сколько баз с меткой.</summary>
    public int Count { get; }

    public string CountText => Count == 0 ? "—" : Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorIndex))]
    public partial TagColor Color { get; set; }

    /// <summary>Номер цвета в палитре (<see cref="TagsEditorViewModel.Colors"/>).</summary>
    public int ColorIndex
    {
        get => (int)Color;
        set
        {
            if (Enum.IsDefined((TagColor)value))
            {
                Color = (TagColor)value;
            }
        }
    }

    [ObservableProperty]
    public partial bool ShowInRow { get; set; }
}

/// <summary>Окно «Метки»: создать, переименовать, удалить метку, выбрать цвет и показ в строках.</summary>
public sealed partial class TagsEditorViewModel : ObservableObject
{
    private readonly List<string> _deleted = [];

    public TagsEditorViewModel(IEnumerable<TagDefinition> catalog, IReadOnlyDictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(counts);
        foreach (var tag in catalog)
        {
            Rows.Add(new TagRowViewModel(tag.Name, tag.Name, tag.Color, tag.ShowInRow, counts.GetValueOrDefault(tag.Name)));
        }

        SelectedRow = Rows.Count > 0 ? Rows[0] : null;
    }

    /// <summary>Цвета палитры в порядке <see cref="TagColor"/>.</summary>
    public static IReadOnlyList<TagColor> Colors { get; } = Enum.GetValues<TagColor>();

    public ObservableCollection<TagRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial TagRowViewModel? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    public bool HasRows => Rows.Count > 0;

    /// <summary>Что не так с наименованиями (пустое, повтор) — окно не закрывается.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    [RelayCommand]
    private void Create()
    {
        var number = 1;
        string name;
        do
        {
            name = number == 1 ? "Новая метка" : $"Новая метка {number}";
            number++;
        }
        while (Rows.Any(r => TagNames.Same(r.Name, name)));

        var color = Colors[1 + (Rows.Count % (Colors.Count - 1))];
        var row = new TagRowViewModel(null, name, color, showInRow: true, count: 0);
        Rows.Add(row);
        SelectedRow = row;
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        if (row.OriginalName is { } original)
        {
            _deleted.Add(original);
        }

        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>Проверка перед «OK».</summary>
    /// <returns><c>true</c> — можно сохранять.</returns>
    public bool Validate()
    {
        var seen = new HashSet<string>(TagNames.Comparer);
        foreach (var row in Rows)
        {
            if (TagNames.Normalize(row.Name) is not { } name)
            {
                SelectedRow = row;
                ErrorText = "Укажите наименование метки.";
                return false;
            }

            if (!seen.Add(name))
            {
                SelectedRow = row;
                ErrorText = $"Метка «{name}» уже есть.";
                return false;
            }
        }

        ErrorText = null;
        return true;
    }

    /// <summary>Переносит изменения в настройки: удаление, переименование, цвет, новые метки.</summary>
    public void ApplyTo(LauncherUserData userData)
    {
        ArgumentNullException.ThrowIfNull(userData);
        foreach (var name in _deleted)
        {
            userData.DeleteTag(name);
        }

        // Переименования — через временные имена: «А»→«Б» и «Б»→«А» не должны слиться.
        var renamed = Rows.Where(r => r.OriginalName is not null && !string.Equals(r.OriginalName, TagNames.Normalize(r.Name), StringComparison.Ordinal)).ToList();
        var temporary = new Dictionary<TagRowViewModel, string>();
        foreach (var row in renamed)
        {
            var name = $"\u0001{Guid.NewGuid():N}";
            userData.RenameTag(row.OriginalName!, name);
            temporary[row] = name;
        }

        foreach (var row in renamed)
        {
            userData.RenameTag(temporary[row], TagNames.Normalize(row.Name)!);
        }

        foreach (var row in Rows)
        {
            var name = TagNames.Normalize(row.Name)!;
            if (row.OriginalName is null)
            {
                userData.EnsureTag(name, row.Color);
            }

            userData.SetTagAppearance(name, row.Color, row.ShowInRow);
        }
    }
}
