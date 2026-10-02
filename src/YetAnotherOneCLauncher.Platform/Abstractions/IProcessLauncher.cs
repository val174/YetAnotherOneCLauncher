using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Запуск процессов платформы и браузера.</summary>
public interface IProcessLauncher
{
    /// <summary>Запускает платформу отдельным процессом и не ждёт его завершения.</summary>
    /// <returns>Идентификатор запущенного процесса.</returns>
    /// <exception cref="LaunchFailedException">Процесс не удалось запустить.</exception>
    int Start(LaunchCommand command);

    /// <summary>Запускает платформу и ждёт завершения (например, <c>CREATEINFOBASE</c>).</summary>
    /// <returns>Код завершения процесса.</returns>
    /// <exception cref="LaunchFailedException">Процесс не удалось запустить.</exception>
    Task<int> RunAsync(LaunchCommand command, CancellationToken cancellationToken = default);

    /// <summary>Открывает адрес в браузере по умолчанию.</summary>
    /// <exception cref="LaunchFailedException">Браузер не удалось запустить.</exception>
    void OpenUrl(Uri url);

    /// <summary>Открывает каталог в проводнике (Windows) или файловом менеджере (Linux).</summary>
    /// <exception cref="LaunchFailedException">Каталог не удалось открыть.</exception>
    void OpenFolder(string path);

    /// <summary>Запускает программу без аргументов (например, стандартный стартер 1С) и не ждёт её завершения.</summary>
    /// <exception cref="LaunchFailedException">Программу не удалось запустить.</exception>
    void StartProgram(string path);

    /// <summary>
    /// Запускает программу средства администрирования с параметрами и не ждёт её завершения. В Windows — через оболочку:
    /// так открываются и оснастки <c>.msc</c>, ярлыки, документы.
    /// </summary>
    /// <exception cref="LaunchFailedException">Программу не удалось запустить.</exception>
    void OpenProgram(string path, string arguments);
}

/// <summary>Процесс не удалось запустить: файла нет, нет прав, не найден браузер и т. п.</summary>
public sealed class LaunchFailedException : Exception
{
    public LaunchFailedException()
    {
    }

    public LaunchFailedException(string message)
        : base(message)
    {
    }

    public LaunchFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
