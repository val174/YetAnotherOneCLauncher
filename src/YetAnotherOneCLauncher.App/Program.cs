using Avalonia;

namespace YetAnotherOneCLauncher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Используется и дизайнером XAML в IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LauncherApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}
