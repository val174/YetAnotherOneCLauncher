using System.Text;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Launching;

/// <summary>
/// Разбор командной строки запущенной платформы 1С: с какой базой работает процесс. Понимает ключи
/// <c>/F</c>, <c>/S</c>, <c>/WS</c>, <c>/IBConnectionString</c> и <c>/IBName</c> — со значением слитно
/// (<c>/S"srv\base"</c>) или отдельно (<c>/S "srv\base"</c>, <c>/S srv\base</c>); кавычки внутри — удвоенные.
/// </summary>
public static class PlatformCommandLine
{
    private const string FileKey = "/F";
    private const string ServerKey = "/S";
    private const string WebKey = "/WS";
    private const string ConnectionKey = "/IBConnectionString";
    private const string NameKey = "/IBName";

    /// <summary>Работает ли процесс с этой базой: адрес из командной строки совпадает с адресом базы (или имя — с <c>/IBName</c>).</summary>
    public static bool Targets(string commandLine, InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(infoBase);
        var key = infoBase.Connection.ToNormalizedKey();
        var (connections, names) = Parse(commandLine);
        return connections.Any(c => c.ToNormalizedKey() == key)
               || names.Any(n => string.Equals(n.Trim(), infoBase.Name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Строки подключения и имена баз (<c>/IBName</c>) из командной строки.</summary>
    public static (IReadOnlyList<ConnectionString> Connections, IReadOnlyList<string> Names) Parse(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var tokens = Tokenize(commandLine);
        var connections = new List<ConnectionString>();
        var names = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var key in new[] { ConnectionKey, NameKey, WebKey, FileKey, ServerKey })
            {
                if (ValueOf(tokens, ref i, key) is not { } value)
                {
                    continue;
                }

                switch (key)
                {
                    case FileKey:
                        connections.Add(ConnectionString.ForFile(value));
                        break;
                    case WebKey:
                        connections.Add(ConnectionString.ForWeb(value));
                        break;
                    case ServerKey:
                        // «сервер\база»: сервер может содержать порт, имя базы — после последнего «\».
                        var slash = value.LastIndexOf('\\');
                        if (slash > 0 && slash < value.Length - 1)
                        {
                            connections.Add(ConnectionString.ForServer(value[..slash], value[(slash + 1)..]));
                        }

                        break;
                    case ConnectionKey:
                        connections.Add(ConnectionString.Parse(value));
                        break;
                    case NameKey:
                        names.Add(value);
                        break;
                }

                break;
            }
        }

        return (connections, names);
    }

    /// <summary>
    /// Значение ключа: токен — сам ключ, значение — следующий токен; или ключ и значение в кавычках слитно.
    /// Слитно без кавычек не принимается: «/SLev» — другой ключ, не «/S» со значением «Lev».
    /// </summary>
    private static string? ValueOf(List<string> tokens, ref int index, string key)
    {
        var token = tokens[index];
        if (string.Equals(token, key, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 < tokens.Count && !tokens[index + 1].StartsWith('/'))
            {
                index++;
                return Unquote(tokens[index]);
            }

            return null;
        }

        return token.Length > key.Length + 1
               && token.StartsWith(key, StringComparison.OrdinalIgnoreCase)
               && token[key.Length] == '"'
            ? Unquote(token[key.Length..])
            : null;
    }

    /// <summary>Разбивает по пробелам вне кавычек; кавычки остаются в токенах.</summary>
    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>Снимает кавычки: «"a ""b"""» → «a "b"».</summary>
    private static string Unquote(string token)
    {
        var result = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];
            if (ch != '"')
            {
                result.Append(ch);
                continue;
            }

            if (quoted && i + 1 < token.Length && token[i + 1] == '"')
            {
                result.Append('"');
                i++;
                continue;
            }

            quoted = !quoted;
        }

        return result.ToString();
    }
}
