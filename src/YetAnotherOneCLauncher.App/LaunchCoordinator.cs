using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App;

/// <summary>Итог попытки запуска для показа пользователю.</summary>
/// <param name="Started">Процесс или браузер запущен.</param>
/// <param name="Message">Что произошло: для строки состояния или сообщения об ошибке.</param>
public sealed record LaunchOutcome(bool Started, string Message);

/// <summary>
/// Связывает план запуска из Core с запуском процесса из Platform:
/// строит план, при необходимости спрашивает пользователя, запускает и пишет в лог.
/// </summary>
public sealed partial class LaunchCoordinator
{
    private readonly IProcessLauncher _processLauncher;
    private readonly LaunchOptions _options;
    private readonly ILogger _logger;

    public LaunchCoordinator(IProcessLauncher processLauncher, LaunchOptions options, ILogger<LaunchCoordinator> logger)
    {
        _processLauncher = processLauncher;
        _options = options;
        _logger = logger;
    }

    /// <param name="request">Что запускать.</param>
    /// <param name="installations">Найденные платформы.</param>
    /// <param name="starterDefaultVersion"><c>DefaultVersion</c> из 1cestart.cfg.</param>
    /// <param name="confirm">Вопрос пользователю; <c>true</c> — согласен.</param>
    public async Task<LaunchOutcome> LaunchAsync(
        LaunchRequest request,
        IReadOnlyList<PlatformInstallation> installations,
        string? starterDefaultVersion,
        Func<string, Task<bool>> confirm)
    {
        var plan = LaunchPlanner.Plan(request, installations, starterDefaultVersion, _options);
        var name = request.InfoBase.Name;

        switch (plan)
        {
            case LaunchPlan.Failed failed:
                LogNotStarted(_logger, name, failed.Message);
                return new LaunchOutcome(false, failed.Message);

            case LaunchPlan.OpenInBrowser browser:
                return Execute(name, () => _processLauncher.OpenUrl(browser.Url), $"«{name}» открыта в браузере.", browser.Url.ToString());

            case LaunchPlan.ConfirmFallback fallback:
                LogWarnings(fallback.Warnings);
                if (!await confirm(fallback.Question).ConfigureAwait(true))
                {
                    return new LaunchOutcome(false, "Запуск отменён.");
                }

                return Start(name, request.Mode, fallback.Command);

            case LaunchPlan.Run run:
                LogWarnings(run.Warnings);
                return Start(name, request.Mode, run.Command);

            default:
                throw new InvalidOperationException($"Неизвестный план запуска: {plan.GetType().Name}");
        }
    }

    private LaunchOutcome Start(string name, LaunchMode mode, LaunchCommand command)
    {
        var modeText = mode == LaunchMode.Designer ? "Конфигуратор" : "Предприятие";
        return Execute(
            name,
            () => _processLauncher.Start(command),
            $"«{name}»: {modeText}, платформа {command.Platform}.",
            command.ToDisplayString());
    }

    private LaunchOutcome Execute(string name, Action action, string successMessage, string commandText)
    {
        try
        {
            action();
            LogStarted(_logger, name, commandText);
            return new LaunchOutcome(true, successMessage);
        }
        catch (LaunchFailedException ex)
        {
            LogStartFailed(_logger, name, commandText, ex);
            return new LaunchOutcome(false, ex.Message);
        }
    }

    private void LogWarnings(IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            LogLaunchWarning(_logger, warning);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Запуск «{Name}»: {Command}")]
    private static partial void LogStarted(ILogger logger, string name, string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не удалось запустить «{Name}»: {Command}")]
    private static partial void LogStartFailed(ILogger logger, string name, string command, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "«{Name}» не запущена: {Reason}")]
    private static partial void LogNotStarted(ILogger logger, string name, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Запуск: {Warning}")]
    private static partial void LogLaunchWarning(ILogger logger, string warning);
}
