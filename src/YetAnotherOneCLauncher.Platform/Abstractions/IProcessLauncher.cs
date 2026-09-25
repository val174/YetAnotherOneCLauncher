using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Запуск процессов платформы и браузера.</summary>
public interface IProcessLauncher
{
    /// <summary>Запускает платформу отдельным процессом и не ждёт его завершения.</summary>
    /// <returns>Идентификатор запущенного процесса.</returns>
    /// <exception cref="LaunchFailedException">Процесс не удалось запустить.</exception>
    int Start(LaunchCommand command);

    /// <summary>Открывает адрес в браузере по умолчанию.</summary>
    /// <exception cref="LaunchFailedException">Браузер не удалось запустить.</exception>
    void OpenUrl(Uri url);
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
