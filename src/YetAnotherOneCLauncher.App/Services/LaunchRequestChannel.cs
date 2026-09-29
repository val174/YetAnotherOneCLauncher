using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Аргумент запуска базы из списка переходов: <c>--launch=&lt;ключ базы в Base64Url&gt;</c>.</summary>
public static class LaunchArgument
{
    public const string Prefix = "--launch=";

    /// <summary>Аргумент для <see cref="Core.Model.InfoBase.IdentityKey"/>: без кавычек и пробелов, чтобы не зависеть от разбора командной строки.</summary>
    public static string Format(string identityKey) => Prefix + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(identityKey));

    /// <summary>Ключ базы из аргументов; <c>null</c> — аргумента нет или он повреждён.</summary>
    public static string? Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var value = args.FirstOrDefault(a => a.StartsWith(Prefix, StringComparison.Ordinal))?[Prefix.Length..];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Приём команд «запустить базу» от других экземпляров лаунчера.</summary>
public interface ILaunchRequestChannel
{
    /// <summary>Начать приём; обработчик вызывается из фонового потока.</summary>
    void Start(Action<string> onLaunchRequest);
}

/// <summary>
/// Именованный канал текущего пользователя: щелчок в списке переходов запускает второй экземпляр лаунчера,
/// он передаёт ключ базы уже открытому и завершается — база запускается из открытого лаунчера.
/// </summary>
public sealed partial class LaunchRequestChannel : ILaunchRequestChannel, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();

    public LaunchRequestChannel(ILogger<LaunchRequestChannel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Команда «показать окно» от повторно запущенного лаунчера (запрет повторного запуска).
    /// С ключом базы не спутать: те начинаются с <c>id:</c> или со строки подключения.
    /// </summary>
    public const string ActivateCommand = "!activate";

    /// <summary>Имя канала: своё у каждого пользователя и сеанса Windows.</summary>
    public static string PipeName =>
        $"YetAnotherOneCLauncher-{Environment.UserName}-{Process.GetCurrentProcess().SessionId}";

    /// <summary>Передать ключ базы открытому лаунчеру.</summary>
    /// <returns><c>false</c> — открытого лаунчера нет.</returns>
    public static bool TryForward(string identityKey)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeout);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(identityKey);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Start(Action<string> onLaunchRequest)
    {
        ArgumentNullException.ThrowIfNull(onLaunchRequest);
        _ = Task.Run(() => ListenAsync(onLaunchRequest, _stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task ListenAsync(Action<string> onLaunchRequest, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Один сервер на имя: если канал уже держит другой экземпляр лаунчера, команды получает он.
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                if (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } key)
                {
                    onLaunchRequest(key);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException ex)
            {
                LogChannelUnavailable(_logger, ex);
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Канал запуска баз занят другим экземпляром лаунчера")]
    private static partial void LogChannelUnavailable(ILogger logger, Exception exception);
}

/// <summary>База, которую нужно запустить сразу после загрузки (лаунчер запущен из списка переходов).</summary>
public sealed record StartupOptions(string? LaunchIdentityKey);
