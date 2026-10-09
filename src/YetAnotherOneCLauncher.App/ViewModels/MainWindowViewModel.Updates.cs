using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Core.Updates;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Обновление лаунчера из релизов GitHub: проверка при запуске и раз в сутки (режим в настройках), полоса-оповещение
/// над строкой состояния, установка с перезапуском и ручная проверка («Проверить обновления»).
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Как часто смотреть, не пора ли проверить (сама проверка — раз в <see cref="UpdateSettings.CheckInterval"/>).</summary>
    private static readonly TimeSpan UpdateTimerTick = TimeSpan.FromHours(1);

    // Служба обновлений; без неё (тесты, неподдерживаемая ОС) обновления недоступны.
    private readonly IUpdateService? _updates;
    private bool _updateLoopRunning;
    private UpdateCheckResult.Available? _availableUpdate;
    private bool _installing;

    /// <summary>Режимы в порядке <see cref="UpdateMode"/>.</summary>
    private static readonly UpdateMode[] UpdateModes = [UpdateMode.Disabled, UpdateMode.CheckOnly, UpdateMode.AutoUpdate];

    /// <summary>Режим обновления (в порядке «Не использовать», «Только проверка», «Автообновление»).</summary>
    public int UpdateModeIndex
    {
        get => Math.Max(0, Array.IndexOf(UpdateModes, _settings.Settings.Updates.Mode));
        set
        {
            var mode = UpdateModes[Math.Clamp(value, 0, UpdateModes.Length - 1)];
            if (mode == _settings.Settings.Updates.Mode)
            {
                return;
            }

            _settings.Settings.Updates.Mode = mode;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    /// <summary>«Версия 0.1.0 · проверено 09.10.2026 10:20» — для настроек.</summary>
    public string UpdateStatusText
    {
        get
        {
            var version = _updates?.CurrentVersion.ToString() ?? "?";
            return _settings.Settings.Updates.LastCheck is { } last
                ? $"Версия {version} · проверено {last.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}"
                : $"Версия {version} · обновления ещё не проверялись";
        }
    }

    public bool CanCheckForUpdates => _updates is not null;

    /// <summary>Полоса-оповещение об обновлении видна.</summary>
    [ObservableProperty]
    public partial bool ShowUpdateBanner { get; private set; }

    [ObservableProperty]
    public partial string UpdateBannerText { get; private set; } = string.Empty;

    /// <summary>Обновление уже установлено — полоса «Перезапустить» (иначе — «Доступна новая версия»).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateOffered))]
    public partial bool IsUpdateInstalled { get; private set; }

    /// <summary>Новая версия найдена, но не установлена: кнопки «Обновить» и «Пропустить эту версию».</summary>
    public bool IsUpdateOffered => ShowUpdateBanner && !IsUpdateInstalled;

    partial void OnShowUpdateBannerChanged(bool value) => OnPropertyChanged(nameof(IsUpdateOffered));

    /// <summary>Фоновые проверки: сразу, если пора, и затем раз в час — не пора ли.</summary>
    private void StartUpdateChecks()
    {
        if (_updates is null || _updateLoopRunning)
        {
            return;
        }

        _updateLoopRunning = true;
        _ = UpdateLoopAsync();
    }

    private void StopUpdateChecks() => _updateLoopRunning = false;

    private async Task UpdateLoopAsync()
    {
        while (_updateLoopRunning)
        {
            await CheckForUpdatesInBackgroundAsync();
            await Task.Delay(UpdateTimerTick);
        }
    }

    /// <summary>Проверка по расписанию: молча, если ничего нового; найденное — полоса (и установка в режиме «Автообновление»).</summary>
    internal async Task CheckForUpdatesInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        var updates = _settings.Settings.Updates;
        if (_updates is null || _installing || IsUpdateInstalled || !updates.IsCheckDue(DateTimeOffset.Now))
        {
            return;
        }

        var result = await _updates.CheckAsync(cancellationToken);
        if (result is UpdateCheckResult.Failed failed)
        {
            LogUpdateCheckFailed(_logger, failed.Reason);
            return;
        }

        MarkChecked();
        if (result is not UpdateCheckResult.Available available
            || string.Equals(updates.SkippedVersion, available.Version.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        OfferUpdate(available);
        if (updates.Mode == UpdateMode.AutoUpdate && _updates.CanInstall)
        {
            await InstallUpdateCoreAsync(available, cancellationToken);
        }
    }

    /// <summary>«Проверить обновления» (настройки, «О программе»): итог — в окне сообщения.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null)
        {
            return;
        }

        const string title = "Обновление лаунчера";
        StatusText = "Проверка обновлений…";
        var result = await _updates.CheckAsync();
        switch (result)
        {
            case UpdateCheckResult.Failed failed:
                StatusText = "Не удалось проверить обновления.";
                await _dialogs.ShowMessageAsync(title, "Не удалось проверить обновления. " + failed.Reason);
                return;
            case UpdateCheckResult.UpToDate upToDate:
                MarkChecked();
                StatusText = string.Empty;
                await _dialogs.ShowMessageAsync(title, $"Установлена последняя версия {upToDate.Current}.");
                return;
            case UpdateCheckResult.Available available:
                MarkChecked();
                StatusText = string.Empty;
                OfferUpdate(available);
                if (IsUpdateInstalled)
                {
                    await AskRestartAsync();
                }
                else if (!_updates.CanInstall)
                {
                    if (await _dialogs.ConfirmAsync(
                            title,
                            $"Доступна новая версия {available.Version} (установлена {_updates.CurrentVersion}). " +
                            $"Установить её автоматически нельзя: {_updates.CannotInstallReason} Открыть страницу релиза, чтобы скачать вручную?",
                            "Открыть страницу"))
                    {
                        ShowUpdateReleaseNotes();
                    }
                }
                else if (await _dialogs.ConfirmAsync(
                             title, $"Доступна новая версия {available.Version} (установлена {_updates.CurrentVersion}). Обновить сейчас?", "Обновить"))
                {
                    await InstallUpdateAsync();
                }

                return;
        }
    }

    /// <summary>«Обновить» на полосе: скачать, проверить и заменить файл программы, затем предложить перезапуск.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } update || _updates is null)
        {
            return;
        }

        if (!_updates.CanInstall)
        {
            await _dialogs.ShowMessageAsync(
                "Обновление лаунчера", $"Установить обновление автоматически нельзя: {_updates.CannotInstallReason} Скачайте его со страницы релиза.");
            ShowUpdateReleaseNotes();
            return;
        }

        if (await InstallUpdateCoreAsync(update, CancellationToken.None))
        {
            await AskRestartAsync();
        }
    }

    private async Task<bool> InstallUpdateCoreAsync(UpdateCheckResult.Available update, CancellationToken cancellationToken)
    {
        if (_updates is null || _installing)
        {
            return false;
        }

        _installing = true;
        try
        {
            StatusText = $"Загрузка обновления {update.Version}…";
            // Сообщения о ходе загрузки приходят через очередь окна: запоздавшее (после установки) не показываем.
            var progress = new Progress<double>(p =>
            {
                if (_installing)
                {
                    StatusText = $"Загрузка обновления {update.Version}… {(int)Math.Round(p * 100)} %";
                }
            });
            var result = await _updates.InstallAsync(update, progress, cancellationToken);
            if (result is UpdateInstallResult.Failed failed)
            {
                StatusText = failed.Reason;
                return false;
            }

            IsUpdateInstalled = true;
            UpdateBannerText = $"Лаунчер обновлён до версии {update.Version}. Новая версия откроется после перезапуска.";
            StatusText = $"Обновление {update.Version} установлено.";
            return true;
        }
        finally
        {
            _installing = false;
        }
    }

    private async Task AskRestartAsync()
    {
        if (await _dialogs.ConfirmAsync("Обновление лаунчера", "Обновление установлено. Перезапустить лаунчер сейчас?", "Перезапустить"))
        {
            RestartToUpdate();
        }
    }

    /// <summary>«Перезапустить»: открыть новую версию и закрыть эту.</summary>
    [RelayCommand]
    private void RestartToUpdate()
    {
        if (_updates?.StartUpdatedVersion() == true)
        {
            _window.Close();
        }
        else
        {
            StatusText = "Не удалось запустить новую версию — она откроется при следующем запуске лаунчера.";
        }
    }

    /// <summary>«Что нового»: страница релиза на GitHub.</summary>
    [RelayCommand]
    private void ShowUpdateReleaseNotes()
    {
        var page = _availableUpdate?.Release.PageUrl ?? GitHubReleaseClient.ReleasesPage;
        try
        {
            _processLauncher.OpenUrl(page);
        }
        catch (LaunchFailedException ex)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>«Пропустить эту версию»: больше не напоминать о ней (о следующей — напомнить).</summary>
    [RelayCommand]
    private void SkipUpdateVersion()
    {
        if (_availableUpdate is { } update)
        {
            _settings.Settings.Updates.SkippedVersion = update.Version.ToString();
            _settings.RequestSave();
        }

        ShowUpdateBanner = false;
    }

    /// <summary>«×»: скрыть полосу до следующей проверки.</summary>
    [RelayCommand]
    private void DismissUpdateBanner() => ShowUpdateBanner = false;

    private void OfferUpdate(UpdateCheckResult.Available update)
    {
        _availableUpdate = update;
        if (!IsUpdateInstalled)
        {
            UpdateBannerText = $"Доступна новая версия {update.Version} (установлена {_updates?.CurrentVersion}).";
        }

        ShowUpdateBanner = true;
    }

    private void MarkChecked()
    {
        _settings.Settings.Updates.LastCheck = DateTimeOffset.Now;
        _settings.RequestSave();
        OnPropertyChanged(nameof(UpdateStatusText));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Проверка обновлений не удалась: {Reason}")]
    private static partial void LogUpdateCheckFailed(ILogger logger, string reason);
}
