using System.Text;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>Тип подключения к информационной базе.</summary>
public enum ConnectionKind
{
    /// <summary>Строка подключения пустая.</summary>
    None,

    /// <summary>Файловая база: <c>File="C:\Bases\Buh";</c></summary>
    File,

    /// <summary>Клиент-серверная база: <c>Srvr="srv:1541";Ref="buh";</c></summary>
    Server,

    /// <summary>База через веб-сервер: <c>ws="http://host/buh";</c></summary>
    Web,

    /// <summary>Строка разобрана, но тип не распознан.</summary>
    Unknown,
}

/// <summary>
/// Строка подключения 1С: <c>Key="value";Key2="value2";</c>.
/// Кавычки внутри значения экранируются удвоением. Неизвестные ключи сохраняются,
/// порядок ключей не меняется.
/// </summary>
public sealed class ConnectionString
{
    public const string FileKey = "File";
    public const string ServerKey = "Srvr";
    public const string InfobaseRefKey = "Ref";
    public const string WebKey = "ws";

    private readonly List<KeyValuePair<string, string>> _parts;
    private readonly List<string> _errors;

    private ConnectionString(List<KeyValuePair<string, string>> parts, List<string> errors)
    {
        _parts = parts;
        _errors = errors;
    }

    public IReadOnlyList<KeyValuePair<string, string>> Parts => _parts;

    /// <summary>Замечания разбора (незакрытая кавычка, пара без '=' и т. п.). Разбор при этом не прерывается.</summary>
    public IReadOnlyList<string> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public ConnectionKind Kind
    {
        get
        {
            if (_parts.Count == 0)
            {
                return ConnectionKind.None;
            }

            if (!string.IsNullOrEmpty(this[FileKey]))
            {
                return ConnectionKind.File;
            }

            if (!string.IsNullOrEmpty(this[ServerKey]))
            {
                return ConnectionKind.Server;
            }

            if (!string.IsNullOrEmpty(this[WebKey]))
            {
                return ConnectionKind.Web;
            }

            return ConnectionKind.Unknown;
        }
    }

    public string? FilePath => this[FileKey];

    public string? Server => this[ServerKey];

    public string? InfobaseName => this[InfobaseRefKey];

    public string? WebUrl => this[WebKey];

    /// <summary>Значение по ключу (регистр не важен). Присваивание <c>null</c> удаляет ключ.</summary>
    public string? this[string key]
    {
        get
        {
            foreach (var part in _parts)
            {
                if (string.Equals(part.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Value;
                }
            }

            return null;
        }
        set
        {
            var index = _parts.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                if (index >= 0)
                {
                    _parts.RemoveAt(index);
                }

                return;
            }

            var pair = new KeyValuePair<string, string>(index >= 0 ? _parts[index].Key : key, value);
            if (index >= 0)
            {
                _parts[index] = pair;
            }
            else
            {
                _parts.Add(pair);
            }
        }
    }

    public static ConnectionString ForFile(string path) => Create((FileKey, path));

    public static ConnectionString ForServer(string server, string infobaseName) =>
        Create((ServerKey, server), (InfobaseRefKey, infobaseName));

    public static ConnectionString ForWeb(string url) => Create((WebKey, url));

    private static ConnectionString Create(params (string Key, string Value)[] parts) =>
        new(parts.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList(), []);

    /// <summary>Разбирает строку подключения. Никогда не бросает исключений: замечания попадают в <see cref="Errors"/>.</summary>
    public static ConnectionString Parse(string? text)
    {
        var parts = new List<KeyValuePair<string, string>>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ConnectionString(parts, errors);
        }

        var s = text;
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ';'))
            {
                i++;
            }

            if (i >= s.Length)
            {
                break;
            }

            var eq = s.IndexOf('=', i);
            if (eq < 0)
            {
                errors.Add($"Фрагмент без '=': «{s[i..].Trim()}».");
                break;
            }

            var key = s[i..eq].Trim();
            i = eq + 1;
            while (i < s.Length && s[i] == ' ')
            {
                i++;
            }

            string value;
            if (i < s.Length && s[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                var closed = false;
                while (i < s.Length)
                {
                    var c = s[i];
                    if (c == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        closed = true;
                        break;
                    }

                    sb.Append(c);
                    i++;
                }

                if (!closed)
                {
                    errors.Add($"Незакрытая кавычка в значении ключа «{key}».");
                }

                value = sb.ToString();

                // Всё до ближайшей ';' после закрывающей кавычки игнорируем.
                var semi = s.IndexOf(';', i);
                if (semi < 0)
                {
                    if (s[i..].Trim().Length > 0)
                    {
                        errors.Add($"Лишние символы после значения ключа «{key}».");
                    }

                    i = s.Length;
                }
                else
                {
                    if (s[i..semi].Trim().Length > 0)
                    {
                        errors.Add($"Лишние символы после значения ключа «{key}».");
                    }

                    i = semi + 1;
                }
            }
            else
            {
                var semi = s.IndexOf(';', i);
                if (semi < 0)
                {
                    semi = s.Length;
                }

                value = s[i..semi].Trim();
                i = semi;
            }

            if (key.Length == 0)
            {
                errors.Add("Пустой ключ в строке подключения.");
                continue;
            }

            parts.Add(new KeyValuePair<string, string>(key, value));
        }

        return new ConnectionString(parts, errors);
    }

    /// <summary>Формирует строку в формате 1С: каждое значение в кавычках, после каждой пары ';'.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in _parts)
        {
            sb.Append(key).Append("=\"").Append(value.Replace("\"", "\"\"")).Append("\";");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Нормализованный ключ для поиска дубликатов: регистр, направление слэшей
    /// и завершающие разделители не учитываются.
    /// </summary>
    public string ToNormalizedKey() => Kind switch
    {
        ConnectionKind.File => "file:" + NormalizePath(FilePath!),
        ConnectionKind.Server => "srvr:" + (Server ?? string.Empty).Trim().ToLowerInvariant()
                                 + "|" + (InfobaseName ?? string.Empty).Trim().ToLowerInvariant(),
        ConnectionKind.Web => "ws:" + WebUrl!.Trim().TrimEnd('/').ToLowerInvariant(),
        _ => ToString().ToLowerInvariant(),
    };

    /// <summary>Короткое описание для интерфейса: путь, «сервер\база» или URL.</summary>
    public string ToDisplayString() => Kind switch
    {
        ConnectionKind.File => FilePath!,
        ConnectionKind.Server => $"{Server}\\{InfobaseName}",
        ConnectionKind.Web => WebUrl!,
        _ => ToString(),
    };

    private static string NormalizePath(string path) =>
        path.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
