using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Компонент администрирования, зарегистрированный в Windows: путь к <c>radmin.dll</c> и её разрядность.</summary>
public sealed record ClusterConsoleRegistration(string LibraryPath, PlatformArchitecture Architecture)
{
    /// <summary>Тот же ли это файл (пути сравниваются полностью, без учёта регистра, с раскрытием переменных).</summary>
    public bool Matches(string libraryPath)
    {
        ArgumentNullException.ThrowIfNull(libraryPath);
        try
        {
            var registered = Environment.ExpandEnvironmentVariables(LibraryPath.Trim().Trim('"'));
            return string.Equals(Path.GetFullPath(registered), Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

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

    /// <summary>Путь к компоненту администрирования платформы.</summary>
    string AdminLibraryPath(PlatformInstallation platform);

    /// <summary>
    /// Зарегистрированные компоненты: 64- и 32-разрядный регистрируются в Windows независимо (разные разделы
    /// реестра), поэтому их может быть два — от разных платформ. Пусто — не зарегистрирован никакой.
    /// </summary>
    IReadOnlyList<ClusterConsoleRegistration> FindRegistered();

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

    public string AdminLibraryPath(PlatformInstallation platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return Path.Combine(platform.BinDirectory, "radmin.dll");
    }

    public IReadOnlyList<ClusterConsoleRegistration> FindRegistered() => [];

    public Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default) =>
        throw new LaunchFailedException("Консоль кластера есть только в Windows.");

    public void Open(PlatformInstallation platform) =>
        throw new LaunchFailedException("Консоль кластера есть только в Windows.");
}
