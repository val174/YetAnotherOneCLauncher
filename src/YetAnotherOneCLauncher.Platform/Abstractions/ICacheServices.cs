namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Проверка, не работает ли база, кэш которой собираются удалить.</summary>
public interface ICacheUsageProbe
{
    /// <summary>Запущенные процессы платформы 1С — для предупреждения: «1cv8c (PID 1234)».</summary>
    IReadOnlyList<string> RunningPlatformProcesses();

    /// <summary>Открыт ли какой-нибудь файл внутри каталога.</summary>
    bool IsDirectoryInUse(string path);
}
