using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Edt;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Проекты 1C:EDT: группа «Проекты 1C:EDT» в дереве (рабочие области из EDT Start), открытие проекта в EDT и
/// связь базы с проектом (поле «Проект 1C:EDT» в форме базы, кнопка «1C:EDT» у базы).
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string EdtFolderKey = "edt:";
    private const string EdtProjectKeyPrefix = "edt:project:";

    private readonly IEdtProjects? _edt;
    private EdtCatalog _edtCatalog = EdtCatalog.Empty;

    /// <summary>Значок 1C:EDT для группы и кнопок; <c>null</c> — EDT не установлен (тогда — свой значок).</summary>
    public Bitmap? EdtIcon => _edt?.Icon;

    public bool HasEdtIcon => EdtIcon is not null;

    /// <summary>
    /// Проекты 1C:EDT включены (настройка «Общие» → «Использовать проекты 1C:EDT»): группа в дереве, поиск, режим
    /// «Проекты 1C:EDT», связь базы с проектом и кнопки EDT. Выключено — ничего этого нет.
    /// </summary>
    public bool ShowEdtProjects
    {
        get => _settings.Settings.Edt.ShowProjects;
        set
        {
            if (value == _settings.Settings.Edt.ShowProjects)
            {
                return;
            }

            _settings.Settings.Edt.ShowProjects = value;
            _settings.RequestSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEdtModeAvailable));
            OnPropertyChanged(nameof(ListFilterPosition));
            ShowEdtProjectsModeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsEdtEnabled));
            UpdateBaseEdtLinks();
            var key = CurrentSelectionKey();
            if (!value && IsEdtProjectsMode)
            {
                // Режима «Проекты 1C:EDT» больше нет; смена режима сама перестроит список.
                ListFilter = BaseListFilter.All;
                key = CurrentSelectionKey();
            }

            RebuildTree();
            RebuildList();
            Reselect(key);
        }
    }

    /// <summary>EDT доступен и не выключен в настройках.</summary>
    private bool EdtEnabled => _edt is not null && ShowEdtProjects;

    /// <summary>Для разметки: строка «Проект 1C:EDT» в свойствах базы и прочее, что есть только при включённых проектах.</summary>
    public bool IsEdtEnabled => EdtEnabled;

    /// <summary>
    /// «Проекты 1C:EDT» (горячая клавиша, по умолчанию Ctrl+Shift+E): включить режим; если он уже включён — вернуться ко всем базам.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsEdtModeAvailable))]
    private void ShowEdtProjectsMode() => ListFilter = IsEdtProjectsMode ? BaseListFilter.All : BaseListFilter.EdtProjects;

    /// <summary>Положение «Проекты 1C:EDT» в переключателе режимов: проекты включены и есть.</summary>
    public bool IsEdtModeAvailable => EdtEnabled && HasEdtProjects;

    /// <summary>Место выбранного режима в переключателе «Все базы / Проекты 1C:EDT / Недавние / Избранное» — для плашки.</summary>
    public int ListFilterPosition => ListFilter switch
    {
        BaseListFilter.All => 0,
        BaseListFilter.EdtProjects => 1,
        BaseListFilter.Recent => IsEdtModeAvailable ? 2 : 1,
        _ => IsEdtModeAvailable ? 3 : 2,
    };

    /// <summary>Есть ли проекты EDT на компьютере — для настройки «Показывать проекты 1C:EDT».</summary>
    public bool HasEdtProjects => !_edtCatalog.IsEmpty;

    /// <summary>Выделенный проект EDT (в дереве); тогда справа — его кнопки и свойства.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdtProjectSelected), nameof(IsBaseOrNothingSelected), nameof(ShowNoSelectionHint), nameof(PropertiesHeaderText),
        nameof(SelectedEdtProjectBases), nameof(SelectedEdtCommandLine), nameof(ShowOpenInEdtMenu))]
    [NotifyCanExecuteChangedFor(nameof(OpenInEdtCommand), nameof(OpenEdtWorkspaceFolderCommand), nameof(CopyEdtCommandLineCommand))]
    public partial EdtProjectNodeViewModel? SelectedEdtProject { get; private set; }

    public bool IsEdtProjectSelected => SelectedEdtProject is not null;

    public bool IsBaseOrNothingSelected => SelectedEdtProject is null;

    /// <summary>«Выберите базу» — ни базы, ни проекта.</summary>
    public bool ShowNoSelectionHint => SelectedInfoBase is null && SelectedEdtProject is null;

    public string PropertiesHeaderText => IsEdtProjectSelected ? "Свойства проекта 1C:EDT" : "Свойства информационной базы";

    /// <summary>Проект EDT выделенной базы (связь из формы базы); <c>null</c> — не привязан или проекта больше нет.</summary>
    public EdtProject? SelectedBaseEdtProject => EdtEnabled && SelectedInfoBase is { } infoBase ? EdtProjectOf(infoBase) : null;

    public bool HasSelectedBaseEdtProject => SelectedBaseEdtProject is not null;

    /// <summary>Пункт «Открыть в 1C:EDT» контекстного меню: у проекта и у базы, связанной с проектом.</summary>
    public bool ShowOpenInEdtMenu => IsEdtProjectSelected || HasSelectedBaseEdtProject;

    /// <summary>Кнопка у базы: «1C:EDT — RetailLime».</summary>
    public string SelectedBaseEdtButtonText => SelectedBaseEdtProject is { } project ? "1C:EDT — " + project.Name : string.Empty;

    /// <summary>Строка «Проект 1C:EDT» в свойствах базы.</summary>
    public string SelectedBaseEdtProjectText => SelectedBaseEdtProject?.Name ?? "не привязан";

    /// <summary>Базы проекта: привязанные в лаунчере и те, с которыми его связал сам EDT.</summary>
    public string SelectedEdtProjectBases
    {
        get
        {
            if (SelectedEdtProject?.Project is not { } project)
            {
                return string.Empty;
            }

            var bound = _edt?.InfobaseIds(project) ?? new HashSet<string>();
            var names = _bases
                .Where(b => SameProject(EdtProjectOf(b), project) || (b.InfoBase.Id is { } id && bound.Contains(id)))
                .Select(b => b.Name)
                .Distinct()
                .ToList();
            return names.Count == 0 ? "не привязаны" : string.Join(", ", names);
        }
    }

    /// <summary>Строка запуска выделенного проекта — в свойствах и для копирования.</summary>
    public string SelectedEdtCommandLine =>
        SelectedEdtProject is { Installation: { } installation } node
            ? EdtLaunch.CommandLine(installation.ExecutablePath, node.Project.Workspace, node.JavaPath)
            : string.Empty;

    public bool CanOpenEdtStart => _edt?.StarterPath is not null;

    /// <summary>Перечитать проекты EDT Start (вместе со списком баз).</summary>
    private void LoadEdtCatalog()
    {
        _edtCatalog = _edt?.Load() ?? EdtCatalog.Empty;
        OnPropertyChanged(nameof(HasEdtProjects));
        OnPropertyChanged(nameof(IsEdtModeAvailable));
        OnPropertyChanged(nameof(ListFilterPosition));
        ShowEdtProjectsModeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(EdtIcon));
        OnPropertyChanged(nameof(HasEdtIcon));
        NotifyBaseEdtLink();
    }

    /// <summary>Узлы проектов по названию — для группы в дереве и для плоского списка.</summary>
    private List<EdtProjectNodeViewModel> CreateEdtNodes()
    {
        if (_edt is null)
        {
            return [];
        }

        var nodes = new List<EdtProjectNodeViewModel>();
        foreach (var project in _edtCatalog.Projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var own = _edtCatalog.InstallationOf(project);
            var installation = own ?? ChosenInstallation(project) ?? _edtCatalog.Newest;
            nodes.Add(new EdtProjectNodeViewModel(project, installation, own is null, installation is null ? null : _edt.JavaFor(installation))
            {
                IsOpen = _edt.IsOpen(project),
            });
        }

        return nodes;
    }

    /// <summary>
    /// Проекты в плоский список: в режиме «Проекты 1C:EDT» — все (или найденные), в поиске «Всех баз» — найденные.
    /// Найден — каждое слово поиска есть в названии или в пути рабочей области.
    /// </summary>
    private void AddEdtListItems()
    {
        if (!EdtEnabled)
        {
            return;
        }

        var words = HasSearch ? SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [];
        foreach (var node in CreateEdtNodes())
        {
            if (words.All(w => node.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                               || node.Project.Workspace.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            {
                ListItems.Add(new EdtListItemViewModel(node, Segments(node.Name, Highlights(node.Name, words)), isStripe: ListItems.Count % 2 == 1));
            }
        }
    }

    /// <summary>Где в названии встретились слова поиска — для подсветки (без пересечений, по порядку).</summary>
    private static List<Core.Search.TextRange> Highlights(string text, IReadOnlyList<string> words)
    {
        var marked = new bool[text.Length];
        foreach (var word in words)
        {
            for (var at = text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase); at >= 0 && word.Length > 0;
                 at = text.IndexOf(word, at + word.Length, StringComparison.CurrentCultureIgnoreCase))
            {
                Array.Fill(marked, true, at, Math.Min(word.Length, text.Length - at));
            }
        }

        var ranges = new List<Core.Search.TextRange>();
        for (var i = 0; i < text.Length; i++)
        {
            if (marked[i] && (i == 0 || !marked[i - 1]))
            {
                var end = i;
                while (end < text.Length && marked[end])
                {
                    end++;
                }

                ranges.Add(new Core.Search.TextRange(i, end - i));
            }
        }

        return ranges;
    }

    /// <summary>Ключ строки списка — для восстановления выделения.</summary>
    private static string? ListItemKey(CatalogListItemViewModel item) => item switch
    {
        BaseListItemViewModel b => b.Base.InfoBase.IdentityKey,
        EdtListItemViewModel e => EdtProjectKeyPrefix + e.Project.Project.Id,
        _ => null,
    };

    /// <summary>У баз — проект для кнопки «1C:EDT» в строке (пусто, если не привязан или проекты выключены).</summary>
    private void UpdateBaseEdtLinks()
    {
        foreach (var infoBase in _bases)
        {
            infoBase.EdtProjectName = EdtEnabled ? EdtProjectOf(infoBase)?.Name : null;
        }

        NotifyBaseEdtLink();
    }

    /// <summary>Группа «Проекты 1C:EDT» для дерева; <c>null</c> — проектов нет или группа выключена.</summary>
    private FolderNodeViewModel? BuildEdtGroup(HashSet<string> collapsed)
    {
        if (!EdtEnabled || _edtCatalog.IsEmpty)
        {
            return null;
        }

        var group = new FolderNodeViewModel("Проекты 1C:EDT", EdtFolderKey, FolderKind.EdtProjects, !collapsed.Contains(EdtFolderKey), OnFolderExpansionChanged);
        foreach (var node in CreateEdtNodes())
        {
            group.Children.Add(node);
        }

        return group;
    }

    /// <summary>Отметить открытые проекты (по таймеру подсветки запущенных баз и после открытия проекта).</summary>
    internal void RefreshEdtOpenState()
    {
        if (_edt is null)
        {
            return;
        }

        var nodes = TreeItems.OfType<FolderNodeViewModel>().Where(f => f.IsEdtProjects).SelectMany(f => f.Children).OfType<EdtProjectNodeViewModel>()
            .Concat(ListItems.OfType<EdtListItemViewModel>().Select(i => i.Project));
        foreach (var node in nodes)
        {
            node.IsOpen = _edt.IsOpen(node.Project);
        }
    }

    /// <summary>
    /// «Открыть в 1C:EDT»: проект из параметра (кнопка в строке проекта), проект базы из параметра (кнопка в строке базы)
    /// или — без параметра — выделенный проект или проект выделенной базы.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenInEdt))]
    private async Task OpenInEdtAsync(object? target)
    {
        if (EdtTarget(target) is { } project)
        {
            await OpenEdtProjectAsync(project);
        }
    }

    private bool CanOpenInEdt(object? target) => EdtTarget(target) is not null;

    private EdtProject? EdtTarget(object? target) => !EdtEnabled ? null : target switch
    {
        EdtProjectNodeViewModel node => node.Project,
        InfoBaseViewModel infoBase => EdtProjectOf(infoBase),
        _ => SelectedEdtProject?.Project ?? SelectedBaseEdtProject,
    };

    /// <summary>
    /// Открыть проект в EDT: уже открыт — сказать об этом; версии проекта нет — спросить, в какой открыть
    /// (выбор запоминается), предупредив о конвертации.
    /// </summary>
    internal async Task OpenEdtProjectAsync(EdtProject project)
    {
        if (_edt is null)
        {
            return;
        }

        const string title = "1C:EDT";
        if (_edt.IsOpen(project))
        {
            await _dialogs.ShowMessageAsync(title, $"Проект «{project.Name}» уже открыт в 1C:EDT.");
            return;
        }

        var installation = _edtCatalog.InstallationOf(project) ?? ChosenInstallation(project);
        if (installation is null)
        {
            var installed = _edtCatalog.Installations.OrderByDescending(i => EdtVersions.SortKey(i.Version)).ToList();
            if (installed.Count == 0)
            {
                await _dialogs.ShowMessageAsync(title, "1C:EDT не найден: установите его через 1C:EDT Start.");
                return;
            }

            var choice = await _dialogs.ChooseAsync(
                title,
                $"Версии 1C:EDT, в которой создан проект «{project.Name}», на компьютере нет. В какой версии открыть? " +
                "EDT предложит сконвертировать проект — вернуть его в прежнюю версию будет нельзя.",
                [.. installed.Select(i => "1C:EDT " + i.Version)]);
            if (choice is not { } index)
            {
                return;
            }

            installation = installed[index];
            _settings.Settings.Edt.ProjectInstallations[project.Id] = installation.Id;
            _settings.RequestSave();
        }

        try
        {
            _processLauncher.OpenProgram(installation.ExecutablePath, EdtLaunch.Arguments(project.Workspace, _edt.JavaFor(installation)));
            StatusText = $"Проект «{project.Name}» открывается в 1C:EDT {installation.Version}.";
        }
        catch (LaunchFailedException ex)
        {
            await _dialogs.ShowMessageAsync(title, ex.Message);
        }
    }

    /// <summary>«1C:EDT Start» — открыть сам EDT Start.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenEdtStart))]
    private void OpenEdtStart()
    {
        if (_edt?.StarterPath is not { } starter)
        {
            return;
        }

        try
        {
            _processLauncher.StartProgram(starter);
        }
        catch (LaunchFailedException ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsEdtProjectSelected))]
    private void OpenEdtWorkspaceFolder()
    {
        if (SelectedEdtProject is not { } node)
        {
            return;
        }

        try
        {
            _processLauncher.OpenFolder(node.Project.Workspace);
        }
        catch (LaunchFailedException ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsEdtProjectSelected))]
    private async Task CopyEdtCommandLineAsync()
    {
        if (SelectedEdtCommandLine is { Length: > 0 } line)
        {
            await _clipboard.SetTextAsync(line);
            StatusText = "Строка запуска 1C:EDT скопирована.";
        }
    }

    /// <summary>Проекты EDT для поля «Проект 1C:EDT» в форме базы.</summary>
    internal IReadOnlyList<EdtProject> EdtProjectChoices => [.. _edtCatalog.Projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Проект, с которым сам EDT связал базу (по её ID) — подсказка «Привязать» в форме.</summary>
    internal EdtProject? SuggestedEdtProject(Core.Model.InfoBase? infoBase) =>
        _edt is null || infoBase?.Id is not { } id ? null : _edtCatalog.Projects.FirstOrDefault(p => _edt.InfobaseIds(p).Contains(id));

    /// <summary>Проект базы по связи из её профиля: по идентификатору, иначе — по рабочей области.</summary>
    internal EdtProject? EdtProjectOf(InfoBaseViewModel infoBase)
    {
        var profile = _settings.UserData.LaunchProfile(infoBase.InfoBase);
        if (profile?.EdtProjectId is null && profile?.EdtWorkspace is null)
        {
            return null;
        }

        return _edtCatalog.Projects.FirstOrDefault(p => string.Equals(p.Id, profile.EdtProjectId, StringComparison.OrdinalIgnoreCase))
               ?? _edtCatalog.Projects.FirstOrDefault(p => SamePath(p.Workspace, profile.EdtWorkspace));
    }

    /// <summary>Сохранить связь базы с проектом (из формы базы); <c>null</c> — отвязать.</summary>
    internal void SetEdtProject(InfoBaseViewModel infoBase, EdtProject? project)
    {
        var profile = _settings.UserData.LaunchProfile(infoBase.InfoBase) ?? new Core.Settings.InfoBaseLaunchProfile();
        _settings.UserData.SetLaunchProfile(infoBase.InfoBase, profile with { EdtProjectId = project?.Id, EdtWorkspace = project?.Workspace });
        _settings.RequestSave();
        UpdateBaseEdtLinks();
    }

    private void NotifyBaseEdtLink()
    {
        OnPropertyChanged(nameof(SelectedBaseEdtProject));
        OnPropertyChanged(nameof(HasSelectedBaseEdtProject));
        OnPropertyChanged(nameof(SelectedBaseEdtButtonText));
        OnPropertyChanged(nameof(SelectedBaseEdtProjectText));
        OnPropertyChanged(nameof(SelectedEdtProjectBases));
        OnPropertyChanged(nameof(ShowOpenInEdtMenu));
        OpenInEdtCommand.NotifyCanExecuteChanged();
    }

    private EdtInstallation? ChosenInstallation(EdtProject project) =>
        _settings.Settings.Edt.ProjectInstallations.TryGetValue(project.Id, out var id)
            ? _edtCatalog.Installations.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;

    private static bool SameProject(EdtProject? left, EdtProject right) =>
        left is not null && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(
            Path.TrimEndingDirectorySeparator(left.Trim()),
            Path.TrimEndingDirectorySeparator(right.Trim()),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static EdtProjectNodeViewModel? FindEdtNode(IEnumerable<TreeNodeViewModel> nodes, string projectId) =>
        nodes.OfType<FolderNodeViewModel>().Where(f => f.IsEdtProjects).SelectMany(f => f.Children).OfType<EdtProjectNodeViewModel>()
            .FirstOrDefault(n => string.Equals(n.Project.Id, projectId, StringComparison.OrdinalIgnoreCase));
}
