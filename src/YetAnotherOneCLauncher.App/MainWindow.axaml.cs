using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Главное окно. Логика — в <see cref="MainWindowViewModel"/>; здесь только то, что относится к представлению:
/// положение окна, фокус и сочетания клавиш.
/// </summary>
/// <remarks>
/// Клавиши по умолчанию (переопределяются в «Настройки» → «Горячие клавиши», см. <see cref="HotKeyMap"/>): Ctrl+F — поиск; Esc, Ctrl+Q — очистить поиск; ↓ из поиска — к списку; Enter, F3 — 1С: Предприятие;
/// F4 — Конфигуратор; F6 — запуск с параметрами; Ctrl+D — избранное; F5 — обновить; Ctrl+N / Ins — новая база; Ctrl+Shift+N — папка;
/// F2 — изменить; Del — удалить; Alt+↑/↓ — порядок. Набор текста в списке уходит в поиск.
/// </remarks>
public partial class MainWindow : Window
{
    private const double MinRestoredSize = 200;
    private const double DragThreshold = 6;

    private static readonly DataFormat<TreeNodeViewModel> TreeNodeFormat =
        DataFormat.CreateInProcessFormat<TreeNodeViewModel>("YetAnotherOneCLauncher.TreeNode");

    /// <summary>Список баз не сжимается уже этого, сколько ни тяни разделитель.</summary>
    private const double MinListWidth = 280;
    private const double MinSearchWidth = 200;

    private PointerPressedEventArgs? _dragStart;

    // Перетаскивание границы колонки: какая колонка, где нажали и какой была ширина.
    private (ListColumn Column, double StartX, double StartWidth)? _columnResize;
    private bool _focusRestorePending;
    private Control? _focusedList;

    // Нужен дизайнеру XAML; в приложении окно создаётся из контейнера.
    public MainWindow()
    {
        InitializeComponent();
        WindowTitleBar.Apply(this, mainWindow: true);
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this, mainWindow: true);
        DataContext = viewModel;
        RestorePlacement(viewModel.WindowPlacement);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        SearchBox.KeyDown += OnSearchBoxKeyDown;
        foreach (var list in new Control[] { CatalogTree, CatalogList })
        {
            list.KeyDown += OnListKeyDown;
            list.AddHandler(KeyDownEvent, OnListPreviewKeyDown, RoutingStrategies.Tunnel);
            list.DoubleTapped += OnListDoubleTapped;
            list.TextInput += OnListTextInput;
        }

        // Перетаскивание в дереве: папки и базы личного списка — в папку или перед базой; базу — в «Избранное».
        CatalogTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        CatalogTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        CatalogTree.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        CatalogTree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        CatalogTree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

        // После правки дерево и список перестраиваются, и фокус клавиатуры теряется вместе со старыми элементами.
        // Возвращаем его на выделенную запись, чтобы можно было сразу продолжать с клавиатуры (Alt+↑, Del…).
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        viewModel.TreeItems.CollectionChanged += (_, _) => RestoreFocusLater(CatalogTree, () => viewModel.SelectedTreeItem);
        viewModel.ListItems.CollectionChanged += (_, _) => RestoreFocusLater(CatalogList, () => viewModel.SelectedListItem);

        Opened += async (_, _) =>
        {
            viewModel.OnWindowOpened();
            EnsureOnScreen();
            SearchBox.Focus();
            await viewModel.InitializeAsync();
        };

        Closing += (_, _) => viewModel.WindowPlacement = CapturePlacement();
        Closed += (_, _) => viewModel.StopRunningWatch();
        InitTray(viewModel);

        // Панель подробностей: ширина — из настроек, меняется разделителем; поле поиска над ней следует за шириной.
        ApplyDetailsLayout();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.ShowDetails) or nameof(MainWindowViewModel.DetailsWidth)
                or nameof(MainWindowViewModel.ShowSideLaunchButtons))
            {
                ApplyDetailsLayout();
            }
        };
        // Ширина колонок списка: тянуть границу в заголовке, двойной щелчок — ширина по умолчанию.
        foreach (var grip in new[] { PlatformColumnGrip, ModeColumnGrip, LastLaunchColumnGrip })
        {
            grip.PointerPressed += OnColumnGripPressed;
            grip.PointerMoved += OnColumnGripMoved;
            grip.PointerReleased += (_, _) => _columnResize = null;
            grip.PointerCaptureLost += (_, _) => _columnResize = null;
        }

        // Кнопка ▶ в строке (кнопки слева от наименования): меню режимов запуска этой базы.
        AddHandler(Button.ClickEvent, OnRowLaunchMenuClick);

        HeaderPanel.SizeChanged += (_, _) => FitSearchBox();
        ToolbarPanel.SizeChanged += (_, _) => FitSearchBox();
        DetailsSplitter.DragDelta += (_, _) => FitSearchBox(DetailsColumn.ActualWidth);
        DetailsSplitter.DragCompleted += (_, _) =>
        {
            viewModel.DetailsWidth = DetailsColumn.ActualWidth;
            ApplyDetailsLayout(); // ширина могла упереться в пределы
        };
    }

    /// <summary>Щелчок по ▶ в строке: строка выделяется, рядом с кнопкой — меню режимов запуска.</summary>
    private void OnRowLaunchMenuClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: InfoBaseViewModel infoBase } button || !button.Classes.Contains("rowLaunchMenu")
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        switch (button.DataContext)
        {
            case BaseListItemViewModel item:
                viewModel.SelectedListItem = item;
                break;
            case TreeNodeViewModel node:
                viewModel.SelectedTreeItem = node;
                break;
        }

        CreateRowLaunchMenu(viewModel, infoBase).ShowAt(button);
    }

    /// <summary>Меню кнопки ▶: те же команды, что у трёх кнопок строки, — для базы этой строки.</summary>
    internal MenuFlyout CreateRowLaunchMenu(MainWindowViewModel viewModel, InfoBaseViewModel infoBase)
    {
        MenuItem Item(string header, ICommand command, KeyGesture? gesture, string icon, string? solidIcon = null) => new()
        {
            Header = header,
            Command = command,
            CommandParameter = infoBase,
            InputGesture = gesture,
            Icon = new Controls.ToolIcon
            {
                Classes = { "buttonIcon" },
                Data = (Avalonia.Media.Geometry?)this.FindResource(icon),
                SolidData = solidIcon is null ? null : (Avalonia.Media.Geometry?)this.FindResource(solidIcon),
            },
        };

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(Item("1С: Предприятие", viewModel.LaunchEnterpriseCommand, viewModel.HotKeys.LaunchEnterprise,
            "EnterpriseIconGeometry", "EnterpriseIconGeometry"));
        menu.Items.Add(Item("Конфигуратор", viewModel.LaunchDesignerCommand, viewModel.HotKeys.LaunchDesigner, "DesignerIconGeometry"));
        menu.Items.Add(Item("Запустить с параметрами…", viewModel.LaunchWithParametersCommand, viewModel.HotKeys.LaunchWithParameters,
            "ParametersIconGeometry", "ParametersKnobsIconGeometry"));
        return menu;
    }

    private void OnColumnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: ListColumn column } grip || DataContext is not MainWindowViewModel vm
            || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _columnResize = null;
            vm.SetColumnWidth(column, null);
            return;
        }

        _columnResize = (column, e.GetPosition(this).X, vm.ColumnWidth(column));
        e.Pointer.Capture(grip);
    }

    /// <summary>Колонки прижаты к правому краю: граница слева от колонки — влево шире, вправо уже.</summary>
    private void OnColumnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_columnResize is not { } resize || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.SetColumnWidth(resize.Column, resize.StartWidth - (e.GetPosition(this).X - resize.StartX));
        e.Handled = true;
    }

    private ColumnDefinition DetailsColumn => BodyGrid.ColumnDefinitions[2];

    private void ApplyDetailsLayout()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        // Правая колонка — кнопки запуска и свойства базы; если оба выключены, колонка с разделителем убирается
        // и список занимает всю ширину.
        BodyGrid.ColumnDefinitions[0].MinWidth = MinListWidth;
        var show = vm.ShowRightPanel;
        DetailsBorder.IsVisible = show;
        DetailsSplitter.IsVisible = show;
        DetailsColumn.MinWidth = show ? MainWindowViewModel.MinDetailsWidth : 0;
        DetailsColumn.MaxWidth = show ? MainWindowViewModel.MaxDetailsWidth : 0;
        DetailsColumn.Width = new GridLength(show ? vm.DetailsWidth : 0);
        FitSearchBox();
    }

    /// <summary>
    /// Поле поиска — над правой панелью: левый край — вровень с её содержимым, правее разделителя.
    /// Без панели ширина та же, что была бы у панели. Если места не хватает, поле сужается, но не уже <see cref="MinSearchWidth"/>.
    /// </summary>
    private void FitSearchBox(double? detailsWidth = null)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var panelWidth = detailsWidth ?? vm.DetailsWidth;
        // Правый край поля — у отступа верхней строки от края окна, левый — там же, где начинается содержимое панели
        // (линия разделителя плюс внутренний отступ панели): поле не заходит за разделитель.
        var target = panelWidth - HeaderPanel.Margin.Right - DetailsBorder.Padding.Left;
        var available = HeaderPanel.Bounds.Width - ToolbarPanel.Bounds.Width - ToolbarPanel.Margin.Right;
        SearchBox.Width = available > 0 ? Math.Max(Math.Min(target, available), Math.Min(MinSearchWidth, available)) : target;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        // Esc — не переопределяется: очищает поиск.
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && vm.HasSearch)
        {
            ClearSearch(vm);
            e.Handled = true;
            return;
        }

        // Остальное — по сочетаниям из настроек (окно «Настройки» → «Горячие клавиши»).
        if (vm.HotKeys.Match(e.Key, e.KeyModifiers, e.PhysicalKey, HotKeyScope.Window) is { } command)
        {
            RunHotKey(vm, command);
            e.Handled = true;
        }
    }

    /// <summary>Клавиши правки — раньше, чем их обработает дерево (стрелки оно забирает себе).</summary>
    private void OnListPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        // Ins — не переопределяется: новая база.
        if (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.None)
        {
            Execute(vm.AddBaseCommand);
            e.Handled = true;
            return;
        }

        if (vm.HotKeys.Match(e.Key, e.KeyModifiers, e.PhysicalKey, HotKeyScope.List) is { } command)
        {
            RunHotKey(vm, command);
            e.Handled = true;
        }
    }

    private void RunHotKey(MainWindowViewModel vm, HotKeyCommand command)
    {
        switch (command)
        {
            case HotKeyCommand.FocusSearch:
                FocusSearch(selectAll: true);
                break;
            case HotKeyCommand.ClearSearch:
                ClearSearch(vm);
                break;
            default:
                Execute(command switch
                {
                    HotKeyCommand.LaunchEnterprise => vm.LaunchEnterpriseCommand,
                    HotKeyCommand.LaunchDesigner => vm.LaunchDesignerCommand,
                    HotKeyCommand.LaunchWithParameters => vm.LaunchWithParametersCommand,
                    HotKeyCommand.Reload => vm.ReloadCommand,
                    HotKeyCommand.ToggleFavorite => vm.ToggleFavoriteCommand,
                    HotKeyCommand.AddBase => vm.AddBaseCommand,
                    HotKeyCommand.AddFolder => vm.AddFolderCommand,
                    HotKeyCommand.Edit => vm.EditCommand,
                    HotKeyCommand.Delete => vm.DeleteCommand,
                    HotKeyCommand.MoveUp => vm.MoveUpCommand,
                    HotKeyCommand.MoveDown => vm.MoveDownCommand,
                    _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
                });
                break;
        }
    }

    private void ClearSearch(MainWindowViewModel vm)
    {
        vm.SearchText = string.Empty;
        FocusSearch(selectAll: false);
    }

    /// <summary>Запоминает, в дереве или в списке фокус; при удалении элемента <c>GotFocus</c> не приходит — значение сохраняется.</summary>
    private void OnAnyGotFocus(object? sender, FocusChangedEventArgs e)
    {
        var visual = e.Source as Visual;
        _focusedList = visual?.FindAncestorOfType<TreeView>(includeSelf: true) == CatalogTree ? CatalogTree
            : visual?.FindAncestorOfType<ListBox>(includeSelf: true) == CatalogList ? CatalogList
            : null;
    }

    private void RestoreFocusLater(Control list, Func<object?> selected)
    {
        if (_focusRestorePending || !ReferenceEquals(_focusedList, list))
        {
            return;
        }

        _focusRestorePending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _focusRestorePending = false;
                if (!list.IsEffectivelyVisible || selected() is not { } item)
                {
                    list.Focus();
                    return;
                }

                var container = list is TreeView tree ? tree.TreeContainerFromItem(item) : ((ItemsControl)list).ContainerFromItem(item);
                (container ?? list).Focus(NavigationMethod.Directional);
            },
            DispatcherPriority.Background);
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetCurrentPoint(CatalogTree).Properties.IsLeftButtonPressed && NodeFrom(e.Source) is { } node && CanDrag(node)
            ? e
            : null;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || NodeFrom(start.Source) is not { } node)
        {
            return;
        }

        var delta = e.GetPosition(CatalogTree) - start.GetPosition(CatalogTree);
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragStart = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TreeNodeFormat, node));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var source = e.DataTransfer.TryGetValue(TreeNodeFormat);
        var target = NodeFrom(e.Source);
        e.DragEffects = source is null || target is null || ReferenceEquals(source, target) ? DragDropEffects.None
            : IsDuplicateDrag(e, source) ? (CanDuplicateOn(target) ? DragDropEffects.Copy : DragDropEffects.None)
            : CanDropOn(source, target) ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm
            || e.DataTransfer.TryGetValue(TreeNodeFormat) is not { } source
            || NodeFrom(e.Source) is not { } target)
        {
            return;
        }

        e.Handled = true;
        if (IsDuplicateDrag(e, source))
        {
            await vm.DuplicateNodeAsync(source, target);
        }
        else
        {
            await vm.MoveNodeAsync(source, target);
        }
    }

    /// <summary>База, перетаскиваемая с зажатым Ctrl, дублируется, а не перемещается.</summary>
    private static bool IsDuplicateDrag(DragEventArgs e, TreeNodeViewModel source) =>
        source is BaseNodeViewModel && e.KeyModifiers.HasFlag(KeyModifiers.Control);

    /// <summary>Дубликат ложится в личный список: в обычную папку или рядом с базой в ней (базу из общего списка тоже можно дублировать).</summary>
    private static bool CanDuplicateOn(TreeNodeViewModel target) =>
        target is FolderNodeViewModel { Kind: FolderKind.Regular } or BaseNodeViewModel;

    private static TreeNodeViewModel? NodeFrom(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as TreeNodeViewModel;

    private static bool CanDrag(TreeNodeViewModel node) => node switch
    {
        BaseNodeViewModel => true, // базу из общего списка можно бросить в «Избранное»
        FolderNodeViewModel folder => folder.IsEditable,
        _ => false,
    };

    private static bool CanDropOn(TreeNodeViewModel source, TreeNodeViewModel target) => target switch
    {
        FolderNodeViewModel { Kind: FolderKind.Favorites } => source is BaseNodeViewModel,
        FolderNodeViewModel { Kind: FolderKind.Regular } => source is BaseNodeViewModel { Base.InfoBase.IsReadOnly: false } or FolderNodeViewModel,
        BaseNodeViewModel => source is BaseNodeViewModel { Base.InfoBase.IsReadOnly: false } or FolderNodeViewModel,
        _ => false,
    };

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                FocusCurrentList();
                e.Handled = true;
                break;

            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                LaunchEnterprise();
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            LaunchEnterprise();
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Двойной щелчок по папке раскрывает её, по базе — запускает.
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext
                   ?? (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext;
        if (item is BaseNodeViewModel or BaseListItemViewModel)
        {
            LaunchEnterprise();
            e.Handled = true;
        }
    }

    /// <summary>Начали печатать в списке — продолжаем в поиске.</summary>
    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (ViewModel is not { } vm || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        vm.SearchText += e.Text;
        FocusSearch(selectAll: false);
        e.Handled = true;
    }

    /// <summary>Enter и двойной щелчок — 1С: Предприятие (как F3).</summary>
    private void LaunchEnterprise()
    {
        if (ViewModel is { } vm)
        {
            Execute(vm.LaunchEnterpriseCommand);
        }
    }

    private void FocusSearch(bool selectAll)
    {
        SearchBox.Focus();
        if (selectAll)
        {
            SearchBox.SelectAll();
        }
        else
        {
            SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
        }
    }

    private void FocusCurrentList()
    {
        if (ViewModel?.ShowTree == true)
        {
            CatalogTree.Focus(NavigationMethod.Directional);
            return;
        }

        if (CatalogList.SelectedIndex < 0 && CatalogList.ItemCount > 0)
        {
            CatalogList.SelectedIndex = 0;
        }

        (CatalogList.ContainerFromIndex(Math.Max(CatalogList.SelectedIndex, 0)) ?? CatalogList).Focus(NavigationMethod.Directional);
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinRestoredSize || placement.Height < MinRestoredSize)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Окно могло сохраниться на мониторе, которого больше нет.</summary>
    private void EnsureOnScreen()
    {
        var visible = Screens.All.Any(s => s.WorkingArea.Contains(Position + new PixelPoint(40, 20)));
        if (!visible && Screens.Primary is { } primary)
        {
            var area = primary.WorkingArea;
            var size = PixelSize.FromSize(ClientSize, DesktopScaling);
            Position = new PixelPoint(
                area.X + Math.Max(0, (area.Width - size.Width) / 2),
                area.Y + Math.Max(0, (area.Height - size.Height) / 2));
        }
    }

    private WindowPlacement CapturePlacement()
    {
        var previous = ViewModel?.WindowPlacement;

        // Закрыт свёрнутым (из трея, с панели задач): положение свёрнутого окна — за пределами экрана, не сохраняем.
        if (WindowState == WindowState.Minimized && previous is not null)
        {
            return previous with { IsMaximized = _restoreState == WindowState.Maximized };
        }

        var isMaximized = WindowState == WindowState.Maximized;

        // У развёрнутого окна сохраняем прежние размеры, чтобы после «восстановить» оно было нормальным.
        if (isMaximized && previous is not null)
        {
            return previous with { IsMaximized = true };
        }

        return new WindowPlacement
        {
            X = Position.X,
            Y = Position.Y,
            Width = Width,
            Height = Height,
            IsMaximized = isMaximized,
        };
    }
}
