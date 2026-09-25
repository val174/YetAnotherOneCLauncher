namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Строка INI-подобного файла 1С: либо «ключ=значение», либо прочее
/// (пустая строка, комментарий, мусор). Исходный текст сохраняется,
/// пока значение не изменено, чтобы запись файла не меняла его без нужды.
/// </summary>
public sealed class IniLine
{
    private string? _raw;
    private string? _value;

    private IniLine(string? key, string? value, string? raw)
    {
        Key = key;
        _value = value;
        _raw = raw;
    }

    /// <summary>Ключ; <c>null</c> для строк, которые не являются парой «ключ=значение».</summary>
    public string? Key { get; }

    public bool IsKeyValue => Key is not null;

    /// <summary>Значение (без пробелов по краям). Для прочих строк — <c>null</c>.</summary>
    public string? Value
    {
        get => _value;
        set
        {
            if (!IsKeyValue)
            {
                throw new InvalidOperationException("Строка не является парой ключ=значение.");
            }

            if (value == _value)
            {
                return;
            }

            _value = value ?? string.Empty;
            _raw = null; // строка будет сформирована заново
        }
    }

    public static IniLine KeyValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new IniLine(key.Trim(), value, raw: null);
    }

    public static IniLine Other(string raw) => new(null, null, raw);

    /// <summary>Разбирает строку файла (без символов перевода строки).</summary>
    public static IniLine Parse(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
        {
            return Other(line);
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return Other(line);
        }

        var key = line[..eq].Trim();
        if (key.Length == 0)
        {
            return Other(line);
        }

        var value = line[(eq + 1)..].Trim();
        return new IniLine(key, value, raw: line);
    }

    public bool KeyEquals(string key) =>
        Key is not null && string.Equals(Key, key, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => _raw ?? $"{Key}={_value}";
}
