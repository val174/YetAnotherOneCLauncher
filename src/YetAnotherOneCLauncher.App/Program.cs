using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace YetAnotherOneCLauncher.App;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var services = AppServices.Build();
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
            LogStopped(logger);
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Приложение завершено")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Необработанное исключение (завершение: {IsTerminating})")]
    private static partial void LogUnhandled(ILogger logger, bool isTerminating, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Необработанное исключение в фоновой задаче")]
    private static partial void LogUnobservedTask(ILogger logger, Exception exception);
}
