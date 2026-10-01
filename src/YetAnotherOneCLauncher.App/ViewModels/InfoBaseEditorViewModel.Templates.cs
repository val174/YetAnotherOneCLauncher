using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Шаги формы добавления базы.</summary>
public enum EditorPage
{
    /// <summary>Выбор варианта: существующая, из шаблона, без конфигурации.</summary>
    Mode,

    /// <summary>Выбор шаблона (только для создания из шаблона).</summary>
    Template,

    /// <summary>Параметры базы.</summary>
    Form,
}

/// <summary>Узел дерева шаблонов: конфигурация или её версия.</summary>
public abstract class TemplateNodeViewModel : ObservableObject
{
    public abstract string Text { get; }

    public abstract string Details { get; }

    public abstract bool IsExpanded { get; set; }
}

/// <summary>Конфигурация (имя из дерева шаблонов 1С) — внутри её версии.</summary>
public sealed class TemplateGroupViewModel : TemplateNodeViewModel
{
    private bool _isExpanded;

    public TemplateGroupViewModel(string name, IReadOnlyList<TemplateItemViewModel> items, bool isExpanded)
    {
        Name = name;
        Items = items;
        _isExpanded = isExpanded;
    }

    public string Name { get; }

    public IReadOnlyList<TemplateItemViewModel> Items { get; }

    public override string Text => Name;

    public override string Details => Items.Count == 1 ? Items[0].Text : $"версий: {Items.Count}";

    public override bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>Версия конфигурации — сам шаблон.</summary>
public sealed class TemplateItemViewModel(ConfigurationTemplate template) : TemplateNodeViewModel
{
    public ConfigurationTemplate Template { get; } = template;

    public override string Text => Template.VersionText;

    public override string Details => Template.Contents;

    public IReadOnlyList<TemplateItemViewModel> Items { get; } = [];

    public override bool IsExpanded { get; set; }
}

/// <summary>Шаг «Шаблон»: дерево «конфигурация → версии», поиск, выбор файла, конфигурация или демонстрационная база.</summary>
public sealed partial class InfoBaseEditorViewModel
{
    private readonly List<ConfigurationTemplate> _templates = [];
    private string? _autoName;

    /// <summary>Найденные шаблоны (каталоги шаблонов 1С): задаёт главное окно.</summary>
    public IReadOnlyList<ConfigurationTemplate> FoundTemplates
    {
        init
        {
            _templates.AddRange(value);
            RebuildTemplateTree();
        }
    }

    /// <summary>Конфигурации, подходящие под поиск; внутри — версии, новые первыми.</summary>
    public ObservableCollection<TemplateGroupViewModel> TemplateGroups { get; } = [];

    public bool HasTemplates => _templates.Count > 0;

    /// <summary>Шаблоны есть, но под поиск не подошёл ни один.</summary>
    public bool ShowNoTemplateMatches => HasTemplates && TemplateGroups.Count == 0;

    /// <summary>Поиск по имени конфигурации и версии: все слова, без учёта регистра.</summary>
    [ObservableProperty]
    public partial string TemplateSearch { get; set; } = string.Empty;

    partial void OnTemplateSearchChanged(string value) => RebuildTemplateTree();

    /// <summary>Выделенный узел дерева: версия выбирает шаблон, конфигурация с одной версией — тоже.</summary>
    [ObservableProperty]
    public partial TemplateNodeViewModel? SelectedTemplateNode { get; set; }

    partial void OnSelectedTemplateNodeChanged(TemplateNodeViewModel? value)
    {
        SelectedTemplate = value switch
        {
            TemplateItemViewModel item => item.Template,
            TemplateGroupViewModel { Items.Count: 1 } group => group.Items[0].Template,
            _ => null,
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTemplateKindChoice), nameof(SelectedTemplateFile), nameof(SelectedTemplateText))]
    public partial ConfigurationTemplate? SelectedTemplate { get; set; }

    partial void OnSelectedTemplateChanged(ConfigurationTemplate? value)
    {
        if (value is not null)
        {
            Errors = string.Empty;
        }

        // Название по умолчанию — последний уровень дерева шаблонов («Бухгалтерия предприятия»), пока его не меняли.
        if (value is not null && (string.IsNullOrWhiteSpace(Name) || Name == _autoName))
        {
            Name = _autoName = value.Catalog.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        }
    }

    /// <summary>В шаблоне есть и конфигурация, и выгрузка — что создавать.</summary>
    public bool ShowTemplateKindChoice => SelectedTemplate?.HasBoth == true;

    /// <summary>Создать демонстрационную базу из выгрузки (.dt); иначе — базу с конфигурацией из .cf, без данных.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseConfigurationOnly), nameof(SelectedTemplateFile))]
    public partial bool UseDemoData { get; set; }

    public bool UseConfigurationOnly
    {
        get => !UseDemoData;
        set
        {
            if (value)
            {
                UseDemoData = false;
            }
        }
    }

    /// <summary>Файл, из которого будет создана база.</summary>
    public string? SelectedTemplateFile => SelectedTemplate switch
    {
        null => null,
        { HasBoth: true } both => UseDemoData ? both.DumpPath : both.ConfigurationPath,
        var single => single.Path,
    };

    /// <summary>Выбранный шаблон — для формы на следующем шаге.</summary>
    public string SelectedTemplateText => SelectedTemplate is null
        ? string.Empty
        : $"{SelectedTemplate} — {(SelectedTemplate.HasBoth ? (UseDemoData ? "демонстрационная база (.dt)" : "конфигурация (.cf)") : SelectedTemplate.Contents)}";

    partial void OnUseDemoDataChanged(bool value) => OnPropertyChanged(nameof(SelectedTemplateText));

    [RelayCommand]
    private async Task BrowseTemplateAsync()
    {
        if (await _files.OpenFileAsync("Шаблон информационной базы", "Конфигурация или выгрузка 1С", ["*.cf", "*.dt"]) is { } path)
        {
            var template = ConfigurationTemplate.FromFile(path);
            _templates.Insert(0, template);
            TemplateSearch = string.Empty;
            RebuildTemplateTree();
            SelectTemplate(template);
        }
    }

    /// <summary>Выделить шаблон в дереве (раскрыв его конфигурацию).</summary>
    public void SelectTemplate(ConfigurationTemplate template)
    {
        foreach (var group in TemplateGroups)
        {
            if (group.Items.FirstOrDefault(i => i.Template == template) is { } item)
            {
                group.IsExpanded = true;
                SelectedTemplateNode = item;
                return;
            }
        }

        SelectedTemplate = template;
    }

    private void RebuildTemplateTree()
    {
        var selected = SelectedTemplate;
        var words = TemplateSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searching = words.Length > 0;
        TemplateGroups.Clear();
        var groups = _templates
            .Where(t => words.All(w => t.Catalog.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                       || t.Version.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(t => t.Catalog, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in groups)
        {
            var items = group
                .OrderByDescending(t => System.Version.TryParse(t.Version, out var v) ? v : new System.Version())
                .Select(t => new TemplateItemViewModel(t))
                .ToList();
            // При поиске и для выбранного шаблона конфигурация раскрыта.
            var expanded = searching || (selected is not null && items.Exists(i => i.Template == selected));
            TemplateGroups.Add(new TemplateGroupViewModel(group.Key, items, expanded));
        }

        OnPropertyChanged(nameof(HasTemplates));
        OnPropertyChanged(nameof(ShowNoTemplateMatches));
        if (selected is not null && TemplateGroups.SelectMany(g => g.Items).FirstOrDefault(i => i.Template == selected) is { } node)
        {
            SelectedTemplateNode = node;
        }
    }
}
