using Avalonia.Controls;
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

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowTitleBar.Height, window.ExtendClientAreaTitleBarHeightHint);
        var root = Assert.IsType<DockPanel>(window.Content);
        var bar = Assert.IsType<Border>(root.Children[0]);
        Assert.Equal(WindowTitleBar.Name, bar.Name);
        Assert.Equal(Dock.Top, DockPanel.GetDock(bar));
        Assert.Single(bar.GetLogicalDescendants().OfType<Image>());
        Assert.Equal("Заголовок формы", bar.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal(2, root.Children.Count); // под заголовком — прежнее содержимое формы

        // У вспомогательных окон из кнопок заголовка — только «Закрыть» (проверено в живом окне: кнопки рисует Avalonia
        // в WindowDrawnDecorations рядом с окном, в headless их нет); главное окно сворачивается и разворачивается.
        var main = name == nameof(MainWindow);
        Assert.Equal(main, window.CanMinimize);
        Assert.Equal(main, window.CanMaximize);
        Assert.NotEqual(main, window.Classes.Contains(WindowTitleBar.AuxiliaryClass)); // по классу стили приложения убирают эти кнопки
    }
}
