using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Консоль кластера серверов и переход в сервис «ПУСК».</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Адрес сервиса «ПУСК» как введён в настройках.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPuskUrlInvalid))]
    public partial string PuskUrl { get; set; } = string.Empty;

    /// <summary>Адрес введён, но это не адрес http(s): переход в «ПУСК» не появится.</summary>
    public bool IsPuskUrlInvalid => !string.IsNullOrWhiteSpace(PuskUrl) && NetworkSettings.ParseWebUrl(PuskUrl) is null;

    partial void OnPuskUrlChanged(string value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Network.PuskUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.RequestSave();
    }

    /// <summary>Окно выбора версии платформы для консоли кластера (и перехода в «ПУСК», если адрес задан).</summary>
    [RelayCommand]
    private async Task OpenClusterConsoleAsync()
    {
        var console = new ClusterConsoleViewModel(
            _installations,
            _clusterConsole,
            _processLauncher,
            NetworkSettings.ParseWebUrl(_settings.Settings.Network.PuskUrl));
        await _dialogs.ShowClusterConsoleAsync(console);
        if (console.ResultMessage is { } message)
        {
            StatusText = message;
        }
    }
}
