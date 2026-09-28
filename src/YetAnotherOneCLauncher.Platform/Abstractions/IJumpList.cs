namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Пункт списка переходов: запуск лаунчера с аргументами.</summary>
/// <param name="Title">Название (имя базы).</param>
/// <param name="Description">Подсказка (подключение).</param>
/// <param name="Arguments">Аргументы лаунчера, например <c>--launch=…</c>.</param>
/// <param name="IconPath">Файл со значком (платформа 1С); <c>null</c> — значок лаунчера.</param>
public sealed record JumpListEntry(string Title, string Description, string Arguments, string? IconPath);

/// <summary>
/// Список переходов у значка программы на панели задач (Windows: правый щелчок по значку).
/// Там, где его нет, — ничего не делает.
/// </summary>
public interface IJumpList
{
    /// <summary>Заменить категорию «Последние базы».</summary>
    /// <exception cref="InvalidOperationException">Оболочка отказала (например, показ недавних элементов выключен в параметрах Windows).</exception>
    void SetRecent(IReadOnlyList<JumpListEntry> entries);
}

/// <summary>Нет списка переходов (Linux и прочее).</summary>
public sealed class NoJumpList : IJumpList
{
    public void SetRecent(IReadOnlyList<JumpListEntry> entries)
    {
    }
}
