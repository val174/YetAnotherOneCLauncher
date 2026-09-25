namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Корзина ОС: удалённое можно вернуть.</summary>
public interface IRecycleBin
{
    /// <summary>Перемещает файл или каталог в корзину.</summary>
    /// <exception cref="IOException">Не удалось: корзины нет на этом диске, нет прав и т. п.</exception>
    void MoveToRecycleBin(string path);
}

/// <summary>Проверка, не работает ли база, кэш которой собираются удалить.</summary>
public interface ICacheUsageProbe
{
    /// <summary>Запущенные процессы платформы 1С — для предупреждения: «1cv8c (PID 1234)».</summary>
    IReadOnlyList<string> RunningPlatformProcesses();

    /// <summary>Открыт ли какой-нибудь файл внутри каталога.</summary>
    bool IsDirectoryInUse(string path);
}
