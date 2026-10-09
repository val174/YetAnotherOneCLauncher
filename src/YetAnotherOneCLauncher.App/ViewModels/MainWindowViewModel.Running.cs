using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Подсветка запущенных баз: зелёная точка справа от наименования. Раз в несколько секунд читаются процессы платформы
/// текущего пользователя (1cv8, 1cv8c) и их командные строки — база запущена, если в командной строке её адрес
/// (<c>/F</c>, <c>/S</c>, <c>/WS</c>, <c>/IBConnectionString</c>) или имя (<c>/IBName</c>). Так видны и базы,
/// открытые не из лаунчера (стартером 1С, ярлыком), и открытые до запуска лаунчера.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Как часто перечитывать процессы.</summary>
    internal static readonly TimeSpan RunningRefreshInterval = TimeSpan.FromSeconds(3);

    // Ключ базы (IdentityKey) → в каких клиентах открыта.
    private Dictionary<string, IReadOnlyList<string>> _running = new(StringComparer.Ordinal);
    // Номер текущего слежения; 0 — не следим. Цикл слежения заканчивается, когда номер сменился.
    private int _runningWatch;
    private int _runningWatchCounter;
    private int _runningGeneration;

    /// <summary>Подсвечивать запущенные базы (настройка «Внешний вид»).</summary>
    [ObservableProperty]
    public partial bool HighlightRunning { get; set; }

    partial void OnHighlightRunningChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.HighlightRunningBases = value;
        _settings.RequestSave();
        if (value)
        {
            StartRunningWatch();
        }
        else
        {
            StopRunningWatch();
            _runningGeneration++;
            _running.Clear();
            ApplyRunning();
        }
    }

    /// <summary>Начать следить за процессами (при открытии окна и при включении подсветки).</summary>
    internal void StartRunningWatch()
    {
        if (!HighlightRunning || _cacheUsage is null || _runningWatch != 0)
        {
            return;
        }

        _runningWatch = ++_runningWatchCounter;
        _ = WatchRunningAsync(_runningWatch);
    }

    /// <summary>Перестать следить (подсветку выключили, окно закрывается).</summary>
    internal void StopRunningWatch() => _runningWatch = 0;

    private async Task WatchRunningAsync(int watch)
    {
        while (watch == _runningWatch)
        {
            await RefreshRunningAsync();
            await Task.Delay(RunningRefreshInterval);
        }
    }

    /// <summary>Перечитать процессы платформы и обновить точки (по таймеру и сразу после запуска базы).</summary>
    internal async Task RefreshRunningAsync()
    {
        if (!HighlightRunning || _cacheUsage is null)
        {
            return;
        }

        var generation = ++_runningGeneration;
        var bases = _bases.Select(b => b.InfoBase).ToList();
        Dictionary<string, IReadOnlyList<string>> running;
        try
        {
            var probe = _cacheUsage;
            running = await Task.Run(() => FindRunning(probe.CurrentUserPlatformProcesses(), bases));
        }
        catch (Exception ex)
        {
            LogRunningFailed(_logger, ex);
            return;
        }

        if (generation != _runningGeneration || !HighlightRunning)
        {
            return; // пока читали, начали новое чтение или подсветку выключили
        }

        _running = running;
        ApplyRunning();
        RefreshEdtOpenState();
    }

    /// <summary>Какие базы открыты и в каких клиентах. Командная строка каждого процесса разбирается один раз.</summary>
    internal static Dictionary<string, IReadOnlyList<string>> FindRunning(IReadOnlyList<PlatformProcess> processes, IReadOnlyList<Core.Model.InfoBase> bases)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (processes.Count == 0)
        {
            return result;
        }

        var parsed = processes.Select(p =>
        {
            var (connections, names) = PlatformCommandLine.Parse(p.CommandLine);
            return (Client: ClientName(p),
                    Keys: connections.Select(c => c.ToNormalizedKey()).ToHashSet(StringComparer.Ordinal),
                    Names: names.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }).Where(p => p.Keys.Count > 0 || p.Names.Count > 0).ToList();

        foreach (var infoBase in bases)
        {
            var key = infoBase.Connection.ToNormalizedKey();
            var clients = parsed.Where(p => p.Keys.Contains(key) || p.Names.Contains(infoBase.Name.Trim()))
                .Select(p => p.Client).Distinct(StringComparer.Ordinal).ToList();
            if (clients.Count > 0)
            {
                result[infoBase.IdentityKey] = clients;
            }
        }

        return result;
    }

    private static string ClientName(PlatformProcess process) =>
        process.CommandLine.Contains(" DESIGNER", StringComparison.OrdinalIgnoreCase) ? "Конфигуратор"
        : string.Equals(process.Name, "1cv8c", StringComparison.OrdinalIgnoreCase) ? "тонкий клиент"
        : "толстый клиент";

    /// <summary>Разложить найденное по текущим базам (после чтения процессов и перестройки каталога).</summary>
    private void ApplyRunning()
    {
        foreach (var infoBase in _bases)
        {
            infoBase.SetRunning(_running.GetValueOrDefault(infoBase.InfoBase.IdentityKey) ?? []);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось прочитать процессы платформы для подсветки запущенных баз")]
    private static partial void LogRunningFailed(ILogger logger, Exception exception);
}
