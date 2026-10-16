using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Панель команд сверху или слева; верхняя строка — заголовок окна (задача #31).</summary>
public class ToolbarLayoutTests
{
    private static double LeftEdge(Control control, Window window) => control.TranslatePoint(default, window)!.Value.X;

    private static double RightEdge(Control control, Window window) => LeftEdge(control, window) + control.Bounds.Width;

    [AvaloniaFact]
    public async Task Top_layout_has_toolbar_in_header_search_and_menu_on_the_right()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;

        Assert.True(vm.IsToolbarTop);
        Assert.Same(window.FindControl<Border>("TopToolbarHost"), window.FindControl<StackPanel>("ToolbarFrame")!.Parent);
        Assert.False(window.FindControl<Border>("SideToolbarHost")!.IsVisible);
        Assert.False(window.FindControl<Button>("SideToolbarToggle")!.IsVisible); // кнопки «скрыть панель» сверху нет
        Assert.Equal(Orientation.Horizontal, window.FindControl<StackPanel>("ToolbarButtons")!.Orientation);
        var search = window.FindControl<TextBox>("SearchBox")!;
        var menu = window.FindControl<Button>("MenuButton")!;
        Assert.True(LeftEdge(search, window) > RightEdge(window.FindControl<Border>("TopToolbarHost")!, window) + 40); // место для перетаскивания
        Assert.True(LeftEdge(menu, window) > RightEdge(search, window));
        Assert.Equal(48, window.FindControl<DockPanel>("HeaderPanel")!.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Left_layout_moves_toolbar_to_side_centers_search_and_toggle_hides_it()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;
        var side = window.FindControl<Border>("SideToolbarHost")!;
        var toolbar = window.FindControl<Panel>("ToolbarPanel")!;

        vm.ToolbarPlacementIndex = (int)ToolbarPlacement.Left;
        MainWindowTests.Render();

        Assert.Equal(ToolbarPlacement.Left, fixture.Settings.Settings.Ui.ToolbarPlacement);
        Assert.Contains("toolbarLeft", window.Classes);
        Assert.Same(side, window.FindControl<StackPanel>("ToolbarFrame")!.Parent);
        Assert.True(side.IsVisible);
        Assert.False(window.FindControl<Border>("TopToolbarHost")!.IsVisible);
        Assert.Equal(Orientation.Vertical, window.FindControl<StackPanel>("ToolbarButtons")!.Orientation);
        Assert.Equal(Orientation.Vertical, window.FindControl<StackPanel>("ListFilterSegments")!.Orientation);
        Assert.True(toolbar.Bounds.Height > toolbar.Bounds.Width); // столбиком
        var add = window.FindControl<Button>("AddButton")!;
        var delete = window.FindControl<Button>("DeleteButton")!;
        Assert.True(delete.TranslatePoint(default, window)!.Value.Y > add.TranslatePoint(default, window)!.Value.Y);

        // Поиск — по центру строки: пустого места слева и справа поровну (± кнопка скрытия слева и меню справа).
        var search = window.FindControl<TextBox>("SearchBox")!;
        var host = window.FindControl<Grid>("SearchHost")!;
        var leftGap = LeftEdge(search, window) - LeftEdge(host, window);
        var rightGap = RightEdge(host, window) - RightEdge(search, window);
        Assert.Equal(leftGap, rightGap, tolerance: 1.5);

        // Плашка переключателя режимов едет вниз.
        vm.IsFavoritesMode = true;
        MainWindowTests.Render();
        Assert.True(window.FindControl<Border>("ListFilterThumb")!.Margin.Top > 0);
        MainWindowTests.Snapshot(window, "55-toolbar-left");

        var toggle = window.FindControl<Button>("SideToolbarToggle")!;
        Assert.True(toggle.IsVisible);
        Assert.Contains("checked", toggle.Classes);
        toggle.Command!.Execute(null);
        MainWindowTests.Render();
        Assert.False(side.IsVisible);
        Assert.DoesNotContain("checked", toggle.Classes);
        Assert.False(fixture.Settings.Settings.Ui.ShowSideToolbar);
        MainWindowTests.Snapshot(window, "55-toolbar-left-hidden");

        // Обратно сверху — панель снова в верхней строке, видна (скрывается только слева).
        vm.ToolbarPlacementIndex = (int)ToolbarPlacement.Top;
        MainWindowTests.Render();
        Assert.Same(window.FindControl<Border>("TopToolbarHost"), window.FindControl<StackPanel>("ToolbarFrame")!.Parent);
        Assert.True(toolbar.IsEffectivelyVisible);
        Assert.Equal(Orientation.Horizontal, window.FindControl<StackPanel>("ToolbarButtons")!.Orientation);
        Assert.Equal(0, window.FindControl<Border>("ListFilterThumb")!.Margin.Top);
        MainWindowTests.Snapshot(window, "55-toolbar-top");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Left_toolbar_in_low_window_moves_extra_buttons_to_overflow_menu()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;
        var overflow = window.FindControl<Button>("ToolbarOverflowButton")!;
        var clip = window.FindControl<Panel>("ToolbarClip")!;
        var side = window.FindControl<Border>("SideToolbarHost")!;
        double Top(Control c) => c.TranslatePoint(default, window)!.Value.Y;

        vm.ToolbarPlacementIndex = (int)ToolbarPlacement.Left;
        window.Height = 900;
        MainWindowTests.Render();
        Assert.False(overflow.IsVisible); // всё помещается
        Assert.True(double.IsNaN(clip.Height));

        window.Height = 400;
        MainWindowTests.Render();
        MainWindowTests.Render();
        Assert.True(overflow.IsVisible);
        // Видимы только целые кнопки; «…» — под последней видимой и внутри полосы.
        var buttons = window.FindControl<StackPanel>("ToolbarButtons")!.Children;
        Assert.All(buttons.Where(c => c.Bounds.Bottom <= clip.Height), c => Assert.True(c.Bounds.Bottom <= clip.Bounds.Height));
        Assert.DoesNotContain("toolbarSeparator", buttons.Last(c => c.Bounds.Bottom <= clip.Height).Classes);
        Assert.True(Top(overflow) >= Top(clip) + clip.Bounds.Height);
        Assert.True(Top(overflow) + overflow.Bounds.Height <= Top(side) + side.Bounds.Height);
        MainWindowTests.Snapshot(window, "56-toolbar-left-overflow");

        var menu = Assert.IsType<MenuFlyout>(overflow.Flyout);
        menu.ShowAt(overflow);
        MainWindowTests.Render();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Показать плоским списком", "Развернуть все папки", "Свернуть все папки"], items.TakeLast(3).Select(i => (string)i.Header!));
        Assert.All(items, i => Assert.NotNull(i.Icon));
        Assert.IsNotType<Separator>(menu.Items[0]);
        Assert.IsNotType<Separator>(menu.Items[^1]);
        MainWindowTests.Snapshot(window, "56-toolbar-left-overflow-menu");
        items[^3].Command!.Execute(items[^3].CommandParameter);
        menu.Hide();
        Assert.False(vm.IsTreeMode);

        // Окно снова высокое — меню «…» не нужно.
        window.Height = 900;
        MainWindowTests.Render();
        MainWindowTests.Render();
        Assert.False(overflow.IsVisible);
        Assert.True(double.IsNaN(clip.Height));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Top_toolbar_in_narrow_window_moves_extra_buttons_to_overflow_menu()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var overflow = window.FindControl<Button>("ToolbarOverflowButton")!;
        var clip = window.FindControl<Panel>("ToolbarClip")!;
        var search = window.FindControl<TextBox>("SearchBox")!;
        Assert.False(overflow.IsVisible); // 1040 — всё помещается
        Assert.True(double.IsNaN(clip.Width));

        window.Width = 640;
        MainWindowTests.Render();
        MainWindowTests.Render();
        Assert.True(overflow.IsVisible);
        Assert.Same(window.FindControl<Border>("TopToolbarHost"), window.FindControl<StackPanel>("ToolbarFrame")!.Parent);
        // Видимы только целые кнопки, «…» — справа от последней; поиск не уже наименьшей ширины и правее «…».
        var buttons = window.FindControl<StackPanel>("ToolbarButtons")!.Children;
        var shown = buttons.Where(c => c.IsVisible && c.Bounds.Right <= clip.Width).ToList();
        Assert.DoesNotContain("toolbarSeparator", shown[^1].Classes);
        Assert.True(LeftEdge(overflow, window) >= RightEdge(clip, window));
        Assert.True(LeftEdge(search, window) > RightEdge(overflow, window));
        Assert.True(search.Bounds.Width >= 200 - 0.5);
        Assert.True(RightEdge(search, window) < LeftEdge(window.FindControl<Button>("ThemeButton")!, window));
        MainWindowTests.Snapshot(window, "57-toolbar-top-overflow");

        var menu = Assert.IsType<MenuFlyout>(overflow.Flyout);
        Assert.Equal(Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft, menu.Placement);
        menu.ShowAt(overflow);
        MainWindowTests.Render();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Показать плоским списком", "Развернуть все папки", "Свернуть все папки"], items.TakeLast(3).Select(i => (string)i.Header!));
        MainWindowTests.Snapshot(window, "57-toolbar-top-overflow-menu");
        menu.Hide();

        window.Width = 1040;
        MainWindowTests.Render();
        MainWindowTests.Render();
        Assert.False(overflow.IsVisible);
        Assert.True(double.IsNaN(clip.Width));
        window.Close();
    }

    [AvaloniaFact]
    public void Settings_window_offers_placement()
    {
        var settings = new ViewModels.SettingsViewModel(new ViewModels.SettingsValues { ToolbarPlacementIndex = 1 });
        var window = new SettingsWindow(settings);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        MainWindowTests.Render();

        var box = window.FindControl<ComboBox>("ToolbarPlacementBox")!;
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal(["Сверху", "Слева"], settings.ToolbarPlacementNames);
        Assert.False(settings.IsAppearanceDirty);
        settings.ToolbarPlacementIndex = 0;
        Assert.True(settings.IsAppearanceDirty);
        Assert.Equal(0, settings.Result.ToolbarPlacementIndex);
        MainWindowTests.Snapshot(window, "55-settings-toolbar-placement");
        window.Close();
    }
}
