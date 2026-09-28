using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Список переходов Windows: последние базы у значка на панели задач и их запуск.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Сколько баз показывать в списке переходов.</summary>
    public const int JumpListCount = 5;

    private bool _catalogLoaded;
    private string? _pendingLaunchKey;

    /// <summary>Обновить категорию «Последние базы»: пять последних запущенных баз.</summary>
    private void UpdateJumpList()
    {
        if (_jumpList is null || _catalog is null)
        {
            return;
        }

        // Значок 1С — из самой новой установленной платформы.
        var newest = _installations.Count > 0 ? _installations[0] : null;
        var icon = newest?.GetExecutablePath(PlatformExecutable.ThinClient) ?? newest?.GetExecutablePath(PlatformExecutable.ThickClient);
        var entries = _settings.UserData.Recent(_catalog.InfoBases, JumpListCount)
            .Select(b => new JumpListEntry(b.Name, b.Connection.ToDisplayString(), LaunchArgument.Format(b.IdentityKey), icon))
            .ToList();
        try
        {
            _jumpList.SetRecent(entries);
        }
        catch (InvalidOperationException ex)
        {
            LogJumpListFailed(_logger, ex);
        }
    }

    /// <summary>Запуск базы из списка переходов (аргумент при старте или команда от другого экземпляра).</summary>
    internal async Task LaunchFromJumpListAsync(string identityKey)
    {
        if (!_catalogLoaded)
        {
            _pendingLaunchKey = identityKey; // запустится после загрузки списков
            return;
        }

        var target = _bases.FirstOrDefault(b => b.InfoBase.IdentityKey == identityKey);
        if (target is null)
        {
            StatusText = "База из списка переходов не найдена в списках баз.";
            await _dialogs.ShowMessageAsync("Запуск", StatusText);
            return;
        }

        await LaunchAsync(target, LaunchMode.Enterprise);
    }

    /// <summary>Команда из канала приходит из фонового потока.</summary>
    private void OnLaunchRequest(string identityKey)
    {
        if (_uiContext is null)
        {
            _ = LaunchFromJumpListAsync(identityKey);
        }
        else
        {
            _uiContext.Post(_ => _ = LaunchFromJumpListAsync(identityKey), null);
        }
    }

    /// <summary>Каталог загружен: можно запускать отложенное и обновить список переходов.</summary>
    private async Task OnCatalogLoadedAsync()
    {
        _catalogLoaded = true;
        UpdateJumpList();
        if (_pendingLaunchKey is { } key)
        {
            _pendingLaunchKey = null;
            await LaunchFromJumpListAsync(key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось обновить список переходов")]
    private static partial void LogJumpListFailed(ILogger logger, Exception exception);
}
