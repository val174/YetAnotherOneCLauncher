using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Главное окно. Логика — в <see cref="MainWindowViewModel"/>; здесь только то, что относится к представлению:
/// положение окна, фокус и сочетания клавиш.
/// </summary>
/// <remarks>
/// Клавиши: Ctrl+F — поиск; Esc — очистить поиск; ↓ из поиска — к списку; Enter — Предприятие;
/// Ctrl+Enter — Конфигуратор; Ctrl+D — избранное; F5 — обновить; Ctrl+N / Ins — новая база; Ctrl+Shift+N — папка;
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

    private PointerPressedEventArgs? _dragStart;
    private bool _focusRestorePending;
    private Control? _focusedList;

    // Нужен дизайнеру XAML; в приложении окно создаётся из контейнера.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
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
            EnsureOnScreen();
            SearchBox.Focus();
            await viewModel.InitializeAsync();
        };

        Closing += (_, _) => viewModel.WindowPlacement = CapturePlacement();

        // Панель подробностей: ширина — из настроек, меняется разделителем; кнопки над ней следуют за шириной.
        ApplyDetailsLayout();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.ShowDetails) or nameof(MainWindowViewModel.DetailsWidth))
            {
                ApplyDetailsLayout();
            }
        };
        DetailsSplitter.DragDelta += (_, _) => AlignToolbar(DetailsColumn.ActualWidth);
        DetailsSplitter.DragCompleted += (_, _) =>
        {
            viewModel.DetailsWidth = DetailsColumn.ActualWidth;
            ApplyDetailsLayout(); // ширина могла упереться в пределы
        };
    }

    private ColumnDefinition DetailsColumn => BodyGrid.ColumnDefinitions[2];

    private void ApplyDetailsLayout()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var width = vm.ShowDetails ? vm.DetailsWidth : 0;
        BodyGrid.ColumnDefinitions[0].MinWidth = MinListWidth;
        DetailsColumn.MinWidth = vm.ShowDetails ? MainWindowViewModel.MinDetailsWidth : 0;
        DetailsColumn.MaxWidth = vm.ShowDetails ? MainWindowViewModel.MaxDetailsWidth : 0;
        DetailsColumn.Width = new GridLength(width);
        AlignToolbar(width);
    }

    /// <summary>Кнопки над панелью занимают её ширину — поле поиска заканчивается над краем списка баз.</summary>
    private void AlignToolbar(double detailsWidth)
    {
        var withDetails = ViewModel?.ShowDetails == true;
        ToolbarPanel.Width = withDetails ? detailsWidth + DetailsSplitter.Width : double.NaN;
        ToolbarPanel.Margin = withDetails ? default : new Thickness(6, 0, 0, 0);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                FocusSearch(selectAll: true);
                e.Handled = true;
                break;

            // Физическая клавиша — чтобы Ctrl+Q работал и в русской раскладке (Ctrl+Й).
            case var _ when e.KeyModifiers == KeyModifiers.Control && (e.Key == Key.Q || e.PhysicalKey == PhysicalKey.Q):
                vm.SearchText = string.Empty;
                FocusSearch(selectAll: false);
                e.Handled = true;
                break;

            case Key.Escape when vm.HasSearch:
                vm.SearchText = string.Empty;
                FocusSearch(selectAll: false);
                e.Handled = true;
                break;

            case Key.F5:
                vm.ReloadCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.D when e.KeyModifiers == KeyModifiers.Control:
                Execute(vm.ToggleFavoriteCommand);
                e.Handled = true;
                break;

            case Key.N when e.KeyModifiers == KeyModifiers.Control:
                Execute(vm.AddBaseCommand);
                e.Handled = true;
                break;

            case Key.N when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                Execute(vm.AddFolderCommand);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Клавиши правки — раньше, чем их обработает дерево (стрелки оно забирает себе).</summary>
    private void OnListPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        ICommand? command = (e.Key, e.KeyModifiers) switch
        {
            (Key.F2, KeyModifiers.None) => vm.EditCommand,
            (Key.Delete, KeyModifiers.None) => vm.DeleteCommand,
            (Key.Insert, KeyModifiers.None) => vm.AddBaseCommand,
            (Key.Up, KeyModifiers.Alt) => vm.MoveUpCommand,
            (Key.Down, KeyModifiers.Alt) => vm.MoveDownCommand,
            _ => null,
        };

        if (command is not null)
        {
            Execute(command);
            e.Handled = true;
        }
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
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var source = e.DataTransfer.TryGetValue(TreeNodeFormat);
        var target = NodeFrom(e.Source);
        e.DragEffects = source is not null && target is not null && !ReferenceEquals(source, target) && CanDropOn(source, target)
            ? DragDropEffects.Move
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
        await vm.MoveNodeAsync(source, target);
    }

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

            case Key.Enter:
                Launch(e.KeyModifiers);
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Launch(e.KeyModifiers);
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
            Launch(KeyModifiers.None);
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

    private void Launch(KeyModifiers modifiers)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        ICommand command = modifiers switch
        {
            KeyModifiers.Control | KeyModifiers.Shift => vm.LaunchWithParametersCommand,
            _ when modifiers.HasFlag(KeyModifiers.Control) => vm.LaunchDesignerCommand,
            _ => vm.LaunchEnterpriseCommand,
        };
        if (command.CanExecute(null))
        {
            command.Execute(null);
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
