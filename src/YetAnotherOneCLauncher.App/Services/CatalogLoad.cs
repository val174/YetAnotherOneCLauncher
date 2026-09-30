using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>
/// Одна загрузка списков баз и поиск платформ — в фоне и параллельно: платформы ищутся, как только прочитан
/// <c>1cestart.cfg</c> (в нём каталоги установки), не дожидаясь общих списков, которые могут читаться по сети
/// до таймаута. Промежуточные каталоги (личный список сразу, общие — по мере чтения) приходят событием.
/// </summary>
public sealed partial class CatalogLoad
{
    private readonly Lock _gate = new();

    // Каталоги установки из 1cestart.cfg: известны с первым промежуточным каталогом (или с итоговым).
    private readonly TaskCompletionSource<IReadOnlyList<string>> _installedRoots =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private InfoBaseCatalog? _latest;

    private CatalogLoad()
    {
    }

    /// <summary>Промежуточный каталог; вызывается из фонового потока.</summary>
    public event Action<InfoBaseCatalog>? Updated;

    /// <summary>Последний промежуточный каталог; <c>null</c> — ещё ничего не прочитано.</summary>
    public InfoBaseCatalog? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Итоговый каталог: все списки прочитаны или отпали по таймауту.</summary>
    public Task<InfoBaseCatalog> Catalog { get; private set; } = null!;

    /// <summary>Установленные платформы.</summary>
    public Task<PlatformScanResult> Platforms { get; private set; } = null!;

    /// <summary>Начать загрузку в фоне.</summary>
    public static CatalogLoad Start(InfoBaseCatalogLoader loader, IPlatformPaths paths, IPlatformLocator locator, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(locator);

        var load = new CatalogLoad();
        var startedAt = Stopwatch.GetTimestamp();
        load.Platforms = load.LocateAsync(locator, logger, startedAt);
        load.Catalog = Task.Run(async () =>
        {
            try
            {
                var catalog = await loader.LoadAsync(paths.ToCatalogSources(), new CallbackProgress(load.OnUpdated)).ConfigureAwait(false);
                load._installedRoots.TrySetResult(catalog.StarterConfig.InstalledLocations);
                return catalog;
            }
            finally
            {
                load._installedRoots.TrySetResult([]); // загрузка упала — платформы всё равно ищутся в стандартных каталогах
            }
        });
        return load;
    }

    private async Task<PlatformScanResult> LocateAsync(IPlatformLocator locator, ILogger? logger, long startedAt)
    {
        var roots = await _installedRoots.Task.ConfigureAwait(false);
        var platforms = await locator.LocateAsync(roots).ConfigureAwait(false);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (logger is not null)
        {
            LogPlatformsFound(logger, platforms.Installations.Count, elapsedMs);
        }

        return platforms;
    }

    private void OnUpdated(InfoBaseCatalog catalog)
    {
        lock (_gate)
        {
            _latest = catalog;
        }

        _installedRoots.TrySetResult(catalog.StarterConfig.InstalledLocations);
        Updated?.Invoke(catalog);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Платформы найдены: {Count}, {ElapsedMs} мс от начала загрузки")]
    private static partial void LogPlatformsFound(ILogger logger, int count, long elapsedMs);

    /// <summary>Прогресс без контекста синхронизации: обработчик вызывается в потоке, который сообщил.</summary>
    private sealed class CallbackProgress(Action<InfoBaseCatalog> handler) : IProgress<InfoBaseCatalog>
    {
        public void Report(InfoBaseCatalog value) => handler(value);
    }
}

/// <summary>
/// Загрузка, начатая при старте процесса — до создания окна, чтобы чтение файлов шло одновременно с запуском
/// интерфейса. Главное окно забирает её при первой загрузке (<see cref="Take"/>); дальше загрузки — обычные.
/// </summary>
public sealed class StartupCatalog
{
    private readonly InfoBaseCatalogLoader _loader;
    private readonly IPlatformPaths _paths;
    private readonly IPlatformLocator _locator;
    private readonly ILogger<CatalogLoad> _logger;
    private CatalogLoad? _pending;

    public StartupCatalog(InfoBaseCatalogLoader loader, IPlatformPaths paths, IPlatformLocator locator, ILogger<CatalogLoad> logger)
    {
        _loader = loader;
        _paths = paths;
        _locator = locator;
        _logger = logger;
    }

    public void Begin() => _pending ??= CatalogLoad.Start(_loader, _paths, _locator, _logger);

    /// <summary>Забрать начатую загрузку (один раз); <c>null</c> — её не было или уже забрали.</summary>
    public CatalogLoad? Take() => Interlocked.Exchange(ref _pending, null);
}
