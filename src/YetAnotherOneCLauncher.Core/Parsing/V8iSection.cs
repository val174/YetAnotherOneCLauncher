namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Секция файла .v8i: <c>[Название]</c> и строки под ней.
/// Секция с непустым <c>Connect</c> — информационная база, без него — папка.
/// </summary>
public sealed class V8iSection
{
    private string _name;
    private string? _rawHeader;

    public V8iSection(string name)
        : this(name, rawHeader: null)
    {
    }

    internal V8iSection(string name, string? rawHeader)
    {
        _name = name;
        _rawHeader = rawHeader;
    }

    /// <summary>Название из заголовка. Может повторяться у разных секций.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (value == _name)
            {
                return;
            }

            _name = value;
            _rawHeader = null;
        }
    }

    public List<IniLine> Lines { get; } = [];

    internal string HeaderText => _rawHeader ?? $"[{_name}]";

    /// <summary>Значение первого ключа с таким именем (регистр не важен) или <c>null</c>.</summary>
    public string? Get(string key)
    {
        foreach (var line in Lines)
        {
            if (line.KeyEquals(key))
            {
                return line.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Устанавливает значение: меняет существующую строку или добавляет новую
    /// после последней пары «ключ=значение» (чтобы не отрывать хвостовые пустые строки).
    /// </summary>
    public void Set(string key, string value)
    {
        foreach (var line in Lines)
        {
            if (line.KeyEquals(key))
            {
                line.Value = value;
                return;
            }
        }

        var insertAt = Lines.FindLastIndex(l => l.IsKeyValue) + 1;
        Lines.Insert(insertAt, IniLine.KeyValue(key, value));
    }

    /// <summary>Удаляет все строки с этим ключом. Возвращает <c>true</c>, если что-то удалено.</summary>
    public bool Remove(string key) => Lines.RemoveAll(l => l.KeyEquals(key)) > 0;
}
