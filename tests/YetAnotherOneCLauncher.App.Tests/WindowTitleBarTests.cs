using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using YetAnotherOneCLauncher.App.Controls;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Заголовок окна в стиле приложения — одинаковый у главного окна и у всех открываемых форм.</summary>
public class WindowTitleBarTests
{
    public static TheoryData<string> Windows =>
    [
        nameof(MainWindow), nameof(SettingsWindow), nameof(InfoBaseEditorWindow), nameof(LaunchParametersWindow),
        nameof(ClusterConsoleWindow), nameof(CacheManagerWindow), nameof(AboutWindow),
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void Window_has_app_title_bar_like_main_window(string name)
    {
        Window window = name switch
        {
            nameof(MainWindow) => new MainWindow(),
            nameof(SettingsWindow) => new SettingsWindow(),
            nameof(InfoBaseEditorWindow) => new InfoBaseEditorWindow(),
            nameof(LaunchParametersWindow) => new LaunchParametersWindow(),
            nameof(ClusterConsoleWindow) => new ClusterConsoleWindow(),
            nameof(CacheManagerWindow) => new CacheManagerWindow(),
            _ => new AboutWindow(),
        };
        window.Title = "Заголовок формы";

        var main = name == nameof(MainWindow);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        // Своя рамка по краю окна (системная в светлой теме не видна поверх белого); у развёрнутого её нет.
        var frame = Assert.IsType<Border>(window.Content);
        Assert.Equal((WindowTitleBar.FrameName, new Avalonia.Thickness(1)), (frame.Name, frame.BorderThickness));
        window.WindowState = WindowState.Maximized;
        Assert.Equal(default, frame.BorderThickness);
        var root = Assert.IsType<DockPanel>(frame.Child);
        if (main)
        {
            // Задача #31: у главного окна своей строки заголовка нет — заголовок окна это верхняя строка с панелью
            // команд, поиском и меню (высота области заголовка — её высота; пустые места перетаскивают окно).
            // Строка — на подложке во всю ширину (её роль тоже «заголовок»).
            var band = Assert.IsType<Border>(root.Children[0]);
            Assert.Equal("HeaderBand", band.Name);
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(band));
            var header = Assert.IsType<DockPanel>(band.Child);
            Assert.Equal("HeaderPanel", header.Name);
            Assert.Equal(header.Height, window.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(header));
            foreach (var interactive in new[] { "MenuButton", "SearchBox", "TopToolbarHost", "SideToolbarToggle" })
            {
                Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(window.FindControl<Control>(interactive)!));
            }

            Assert.DoesNotContain(root.GetLogicalDescendants().OfType<Border>(), b => b.Name == WindowTitleBar.Name);
        }
        else
        {
            Assert.Equal(WindowTitleBar.Height, window.ExtendClientAreaTitleBarHeightHint);
            var bar = Assert.IsType<Border>(root.Children[0]);
            Assert.Equal(WindowTitleBar.Name, bar.Name);
            Assert.Equal(Dock.Top, DockPanel.GetDock(bar));
            Assert.Single(bar.GetLogicalDescendants().OfType<Image>());
            Assert.Equal("Заголовок формы", bar.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
            Assert.Equal(2, root.Children.Count); // под заголовком — прежнее содержимое формы
        }

        // У вспомогательных окон из кнопок заголовка — только «Закрыть» (проверено в живом окне: кнопки рисует Avalonia
        // в WindowDrawnDecorations рядом с окном, в headless их нет); главное окно сворачивается и разворачивается.
        Assert.Equal(main, window.CanMinimize);
        Assert.Equal(main, window.CanMaximize);
        Assert.NotEqual(main, window.Classes.Contains(WindowTitleBar.AuxiliaryClass)); // по классу стили приложения убирают эти кнопки
    }
}
