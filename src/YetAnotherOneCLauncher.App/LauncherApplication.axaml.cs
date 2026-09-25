using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

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
            desktop.MainWindow = _services?.GetRequiredService<MainWindow>() ?? new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
