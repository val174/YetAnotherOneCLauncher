using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// Ctrl+Enter — Конфигуратор; Ctrl+D — избранное; F5 — обновить. Набор текста в списке уходит в поиск.
/// </remarks>
public partial class MainWindow : Window
{
    private const double MinRestoredSize = 200;

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
            list.DoubleTapped += OnListDoubleTapped;
            list.TextInput += OnListTextInput;
        }

        Opened += async (_, _) =>
        {
            EnsureOnScreen();
            SearchBox.Focus();
            await viewModel.InitializeAsync();
        };

        Closing += (_, _) => viewModel.WindowPlacement = CapturePlacement();
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
                if (vm.ToggleFavoriteCommand.CanExecute(null))
                {
                    vm.ToggleFavoriteCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }

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

        var command = modifiers.HasFlag(KeyModifiers.Control) ? vm.LaunchDesignerCommand : vm.LaunchEnterpriseCommand;
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
