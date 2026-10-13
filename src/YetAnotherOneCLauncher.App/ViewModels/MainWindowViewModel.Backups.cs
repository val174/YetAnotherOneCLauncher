using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Копии личного списка перед загрузкой из файлов (JSON и .v8i) и восстановление: полоса «Вернуть прежний список»
/// после загрузки, «Восстановить список…» в меню ≡ и в настройках.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string RestoreTitle = "Восстановление списка баз";

    private ListBackupStore? _listBackups;

    /// <summary>Копия, сделанная перед последней загрузкой, — для кнопки на полосе.</summary>
    private ListBackup? _undoBackup;

    /// <summary>Каталог копий: <c>list-backups</c> в данных лаунчера.</summary>
    internal ListBackupStore? ListBackups =>
        _listBackups ??= _paths is null ? null : new ListBackupStore(Path.Combine(_paths.AppDataDirectory, "list-backups"));

    /// <summary>Полоса после загрузки из файла: «Вернуть прежний список».</summary>
    [ObservableProperty]
    public partial bool ShowRestoreBanner { get; private set; }

    [ObservableProperty]
    public partial string RestoreBannerText { get; private set; } = string.Empty;

    /// <summary>Сохранить копию списка перед загрузкой из файла.</summary>
    /// <returns><c>Proceed = false</c> — копию сделать не удалось и пользователь отказался продолжать.</returns>
    private async Task<(bool Proceed, ListBackup? Backup)> BackupBeforeImportAsync(string sourcePath, ImportMode mode)
    {
        if (_store is null || ListBackups is not { } backups)
        {
            return (true, null);
        }

        try
        {
            var content = await _store.ReadContentAsync();
            return (true, await backups.CreateAsync(content, ListBackupReason.Import, _settings.Settings, Path.GetFileName(sourcePath), mode));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFailed(_logger, backups.Directory, ex);
            var proceed = await _dialogs.ConfirmAsync(
                ListTitle,
                $"Не удалось сохранить копию списка ({ex.Message}). Вернуть прежний список после загрузки будет нельзя. Всё равно загрузить?",
                "Загрузить");
            return (proceed, null);
        }
    }

    /// <summary>Загрузка прошла: полоса с кнопкой «Вернуть прежний список».</summary>
    private void OfferUndo(ListBackup? backup, string sourcePath, string summary)
    {
        _undoBackup = backup;
        if (backup is null)
        {
            return;
        }

        RestoreBannerText = $"Список загружен из {Path.GetFileName(sourcePath)} ({summary}). Если результат не тот — верните прежний список.";
        ShowRestoreBanner = true;
    }

    [RelayCommand]
    private async Task UndoLastImportAsync()
    {
        if (_undoBackup is not { } backup)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(
                RestoreTitle,
                $"Вернуть список баз и их настройки к состоянию до загрузки из {backup.Info.SourceFile}? Текущий список сохранится копией.",
                "Вернуть"))
        {
            await RestoreAsync(backup, withSettings: true);
        }
    }

    [RelayCommand]
    private void DismissRestoreBanner() => ShowRestoreBanner = false;

    /// <summary>Окно со всеми копиями: восстановить выбранную.</summary>
    [RelayCommand(CanExecute = nameof(HasPersonalList))]
    private async Task RestoreListAsync()
    {
        if (ListBackups is not { } backups)
        {
            return;
        }

        var restore = new RestoreListViewModel(backups.List(), OpenListBackupsFolderCommand);
        if (await _dialogs.ChooseListBackupAsync(restore) && restore.SelectedRow is { } row)
        {
            await RestoreAsync(row.Backup, restore.RestoreSettings);
        }
    }

    [RelayCommand]
    private async Task OpenListBackupsFolderAsync()
    {
        if (ListBackups is not { } backups)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(backups.Directory);
            _processLauncher.OpenFolder(backups.Directory);
        }
        catch (Exception ex) when (ex is LaunchFailedException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(RestoreTitle, ex.Message);
        }
    }

    /// <summary>Записать копию в личный список; перед этим текущий список тоже сохраняется копией.</summary>
    internal async Task<bool> RestoreAsync(ListBackup backup, bool withSettings)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (_store is null || _catalog is null || ListBackups is not { } backups)
        {
            return false;
        }

        try
        {
            var content = await ListBackupStore.ReadListAsync(backup);
            await backups.CreateAsync(await _store.ReadContentAsync(), ListBackupReason.Restore, _settings.Settings);
            var document = await _store.ReplaceAsync(content);
            if (withSettings)
            {
                backup.Info.Settings.ApplyTo(_settings.Settings);
                _settings.RequestSave();
            }

            _undoBackup = null;
            ShowRestoreBanner = false;
            ShowCatalog(_catalog.WithPersonalDocument(document), CurrentSelectionKey());
            var when = backup.Info.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            StatusText = $"Список восстановлен из копии от {when} (записей: {backup.Info.EntryCount})"
                         + (withSettings ? " вместе с настройками баз." : ".");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFailed(_logger, backups.Directory, ex);
            await _dialogs.ShowMessageAsync(RestoreTitle, $"Не удалось восстановить список: {ex.Message}");
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Копия списка баз: ошибка ({Directory})")]
    private static partial void LogBackupFailed(ILogger logger, string directory, Exception exception);
}
