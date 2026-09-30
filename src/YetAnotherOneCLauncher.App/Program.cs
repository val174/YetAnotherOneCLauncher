using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;

namespace YetAnotherOneCLauncher.App;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Щелчок по базе в списке переходов Windows: если лаунчер уже открыт, база запускается в нём.
        var launchKey = LaunchArgument.Parse(args);
        if (launchKey is not null && Forward(launchKey))
        {
            return 0;
        }

        // Метка «лаунчер открыт» держится до выхода: по ней следующий экземпляр узнаёт, что он не первый.
        using var instance = new InstanceLock(InstanceLock.DefaultName);

        if (OperatingSystem.IsWindows())
        {
            // Один идентификатор у процесса и списка переходов — иначе Windows не свяжет их со значком.
            Platform.Windows.WindowsJumpList.SetProcessAppUserModelId();
        }

        using var services = AppServices.Build(new StartupOptions(launchKey));
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogUnhandled(logger, e.IsTerminating, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnobservedTask(logger, e.Exception);
            e.SetObserved();
        };

        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "?";
        LogStarting(
            logger,
            version,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            AppServices.LogDirectory);

        // Настройки нужны до показа окна (положение, тема). Контекста синхронизации ещё нет — ждать безопасно.
        var settings = services.GetRequiredService<UserSettingsService>();
        settings.LoadAsync().GetAwaiter().GetResult();

        // Повторный запуск запрещён: показываем уже открытый лаунчер (в том числе из трея) и выходим.
        if (!instance.IsFirst && settings.Settings.Ui.SingleInstance)
        {
            var shown = Forward(LaunchRequestChannel.ActivateCommand);
            LogAlreadyRunning(logger, shown);
            return 0;
        }

        // Списки баз и платформы начинают читаться в фоне уже сейчас — пока запускается интерфейс.
        services.GetService<StartupCatalog>()?.Begin();

        try
        {
            return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogUnhandled(logger, isTerminating: true, ex);
            throw;
        }
        finally
        {
            settings.FlushAsync().GetAwaiter().GetResult();
            LogStopped(logger);
        }
    }

    /// <summary>Передать команду открытому лаунчеру и разрешить ему выйти на передний план.</summary>
    private static bool Forward(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.WindowsForeground.AllowAnyProcess();
        }

        return LaunchRequestChannel.TryForward(command);
    }

    // Используется дизайнером XAML в IDE: без контейнера зависимостей.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(services: null);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services) =>
        AppBuilder.Configure(() => new LauncherApplication(services))
            .UsePlatformDetect()
            .LogToTrace();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Запуск YetAnotherOneCLauncher {Version}; ОС: {Os}; среда: {Runtime}; логи: {LogDirectory}")]
    private static partial void LogStarting(ILogger logger, string version, string os, string runtime, string logDirectory);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Лаунчер уже открыт, повторный запуск запрещён настройкой; окно показано: {Shown}")]
    private static partial void LogAlreadyRunning(ILogger logger, bool shown);

    [LoggerMessage(Level = LogLevel.Information, Message = "Приложение завершено")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Необработанное исключение (завершение: {IsTerminating})")]
    private static partial void LogUnhandled(ILogger logger, bool isTerminating, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Необработанное исключение в фоновой задаче")]
    private static partial void LogUnobservedTask(ILogger logger, Exception exception);
}
