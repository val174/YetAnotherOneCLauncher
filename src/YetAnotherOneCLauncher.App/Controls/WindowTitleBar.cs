using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Заголовок окна в стиле приложения (как у 1С): фон окна, свой значок и название, системные кнопки окна — справа
/// поверх строки. Один для всех окон лаунчера, чтобы главное окно и открываемые из него формы выглядели одинаково.
/// </summary>
public static class WindowTitleBar
{
    /// <summary>Имя строки заголовка в окне.</summary>
    public const string Name = "TitleBar";

    /// <summary>Высота области заголовка, которую просим у системы.</summary>
    public const double Height = 34;

    private static Bitmap? _icon;

    /// <summary>
    /// Продлить окно в область заголовка и поставить строку заголовка над содержимым. Вызывать после того, как
    /// содержимое окна задано (после InitializeComponent). Высота строки — у области заголовка, которую отдаёт система;
    /// без продления в заголовок (например, в тестах) — 0.
    /// У вспомогательных окон (<paramref name="mainWindow"/> = false) в заголовке только кнопка «Закрыть».
    /// </summary>
    public static void Apply(Window window, bool mainWindow = false)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = Height;
        if (!mainWindow)
        {
            window.CanMinimize = false;
            window.CanMaximize = false;
        }

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.TextProperty, window.GetObservable(Window.TitleProperty));
        title.Bind(TextBlock.FontSizeProperty, title.GetResourceObservable("SmallFontSize"));

        var bar = new Border
        {
            Name = Name,
            Background = Avalonia.Media.Brushes.Transparent,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Children = { new Image { Source = Icon, Width = 18, Height = 18 }, title },
            },
        };
        bar.Bind(Layoutable.HeightProperty, new Binding("WindowDecorationMargin.Top") { Source = window });
        WindowDecorationProperties.SetElementRole(bar, WindowDecorationsElementRole.TitleBar);
        DockPanel.SetDock(bar, Dock.Top);

        var content = (Control?)window.Content;
        window.Content = null;
        var root = new DockPanel { Children = { bar } };
        if (content is not null)
        {
            root.Children.Add(content);
        }

        window.Content = root;
    }

    private static Bitmap Icon => _icon ??= new Bitmap(AssetLoader.Open(new Uri("avares://YetAnotherOneCLauncher/Assets/app.png")));
}
