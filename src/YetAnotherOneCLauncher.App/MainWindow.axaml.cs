using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Временное главное окно: дерево баз из всех списков, найденные платформы и запуск.
/// Двойной щелчок или Enter — Предприятие, Ctrl+Enter или контекстное меню — Конфигуратор.
/// Код будет перенесён в MVVM на этапе 3.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly (string Title, ThemeVariant Variant)[] Themes =
    [
        ("Тема: как в системе", ThemeVariant.Default),
        ("Тема: светлая", ThemeVariant.Light),
        ("Тема: тёмная", ThemeVariant.Dark),
    ];

    private readonly InfoBaseCatalogLoader _loader;
    private readonly LaunchCoordinator? _launcher;
    private readonly ILogger _logger;
    private readonly IPlatformPaths? _paths;
    private readonly IPlatformLocator? _locator;

    private IReadOnlyList<PlatformInstallation> _installations = [];
    private string? _starterDefaultVersion;

    // Нужен дизайнеру XAML; в приложении окно создаётся из контейнера.
    public MainWindow()
        : this(new InfoBaseCatalogLoader(), launcher: null, NullLogger<MainWindow>.Instance)
    {
    }

    /// <param name="loader">Загрузка списков баз.</param>
    /// <param name="launcher">Запуск баз; <c>null</c> — в дизайнере.</param>
    /// <param name="logger">Лог.</param>
    /// <param name="paths"><c>null</c> на неподдерживаемой ОС.</param>
    /// <param name="locator">Поиск платформ; <c>null</c> на неподдерживаемой ОС.</param>
    public MainWindow(
        InfoBaseCatalogLoader loader,
        LaunchCoordinator? launcher,
        ILogger<MainWindow> logger,
        IPlatformPaths? paths = null,
        IPlatformLocator? locator = null)
    {
        _loader = loader;
        _launcher = launcher;
        _logger = logger;
        _paths = paths;
        _locator = locator;

        InitializeComponent();

        ThemeBox.ItemsSource = Themes.Select(t => t.Title).ToList();
        ThemeBox.SelectedIndex = 0;
        ThemeBox.SelectionChanged += OnThemeChanged;

        CatalogTree.DoubleTapped += OnTreeDoubleTapped;
        CatalogTree.KeyDown += OnTreeKeyDown;

        ReloadButton.Click += async (_, _) => await ReloadAsync();
        Opened += async (_, _) => await ReloadAsync();
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ThemeBox.SelectedIndex;
        if (Application.Current is { } app && index >= 0 && index < Themes.Length)
        {
            app.RequestedThemeVariant = Themes[index].Variant;
        }
    }

    private async Task ReloadAsync()
    {
        ReloadButton.IsEnabled = false;
        StatusText.Text = "Загрузка…";
        try
        {
            if (_paths is null || _locator is null)
            {
                StatusText.Text = PlatformServices.IsSupported
                    ? "Пути 1С не заданы."
                    : "Эта ОС пока не поддерживается.";
                return;
            }

            var catalog = await _loader.LoadAsync(_paths.ToCatalogSources());
            var platforms = await _locator.LocateAsync(catalog.StarterConfig.InstalledLocations);
            _installations = platforms.Installations;
            _starterDefaultVersion = catalog.StarterConfig.DefaultVersion;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var platformList = string.Join(", ", _installations);
                LogPlatformsFound(_logger, _installations.Count, platformList);
            }

            CatalogTree.ItemsSource = null;
            CatalogTree.Items.Clear();
            foreach (var item in CreateItems(catalog.BuildTree()))
            {
                CatalogTree.Items.Add(item);
            }

            var available = catalog.Lists.Count(l => l.IsAvailable);
            StatusText.Text =
                $"Баз: {catalog.InfoBases.Count}, папок: {catalog.Folders.Count}, " +
                $"списков прочитано: {available} из {catalog.Lists.Count}, платформ: {_installations.Count}";

            ShowPlatforms(platforms);

            var warnings = catalog.Warnings.Select(w => w.ToString()).Concat(platforms.Warnings).ToList();
            WarningsExpander.IsVisible = warnings.Count > 0;
            WarningsExpander.Header = $"Предупреждения ({warnings.Count})";
            WarningsText.Text = string.Join(Environment.NewLine, warnings);
        }
        catch (Exception ex)
        {
            LogLoadFailed(_logger, ex);
            StatusText.Text = "Ошибка загрузки: " + ex.Message;
        }
        finally
        {
            ReloadButton.IsEnabled = true;
        }
    }

    private void ShowPlatforms(PlatformScanResult platforms)
    {
        PlatformsExpander.Header = $"Платформы ({platforms.Installations.Count})";
        PlatformsText.Text = platforms.Installations.Count == 0
            ? "Установленные платформы 1С не найдены."
            : string.Join(
                Environment.NewLine,
                platforms.Installations.Select(i =>
                    $"{i,-18} {ClientsText(i)}   {i.BinDirectory}"));

        if (_starterDefaultVersion is not null)
        {
            PlatformsText.Text += $"{Environment.NewLine}Версия по умолчанию (1cestart.cfg): {_starterDefaultVersion}";
        }
    }

    private static string ClientsText(PlatformInstallation installation) =>
        (installation.Has(PlatformExecutable.ThickClient), installation.Has(PlatformExecutable.ThinClient)) switch
        {
            (true, true) => "толстый + тонкий",
            (true, false) => "толстый        ",
            _ => "тонкий         ",
        };

    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source
            && source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { Tag: InfoBase infoBase })
        {
            e.Handled = true;
            await LaunchAsync(infoBase, LaunchMode.Enterprise);
        }
    }

    private async void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || CatalogTree.SelectedItem is not TreeViewItem { Tag: InfoBase infoBase })
        {
            return;
        }

        e.Handled = true;
        var mode = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? LaunchMode.Designer : LaunchMode.Enterprise;
        await LaunchAsync(infoBase, mode);
    }

    private async Task LaunchAsync(InfoBase infoBase, LaunchMode mode)
    {
        if (_launcher is null)
        {
            return;
        }

        try
        {
            var outcome = await _launcher.LaunchAsync(
                new LaunchRequest(infoBase, mode),
                _installations,
                _starterDefaultVersion,
                question => MessageDialog.AskAsync(this, "Нет нужной версии платформы", question, "Запустить"));

            StatusText.Text = outcome.Message;
            if (!outcome.Started && outcome.Message != "Запуск отменён.")
            {
                await MessageDialog.ShowAsync(this, "Запуск невозможен", outcome.Message);
            }
        }
        catch (Exception ex)
        {
            // async void: исключение не должно уйти в цикл сообщений и уронить окно.
            LogLaunchCrashed(_logger, infoBase.Name, ex);
            StatusText.Text = "Ошибка запуска: " + ex.Message;
        }
    }

    private IEnumerable<TreeViewItem> CreateItems(CatalogFolderNode folder)
    {
        foreach (var item in folder.Items)
        {
            switch (item)
            {
                case CatalogFolderNode subFolder:
                {
                    var node = new TreeViewItem { Header = "📁 " + subFolder.Name, IsExpanded = true };
                    foreach (var child in CreateItems(subFolder))
                    {
                        node.Items.Add(child);
                    }

                    yield return node;
                    break;
                }

                case CatalogInfoBaseItem baseItem:
                {
                    var infoBase = baseItem.InfoBase;
                    var readOnlyMark = infoBase.IsReadOnly ? "  [общий список]" : string.Empty;
                    var versionMark = infoBase.Version is { } version ? $"  [{version}]" : string.Empty;
                    yield return new TreeViewItem
                    {
                        Header = $"{infoBase.Name}   —   {infoBase.Connection.ToDisplayString()}{versionMark}{readOnlyMark}",
                        Tag = infoBase,
                        ContextMenu = CreateContextMenu(infoBase),
                    };
                    break;
                }
            }
        }
    }

    private ContextMenu CreateContextMenu(InfoBase infoBase)
    {
        var enterprise = new MenuItem { Header = "Предприятие", InputGesture = new KeyGesture(Key.Enter) };
        enterprise.Click += async (_, _) => await LaunchAsync(infoBase, LaunchMode.Enterprise);

        var designer = new MenuItem { Header = "Конфигуратор", InputGesture = new KeyGesture(Key.Enter, KeyModifiers.Control) };
        designer.Click += async (_, _) => await LaunchAsync(infoBase, LaunchMode.Designer);

        return new ContextMenu { Items = { enterprise, designer } };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не удалось загрузить каталог баз")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Найдено платформ: {Count}: {Platforms}")]
    private static partial void LogPlatformsFound(ILogger logger, int count, string platforms);

    [LoggerMessage(Level = LogLevel.Error, Message = "Сбой при запуске «{Name}»")]
    private static partial void LogLaunchCrashed(ILogger logger, string name, Exception exception);
}
