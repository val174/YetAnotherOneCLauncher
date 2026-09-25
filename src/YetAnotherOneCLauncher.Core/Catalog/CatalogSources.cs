namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>
/// Откуда читать списки. Пути подставляет слой Platform в зависимости от ОС,
/// поэтому ядро не зависит от операционной системы.
/// </summary>
/// <param name="PersonalListPath">Личный ibases.v8i.</param>
/// <param name="StarterConfigPaths">
/// Файлы 1cestart.cfg в порядке применения: сначала общий для компьютера, затем пользовательский.
/// </param>
public sealed record CatalogSources(string PersonalListPath, IReadOnlyList<string> StarterConfigPaths);

public sealed class CatalogLoadOptions
{
    /// <summary>
    /// Сколько ждать чтения одного файла. Важно для общих списков на сетевых дисках:
    /// недоступный сервер не должен подвешивать запуск приложения.
    /// </summary>
    public TimeSpan FileTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
