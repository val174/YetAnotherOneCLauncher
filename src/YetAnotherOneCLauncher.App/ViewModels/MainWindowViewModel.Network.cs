using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Availability;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Постепенная загрузка списков и фоновая проверка доступности баз.</summary>
public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, AvailabilityResult> _availability = new(StringComparer.Ordinal);
    private int _availabilityGeneration;
    private int _loadGeneration;

    /// <summary>Последняя проверка доступности — тесты ждут её завершения.</summary>
    internal Task AvailabilityTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool CheckAvailability { get; set; }

    partial void OnCheckAvailabilityChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Network.CheckAvailability = value;
        _settings.RequestSave();
        if (value)
        {
            StartAvailabilityCheck();
        }
        else
        {
            _availabilityGeneration++;
            _availability.Clear();
            ApplyAvailability();
        }
    }

    /// <summary>
    /// Загрузка каталога и поиск платформ — в фоне и параллельно (<see cref="CatalogLoad"/>). Промежуточные каталоги
    /// показываются сразу; платформы применяются, как только найдены, — запускать базы можно, не дожидаясь
    /// общих списков, которые могут читаться по сети до таймаута. Первая загрузка берёт ту, что начата при старте.
    /// </summary>
    private async Task LoadCatalogAsync(IPlatformPaths paths, IPlatformLocator locator)
    {
        var generation = ++_loadGeneration;
        var finished = false;
        var writtenBefore = _store?.LastWrittenFingerprint;
        var load = _startupCatalog?.Take() ?? CatalogLoad.Start(_loader, paths, locator);

        // Промежуточные каталоги приходят из фоновых потоков — в поток интерфейса. Без контекста интерфейса
        // (обычные тесты) показывается только итог.
        var ui = SynchronizationContext.Current;
        void ShowPartial(InfoBaseCatalog partial)
        {
            if (finished || generation != _loadGeneration)
            {
                return;
            }

            ShowCatalog(partial, CurrentSelectionKey());
            LogFirstCatalogShown();
            var pending = partial.Lists.Count(l => l.IsPending);
            StatusText = pending > 0 ? $"Баз: {partial.InfoBases.Count}; читаются общие списки: {pending}…" : StatusText;
        }

        void OnUpdated(InfoBaseCatalog partial) => ui?.Post(_ => ShowPartial(partial), null);
        if (ui is not null)
        {
            load.Updated += OnUpdated;
            if (load.Latest is { } latest)
            {
                ShowPartial(latest); // начатая при старте загрузка могла уже что-то прочитать
            }
        }

        try
        {
            // Платформы — как только найдены, не дожидаясь итогового каталога.
            var platformsFirst = await Task.WhenAny(load.Platforms, load.Catalog);
            if (platformsFirst == load.Platforms && !load.Catalog.IsCompleted && generation == _loadGeneration)
            {
                ApplyPlatforms(await load.Platforms, load.Latest?.StarterConfig.DefaultVersion);
                UpdatePlatformColumn(_bases);
            }

            var catalog = await load.Catalog;
            var platforms = await load.Platforms;
            if (generation != _loadGeneration)
            {
                return; // пока читали, началась новая загрузка
            }

            finished = true;
            Apply(catalog, platforms);
        }
        finally
        {
            load.Updated -= OnUpdated;
        }

        // Если за время загрузки личный список правили в лаунчере, в итоговом каталоге его прежняя версия.
        if (_store is not null && _store.LastWrittenFingerprint != writtenBefore)
        {
            await ReloadPersonalListAsync();
        }
    }

    /// <summary>Замер для лога: окно открыто (вызывает окно).</summary>
    internal void OnWindowOpened() => LogWindowOpened(_logger, (long)StartupClock.Elapsed.TotalMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Окно открыто через {ElapsedMs} мс после запуска")]
    private static partial void LogWindowOpened(ILogger logger, long elapsedMs);

    private bool _firstCatalogLogged;

    /// <summary>Замер для лога: когда после запуска процесса показан первый список баз.</summary>
    private void LogFirstCatalogShown()
    {
        if (_firstCatalogLogged)
        {
            return;
        }

        _firstCatalogLogged = true;
        LogCatalogShown(_logger, (long)StartupClock.Elapsed.TotalMilliseconds);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Список баз показан через {ElapsedMs} мс после запуска")]
    private static partial void LogCatalogShown(ILogger logger, long elapsedMs);

    /// <summary>Проверить доступность всех баз в фоне (если включено).</summary>
    private void StartAvailabilityCheck()
    {
        var generation = ++_availabilityGeneration;
        if (!CheckAvailability || _availabilityChecker is null || _catalog is null)
        {
            return;
        }

        AvailabilityTask = CheckAvailabilityAsync(_catalog.InfoBases.ToList(), generation);
    }

    /// <summary>Проверка ограничена по времени, поэтому не отменяется: устаревший результат просто отбрасывается.</summary>
    private async Task CheckAvailabilityAsync(IReadOnlyList<Core.Model.InfoBase> infoBases, int generation)
    {
        var results = await _availabilityChecker!.CheckAsync(infoBases);
        if (generation != _availabilityGeneration)
        {
            return; // началась новая проверка или проверку выключили
        }

        _availability.Clear();
        foreach (var (key, result) in results)
        {
            _availability[key] = result;
        }

        ApplyAvailability();
        var unavailable = results.Values.Count(r => r.Status == AvailabilityStatus.Unavailable);
        UnavailableCount = unavailable;
        LogAvailabilityChecked(_logger, results.Count, unavailable);
    }

    /// <summary>Сколько баз не отвечает — для строки состояния.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnavailable))]
    public partial int UnavailableCount { get; private set; }

    public bool HasUnavailable => UnavailableCount > 0;

    /// <summary>Разложить результаты проверки по текущим базам (после загрузки и перестройки каталога).</summary>
    private void ApplyAvailability()
    {
        foreach (var infoBase in _bases)
        {
            infoBase.SetAvailability(_availability.GetValueOrDefault(infoBase.InfoBase.IdentityKey));
        }

        if (_availability.Count == 0)
        {
            UnavailableCount = 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Доступность баз проверена: {Count}, не отвечают: {Unavailable}")]
    private static partial void LogAvailabilityChecked(ILogger logger, int count, int unavailable);
}
