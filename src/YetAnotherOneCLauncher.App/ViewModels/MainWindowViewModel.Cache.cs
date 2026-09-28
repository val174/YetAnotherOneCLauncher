using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Cache;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Кэш баз: размеры, очистка, «Очистить кэш и запустить».</summary>
public sealed partial class MainWindowViewModel
{
    private const string CacheTitle = "Кэш баз";

    private CacheScanResult? _cacheScan;
    private CacheReport _cacheReport = CacheReport.Empty;

    /// <summary>Последний пересчёт кэша — тесты ждут его завершения.</summary>
    internal Task CacheScanTask { get; private set; } = Task.CompletedTask;

    /// <summary>Общий размер кэша для строки состояния.</summary>
    [ObservableProperty]
    public partial string CacheSummaryText { get; private set; } = string.Empty;

    private bool CanManageCache => _paths is not null && _cacheUsage is not null;

    /// <summary>Пересчитать кэш в фоне (после загрузки списков).</summary>
    private void StartCacheScan()
    {
        if (_paths is null)
        {
            return;
        }

        CacheScanTask = ScanCacheAsync();
    }

    private async Task<CacheReport> ScanCacheAsync()
    {
        if (_paths is null)
        {
            return CacheReport.Empty;
        }

        try
        {
            _cacheScan = await CacheScanner.ScanAsync(_paths.InfoBaseCacheRoots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCacheFailed(_logger, ex);
            _cacheScan = new CacheScanResult([], [ex.Message]);
        }

        ApplyCacheReport();
        return _cacheReport;
    }

    /// <summary>Сопоставить найденный кэш с текущими базами (после загрузки или правки списка).</summary>
    private void ApplyCacheReport()
    {
        if (_cacheScan is null || _catalog is null)
        {
            return;
        }

        _cacheReport = CacheReport.Build(_cacheScan, _catalog.InfoBases);
        foreach (var infoBase in _bases)
        {
            infoBase.SetCache(_cacheReport.For(infoBase.InfoBase));
        }

        ClearCacheCommand.NotifyCanExecuteChanged();
        ClearCacheAndLaunchCommand.NotifyCanExecuteChanged();

        CacheSummaryText = _cacheReport.Owners.Count == 0
            ? string.Empty
            : $"Кэш: {ByteSize.Format(_cacheReport.TotalBytes)}";
    }

    [RelayCommand(CanExecute = nameof(CanClearCache))]
    private async Task ClearCacheAsync(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target is not null)
        {
            await ClearBaseCacheAsync(target, launchAfter: null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearCacheAndLaunch))]
    private async Task ClearCacheAndLaunchAsync(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target is not null)
        {
            await ClearBaseCacheAsync(target, launchAfter: LaunchMode.Enterprise);
        }
    }

    private bool CanClearCache(InfoBaseViewModel? target) => CanManageCache && (target ?? SelectedInfoBase)?.HasCache == true;

    private bool CanClearCacheAndLaunch(InfoBaseViewModel? target) => CanClearCache(target) && CanLaunch(target);

    private async Task ClearBaseCacheAsync(InfoBaseViewModel target, LaunchMode? launchAfter)
    {
        await CacheScanTask;
        var owner = _cacheReport.For(target.InfoBase);
        var directories = owner?.In(local: true, roaming: _settings.Settings.Cache.IncludeRoaming).ToList() ?? [];
        if (directories.Count == 0)
        {
            StatusText = $"У «{target.Name}» нет кэша.";
            if (launchAfter is { } mode)
            {
                await LaunchAsync(target, mode);
            }

            return;
        }

        var result = await CleanCacheAsync(directories, $"кэш «{target.Name}»");
        if (result is null || launchAfter is not { } launchMode)
        {
            return;
        }

        if (result.Items.Any(i => i.Status == CacheCleanStatus.InUse)
            && !await _dialogs.ConfirmAsync(CacheTitle, $"Кэш «{target.Name}» занят — база, похоже, уже открыта. Всё равно запустить?", "Запустить"))
        {
            return;
        }

        await LaunchAsync(target, launchMode);
    }

    /// <summary>Окно «Кэш баз»: все каталоги, кэш без хозяина, массовая очистка.</summary>
    [RelayCommand(CanExecute = nameof(CanManageCache))]
    private async Task OpenCacheManagerAsync()
    {
        await CacheScanTask;
        var cache = _settings.Settings.Cache;
        var manager = new CacheManagerViewModel(
            _cacheReport,
            cache.IncludeRoaming,
            cache.DeletePermanently,
            hasRoaming: _paths?.InfoBaseCacheRoots.Any(r => r.Location == CacheLocation.Roaming) == true,
            async directories =>
            {
                var result = await CleanCacheAsync(directories, $"выбранный кэш ({directories.Count} каталогов)");
                return result is null ? null : _cacheReport;
            },
            ScanCacheAsync);
        manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CacheManagerViewModel.IncludeRoaming) or nameof(CacheManagerViewModel.DeletePermanently))
            {
                cache.IncludeRoaming = manager.IncludeRoaming;
                cache.DeletePermanently = manager.DeletePermanently;
                _settings.RequestSave();
            }
        };

        await _dialogs.ShowCacheManagerAsync(manager);
    }

    /// <summary>Подтверждение, очистка, пересчёт и итог. <c>null</c> — пользователь отказался.</summary>
    /// <param name="directories">Что удалить.</param>
    /// <param name="what">Что это — для вопроса.</param>
    /// <param name="confirm"><c>false</c> — согласие уже получено (флажок при удалении базы из списка).</param>
    private async Task<CacheCleanResult?> CleanCacheAsync(IReadOnlyList<CacheDirectory> directories, string what, bool confirm = true)
    {
        if (_cacheUsage is null || directories.Count == 0)
        {
            return null;
        }

        var permanently = _settings.Settings.Cache.DeletePermanently || _recycleBin is null;
        var question = new StringBuilder()
            .Append(permanently ? "Удалить насовсем " : "Переместить в корзину ")
            .Append(what).Append(" — ").Append(ByteSize.Format(directories.Sum(d => d.SizeBytes))).Append('?');
        if (directories.Any(d => d.Location == CacheLocation.Roaming))
        {
            question.AppendLine().AppendLine()
                .Append("Будут удалены и локальные настройки пользователя (Roaming): размеры окон, последние значения и т. п.");
        }

        var running = _cacheUsage.RunningPlatformProcesses();
        if (running.Count > 0)
        {
            question.AppendLine().AppendLine()
                .Append("Запущена платформа 1С: ").Append(string.Join(", ", running))
                .Append(". Кэш открытых баз будет пропущен.");
        }

        if (confirm && !await _dialogs.ConfirmAsync(CacheTitle, question.ToString(), permanently ? "Удалить" : "В корзину"))
        {
            return null;
        }

        Action<string> remove = permanently ? CacheCleaner.DeletePermanently : _recycleBin!.MoveToRecycleBin;
        CacheCleanResult result;
        try
        {
            result = await CacheCleaner.CleanAsync(directories, remove, _cacheUsage.IsDirectoryInUse);
        }
        catch (InvalidOperationException ex)
        {
            LogCacheFailed(_logger, ex);
            await _dialogs.ShowMessageAsync(CacheTitle, ex.Message);
            return null;
        }

        foreach (var problem in result.Problems)
        {
            LogCacheProblem(_logger, problem.Directory.Path, problem.Status, problem.Message);
        }

        LogCacheCleaned(_logger, result.RemovedCount, result.RemovedBytes, permanently);
        await ScanCacheAsync();

        var removed = $"Освобождено {ByteSize.Format(result.RemovedBytes)} ({result.RemovedCount} каталогов{(permanently ? string.Empty : ", в корзине")}).";
        var problems = result.Problems.ToList();
        StatusText = problems.Count == 0 ? removed : $"{removed} Не удалось: {problems.Count}.";
        if (problems.Count > 0)
        {
            await _dialogs.ShowMessageAsync(
                CacheTitle,
                removed + Environment.NewLine + Environment.NewLine + "Не удалены:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(p => $"{p.Directory.Path} — {p.Message}")));
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Кэш баз")]
    private static partial void LogCacheFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Кэш {Path} не удалён ({Status}): {Reason}")]
    private static partial void LogCacheProblem(ILogger logger, string path, CacheCleanStatus status, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Кэш очищен: каталогов {Count}, байт {Bytes}, насовсем: {Permanently}")]
    private static partial void LogCacheCleaned(ILogger logger, int count, long bytes, bool permanently);
}
