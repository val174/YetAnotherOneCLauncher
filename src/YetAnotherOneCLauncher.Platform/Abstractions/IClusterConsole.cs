using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>
/// Консоль кластера серверов 1С («Администрирование серверов 1С:Предприятия»): оснастка MMC, которая работает
/// через компонент <c>radmin.dll</c> той версии платформы, что зарегистрирована в Windows. Чтобы открыть консоль
/// нужной версии, её <c>radmin.dll</c> регистрируется (с правами администратора), затем открывается оснастка.
/// </summary>
public interface IClusterConsole
{
    /// <summary>Консоль есть только в Windows.</summary>
    bool IsSupported { get; }

    /// <summary>Есть ли у платформы компонент администрирования и файл оснастки.</summary>
    bool IsAvailable(PlatformInstallation platform);

    /// <summary>Зарегистрирован ли в Windows компонент именно этой платформы.</summary>
    bool IsRegistered(PlatformInstallation platform);

    /// <summary>Зарегистрировать компонент платформы: Windows спросит права администратора.</summary>
    /// <exception cref="LaunchFailedException">Не зарегистрирован: пользователь отказал в правах или regsvr32 вернул ошибку.</exception>
    Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default);

    /// <summary>Открыть оснастку (разрядность — как у платформы).</summary>
    /// <exception cref="LaunchFailedException">Консоль не открылась.</exception>
    void Open(PlatformInstallation platform);
}

/// <summary>Консоли нет (Linux и прочее).</summary>
public sealed class NoClusterConsole : IClusterConsole
{
    public bool IsSupported => false;

    public bool IsAvailable(PlatformInstallation platform) => false;

    public bool IsRegistered(PlatformInstallation platform) => false;

    public Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default) =>
        throw new LaunchFailedException("Консоль кластера есть только в Windows.");

    public void Open(PlatformInstallation platform) =>
        throw new LaunchFailedException("Консоль кластера есть только в Windows.");
}
