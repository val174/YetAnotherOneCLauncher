namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Запущенный процесс платформы 1С.</summary>
/// <param name="Id">PID.</param>
/// <param name="Name">Имя: <c>1cv8</c>, <c>1cv8c</c>.</param>
/// <param name="CommandLine">Командная строка (с какой базой он работает).</param>
public sealed record PlatformProcess(int Id, string Name, string CommandLine)
{
    public override string ToString() => $"{Name} (PID {Id})";
}

/// <summary>Проверка, не работает ли база, кэш которой собираются удалить.</summary>
public interface ICacheUsageProbe
{
    /// <summary>Процессы платформы 1С текущего пользователя — с командными строками, чтобы понять, какая база открыта.</summary>
    IReadOnlyList<PlatformProcess> CurrentUserPlatformProcesses();

    /// <summary>Открыт ли какой-нибудь файл внутри каталога.</summary>
    bool IsDirectoryInUse(string path);
}
