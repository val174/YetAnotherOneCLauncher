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

    /// <summary>Показывать группу «Проекты 1C:EDT» (настройка «Общие»).</summary>
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
            if (ShowTree)
            {
                var key = CurrentSelectionKey();
                RebuildTree();
                Reselect(key);
            }
        }
    }

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
    public EdtProject? SelectedBaseEdtProject => SelectedInfoBase is { } infoBase ? EdtProjectOf(infoBase) : null;

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
        OnPropertyChanged(nameof(EdtIcon));
        OnPropertyChanged(nameof(HasEdtIcon));
        NotifyBaseEdtLink();
    }

    /// <summary>Группа «Проекты 1C:EDT» для дерева; <c>null</c> — проектов нет или группа выключена.</summary>
    private FolderNodeViewModel? BuildEdtGroup(HashSet<string> collapsed)
    {
        if (_edt is null || _edtCatalog.IsEmpty || !ShowEdtProjects)
        {
            return null;
        }

        var group = new FolderNodeViewModel("Проекты 1C:EDT", EdtFolderKey, FolderKind.EdtProjects, !collapsed.Contains(EdtFolderKey), OnFolderExpansionChanged);
        foreach (var project in _edtCatalog.Projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var own = _edtCatalog.InstallationOf(project);
            var installation = own ?? ChosenInstallation(project) ?? _edtCatalog.Newest;
            group.Children.Add(new EdtProjectNodeViewModel(project, installation, own is null, installation is null ? null : _edt.JavaFor(installation))
            {
                IsOpen = _edt.IsOpen(project),
            });
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

        foreach (var node in TreeItems.OfType<FolderNodeViewModel>().Where(f => f.IsEdtProjects).SelectMany(f => f.Children).OfType<EdtProjectNodeViewModel>())
        {
            node.IsOpen = _edt.IsOpen(node.Project);
        }
    }

    /// <summary>«Открыть в 1C:EDT»: выделенный проект или проект выделенной базы.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenInEdt))]
    private async Task OpenInEdtAsync()
    {
        if ((SelectedEdtProject?.Project ?? SelectedBaseEdtProject) is { } project)
        {
            await OpenEdtProjectAsync(project);
        }
    }

    private bool CanOpenInEdt() => _edt is not null && (SelectedEdtProject is not null || SelectedBaseEdtProject is not null);

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
        NotifyBaseEdtLink();
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
