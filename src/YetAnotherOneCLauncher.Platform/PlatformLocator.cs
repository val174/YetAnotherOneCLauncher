using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>Ищет платформы в <c>InstalledLocation</c> и стандартных каталогах текущей ОС.</summary>
public sealed class PlatformLocator : IPlatformLocator
{
    private readonly IPlatformPaths _paths;

    public PlatformLocator(IPlatformPaths paths)
    {
        _paths = paths;
    }

    public Task<PlatformScanResult> LocateAsync(
        IEnumerable<string> additionalRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additionalRoots);

        var roots = additionalRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Environment.ExpandEnvironmentVariables(r.Trim().Trim('"')))
            .Concat(_paths.DefaultPlatformInstallRoots)
            .ToList();

        // Обход каталогов — синхронный ввод-вывод; не держим поток интерфейса.
        return Task.Run(() => PlatformScanner.Scan(roots, _paths.PlatformExecutableNames), cancellationToken);
    }
}
