using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка таблицы параметров: встроенный или свой (свой можно изменить и удалить).</summary>
public sealed class ParameterTemplateRow
{
    public ParameterTemplateRow(ParameterTemplate template, bool isCustom)
    {
        Template = template;
        IsCustom = isCustom;
    }

    public ParameterTemplate Template { get; }

    public bool IsCustom { get; }

    /// <summary>Без «/»: он подставляется только при вставке в параметры запуска.</summary>
    public string Parameter => Template.Key;

    public string Description => ParameterLibrary.DescriptionOf(Template);
}

/// <summary>
/// Окно «Свои шаблоны параметров»: таблица известных параметров (свои — сверху), добавление своего
/// (параметр и описание), изменение и удаление своих. Изменения применяются кнопкой «Сохранить».
/// </summary>
public sealed partial class ParameterTemplatesViewModel : ObservableObject
{
    public ParameterTemplatesViewModel(IEnumerable<ParameterTemplate> custom)
    {
        ArgumentNullException.ThrowIfNull(custom);
        foreach (var template in custom)
        {
            Rows.Add(new ParameterTemplateRow(template, isCustom: true));
        }

        foreach (var template in ParameterLibrary.BuiltIn)
        {
            Rows.Add(new ParameterTemplateRow(template, isCustom: false));
        }
    }

    public ObservableCollection<ParameterTemplateRow> Rows { get; } = [];

    /// <summary>Свои шаблоны в текущем порядке — результат окна.</summary>
    public List<ParameterTemplate> CustomTemplates => Rows.Where(r => r.IsCustom).Select(r => r.Template).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(EditCommand))]
    public partial ParameterTemplateRow? SelectedRow { get; set; }

    /// <summary>Свой параметр, который сейчас изменяется в полях ввода; <c>null</c> — поля для нового.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(FormTitle), nameof(ApplyText))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial ParameterTemplateRow? EditingRow { get; set; }

    public bool IsEditing => EditingRow is not null;

    public string FormTitle => IsEditing ? "Изменение параметра" : "Новый параметр";

    public string ApplyText => IsEditing ? "Применить" : "Добавить";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string NewParameter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewDescription { get; set; } = string.Empty;

    /// <summary>Почему параметр не принят (повтор); пусто — всё в порядке.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    partial void OnNewParameterChanged(string value) => ErrorText = string.Empty;

    /// <summary>Добавить новый параметр или применить изменение выбранного.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var key = ParameterLibrary.NormalizeKey(NewParameter);
        var duplicate = Rows.FirstOrDefault(r => r != EditingRow && string.Equals(r.Parameter, key, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            ErrorText = duplicate.IsCustom ? "Такой параметр уже добавлен." : "Такой параметр уже есть среди встроенных.";
            if (!IsEditing)
            {
                SelectedRow = duplicate;
            }

            return;
        }

        var row = new ParameterTemplateRow(ParameterLibrary.Custom(key, NewDescription), isCustom: true);
        if (EditingRow is { } editing)
        {
            Rows[Rows.IndexOf(editing)] = row; // на прежнем месте
        }
        else
        {
            Rows.Insert(Rows.Count(r => r.IsCustom), row); // после своих, перед встроенными
        }

        SelectedRow = row;
        ClearForm();
    }

    private bool CanApply() => ParameterLibrary.NormalizeKey(NewParameter).Length > 0;

    /// <summary>Выбранный свой параметр — в поля ввода для изменения.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSelected))]
    private void Edit()
    {
        if (SelectedRow is not { IsCustom: true } row)
        {
            return;
        }

        EditingRow = row;
        NewParameter = row.Parameter;
        NewDescription = row.Description;
    }

    /// <summary>Отказаться от изменения: поля снова для нового параметра.</summary>
    [RelayCommand]
    private void CancelEdit() => ClearForm();

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedRow is not { IsCustom: true } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        // Выделение — на соседний свой параметр, чтобы удалять подряд.
        SelectedRow = Rows.Where(r => r.IsCustom).ElementAtOrDefault(Math.Min(index, Rows.Count(r => r.IsCustom) - 1));
    }

    private bool CanChangeSelected() => SelectedRow?.IsCustom == true;

    // Изменяемый сейчас не удаляется: сначала применить или отменить изменение.
    private bool CanDelete() => CanChangeSelected() && SelectedRow != EditingRow;

    private void ClearForm()
    {
        EditingRow = null;
        NewParameter = string.Empty;
        NewDescription = string.Empty;
        ErrorText = string.Empty;
    }
}
