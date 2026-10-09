using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using YetAnotherOneCLauncher.App.Services;

namespace YetAnotherOneCLauncher.App;

public partial class LauncherApplication : Application
{
    private readonly IServiceProvider? _services;

    // Нужен дизайнеру XAML.
    public LauncherApplication()
        : this(services: null)
    {
    }

    public LauncherApplication(IServiceProvider? services)
    {
        _services = services;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Оттенок тёмной темы — до первого окна, чтобы при запуске не мелькал чёрный фон.
            if (_services?.GetService<UserSettingsService>() is { } settings)
            {
                DarkShades.Apply(this, settings.Settings.Ui.DarkShade);
            }

            desktop.MainWindow = _services?.GetRequiredService<MainWindow>() ?? new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
