using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Catalog;
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

    private const string FavoritesFolderKey = ":favorites";
    private const string RecentFolderKey = ":recent";
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

    private readonly List<InfoBaseViewModel> _bases = [];
    private InfoBaseCatalog? _catalog;
    private IReadOnlyList<PlatformInstallation> _installations = [];
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
        ILogger<MainWindowViewModel> logger,
        IPlatformPaths? paths = null,
        IPlatformLocator? locator = null)
    {
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
        ThemeIndex = (int)ui.Theme;
        AfterLaunchIndex = (int)ui.AfterLaunch;
        ShowDetails = ui.ShowDetails;
        UseThickClientForFileBases = settings.Settings.Launch.UseThickClientForFileBasesByDefault;
        _suppressSettingsSync = false;
    }

    public ObservableCollection<TreeNodeViewModel> TreeItems { get; } = [];

    public ObservableCollection<BaseListItemViewModel> ListItems { get; } = [];

    public ObservableCollection<PlatformChoice> PlatformChoices { get; } = [AsInListChoice];

    public IReadOnlyList<string> ThemeNames { get; } = ["Как в системе", "Светлая", "Тёмная"];

    public IReadOnlyList<string> AfterLaunchNames { get; } = ["Ничего не делать", "Свернуть окно", "Закрыть лаунчер"];

    /// <summary>Все загруженные базы.</summary>
    public IReadOnlyList<InfoBaseViewModel> InfoBases => _bases;

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
    [NotifyPropertyChangedFor(nameof(HasSearch), nameof(ShowTree), nameof(ShowList))]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTree), nameof(ShowList))]
    public partial bool IsTreeMode { get; set; }

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Дерево показывается только без поиска; результаты поиска — всегда списком.</summary>
    public bool ShowTree => IsTreeMode && !HasSearch;

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
        nameof(ToggleFavoriteCommand),
        nameof(CopyConnectionStringCommand),
        nameof(OpenBaseFolderCommand))]
    public partial InfoBaseViewModel? SelectedInfoBase { get; private set; }

    public bool HasSelection => SelectedInfoBase is not null;

    [ObservableProperty]
    public partial PlatformChoice? SelectedPlatformChoice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchEnterpriseCommand), nameof(LaunchDesignerCommand))]
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
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int AfterLaunchIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    [ObservableProperty]
    public partial bool UseThickClientForFileBases { get; set; }

    /// <summary>Первая загрузка при открытии окна.</summary>
    public Task InitializeAsync()
    {
        _theme.Apply((ThemeMode)ThemeIndex);
        return ReloadAsync();
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

            var catalog = await _loader.LoadAsync(_paths.ToCatalogSources());
            var platforms = await _locator.LocateAsync(catalog.StarterConfig.InstalledLocations);
            Apply(catalog, platforms);
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
        var selectedKey = SelectedInfoBase?.InfoBase.IdentityKey;

        _installations = platforms.Installations;
        _starterDefaultVersion = catalog.StarterConfig.DefaultVersion;
        _catalog = catalog;
        _bases.Clear();
        _bases.AddRange(catalog.InfoBases.Select(b => new InfoBaseViewModel(b, _settings.UserData)));

        PlatformChoices.Clear();
        PlatformChoices.Add(AsInListChoice);
        foreach (var version in _installations.Select(i => i.Version).Distinct())
        {
            PlatformChoices.Add(new PlatformChoice(version.ToString(), version.ToString()));
        }

        PlatformCount = _installations.Count;
        PlatformsText = _installations.Count == 0
            ? "Установленные платформы 1С не найдены."
            : string.Join(Environment.NewLine, _installations.Select(DescribePlatform));
        if (_starterDefaultVersion is not null)
        {
            PlatformsText += $"{Environment.NewLine}Версия по умолчанию (1cestart.cfg): {_starterDefaultVersion}";
        }

        var warnings = catalog.Warnings.Select(w => w.ToString())
            .Concat(platforms.Warnings)
            .Concat(_settings.LoadWarning is { } w ? [w] : [])
            .ToList();
        WarningCount = warnings.Count;
        WarningsText = string.Join(Environment.NewLine, warnings);

        RebuildTree();
        RebuildList();
        Reselect(selectedKey);

        StatusText =
            $"Баз: {catalog.InfoBases.Count}, списков прочитано: {catalog.Lists.Count(l => l.IsAvailable)} из {catalog.Lists.Count}, " +
            $"платформ: {_installations.Count}";
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

        var key = SelectedInfoBase?.InfoBase.IdentityKey;
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

    [RelayCommand]
    private void ToggleViewMode() => IsTreeMode = !IsTreeMode;

    partial void OnSearchTextChanged(string value)
    {
        var key = SelectedInfoBase?.InfoBase.IdentityKey;
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
        Reselect(SelectedInfoBase?.InfoBase.IdentityKey);
    }

    partial void OnSelectedTreeItemChanged(TreeNodeViewModel? value)
    {
        if (ShowTree)
        {
            SelectedInfoBase = (value as BaseNodeViewModel)?.Base;
        }
    }

    partial void OnSelectedListItemChanged(BaseListItemViewModel? value)
    {
        if (ShowList)
        {
            SelectedInfoBase = value?.Base;
        }
    }

    partial void OnSelectedInfoBaseChanged(InfoBaseViewModel? value)
    {
        var version = value?.PlatformVersionOverride;
        var choice = version is null ? AsInListChoice : PlatformChoices.FirstOrDefault(c => c.Version == version);
        if (choice is null)
        {
            // Выбранной версии больше нет среди установленных — показываем её, чтобы было видно, что выбрано.
            choice = new PlatformChoice($"{version} (не установлена)", version);
            PlatformChoices.Add(choice);
        }

        _suppressSettingsSync = true;
        SelectedPlatformChoice = choice;
        _suppressSettingsSync = false;
    }

    partial void OnSelectedPlatformChoiceChanged(PlatformChoice? value)
    {
        if (_suppressSettingsSync || value is null || SelectedInfoBase is not { } target)
        {
            return;
        }

        _settings.UserData.SetPlatformVersionOverride(target.InfoBase, value.Version);
        target.Refresh();
        _settings.RequestSave();
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

    partial void OnShowDetailsChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ShowDetails = value;
        _settings.RequestSave();
    }

    partial void OnUseThickClientForFileBasesChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Launch.UseThickClientForFileBasesByDefault = value;
        _settings.RequestSave();
    }

    private async Task LaunchAsync(InfoBaseViewModel? target, LaunchMode mode)
    {
        if (target is null || IsLaunching)
        {
            return;
        }

        IsLaunching = true;
        try
        {
            var request = new LaunchRequest(target.InfoBase, mode) { PlatformVersionOverride = target.PlatformVersionOverride };
            var outcome = await _launcher.LaunchAsync(
                request,
                _installations,
                _starterDefaultVersion,
                _settings.Settings.Launch.ToLaunchOptions(),
                question => _dialogs.ConfirmAsync("Нет нужной версии платформы", question, "Запустить"));

            StatusText = outcome.Message;
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
        target.Refresh();
        _settings.RequestSave();

        var key = SelectedInfoBase?.InfoBase.IdentityKey;
        RebuildTree(); // обновить «Недавние»
        Reselect(key);

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

        var recent = _settings.UserData.Recent(_catalog.InfoBases, RecentCount).Select(b => byInfoBase[b]).ToList();
        if (recent.Count > 0)
        {
            TreeItems.Add(SpecialFolder("Недавние", RecentFolderKey, FolderKind.Recent, recent, collapsed));
        }

        foreach (var item in _catalog.BuildTree().Items)
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
        var folder = new FolderNodeViewModel(
            folderNode.Name, folderNode.Path, FolderKind.Regular, !collapsed.Contains(folderNode.Path), OnFolderExpansionChanged);
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

        if (HasSearch)
        {
            var byInfoBase = _bases.ToDictionary(b => b.InfoBase);
            foreach (var match in InfoBaseSearch.Search(_catalog.InfoBases, SearchText, b => SearchBoost(byInfoBase[b])))
            {
                var infoBase = byInfoBase[match.InfoBase];
                ListItems.Add(new BaseListItemViewModel(infoBase, Segments(infoBase.Name, match.NameHighlights)));
            }

            ShowNothingFound = ListItems.Count == 0;

            return;
        }

        foreach (var infoBase in _bases
                     .OrderByDescending(b => b.IsFavorite)
                     .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ListItems.Add(new BaseListItemViewModel(infoBase, [new TextSegment(infoBase.Name, false)]));
        }
    }

    private static int SearchBoost(InfoBaseViewModel infoBase) =>
        (infoBase.IsFavorite ? FavoriteSearchBoost : 0) + Math.Min(infoBase.LaunchCount, MaxUsageSearchBoost);

    /// <summary>Выделить базу по ключу в текущем представлении; без ключа (или если её нет) — первую в списке.</summary>
    private void Reselect(string? identityKey)
    {
        if (ShowTree)
        {
            SelectedListItem = null;
            SelectedTreeItem = identityKey is null ? null : FindNode(TreeItems, identityKey);
            SelectedInfoBase = (SelectedTreeItem as BaseNodeViewModel)?.Base;
            return;
        }

        SelectedTreeItem = null;
        SelectedListItem = (identityKey is null ? null : ListItems.FirstOrDefault(i => i.Base.InfoBase.IdentityKey == identityKey))
                           ?? ListItems.FirstOrDefault();
        SelectedInfoBase = SelectedListItem?.Base;
    }

    private static BaseNodeViewModel? FindNode(IEnumerable<TreeNodeViewModel> nodes, string identityKey)
    {
        // Сначала в обычных папках: специальные («Избранное», «Недавние») дублируют базы.
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
