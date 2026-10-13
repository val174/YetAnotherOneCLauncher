using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Editing;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Выход из лаунчера (в том числе при выключении Windows) не заканчивается окном «Ошибка приложения».</summary>
public class ShutdownTests
{
    [Fact]
    public void Launch_channel_can_be_disposed_twice()
    {
        var channel = new LaunchRequestChannel(NullLogger<LaunchRequestChannel>.Instance);

        channel.Dispose();
        channel.Dispose();
    }

    [Fact]
    public void Container_of_the_app_is_disposed_without_errors()
    {
        var services = AppServices.Build(new StartupOptions(LaunchIdentityKey: null));
        _ = services.GetService<ILaunchRequestChannel>();
        _ = services.GetService<IListChangeWatcher>();
        _ = services.GetService<PersonalListStore>();
        _ = services.GetRequiredService<UserSettingsService>();

        services.Dispose();
    }

    [Fact]
    public void Error_while_disposing_services_is_logged_not_thrown()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<ThrowingService>();
        var services = collection.BuildServiceProvider();
        _ = services.GetRequiredService<ThrowingService>();
        var logger = new CollectingLogger();

        Program.DisposeServices(services, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<ObjectDisposedException>(entry.Exception);
    }

    private sealed class ThrowingService : IDisposable
    {
        public void Dispose() => throw new ObjectDisposedException(nameof(ThrowingService));
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
