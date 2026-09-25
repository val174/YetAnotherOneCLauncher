using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>
/// Настройки лаунчера на время работы: загружаются при старте, сохраняются с небольшой задержкой
/// после изменений (чтобы не писать файл на каждый щелчок) и при выходе.
/// </summary>
public sealed partial class UserSettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SettingsStore? _store;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pendingSave;

    /// <param name="store"><c>null</c> — настройки только в памяти (неподдерживаемая ОС, тесты).</param>
    /// <param name="logger">Лог.</param>
    public UserSettingsService(SettingsStore? store, ILogger<UserSettingsService> logger)
    {
        _store = store;
        _logger = logger;
        UserData = new LauncherUserData(new LauncherSettings());
    }

    public LauncherSettings Settings => UserData.Settings;

    public LauncherUserData UserData { get; private set; }

    /// <summary>Предупреждение при загрузке (повреждённый файл и т. п.).</summary>
    public string? LoadWarning { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return;
        }

        var result = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        UserData = new LauncherUserData(result.Settings);
        LoadWarning = result.Warning;
        if (result.Warning is not null)
        {
            LogLoadWarning(_logger, result.Warning);
        }
    }

    /// <summary>Сохранить чуть позже; повторные вызовы откладывают запись.</summary>
    public void RequestSave()
    {
        if (_store is null)
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            _pendingSave = cts = new CancellationTokenSource();
        }

        _ = SaveLaterAsync(cts.Token);
    }

    /// <summary>Сохранить немедленно (при выходе).</summary>
    public async Task FlushAsync()
    {
        lock (_gate)
        {
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            _pendingSave = null;
        }

        await SaveNowAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            _pendingSave = null;
        }
    }

    private async Task SaveLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SaveDelay, cancellationToken).ConfigureAwait(false);
            await SaveNowAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Отложено следующим изменением или сохранено при выходе.
        }
    }

    private async Task SaveNowAsync()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            await _store.SaveAsync(Settings).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, _store.FilePath, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Настройки: {Warning}")]
    private static partial void LogLoadWarning(ILogger logger, string warning);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не удалось сохранить настройки в {Path}")]
    private static partial void LogSaveFailed(ILogger logger, string path, Exception exception);
}
