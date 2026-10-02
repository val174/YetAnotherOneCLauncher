using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Окно «Средства администрирования»: инструменты из настроек и консоль кластера серверов.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Окно «Средства администрирования»: инструменты из таблицы «Инструменты» настроек, затем консоли кластера серверов —
    /// зарегистрированные и доступные к регистрации.
    /// </summary>
    [RelayCommand]
    private async Task OpenClusterConsoleAsync()
    {
        var console = new ClusterConsoleViewModel(
            _installations,
            _clusterConsole,
            _processLauncher,
            _settings.Settings.AdminTools,
            _toolIcons);
        // Для разбора «не видит консоль»: что нашлось в реестре и что попало в список.
        var registrations = console.Registrations.Count == 0
            ? "нет"
            : string.Join("; ", console.Registrations.Select(r => $"{r.Architecture} {r.LibraryPath} {r.SnapInClassId}{(r.IsActive ? string.Empty : " (не действует)")}"));
        var options = string.Join(
            "; ",
            console.Versions.Select(o => $"{o.Title} {o.Detail}{(o.IsRegistered ? " (зарегистрирована)" : string.Empty)}"));
        LogClusterConsoles(_logger, registrations, options);

        await _dialogs.ShowClusterConsoleAsync(console);
        if (console.ResultMessage is { } message)
        {
            StatusText = message;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Консоль кластера: в реестре — {Registrations}; в списке — {Options}")]
    private static partial void LogClusterConsoles(ILogger logger, string registrations, string options);
}
