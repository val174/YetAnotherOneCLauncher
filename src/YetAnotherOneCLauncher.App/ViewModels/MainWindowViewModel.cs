using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Availability;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Core.Search;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Вариант выбора версии платформы для базы; <see cref="Version"/> = <c>null</c> — как в списке.</summary>
public sealed record PlatformChoice(string Label, string? Version)
{
    public override string ToString() => Label;
}

/// <summary>
/// Главное окно: дерево или список баз, быстрый поиск, избранное и недавние, подробности и запуск.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>Сколько баз показывать в «Недавних».</summary>
    public const int RecentCount = 10;

    private const string FolderKeyPrefix = "folder:";
    private const string FavoritesFolderKey = ":favorites";
    // Надбавки меньше разрыва между «начало имени» и «начало слова» (20): точность совпадения важнее.
    private const int FavoriteSearchBoost = 12;
    private const int MaxUsageSearchBoost = 5;

    private static readonly PlatformChoice AsInListChoice = new("Как в списке баз", null);

    private readonly InfoBaseCatalogLoader _loader;
    private readonly LaunchCoordinator _launcher;
    private readonly UserSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IWindowService _window;
    private readonly IThemeService _theme;
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger _logger;
    private readonly IPlatformPaths? _paths;
    private readonly IPlatformLocator? _locator;
    private readonly IFileDialogService _files;
    private readonly PersonalListStore? _store;
    private readonly IListChangeWatcher? _watcher;
    private readonly ICredentialStore? _credentials;
    private readonly ICacheUsageProbe? _cacheUsage;
    private readonly AvailabilityChecker? _availabilityChecker;
    private readonly IJumpList? _jumpList;
    private readonly ILaunchRequestChannel? _launchChannel;
    private readonly IClusterConsole _clusterConsole;
    private readonly StartupCatalog? _startupCatalog;
    private readonly SynchronizationContext? _uiContext;

    private readonly List<InfoBaseViewModel> _bases = [];
    private InfoBaseCatalog? _catalog;
    private IReadOnlyList<PlatformInstallation> _installations = [];
    private IReadOnlyList<string> _platformWarnings = [];
    private bool _watching;
    private string? _starterDefaultVersion;
    private bool _suppressSettingsSync = true;

    public MainWindowViewModel(
        InfoBaseCatalogLoader loader,
        LaunchCoordinator launcher,
        UserSettingsService settings,
        IDialogService dialogs,
        IClipboardService clipboard,
        IWindowService window,
        IThemeService theme,
        IProcessLauncher processLauncher,
        IFileDialogService files,
        ILogger<MainWindowViewModel> logger,
        IPlatformPaths? paths = null,
        IPlatformLocator? locator = null,
        PersonalListStore? store = null,
        IListChangeWatcher? watcher = null,
        ICredentialStore? credentials = null,
        ICacheUsageProbe? cacheUsage = null,
        AvailabilityChecker? availabilityChecker = null,
        IJumpList? jumpList = null,
        ILaunchRequestChannel? launchChannel = null,
        StartupOptions? startup = null,
        IClusterConsole? clusterConsole = null,
        StartupCatalog? startupCatalog = null)
    {
        _files = files;
        _store = store;
        _watcher = watcher;
        _credentials = credentials;
        _cacheUsage = cacheUsage;
        _availabilityChecker = availabilityChecker;
        _jumpList = jumpList;
        _launchChannel = launchChannel;
        _clusterConsole = clusterConsole ?? new NoClusterConsole();
        _startupCatalog = startupCatalog;
        _pendingLaunchKey = startup?.LaunchIdentityKey;
        _uiContext = SynchronizationContext.Current;
        _loader = loader;
        _launcher = launcher;
        _settings = settings;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _window = window;
        _theme = theme;
        _processLauncher = processLauncher;
        _logger = logger;
        _paths = paths;
        _locator = locator;

        var ui = settings.Settings.Ui;
        IsTreeMode = ui.ViewMode == CatalogViewMode.Tree;
        IsSortedByName = ui.SortMode != CatalogSortMode.Custom;
        ThemeIndex = (int)ui.Theme;
        AfterLaunchIndex = (int)ui.AfterLaunch;
        SingleInstance = ui.SingleInstance;
        MinimizeToTray = ui.MinimizeToTray;
        IconStyleIndex = Math.Max(0, Array.IndexOf(IconStyles, ui.IconStyle));
        ShowDetails = ui.ShowDetails;
        ShowRowLaunchButtons = ui.ShowRowLaunchButtons;
        ShowSideLaunchButtons = ui.ShowSideLaunchButtons;
        UseThickClientForFileBases = settings.Settings.Launch.UseThickClientForFileBasesByDefault;
        CheckAvailability = settings.Settings.Network.CheckAvailability;
        PuskUrl = settings.Settings.Network.PuskUrl ?? string.Empty;
        HotKeys = HotKeyMap.FromSettings(ui.HotKeys);
        _suppressSettingsSync = false;
    }

    public ObservableCollection<TreeNodeViewModel> TreeItems { get; } = [];

    public ObservableCollection<BaseListItemViewModel> ListItems { get; } = [];

    public IReadOnlyList<string> ThemeNames { get; } = ["Как в системе", "Светлая", "Тёмная"];

    /// <summary>Стили значков в порядке списка «Стиль значков».</summary>
    private static readonly IconStyle[] IconStyles = [IconStyle.Outline, IconStyle.Plate];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconStyle))]
    public partial int IconStyleIndex { get; set; }

    /// <summary>Стиль значков окна: наследуется всеми значками (<see cref="Controls.ToolIcon"/>).</summary>
    public IconStyle IconStyle => IconStyleIndex >= 0 && IconStyleIndex < IconStyles.Length ? IconStyles[IconStyleIndex] : IconStyle.Outline;

    partial void OnIconStyleIndexChanged(int value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.IconStyle = IconStyle;
        _settings.RequestSave();
    }

    /// <summary>Все загруженные базы.</summary>
    public IReadOnlyList<InfoBaseViewModel> InfoBases => _bases;

    public const double MinDetailsWidth = 260;
    public const double MaxDetailsWidth = 700;

    /// <summary>Ширина панели подробностей: задаёт разделитель, запоминается в настройках.</summary>
    public double DetailsWidth
    {
        get => Math.Clamp(
            double.IsFinite(_settings.Settings.Ui.DetailsWidth) ? _settings.Settings.Ui.DetailsWidth : UiSettings.DefaultDetailsWidth,
            MinDetailsWidth,
            MaxDetailsWidth);
        set
        {
            var width = Math.Round(Math.Clamp(value, MinDetailsWidth, MaxDetailsWidth));
            if (width != _settings.Settings.Ui.DetailsWidth)
            {
                _settings.Settings.Ui.DetailsWidth = width;
                _settings.RequestSave();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Положение окна: читает и пишет представление.</summary>
    public WindowPlacement? WindowPlacement
    {
        get => _settings.Settings.Ui.Window;
        set
        {
            _settings.Settings.Ui.Window = value;
            _settings.RequestSave();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearch), nameof(ShowTree), nameof(ShowList), nameof(EmptyListText))]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTree), nameof(ShowList))]
    public partial bool IsTreeMode { get; set; }

    /// <summary>
    /// Показаны только недавние базы (кнопка с часами): плоский список в порядке запусков, поиск — среди них.
    /// Добавлять, удалять и переставлять записи здесь нельзя; остальное — как в общем списке.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTree), nameof(ShowList), nameof(IsAllBasesMode), nameof(CanEditList), nameof(EmptyListText))]
    [NotifyCanExecuteChangedFor(
        nameof(AddBaseCommand), nameof(AddFolderCommand), nameof(ImportCommand), nameof(DeleteCommand),
        nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(SortFolderByNameCommand), nameof(ToggleViewModeCommand), nameof(ToggleSortCommand))]
    public partial bool IsRecentMode { get; set; }

    public bool IsAllBasesMode => !IsRecentMode;

    /// <summary>Дерево: по наименованию (по умолчанию) или свой порядок из списка баз.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortToolTip))]
    public partial bool IsSortedByName { get; set; }

    public string SortToolTip => IsSortedByName
        ? "Упорядочено по наименованию. Нажмите — свой порядок из списка баз. "
          + "Если переставить базу вручную, этот порядок сохранится в списке баз."
        : "Свой порядок из списка баз. Нажмите — упорядочить по наименованию.";

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Дерево показывается только без поиска и не для недавних; результаты поиска — всегда списком.</summary>
    public bool ShowTree => IsTreeMode && !HasSearch && !IsRecentMode;

    public bool ShowList => !ShowTree;

    [ObservableProperty]
    public partial TreeNodeViewModel? SelectedTreeItem { get; set; }

    [ObservableProperty]
    public partial BaseListItemViewModel? SelectedListItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(
        nameof(LaunchEnterpriseCommand),
        nameof(LaunchDesignerCommand),
        nameof(LaunchWithParametersCommand),
        nameof(LaunchEnterpriseWithParametersCommand),
        nameof(LaunchDesignerWithParametersCommand),
        nameof(EditLaunchSettingsCommand),
        nameof(ClearCacheCommand),
        nameof(ClearCacheAndLaunchCommand),
        nameof(ToggleFavoriteCommand),
        nameof(CopyConnectionStringCommand),
        nameof(OpenBaseFolderCommand),
        nameof(EditCommand),
        nameof(EditAsTextCommand),
        nameof(DeleteCommand),
        nameof(MoveUpCommand),
        nameof(MoveDownCommand),
        nameof(SortFolderByNameCommand),
        nameof(CopyToPersonalCommand),
        nameof(ExportCommand))]
    public partial InfoBaseViewModel? SelectedInfoBase { get; private set; }

    public bool HasSelection => SelectedInfoBase is not null;

    /// <summary>Выделенная обычная папка дерева (не «Избранное»).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(EditAsTextCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(SortFolderByNameCommand), nameof(ExportCommand), nameof(EditLaunchSettingsCommand))]
    public partial FolderNodeViewModel? SelectedFolder { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(LaunchEnterpriseCommand),
        nameof(LaunchDesignerCommand),
        nameof(LaunchWithParametersCommand),
        nameof(LaunchEnterpriseWithParametersCommand),
        nameof(LaunchDesignerWithParametersCommand),
        nameof(ClearCacheAndLaunchCommand))]
    public partial bool IsLaunching { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string WarningsText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial int WarningCount { get; private set; }

    public bool HasWarnings => WarningCount > 0;

    /// <summary>Поиск ничего не нашёл.</summary>
    [ObservableProperty]
    public partial bool ShowNothingFound { get; private set; }

    [ObservableProperty]
    public partial string PlatformsText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int PlatformCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme), nameof(ThemeToolTip))]
    public partial int ThemeIndex { get; set; }

    public bool IsSystemTheme => ThemeIndex == (int)ThemeMode.System;

    public bool IsLightTheme => ThemeIndex == (int)ThemeMode.Light;

    public bool IsDarkTheme => ThemeIndex == (int)ThemeMode.Dark;

    public string ThemeToolTip => (ThemeMode)ThemeIndex switch
    {
        ThemeMode.Light => "Тема: светлая. Нажмите — тёмная",
        ThemeMode.Dark => "Тема: тёмная. Нажмите — как в системе",
        _ => "Тема: как в системе. Нажмите — светлая",
    };

    /// <summary>Кнопка темы: как в системе → светлая → тёмная → как в системе.</summary>
    [RelayCommand]
    private void CycleTheme() => ThemeIndex = (ThemeIndex + 1) % ThemeNames.Count;

    [RelayCommand]
    private Task ShowAboutAsync() => _dialogs.ShowAboutAsync(new AboutViewModel());

    [ObservableProperty]
    public partial int AfterLaunchIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRightPanel))]
    public partial bool ShowDetails { get; set; }

    /// <summary>Кнопки запуска в строках списка баз.</summary>
    [ObservableProperty]
    public partial bool ShowRowLaunchButtons { get; set; }

    /// <summary>Кнопки запуска справа от списка, над свойствами базы.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRightPanel))]
    public partial bool ShowSideLaunchButtons { get; set; }

    /// <summary>Правой панели есть что показать: кнопки запуска или свойства. Нет — колонка убирается.</summary>
    public bool ShowRightPanel => ShowDetails || ShowSideLaunchButtons;

    /// <summary>Запретить повторный запуск: действует со следующего запуска лаунчера.</summary>
    [ObservableProperty]
    public partial bool SingleInstance { get; set; }

    /// <summary>Сворачивать в трей: окно прячется с панели задач, в области уведомлений — значок лаунчера.</summary>
    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool UseThickClientForFileBases { get; set; }

    /// <summary>Первая загрузка при открытии окна.</summary>
    public async Task InitializeAsync()
    {
        _theme.Apply((ThemeMode)ThemeIndex);
        await ReloadAsync();

        if (_watcher is not null && _paths is not null && !_watching)
        {
            _watching = true;
            _watcher.Changed += OnListFileChanged;
            _watcher.Start(_paths.PersonalInfoBaseListPath, _paths.StarterConfigPaths);
        }

        // Щелчок по базе в списке переходов Windows, когда лаунчер уже открыт.
        _launchChannel?.Start(OnLaunchRequest);
    }

    /// <summary>Сохранить всё при закрытии окна.</summary>
    public Task ShutdownAsync() => _settings.FlushAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsLoading = true;
        StatusText = "Загрузка…";
        try
        {
            if (_paths is null || _locator is null)
            {
                StatusText = PlatformServices.IsSupported ? "Пути 1С не заданы." : "Эта ОС пока не поддерживается.";
                return;
            }

            await LoadCatalogAsync(_paths, _locator);
        }
        catch (Exception ex)
        {
            LogReloadFailed(_logger, ex);
            StatusText = "Ошибка загрузки: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Показать загруженный каталог и платформы. Отдельно от загрузки — для тестов.</summary>
    internal void Apply(InfoBaseCatalog catalog, PlatformScanResult platforms)
    {
        ApplyPlatforms(platforms, catalog.StarterConfig.DefaultVersion);
        ShowCatalog(catalog, CurrentSelectionKey());
        StartCacheScan();
        StartAvailabilityCheck();
        _ = OnCatalogLoadedAsync();

        StatusText =
            $"Баз: {catalog.InfoBases.Count}, списков прочитано: {catalog.Lists.Count(l => l.IsAvailable)} из {catalog.Lists.Count}, " +
            $"платформ: {_installations.Count}";
    }

    /// <summary>Установленные платформы: с ними можно запускать базы и показывать колонку «Платформа».</summary>
    private void ApplyPlatforms(PlatformScanResult platforms, string? starterDefaultVersion)
    {
        _installations = platforms.Installations;
        _platformWarnings = platforms.Warnings;
        _starterDefaultVersion = starterDefaultVersion;

        PlatformCount = _installations.Count;
        PlatformsText = _installations.Count == 0
            ? "Установленные платформы 1С не найдены."
            : string.Join(Environment.NewLine, _installations.Select(DescribePlatform));
        if (_starterDefaultVersion is not null)
        {
            PlatformsText += $"{Environment.NewLine}Версия по умолчанию (1cestart.cfg): {_starterDefaultVersion}";
        }
    }

    /// <summary>Показать каталог (после загрузки или правки) и выделить запись по ключу.</summary>
    private void ShowCatalog(InfoBaseCatalog catalog, string? selectionKey)
    {
        _catalog = catalog;
        _bases.Clear();
        _bases.AddRange(catalog.InfoBases.Select(b => new InfoBaseViewModel(b, _settings.UserData)));
        UpdatePlatformColumn(_bases);

        var warnings = catalog.Warnings.Select(w => w.ToString())
            .Concat(_platformWarnings)
            .Concat(_settings.LoadWarning is { } w ? [w] : [])
            .ToList();
        WarningCount = warnings.Count;
        WarningsText = string.Join(Environment.NewLine, warnings);

        ApplyCacheReport();
        ApplyAvailability();
        RebuildTree();
        RebuildList();
        Reselect(selectionKey);
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchEnterpriseAsync(InfoBaseViewModel? target) => LaunchAsync(target ?? SelectedInfoBase, LaunchMode.Enterprise);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchDesignerAsync(InfoBaseViewModel? target) => LaunchAsync(target ?? SelectedInfoBase, LaunchMode.Designer);

    private bool CanLaunch(InfoBaseViewModel? target) => !IsLaunching && (target ?? SelectedInfoBase) is not null;

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private void ToggleFavorite(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target is null)
        {
            return;
        }

        var key = CurrentSelectionKey();
        _settings.UserData.SetFavorite(target.InfoBase, !target.IsFavorite);
        target.Refresh();
        _settings.RequestSave();
        StatusText = target.IsFavorite ? $"«{target.Name}» добавлена в избранное." : $"«{target.Name}» убрана из избранного.";

        RebuildTree();
        RebuildList();
        Reselect(key);
    }

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private async Task CopyConnectionStringAsync(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target is null)
        {
            return;
        }

        await _clipboard.SetTextAsync(target.ConnectionString);
        StatusText = "Строка подключения скопирована.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenBaseFolder))]
    private async Task OpenBaseFolderAsync(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target?.InfoBase.Connection.FilePath is not { } path)
        {
            return;
        }

        try
        {
            _processLauncher.OpenFolder(path);
        }
        catch (LaunchFailedException ex)
        {
            await _dialogs.ShowMessageAsync("Каталог базы", ex.Message);
        }
    }

    private bool HasTarget(InfoBaseViewModel? target) => (target ?? SelectedInfoBase) is not null;

    private bool CanOpenBaseFolder(InfoBaseViewModel? target) => (target ?? SelectedInfoBase)?.IsFileBase == true;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand(CanExecute = nameof(IsAllBasesMode))]
    private void ToggleViewMode() => IsTreeMode = !IsTreeMode;

    /// <summary>Заголовок «Наименование»: по наименованию ↔ свой порядок.</summary>
    [RelayCommand(CanExecute = nameof(IsAllBasesMode))]
    private void ToggleSort() => IsSortedByName = !IsSortedByName;

    /// <summary>Показать только недавние базы.</summary>
    [RelayCommand]
    private void ShowRecent() => IsRecentMode = true;

    /// <summary>Вернуться к общему списку баз.</summary>
    [RelayCommand]
    private void ShowAllBases() => IsRecentMode = false;

    partial void OnIsRecentModeChanged(bool value)
    {
        var key = CurrentSelectionKey();
        RebuildList();
        Reselect(key);
        StatusText = value ? "Показаны недавние базы." : string.Empty;
    }

    partial void OnIsSortedByNameChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.SortMode = value ? CatalogSortMode.Name : CatalogSortMode.Custom;
        _settings.RequestSave();
        var key = CurrentSelectionKey();
        RebuildTree();
        Reselect(key);
    }

    /// <summary>Развернуть все папки дерева, включая «Избранное». Состояние запоминается.</summary>
    [RelayCommand]
    private void ExpandAll() => SetExpanded(TreeItems, expanded: true);

    /// <summary>Свернуть все папки дерева.</summary>
    [RelayCommand]
    private void CollapseAll() => SetExpanded(TreeItems, expanded: false);

    private static void SetExpanded(IEnumerable<TreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var folder in nodes.OfType<FolderNodeViewModel>())
        {
            folder.IsExpanded = expanded;
            SetExpanded(folder.Children, expanded);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        var key = CurrentSelectionKey();
        RebuildList();

        // При поиске выделяется лучший результат, чтобы Enter сразу его запускал.
        Reselect(HasSearch ? null : key);
    }

    partial void OnIsTreeModeChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ViewMode = value ? CatalogViewMode.Tree : CatalogViewMode.List;
        _settings.RequestSave();
        Reselect(CurrentSelectionKey());
    }

    partial void OnSelectedTreeItemChanged(TreeNodeViewModel? value)
    {
        if (ShowTree)
        {
            SelectedInfoBase = (value as BaseNodeViewModel)?.Base;
            SelectedFolder = value as FolderNodeViewModel is { Kind: FolderKind.Regular } folder ? folder : null;
        }
    }

    partial void OnSelectedListItemChanged(BaseListItemViewModel? value)
    {
        if (ShowList)
        {
            SelectedInfoBase = value?.Base;
        }
    }

    /// <summary>Варианты платформы для «Запустить с параметрами»: как в списке баз и найденные версии.</summary>
    internal List<PlatformChoice> PlatformChoicesFor(InfoBaseViewModel target)
    {
        var choices = new List<PlatformChoice> { AsInListChoice };
        choices.AddRange(_installations.Select(i => i.Version).Distinct().OrderDescending()
            .Select(v => new PlatformChoice(v.ToString(), v.ToString())));
        if (target.PlatformVersionOverride is { } saved && choices.TrueForAll(c => c.Version != saved))
        {
            // Сохранённой версии больше нет среди установленных — показываем её, чтобы было видно, что выбрано.
            choices.Add(new PlatformChoice($"{saved} (не установлена)", saved));
        }

        return choices;
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        var theme = Enum.IsDefined((ThemeMode)value) ? (ThemeMode)value : ThemeMode.System;
        _settings.Settings.Ui.Theme = theme;
        _theme.Apply(theme);
        _settings.RequestSave();
    }

    partial void OnAfterLaunchIndexChanged(int value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.AfterLaunch = Enum.IsDefined((AfterLaunchAction)value) ? (AfterLaunchAction)value : AfterLaunchAction.Nothing;
        _settings.RequestSave();
    }

    partial void OnSingleInstanceChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.SingleInstance = value;
        _settings.RequestSave();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.MinimizeToTray = value;
        _settings.RequestSave();
    }

    partial void OnShowDetailsChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ShowDetails = value;
        _settings.RequestSave();
    }

    partial void OnShowRowLaunchButtonsChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ShowRowLaunchButtons = value;
        _settings.RequestSave();
    }

    partial void OnShowSideLaunchButtonsChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ShowSideLaunchButtons = value;
        _settings.RequestSave();
    }

    partial void OnUseThickClientForFileBasesChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Launch.UseThickClientForFileBasesByDefault = value;
        UpdatePlatformColumn(_bases);
        _settings.RequestSave();
    }

    private async Task LaunchAsync(InfoBaseViewModel? target, LaunchMode mode, OneOffLaunch? oneOff = null)
    {
        if (target is null || IsLaunching)
        {
            return;
        }

        IsLaunching = true;
        try
        {
            var (request, credentialWarning) = BuildRequest(target, mode, oneOff);
            var outcome = await _launcher.LaunchAsync(
                request,
                _installations,
                _starterDefaultVersion,
                _settings.Settings.Launch.ToLaunchOptions(),
                question => _dialogs.ConfirmAsync("Нет нужной версии платформы", question, "Запустить"));

            StatusText = outcome.Message;
            if (credentialWarning is not null && outcome.Started)
            {
                StatusText += " " + credentialWarning;
            }

            if (outcome.Started)
            {
                OnLaunched(target, mode);
            }
            else if (!outcome.Cancelled)
            {
                await _dialogs.ShowMessageAsync("Запуск невозможен", outcome.Message);
            }
        }
        catch (Exception ex)
        {
            LogLaunchCrashed(_logger, target.Name, ex);
            StatusText = "Ошибка запуска: " + ex.Message;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private void OnLaunched(InfoBaseViewModel target, LaunchMode mode)
    {
        _settings.UserData.RecordLaunch(target.InfoBase, mode);
        UpdateJumpList();
        target.Refresh();
        _settings.RequestSave();

        if (IsRecentMode)
        {
            var key = CurrentSelectionKey();
            RebuildList(); // запущенная база — наверх недавних
            Reselect(key);
        }

        switch (_settings.Settings.Ui.AfterLaunch)
        {
            case AfterLaunchAction.Minimize:
                _window.Minimize();
                break;
            case AfterLaunchAction.Close:
                _window.Close();
                break;
        }
    }

    private void RebuildTree()
    {
        TreeItems.Clear();
        if (_catalog is null)
        {
            return;
        }

        var collapsed = _settings.Settings.Ui.CollapsedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byInfoBase = _bases.ToDictionary(b => b.InfoBase);

        var favorites = _bases.Where(b => b.IsFavorite).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (favorites.Count > 0)
        {
            TreeItems.Add(SpecialFolder("Избранное", FavoritesFolderKey, FolderKind.Favorites, favorites, collapsed));
        }

        // Недавние — не папкой в дереве, а отдельным режимом (IsRecentMode).
        foreach (var item in _catalog.BuildTree(IsSortedByName ? CatalogSortMode.Name : CatalogSortMode.Custom).Items)
        {
            TreeItems.Add(ToNode(item, byInfoBase, collapsed));
        }
    }

    private FolderNodeViewModel SpecialFolder(
        string name,
        string key,
        FolderKind kind,
        IEnumerable<InfoBaseViewModel> bases,
        HashSet<string> collapsed)
    {
        var folder = new FolderNodeViewModel(name, key, kind, !collapsed.Contains(key), OnFolderExpansionChanged);
        foreach (var infoBase in bases)
        {
            folder.Children.Add(new BaseNodeViewModel(infoBase));
        }

        return folder;
    }

    private TreeNodeViewModel ToNode(
        CatalogTreeItem item,
        Dictionary<InfoBase, InfoBaseViewModel> byInfoBase,
        HashSet<string> collapsed)
    {
        if (item is CatalogInfoBaseItem baseItem)
        {
            return new BaseNodeViewModel(byInfoBase[baseItem.InfoBase]);
        }

        var folderNode = (CatalogFolderNode)item;
        // Папку можно менять, если это запись личного списка или (без записи) в ней есть базы личного списка.
        var isEditable = folderNode.Folder is { } record
            ? !record.IsReadOnly
            : folderNode.DescendantInfoBases.Any(b => !b.IsReadOnly);
        var folder = new FolderNodeViewModel(
            folderNode.Name, folderNode.Path, FolderKind.Regular, !collapsed.Contains(folderNode.Path), OnFolderExpansionChanged)
        {
            Record = folderNode.Folder,
            IsEditable = isEditable,
        };
        foreach (var child in folderNode.Items)
        {
            folder.Children.Add(ToNode(child, byInfoBase, collapsed));
        }

        return folder;
    }

    private void OnFolderExpansionChanged(FolderNodeViewModel folder)
    {
        var collapsed = _settings.Settings.Ui.CollapsedFolders;
        collapsed.RemoveAll(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase));
        if (!folder.IsExpanded)
        {
            collapsed.Add(folder.Path);
        }

        _settings.RequestSave();
    }

    private void RebuildList()
    {
        ListItems.Clear();
        ShowNothingFound = false;
        if (_catalog is null)
        {
            return;
        }

        var byInfoBase = _bases.ToDictionary(b => b.InfoBase);
        // Недавние: последние запуски, свежие сверху; поиск — только среди них.
        var recent = IsRecentMode ? _settings.UserData.Recent(_catalog.InfoBases, RecentCount) : null;
        if (HasSearch)
        {
            foreach (var match in InfoBaseSearch.Search(recent ?? _catalog.InfoBases, SearchText, b => SearchBoost(byInfoBase[b])))
            {
                var infoBase = byInfoBase[match.InfoBase];
                ListItems.Add(new BaseListItemViewModel(infoBase, Segments(infoBase.Name, match.NameHighlights)));
            }
        }
        else
        {
            var ordered = recent?.Select(b => byInfoBase[b])
                          ?? _bases.OrderByDescending(b => b.IsFavorite).ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var infoBase in ordered)
            {
                ListItems.Add(new BaseListItemViewModel(infoBase, [new TextSegment(infoBase.Name, false)]));
            }
        }

        ShowNothingFound = ListItems.Count == 0 && (HasSearch || IsRecentMode);
    }

    /// <summary>Надпись на пустом списке: ничего не нашлось или ещё нет запусков.</summary>
    public string EmptyListText => IsRecentMode && !HasSearch ? "Недавних запусков пока нет" : "Ничего не найдено";

    private static int SearchBoost(InfoBaseViewModel infoBase) =>
        (infoBase.IsFavorite ? FavoriteSearchBoost : 0) + Math.Min(infoBase.LaunchCount, MaxUsageSearchBoost);

    /// <summary>Ключ выделенной записи: <see cref="InfoBase.IdentityKey"/> базы или «folder:путь» папки.</summary>
    private string? CurrentSelectionKey() =>
        SelectedInfoBase?.InfoBase.IdentityKey ?? (SelectedFolder is { } folder ? FolderSelectionKey(folder.Path) : null);

    private static string FolderSelectionKey(string path) => FolderKeyPrefix + path;

    /// <summary>Выделить запись по ключу в текущем представлении; без ключа (или если её нет) — первую в списке.</summary>
    private void Reselect(string? identityKey)
    {
        if (ShowTree)
        {
            SelectedListItem = null;
            SelectedTreeItem = identityKey switch
            {
                null => null,
                _ when identityKey.StartsWith(FolderKeyPrefix, StringComparison.Ordinal) =>
                    FindFolder(TreeItems, identityKey[FolderKeyPrefix.Length..]),
                _ => FindNode(TreeItems, identityKey),
            };
            SelectedInfoBase = (SelectedTreeItem as BaseNodeViewModel)?.Base;
            SelectedFolder = SelectedTreeItem as FolderNodeViewModel;
            return;
        }

        SelectedTreeItem = null;
        SelectedFolder = null;
        SelectedListItem = (identityKey is null ? null : ListItems.FirstOrDefault(i => i.Base.InfoBase.IdentityKey == identityKey))
                           ?? ListItems.FirstOrDefault();
        SelectedInfoBase = SelectedListItem?.Base;
    }

    private static FolderNodeViewModel? FindFolder(IEnumerable<TreeNodeViewModel> nodes, string path)
    {
        foreach (var folder in nodes.OfType<FolderNodeViewModel>().Where(f => f.Kind == FolderKind.Regular))
        {
            if (string.Equals(folder.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return folder;
            }

            if (FindFolder(folder.Children, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static BaseNodeViewModel? FindNode(IEnumerable<TreeNodeViewModel> nodes, string identityKey)
    {
        // Сначала в обычных папках: «Избранное» дублирует базы.
        BaseNodeViewModel? special = null;
        foreach (var node in nodes)
        {
            switch (node)
            {
                case BaseNodeViewModel baseNode when baseNode.Base.InfoBase.IdentityKey == identityKey:
                    return baseNode;
                case FolderNodeViewModel { Kind: FolderKind.Regular } folder when FindNode(folder.Children, identityKey) is { } found:
                    return found;
                case FolderNodeViewModel folder when special is null:
                    special = FindNode(folder.Children, identityKey);
                    break;
            }
        }

        return special;
    }

    private static List<TextSegment> Segments(string text, IReadOnlyList<TextRange> highlights)
    {
        var segments = new List<TextSegment>();
        var position = 0;
        foreach (var range in highlights)
        {
            if (range.Start > position)
            {
                segments.Add(new TextSegment(text[position..range.Start], false));
            }

            segments.Add(new TextSegment(text.Substring(range.Start, range.Length), true));
            position = range.Start + range.Length;
        }

        if (position < text.Length)
        {
            segments.Add(new TextSegment(text[position..], false));
        }

        return segments;
    }

    private static string DescribePlatform(PlatformInstallation installation)
    {
        var clients = (installation.Has(PlatformExecutable.ThickClient), installation.Has(PlatformExecutable.ThinClient)) switch
        {
            (true, true) => "толстый + тонкий",
            (true, false) => "толстый",
            _ => "тонкий",
        };
        return $"{installation,-18} {clients,-16}  {installation.BinDirectory}";
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не удалось загрузить каталог баз")]
    private static partial void LogReloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Сбой при запуске «{Name}»")]
    private static partial void LogLaunchCrashed(ILogger logger, string name, Exception exception);
}
