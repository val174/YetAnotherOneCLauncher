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

    /// <summary>Шаблоны, выбранные файлом («Выбрать файл…»): при обновлении списка остаются.</summary>
    private readonly List<ConfigurationTemplate> _browsedTemplates = [];
    private string? _autoName;
    private int _existenceCheck;

    /// <summary>Шаблоны из кэша (каталоги шаблонов 1С при последнем «Обновить»): задаёт главное окно.</summary>
    public IReadOnlyList<ConfigurationTemplate> FoundTemplates
    {
        init
        {
            _templates.AddRange(value);
            RebuildTemplateTree();
        }
    }

    /// <summary>
    /// «Обновить»: просмотр каталогов шаблонов в фоне (и запись в кэш) — задаёт главное окно; <c>null</c> — кнопки нет.
    /// </summary>
    public Func<Task<TemplateCacheData>>? TemplateRefresher { get; init; }

    /// <summary>Есть ли файл шаблона (проверка выбранного шаблона из кэша); подменяется в тестах.</summary>
    public Func<string, bool> TemplateFileExists { get; init; } = File.Exists;

    public bool CanRefreshTemplates => TemplateRefresher is not null;

    /// <summary>Когда список шаблонов обновляли; <c>null</c> — ни разу (кэша нет).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplatesStatusText), nameof(ShowTemplatesNeverLoaded), nameof(ShowNoTemplatesFound))]
    public partial DateTimeOffset? TemplatesUpdatedAt { get; set; }

    /// <summary>Каталоги шаблонов в 1cestart.cfg изменились после обновления списка.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplatesStatusText))]
    public partial bool TemplateRootsChanged { get; set; }

    /// <summary>Идёт обновление списка: вращающееся кольцо и «Выполняется обновление списка шаблонов».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTemplatesNeverLoaded), nameof(ShowNoTemplatesFound), nameof(ShowNoTemplateMatches))]
    [NotifyCanExecuteChangedFor(nameof(RefreshTemplatesCommand))]
    public partial bool IsRefreshingTemplates { get; set; }

    /// <summary>Строка под поиском: когда обновлялся список.</summary>
    public string TemplatesStatusText => TemplatesUpdatedAt is not { } at
        ? "Список шаблонов ещё не загружался."
        : $"Список обновлён {at.ToLocalTime():dd.MM.yyyy HH:mm}."
          + (TemplateRootsChanged ? " Каталоги шаблонов в настройках 1С изменились — обновите список." : string.Empty);

    /// <summary>Список ни разу не обновляли — подсказка нажать «Обновить».</summary>
    public bool ShowTemplatesNeverLoaded => !HasTemplates && TemplatesUpdatedAt is null && !IsRefreshingTemplates;

    /// <summary>Обновляли, но в каталогах шаблонов ничего нет.</summary>
    public bool ShowNoTemplatesFound => !HasTemplates && TemplatesUpdatedAt is not null && !IsRefreshingTemplates;

    /// <summary>Выбранного шаблона больше нет на диске (удалён или перенесён после обновления списка).</summary>
    [ObservableProperty]
    public partial bool IsSelectedTemplateMissing { get; set; }

    [RelayCommand(CanExecute = nameof(CanStartTemplatesRefresh))]
    private async Task RefreshTemplatesAsync()
    {
        if (TemplateRefresher is null)
        {
            return;
        }

        IsRefreshingTemplates = true;
        try
        {
            var data = await TemplateRefresher();
            var selectedPath = SelectedTemplate?.Path;
            _templates.Clear();
            _templates.AddRange(_browsedTemplates);
            _templates.AddRange(data.ToTemplates());
            TemplatesUpdatedAt = data.UpdatedAt;
            TemplateRootsChanged = false;
            RebuildTemplateTree();
            if (selectedPath is not null)
            {
                if (_templates.FirstOrDefault(t => string.Equals(t.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) is { } again)
                {
                    SelectTemplate(again);
                }
                else
                {
                    SelectedTemplateNode = null;
                    SelectedTemplate = null;
                }
            }
        }
        finally
        {
            IsRefreshingTemplates = false;
        }
    }

    private bool CanStartTemplatesRefresh() => TemplateRefresher is not null && !IsRefreshingTemplates;

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

        _ = CheckSelectedTemplateAsync();

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

    partial void OnUseDemoDataChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedTemplateText));
        _ = CheckSelectedTemplateAsync();
    }

    /// <summary>Сообщение «шаблона больше нет» — его же «Далее» показывает снова.</summary>
    public string MissingTemplateText { get; private set; } = string.Empty;

    /// <summary>
    /// Выбранный шаблон взят из кэша — файла могло уже не стать. Проверка в фоне (каталог шаблонов бывает в сети);
    /// нет файла — сообщение в форме, «Далее» не пускает.
    /// </summary>
    private async Task CheckSelectedTemplateAsync()
    {
        var check = ++_existenceCheck;
        IsSelectedTemplateMissing = false;
        if (SelectedTemplate is not { } template || SelectedTemplateFile is not { } file)
        {
            return;
        }

        var exists = TemplateFileExists;
        var found = await Task.Run(() => exists(file));
        if (check != _existenceCheck || found)
        {
            return;
        }

        MissingTemplateText = $"Шаблона «{template}» больше нет: не найден файл {file}. "
                              + (CanRefreshTemplates ? "Нажмите «Обновить», чтобы обновить список шаблонов, или выберите другой." : "Выберите другой шаблон.");
        IsSelectedTemplateMissing = true;
        Errors = MissingTemplateText;
    }

    [RelayCommand]
    private async Task BrowseTemplateAsync()
    {
        if (await _files.OpenFileAsync("Шаблон информационной базы", "Конфигурация или выгрузка 1С", ["*.cf", "*.dt"]) is { } path)
        {
            var template = ConfigurationTemplate.FromFile(path);
            _browsedTemplates.Insert(0, template);
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
        OnPropertyChanged(nameof(ShowTemplatesNeverLoaded));
        OnPropertyChanged(nameof(ShowNoTemplatesFound));
        if (selected is not null && TemplateGroups.SelectMany(g => g.Items).FirstOrDefault(i => i.Template == selected) is { } node)
        {
            SelectedTemplateNode = node;
        }
    }
}
