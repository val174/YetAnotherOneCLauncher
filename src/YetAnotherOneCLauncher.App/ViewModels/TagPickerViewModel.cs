using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Выбор метки (кнопка «+» в свойствах базы, «+ Добавить» в форме базы): поле ввода и имеющиеся метки, которых у базы
/// ещё нет. Щелчок по метке — поставить её; Enter — поставить введённую (новая создаётся). При вводе список сужается.
/// </summary>
public sealed partial class TagPickerViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TagDefinition>> _catalog;
    private readonly Func<IReadOnlyCollection<string>> _assigned;
    private readonly Action<string> _pick;

    /// <param name="catalog">Все метки.</param>
    /// <param name="assigned">Метки, которые уже стоят: в списке их нет.</param>
    /// <param name="pick">Поставить метку (имя уже без лишних пробелов).</param>
    public TagPickerViewModel(Func<IReadOnlyList<TagDefinition>> catalog, Func<IReadOnlyCollection<string>> assigned, Action<string> pick)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _assigned = assigned ?? throw new ArgumentNullException(nameof(assigned));
        _pick = pick ?? throw new ArgumentNullException(nameof(pick));
    }

    /// <summary>Метку поставили — окно выбора можно закрыть.</summary>
    public event EventHandler? Picked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Available), nameof(HasAvailable), nameof(EnterTarget), nameof(CreateText), nameof(CanCreate), nameof(HasEnterHint))]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>Имеющиеся метки, которых ещё нет, — по наименованию; при вводе — содержащие введённое.</summary>
    public IReadOnlyList<TagChipViewModel> Available
    {
        get
        {
            var assigned = _assigned();
            var filter = TagNames.Normalize(Filter);
            return
            [
                .. _catalog()
                    .Where(t => !assigned.Contains(t.Name, TagNames.Comparer)
                                && (filter is null || t.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
                    .Select(t => new TagChipViewModel(t)),
            ];
        }
    }

    public bool HasAvailable => Available.Count > 0;

    /// <summary>Что сделает Enter: имя метки; <c>null</c> — ничего не введено (или введённая уже стоит).</summary>
    public string? EnterTarget
    {
        get
        {
            if (TagNames.Normalize(Filter) is not { } name)
            {
                return null;
            }

            if (_catalog().FirstOrDefault(t => TagNames.Same(t.Name, name)) is { } existing)
            {
                return _assigned().Contains(existing.Name, TagNames.Comparer) ? null : existing.Name;
            }

            // Подходящие имеющиеся — первая из них; таких нет — новая.
            return Available is [var first, ..] ? first.Name : name;
        }
    }

    /// <summary>Enter создаст новую метку.</summary>
    public bool CanCreate => EnterTarget is { } target && !_catalog().Any(t => TagNames.Same(t.Name, target));

    /// <summary>Подсказка под полем: что сделает Enter.</summary>
    public string CreateText => EnterTarget is not { } target
        ? string.Empty
        : CanCreate ? $"Enter — создать метку «{target}»" : $"Enter — поставить «{target}»";

    public bool HasEnterHint => EnterTarget is not null;

    /// <summary>Список заново (метки поставили или сняли в другом месте).</summary>
    public void Refresh()
    {
        Filter = string.Empty;
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(HasAvailable));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CreateText));
        OnPropertyChanged(nameof(EnterTarget));
        OnPropertyChanged(nameof(HasEnterHint));
    }

    /// <summary>Поставить метку: щелчок по метке из списка или Enter (<c>null</c> — введённая).</summary>
    [RelayCommand]
    private void Pick(string? name)
    {
        // Enter — подходящая имеющаяся или новая (EnterTarget); щелчок — выбранная.
        if (TagNames.Normalize(name ?? EnterTarget) is not { } tag)
        {
            return;
        }

        _pick(_catalog().FirstOrDefault(t => TagNames.Same(t.Name, tag))?.Name ?? tag);
        Refresh();
        Picked?.Invoke(this, EventArgs.Empty);
    }
}
