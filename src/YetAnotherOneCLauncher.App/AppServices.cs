using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App;

/// <summary>Сборка контейнера зависимостей и настройка логирования.</summary>
internal static class AppServices
{
    private const long LogFileSizeLimitBytes = 10 * 1024 * 1024;
    private const int RetainedLogFileCount = 14;

    public static ServiceProvider Build()
    {
        // На неподдерживаемой ОС путей 1С нет, но окно и лог должны работать, чтобы показать ошибку.
        var paths = PlatformServices.IsSupported ? PlatformServices.CreatePaths() : null;
        var appDataDirectory = paths?.AppDataDirectory
                               ?? Path.Combine(Path.GetTempPath(), PlatformServices.AppFolderName);
        LogDirectory = Path.Combine(appDataDirectory, "logs");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .ClearProviders()
            .SetMinimumLevel(MinimumLevel)
            .AddSerilog(CreateFileLogger(LogDirectory), dispose: true));

        if (paths is not null)
        {
            services.AddSingleton(paths);
            services.AddSingleton<IPlatformLocator, PlatformLocator>();
        }

        // Без путей ОС (неподдерживаемая система) настройки живут только в памяти.
        var settingsStore = paths is null ? null : new SettingsStore(paths.AppDataDirectory);
        services.AddSingleton(sp => new UserSettingsService(settingsStore, sp.GetRequiredService<ILogger<UserSettingsService>>()));

        services.AddSingleton(new CatalogLoadOptions());
        services.AddSingleton<InfoBaseCatalogLoader>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<LaunchCoordinator>();

        services.AddSingleton<DesktopUiServices>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DesktopUiServices>());
        services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<DesktopUiServices>());
        services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<DesktopUiServices>());
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<DesktopUiServices>());

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>Каталог логов; заполняется в <see cref="Build"/>.</summary>
    public static string LogDirectory { get; private set; } = string.Empty;

#if DEBUG
    private static LogLevel MinimumLevel => LogLevel.Debug;
#else
    private static LogLevel MinimumLevel => LogLevel.Information;
#endif

    private static Logger CreateFileLogger(string logDirectory) =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose() // фильтрует Microsoft.Extensions.Logging
            .WriteTo.File(
                Path.Combine(logDirectory, "launcher-.log"),
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedLogFileCount,
                fileSizeLimitBytes: LogFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: true) // несколько экземпляров лаунчера пишут в один файл
            .CreateLogger();
}
