using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Поиск установленных платформ 1С.</summary>
public interface IPlatformLocator
{
    /// <param name="additionalRoots">
    /// Дополнительные корни поиска — обычно <c>InstalledLocation</c> из 1cestart.cfg.
    /// Просматриваются раньше стандартных каталогов; <c>%ПЕРЕМЕННЫЕ%</c> раскрываются.
    /// </param>
    /// <param name="cancellationToken">Отмена поиска.</param>
    Task<PlatformScanResult> LocateAsync(IEnumerable<string> additionalRoots, CancellationToken cancellationToken = default);
}
