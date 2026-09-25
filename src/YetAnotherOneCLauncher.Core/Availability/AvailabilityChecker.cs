using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Availability;

public enum AvailabilityStatus
{
    /// <summary>Не проверялась или проверить нельзя (строка подключения не распознана).</summary>
    Unknown,

    Available,

    Unavailable,
}

/// <param name="Status">Итог.</param>
/// <param name="Message">Что именно проверено: «каталог не найден», «srv1c:1541 не отвечает».</param>
public sealed record AvailabilityResult(AvailabilityStatus Status, string Message)
{
    public static AvailabilityResult Unknown { get; } = new(AvailabilityStatus.Unknown, "не проверялась");
}

/// <summary>Низкоуровневые проверки — отдельно, чтобы подменять в тестах.</summary>
public interface IAvailabilityProbe
{
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken);
}

/// <summary>Проверки средствами .NET: каталог и TCP-подключение.</summary>
public sealed class DefaultAvailabilityProbe : IAvailabilityProbe
{
    // Directory.Exists на недоступном сетевом пути может висеть — поэтому в фоне; ожидание ограничивает вызывающий.
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Directory.Exists(path), cancellationToken);

    public async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

/// <summary>
/// Фоновая проверка доступности баз: у файловой — существует ли каталог, у серверной — отвечает ли менеджер
/// кластера по TCP (по умолчанию порт 1541), у веб-базы — отвечает ли веб-сервер. Одинаковые адреса проверяются
/// один раз; одновременно — не больше <see cref="MaxParallel"/> проверок, каждая ограничена по времени.
/// </summary>
public sealed class AvailabilityChecker
{
    public const int DefaultClusterPort = 1541;
    public const int MaxParallel = 8;

    private readonly IAvailabilityProbe _probe;
    private readonly TimeSpan _timeout;

    public AvailabilityChecker(IAvailabilityProbe? probe = null, TimeSpan? timeout = null)
    {
        _probe = probe ?? new DefaultAvailabilityProbe();
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    /// <param name="infoBases">Что проверить.</param>
    /// <param name="progress">Результат по каждой базе, как только он готов (ключ — <see cref="InfoBase.IdentityKey"/>).</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Результаты по <see cref="InfoBase.IdentityKey"/>.</returns>
    public async Task<IReadOnlyDictionary<string, AvailabilityResult>> CheckAsync(
        IEnumerable<InfoBase> infoBases,
        IProgress<KeyValuePair<string, AvailabilityResult>>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(infoBases);
        var targets = infoBases
            .GroupBy(b => b.IdentityKey, StringComparer.Ordinal)
            .Select(g => (Key: g.Key, Target: TargetOf(g.First())))
            .ToList();

        var results = new ConcurrentDictionary<string, AvailabilityResult>(StringComparer.Ordinal);
        var probes = new ConcurrentDictionary<Target, Lazy<Task<AvailabilityResult>>>();
        using var throttle = new SemaphoreSlim(MaxParallel);

        await Task.WhenAll(targets.Select(async item =>
        {
            var result = item.Target is { } target
                ? await probes.GetOrAdd(target, t => new Lazy<Task<AvailabilityResult>>(() => ProbeAsync(t, throttle, cancellationToken))).Value
                    .ConfigureAwait(false)
                : AvailabilityResult.Unknown;
            results[item.Key] = result;
            progress?.Report(new KeyValuePair<string, AvailabilityResult>(item.Key, result));
        })).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Адрес менеджера кластера из <c>Srvr</c>: «srv», «srv:1641», «tcp://srv:1541», «[::1]:1541»;
    /// из нескольких адресов через запятую берётся первый.
    /// </summary>
    public static (string Host, int Port)? ParseServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return null;
        }

        var first = server.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first))
        {
            return null;
        }

        var schemeEnd = first.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            first = first[(schemeEnd + 3)..];
        }

        string host;
        string? port = null;
        if (first.StartsWith('['))
        {
            var close = first.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            host = first[1..close];
            if (close + 1 < first.Length && first[close + 1] == ':')
            {
                port = first[(close + 2)..];
            }
        }
        else
        {
            var colon = first.LastIndexOf(':');
            host = colon >= 0 ? first[..colon] : first;
            port = colon >= 0 ? first[(colon + 1)..] : null;
        }

        if (host.Length == 0)
        {
            return null;
        }

        if (port is null)
        {
            return (host, DefaultClusterPort);
        }

        return int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is > 0 and < 65536
            ? (host, value)
            : null;
    }

    private static Target? TargetOf(InfoBase infoBase)
    {
        var connection = infoBase.Connection;
        switch (connection.Kind)
        {
            case ConnectionKind.File when !string.IsNullOrWhiteSpace(connection.FilePath):
                return new Target(TargetKind.Directory, connection.FilePath!, 0);

            case ConnectionKind.Server when ParseServer(connection.Server) is { } endpoint:
                return new Target(TargetKind.Tcp, endpoint.Host, endpoint.Port);

            case ConnectionKind.Web when Uri.TryCreate(connection.WebUrl, UriKind.Absolute, out var url) && !url.IsFile:
                return new Target(TargetKind.Tcp, url.Host, url.Port);

            default:
                return null;
        }
    }

    private async Task<AvailabilityResult> ProbeAsync(Target target, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                if (target.Kind == TargetKind.Directory)
                {
                    var exists = await _probe.DirectoryExistsAsync(target.Address, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                    return exists
                        ? new AvailabilityResult(AvailabilityStatus.Available, "каталог базы найден")
                        : new AvailabilityResult(AvailabilityStatus.Unavailable, "каталог базы не найден: " + target.Address);
                }

                var endpoint = target.Address.Contains(':', StringComparison.Ordinal)
                    ? $"[{target.Address}]:{target.Port.ToString(CultureInfo.InvariantCulture)}"
                    : $"{target.Address}:{target.Port.ToString(CultureInfo.InvariantCulture)}";
                return await _probe.CanConnectAsync(target.Address, target.Port, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false)
                    ? new AvailabilityResult(AvailabilityStatus.Available, endpoint + " отвечает")
                    : new AvailabilityResult(AvailabilityStatus.Unavailable, endpoint + " не отвечает");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AvailabilityResult(
                    AvailabilityStatus.Unavailable,
                    string.Create(CultureInfo.CurrentCulture, $"{target.Address}: нет ответа за {_timeout.TotalSeconds:0} с"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException or ArgumentException)
            {
                return new AvailabilityResult(AvailabilityStatus.Unavailable, $"{target.Address}: {ex.Message}");
            }
        }
        finally
        {
            throttle.Release();
        }
    }

    private enum TargetKind
    {
        Directory,
        Tcp,
    }

    private sealed record Target(TargetKind Kind, string Address, int Port);
}
