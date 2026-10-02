using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Chrome;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        // У вспомогательных окон из кнопок заголовка — только «Закрыть»; главное окно сворачивается и разворачивается.
        var main = name == nameof(MainWindow);
        Assert.Equal(main, window.CanMinimize);
        Assert.Equal(main, window.CanMaximize);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Auxiliary_window_title_bar_has_only_close_button(bool main)
    {
        Window window = main ? new MainWindow() : new AboutWindow();
        // Заголовок, который рисует Avalonia: в тестах система его не просит — ставим его кнопки сами в окно.
        var decorations = new WindowDrawnDecorations();
        var overlay = new Panel();
        ((DockPanel)window.Content!).Children.Insert(0, overlay);
        overlay.Children.Add(Assert.IsType<WindowDrawnDecorationsContent>(BuildTemplate(decorations).Result).Overlay!);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = overlay.GetVisualDescendants().OfType<Control>().Where(c => c.Name is "PART_CloseButton" or "PART_MinimizeButton" or "PART_MaximizeButton")
            .ToDictionary(c => c.Name!);
        Assert.True(buttons["PART_CloseButton"].IsVisible);
        Assert.Equal(main, buttons["PART_MinimizeButton"].IsVisible);
        Assert.Equal(main, buttons["PART_MaximizeButton"].IsVisible);
        window.Close();
    }

    // Шаблон рисуемого заголовка из темы приложения (Fluent).
    private static Avalonia.Controls.Templates.TemplateResult<WindowDrawnDecorationsContent> BuildTemplate(WindowDrawnDecorations decorations)
    {
        var theme = Assert.IsType<Avalonia.Styling.ControlTheme>(Avalonia.Application.Current!.FindResource(typeof(WindowDrawnDecorations)));
        decorations.Theme = theme;
        decorations.ApplyStyling();
        return Assert.IsAssignableFrom<IWindowDrawnDecorationsTemplate>(decorations.Template).Build();
    }
}
