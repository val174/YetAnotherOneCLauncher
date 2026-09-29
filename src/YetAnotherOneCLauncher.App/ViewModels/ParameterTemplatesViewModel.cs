using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка таблицы параметров: встроенный или свой (свой можно удалить).</summary>
public sealed class ParameterTemplateRow
{
    public ParameterTemplateRow(ParameterTemplate template, bool isCustom)
    {
        Template = template;
        IsCustom = isCustom;
    }

    public ParameterTemplate Template { get; }

    public bool IsCustom { get; }

    public string Parameter => Template.Text;

    public string Description => ParameterLibrary.DescriptionOf(Template);
}

/// <summary>
/// Окно «Свои шаблоны параметров»: таблица известных параметров (свои — сверху), добавление своего
/// (параметр и описание) и удаление своих. Изменения применяются кнопкой «Сохранить».
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
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial ParameterTemplateRow? SelectedRow { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewParameter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewDescription { get; set; } = string.Empty;

    /// <summary>Почему параметр не добавлен (повтор); пусто — всё в порядке.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    partial void OnNewParameterChanged(string value) => ErrorText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var parameter = NewParameter.Trim();
        if (Rows.FirstOrDefault(r => string.Equals(r.Parameter, parameter, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            ErrorText = existing.IsCustom ? "Такой параметр уже добавлен." : "Такой параметр уже есть среди встроенных.";
            SelectedRow = existing;
            return;
        }

        // Свои — после уже добавленных своих, перед встроенными.
        var row = new ParameterTemplateRow(ParameterLibrary.Custom(parameter, NewDescription), isCustom: true);
        Rows.Insert(Rows.Count(r => r.IsCustom), row);
        SelectedRow = row;
        NewParameter = string.Empty;
        NewDescription = string.Empty;
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewParameter);

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

    private bool CanDelete() => SelectedRow?.IsCustom == true;
}
