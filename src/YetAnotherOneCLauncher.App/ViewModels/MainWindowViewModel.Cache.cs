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
        OpenLocalCacheCommand.NotifyCanExecuteChanged();
        OpenRoamingCacheCommand.NotifyCanExecuteChanged();

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

    /// <summary>Открыть каталог программного кэша базы (Local).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenLocalCache))]
    private Task OpenLocalCacheAsync(InfoBaseViewModel? target) =>
        OpenCacheFolderAsync((target ?? SelectedInfoBase)?.LocalCachePath);

    /// <summary>Открыть каталог пользовательского кэша базы — настроек пользователя (Roaming).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenRoamingCache))]
    private Task OpenRoamingCacheAsync(InfoBaseViewModel? target) =>
        OpenCacheFolderAsync((target ?? SelectedInfoBase)?.RoamingCachePath);

    private bool CanOpenLocalCache(InfoBaseViewModel? target) => (target ?? SelectedInfoBase)?.LocalCachePath is not null;

    private bool CanOpenRoamingCache(InfoBaseViewModel? target) => (target ?? SelectedInfoBase)?.RoamingCachePath is not null;

    private async Task OpenCacheFolderAsync(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            // Кэш могли удалить после последнего пересчёта (например, сама 1С) — пересчитать и сказать.
            await _dialogs.ShowMessageAsync(CacheTitle, $"Каталога кэша больше нет:\n{path}");
            StartCacheScan();
            return;
        }

        try
        {
            _processLauncher.OpenFolder(path);
        }
        catch (LaunchFailedException ex)
        {
            await _dialogs.ShowMessageAsync(CacheTitle, ex.Message);
        }
    }

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
            hasRoaming: _paths?.InfoBaseCacheRoots.Any(r => r.Location == CacheLocation.Roaming) == true,
            async directories =>
            {
                var result = await CleanCacheAsync(directories, $"выбранный кэш ({directories.Count} каталогов)");
                return result is null ? null : _cacheReport;
            },
            ScanCacheAsync);
        manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CacheManagerViewModel.IncludeRoaming))
            {
                cache.IncludeRoaming = manager.IncludeRoaming;
                _settings.RequestSave();
            }
        };

        await _dialogs.ShowCacheManagerAsync(manager);
    }

    /// <summary>Базы из списков, чей кэш среди удаляемого, открытые в 1С текущим пользователем.</summary>
    private List<(string Id, Core.Model.InfoBase InfoBase, IReadOnlyList<PlatformProcess> Processes)> BasesInUse(IReadOnlyList<CacheDirectory> directories)
    {
        var processes = _cacheUsage!.CurrentUserPlatformProcesses();
        if (processes.Count == 0)
        {
            return [];
        }

        var ids = directories.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. _cacheReport.Owners
            .Where(o => o.InfoBase is not null && ids.Contains(o.Id))
            .Select(o => (o.Id, InfoBase: o.InfoBase!, Processes: (IReadOnlyList<PlatformProcess>)[.. processes.Where(p => PlatformCommandLine.Targets(p.CommandLine, o.InfoBase!))]))
            .Where(b => b.Processes.Count > 0)];
    }

    /// <summary>
    /// Проверка, подтверждение, очистка, пересчёт и итог. <c>null</c> — удалять нечего или пользователь отказался.
    /// Кэш баз, открытых в 1С текущим пользователем (в командной строке процесса — адрес базы), не удаляется.
    /// </summary>
    /// <param name="directories">Что удалить.</param>
    /// <param name="what">Что это — для вопроса.</param>
    /// <param name="confirm"><c>false</c> — согласие уже получено (флажок при удалении базы из списка).</param>
    private async Task<CacheCleanResult?> CleanCacheAsync(IReadOnlyList<CacheDirectory> directories, string what, bool confirm = true)
    {
        if (_cacheUsage is null || directories.Count == 0)
        {
            return null;
        }

        var busy = BasesInUse(directories);
        if (busy.Count > 0)
        {
            var list = string.Join(Environment.NewLine, busy.Select(b => $"«{b.InfoBase.Name}» — {string.Join(", ", b.Processes)}"));
            directories = [.. directories.Where(d => !busy.Any(b => string.Equals(b.Id, d.Id, StringComparison.OrdinalIgnoreCase)))];
            await _dialogs.ShowMessageAsync(
                CacheTitle,
                directories.Count == 0
                    ? $"Очистка кэша невозможна: база используется.{Environment.NewLine}{Environment.NewLine}{list}"
                    : $"Кэш этих баз не будет удалён: они используются.{Environment.NewLine}{Environment.NewLine}{list}");
            if (directories.Count == 0)
            {
                return null;
            }
        }

        var question = new StringBuilder()
            .AppendLine("Удалить кэш?")
            .AppendLine()
            .Append(char.ToUpper(what[0], System.Globalization.CultureInfo.CurrentCulture)).Append(what[1..])
            .Append(" — ").Append(ByteSize.Format(directories.Sum(d => d.SizeBytes)))
            .Append(", удаляется насовсем (без корзины).");
        if (directories.Any(d => d.Location == CacheLocation.Roaming))
        {
            question.AppendLine().AppendLine()
                .Append("Будут удалены и локальные настройки пользователя (Roaming): размеры окон, последние значения и т. п.");
        }

        if (confirm && !await _dialogs.ConfirmAsync(CacheTitle, question.ToString(), "Удалить"))
        {
            return null;
        }

        CacheCleanResult result;
        try
        {
            result = await CacheCleaner.CleanAsync(directories, CacheCleaner.DeletePermanently, _cacheUsage.IsDirectoryInUse);
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

        LogCacheCleaned(_logger, result.RemovedCount, result.RemovedBytes);
        await ScanCacheAsync();

        var removed = $"Освобождено {ByteSize.Format(result.RemovedBytes)} ({result.RemovedCount} каталогов).";
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Кэш очищен: каталогов {Count}, байт {Bytes}")]
    private static partial void LogCacheCleaned(ILogger logger, int count, long bytes);
}
