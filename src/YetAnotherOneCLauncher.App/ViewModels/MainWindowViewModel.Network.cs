using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
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

    /// <summary>Загрузка каталога: промежуточные каталоги показываются сразу, итоговый — с платформами.</summary>
    private async Task LoadCatalogAsync(IPlatformPaths paths, IPlatformLocator locator)
    {
        var generation = ++_loadGeneration;
        var finished = false;
        var writtenBefore = _store?.LastWrittenFingerprint;

        // Промежуточные каталоги приходят из фоновых потоков; Progress передаёт их в поток интерфейса.
        // Без контекста интерфейса (обычные тесты) показывается только итог.
        IProgress<InfoBaseCatalog>? progress = SynchronizationContext.Current is null
            ? null
            : new Progress<InfoBaseCatalog>(partial =>
            {
                if (!finished && generation == _loadGeneration)
                {
                    ShowCatalog(partial, CurrentSelectionKey());
                    var pending = partial.Lists.Count(l => l.IsPending);
                    StatusText = pending > 0 ? $"Баз: {partial.InfoBases.Count}; читаются общие списки: {pending}…" : StatusText;
                }
            });

        var catalog = await _loader.LoadAsync(paths.ToCatalogSources(), progress);
        var platforms = await locator.LocateAsync(catalog.StarterConfig.InstalledLocations);
        if (generation != _loadGeneration)
        {
            return; // пока читали, началась новая загрузка
        }

        finished = true;
        Apply(catalog, platforms);

        // Если за время загрузки личный список правили в лаунчере, в итоговом каталоге его прежняя версия.
        if (_store is not null && _store.LastWrittenFingerprint != writtenBefore)
        {
            await ReloadPersonalListAsync();
        }
    }

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
