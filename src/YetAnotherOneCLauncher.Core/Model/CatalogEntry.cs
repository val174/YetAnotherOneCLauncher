using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Общая часть базы и папки: имя, положение в дереве, источник.</summary>
public abstract class CatalogEntry
{
    protected CatalogEntry(V8iSection section, ListSource source)
    {
        Section = section;
        Source = source;
    }

    /// <summary>Исходная секция файла. Через неё изменения записываются обратно без потери неизвестных ключей.</summary>
    public V8iSection Section { get; }

    public ListSource Source { get; }

    public bool IsReadOnly => Source.IsReadOnly;

    public string Name => Section.Name;

    /// <summary>GUID записи из ключа <c>ID</c>; может отсутствовать в старых или ручных файлах.</summary>
    public string? Id => NullIfEmpty(Section.Get(V8iKeys.Id));

    /// <summary>Папка, в которой лежит запись: "/" — корень, "/Бухгалтерия/Архив" — вложенная.</summary>
    public string FolderPath => FolderPaths.Normalize(Section.Get(V8iKeys.Folder));

    public long? OrderInList => ParseLong(Section.Get(V8iKeys.OrderInList));

    public long? OrderInTree => ParseLong(Section.Get(V8iKeys.OrderInTree));

    protected static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
