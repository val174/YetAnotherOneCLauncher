using System.Globalization;
using System.Text.RegularExpressions;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Что удалось определить по вставленной строке подключения. <see cref="Kind"/> =
/// <see cref="ConnectionKind.Unknown"/> — не распознано (причина в <see cref="Description"/>).
/// </summary>
public sealed record DetectedConnection
{
    public ConnectionKind Kind { get; init; } = ConnectionKind.Unknown;

    /// <summary>Каталог файловой базы.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Адрес публикации на веб-сервере, без адресов клиента и сервисов (<c>/ru_RU/</c>, <c>/odata/…</c>, <c>#e1cib/…</c>).</summary>
    public string WebUrl { get; init; } = string.Empty;

    /// <summary>Кластер серверов как в строке подключения: <c>srv</c>, <c>srv:1541</c>, <c>srv1,srv2</c>.</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Адрес сервера без порта; для списка серверов — пусто.</summary>
    public string ServerHost { get; init; } = string.Empty;

    /// <summary>Порт кластера, если указан.</summary>
    public int? ServerPort { get; init; }

    /// <summary>Имя базы в кластере.</summary>
    public string InfobaseName { get; init; } = string.Empty;

    /// <summary>
    /// Строка подключения из вставленного текста — без пользователя и пароля (<c>Usr</c>, <c>Pwd</c>):
    /// её дополнительные ключи (например, <c>wsn</c>) сохраняются вместе с базой.
    /// </summary>
    public ConnectionString? Connection { get; init; }

    /// <summary>Название базы по умолчанию: каталог, имя в кластере или публикация.</summary>
    public string SuggestedName { get; init; } = string.Empty;

    /// <summary>Что определено (или почему не получилось) — для подсказки под полем.</summary>
    public string Description { get; init; } = string.Empty;

    public bool IsRecognized => Kind is ConnectionKind.File or ConnectionKind.Server or ConnectionKind.Web;

    /// <summary>Распознано, но данных не хватает (например, нет имени базы в кластере).</summary>
    public bool IsIncomplete { get; init; }
}

/// <summary>
/// Определяет параметры базы по тому, что пользователь вставил в поле «Строка подключения»:
/// <list type="bullet">
/// <item>строка подключения 1С: <c>File="C:\Bases\Buh";</c>, <c>Srvr="srv:1541";Ref="buh";</c>, <c>ws="http://host/buh";</c>
/// (ключи в любом регистре, значения в кавычках или без, с префиксом <c>Connect=</c> из ibases.v8i);</item>
/// <item>параметры командной строки 1С: <c>/F"C:\Bases\Buh"</c>, <c>/S"srv:1541\buh"</c>, <c>/WS"http://host/buh"</c>,
/// <c>/IBConnectionString"…"</c>;</item>
/// <item>адрес в браузере: <c>https://host/buh/ru_RU/#e1cib/…</c> — берётся адрес публикации <c>https://host/buh</c>;</item>
/// <item>сервер и база, как их показывает стартер: <c>srv\buh</c>, <c>srv:1541\buh</c>, <c>tcp://srv:1541/buh</c>;</item>
/// <item>путь к каталогу базы или к файлу <c>1Cv8.1CD</c>: <c>C:\Bases\Buh</c>, <c>\\server\share\buh</c>, <c>/home/user/buh</c>.</item>
/// </list>
/// </summary>
public static partial class ConnectionStringDetector
{
    /// <summary>Порт менеджера кластера по умолчанию.</summary>
    public const int DefaultClusterPort = 1541;

    private const string DatabaseFileName = "1Cv8.1CD";

    // Сегменты адреса, с которых начинается уже не публикация, а клиент или сервис внутри неё.
    private static readonly HashSet<string> WebServiceSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "ws", "hs", "odata", "e1cib", "e1csys", "int",
    };

    private static readonly string[] SecretKeys = ["Usr", "Pwd"];

    public static DetectedConnection Detect(string? text)
    {
        var input = Unwrap(text);
        if (input.Length == 0)
        {
            return new DetectedConnection { Kind = ConnectionKind.None };
        }

        if (FromCommandLine(input) is { } fromCommandLine)
        {
            return fromCommandLine;
        }

        if (KeyValueRegex().IsMatch(input))
        {
            return FromConnectionString(ConnectionString.Parse(input));
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return ForWeb(input);
        }

        if (TcpRegex().Match(input) is { Success: true } tcp)
        {
            return ForServer(tcp.Groups["server"].Value, Uri.UnescapeDataString(tcp.Groups["base"].Value), null);
        }

        if (ServerAndBaseRegex().Match(input) is { Success: true } serverAndBase)
        {
            return ForServer(serverAndBase.Groups["server"].Value, serverAndBase.Groups["base"].Value, null);
        }

        if (IsAbsolutePath(input))
        {
            return ForFile(input, null);
        }

        return Unrecognized(
            "Не удалось определить параметры. Вставьте строку подключения (File=\"…\"; Srvr=\"…\";Ref=\"…\"; ws=\"…\"), " +
            "путь к каталогу базы, адрес веб-публикации или «сервер\\база».");
    }

    /// <summary>
    /// Разделяет кластер на адрес и порт: <c>srv:1541</c> → (srv, 1541), <c>[::1]:1541</c> → (::1, 1541).
    /// Для списка серверов через запятую — (пусто, <c>null</c>).
    /// </summary>
    public static (string Host, int? Port) SplitServer(string server)
    {
        var value = server.Trim();
        if (value.Length == 0 || value.Contains(',', StringComparison.Ordinal))
        {
            return (string.Empty, null);
        }

        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']', StringComparison.Ordinal);
            if (close > 0)
            {
                var host = value[1..close];
                var rest = value[(close + 1)..];
                return (host, rest.StartsWith(':') ? ParsePort(rest[1..]) : null);
            }
        }

        // «host:port» — двоеточие одно; несколько — адрес IPv6 без скобок, порта нет.
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && colon == value.LastIndexOf(':') && ParsePort(value[(colon + 1)..]) is { } port)
        {
            return (value[..colon], port);
        }

        return (value, null);
    }

    /// <summary>Пробелы, кавычки вокруг всего текста и префикс <c>Connect=</c> из ibases.v8i.</summary>
    private static string Unwrap(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith("Connect=", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Connect=".Length..].Trim();
        }

        // «"C:\Bases\Buh"» — кавычки вокруг пути; у строки подключения кавычки внутри, её не трогаем.
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"' && value.IndexOf('"', 1) == value.Length - 1)
        {
            value = value[1..^1].Trim();
        }

        return value;
    }

    private static DetectedConnection? FromCommandLine(string input)
    {
        if (CommandLineConnectionRegex().Match(input) is { Success: true } connection)
        {
            return FromConnectionString(ConnectionString.Parse(UnquoteArgument(connection.Groups["value"].Value)));
        }

        if (CommandLineFileRegex().Match(input) is { Success: true } file)
        {
            return ForFile(UnquoteArgument(file.Groups["value"].Value), null);
        }

        if (CommandLineWebRegex().Match(input) is { Success: true } web)
        {
            return ForWeb(UnquoteArgument(web.Groups["value"].Value));
        }

        if (CommandLineServerRegex().Match(input) is { Success: true } server)
        {
            var value = UnquoteArgument(server.Groups["value"].Value);
            var slash = value.LastIndexOf('\\');
            return slash > 0
                ? ForServer(value[..slash], value[(slash + 1)..], null)
                : ForServer(value, string.Empty, null);
        }

        return null;
    }

    /// <summary>Значение параметра командной строки: в кавычках (удвоенные кавычки внутри) или до пробела.</summary>
    private static string UnquoteArgument(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            while (end > 0 && end + 1 < trimmed.Length && trimmed[end + 1] == '"')
            {
                end = trimmed.IndexOf('"', end + 2);
            }

            return (end > 0 ? trimmed[1..end] : trimmed[1..]).Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        var space = trimmed.IndexOfAny([' ', '\t']);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static DetectedConnection FromConnectionString(ConnectionString connection)
    {
        foreach (var key in SecretKeys)
        {
            connection[key] = null;
        }

        return connection.Kind switch
        {
            ConnectionKind.File => ForFile(connection.FilePath!, connection),
            ConnectionKind.Server => ForServer(connection.Server!, connection.InfobaseName ?? string.Empty, connection),
            ConnectionKind.Web => ForWeb(connection.WebUrl!, connection),
            _ when !string.IsNullOrEmpty(connection.InfobaseName) => Unrecognized(
                $"Есть имя базы «{connection.InfobaseName}», но не указан кластер серверов (Srvr=\"…\")."),
            _ => Unrecognized("В строке подключения нет ни каталога базы (File), ни сервера (Srvr), ни веб-адреса (ws)."),
        };
    }

    private static DetectedConnection ForFile(string path, ConnectionString? connection)
    {
        var directory = path.Trim();
        // Путь к самому файлу базы — нужен каталог.
        var fileName = directory.Split('\\', '/').LastOrDefault() ?? string.Empty;
        if (fileName.Equals(DatabaseFileName, StringComparison.OrdinalIgnoreCase))
        {
            directory = directory[..^fileName.Length];
        }

        directory = TrimEndSeparators(directory);
        return new DetectedConnection
        {
            Kind = ConnectionKind.File,
            FilePath = directory,
            Connection = WithValue(connection, ConnectionString.FileKey, directory),
            SuggestedName = directory.Split('\\', '/').LastOrDefault(s => s.Length > 0 && !s.EndsWith(':')) ?? string.Empty,
            Description = $"Файловая база, каталог: {directory}",
        };
    }

    private static DetectedConnection ForServer(string server, string infobaseName, ConnectionString? connection)
    {
        var cluster = server.Trim();
        var name = infobaseName.Trim();
        var (host, port) = SplitServer(cluster);
        var location = host.Length > 0
            ? $"сервер {host}, порт {(port is { } p ? p.ToString(CultureInfo.InvariantCulture) : $"{DefaultClusterPort} (по умолчанию)")}"
            : $"кластер {cluster}";
        var incomplete = name.Length == 0;
        return new DetectedConnection
        {
            Kind = ConnectionKind.Server,
            Server = cluster,
            ServerHost = host,
            ServerPort = port,
            InfobaseName = name,
            Connection = WithValue(WithValue(connection, ConnectionString.ServerKey, cluster), ConnectionString.InfobaseRefKey, name),
            SuggestedName = name,
            IsIncomplete = incomplete,
            Description = incomplete
                ? $"База на сервере 1С:Предприятия: {location}. Не указано имя базы в кластере (Ref=\"…\")."
                : $"База на сервере 1С:Предприятия: {location}, база в кластере: {name}",
        };
    }

    private static DetectedConnection ForWeb(string url, ConnectionString? connection = null)
    {
        var publication = PublicationAddress(url.Trim());
        var name = Uri.TryCreate(publication, UriKind.Absolute, out var uri)
            ? Uri.UnescapeDataString(uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? string.Empty)
            : string.Empty;
        return new DetectedConnection
        {
            Kind = ConnectionKind.Web,
            WebUrl = publication,
            Connection = WithValue(connection, ConnectionString.WebKey, publication),
            SuggestedName = name.Length > 0 ? name : uri?.Host ?? string.Empty,
            Description = $"База на веб-сервере, адрес публикации: {publication}",
        };
    }

    /// <summary>
    /// Адрес публикации из адреса в браузере: без запроса и якоря (<c>?…</c>, <c>#e1cib/…</c>),
    /// без языка клиента (<c>/ru_RU/</c>) и адресов сервисов (<c>/odata/…</c>, <c>/hs/…</c>, <c>/ws/…</c>).
    /// </summary>
    private static string PublicationAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var kept = new List<string>();
        foreach (var segment in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kept.Count > 0 && (WebServiceSegments.Contains(segment) || ClientLanguageRegex().IsMatch(segment)))
            {
                break;
            }

            kept.Add(segment);
        }

        var path = kept.Count == 0 ? string.Empty : "/" + string.Join('/', kept);
        return uri.GetLeftPart(UriPartial.Authority) + path;
    }

    private static ConnectionString? WithValue(ConnectionString? connection, string key, string value)
    {
        if (connection is not null)
        {
            connection[key] = value;
        }

        return connection;
    }

    private static string TrimEndSeparators(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        // «C:\» и «/» оставляем корнем.
        return trimmed.Length == 0 ? path[..Math.Min(1, path.Length)]
            : trimmed.EndsWith(':') ? trimmed + path[trimmed.Length]
            : trimmed;
    }

    private static bool IsAbsolutePath(string value) =>
        DriveRootRegex().IsMatch(value) || value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith('/') || value.StartsWith("~/", StringComparison.Ordinal);

    private static DetectedConnection Unrecognized(string description) => new() { Description = description };

    private static int? ParsePort(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535 ? port : null;

    // Пара «ключ=значение» строки подключения с известным ключом.
    [GeneratedRegex(@"(^|;)\s*(File|Srvr|Ref|ws)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"(^|\s)/IBConnectionString\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLineConnectionRegex();

    // /F"C:\Bases\Buh", /F C:\Bases\Buh, /F\\server\share; «/Fxxx» без кавычки, пробела, диска или «\\» — это путь Linux.
    [GeneratedRegex(@"(^|\s)/F(?<value>\s*""[^""]*""?|\s+\S+|[A-Za-z]:[\\/]\S*|\\\\\S*)", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLineFileRegex();

    [GeneratedRegex(@"(^|\s)/WS(?<value>\s*""[^""]*""?|\s+\S+|https?://\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLineWebRegex();

    [GeneratedRegex(@"(^|\s)/S(?<value>\s*""[^""]*""?|\s+\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLineServerRegex();

    // tcp://srv:1541/buh
    [GeneratedRegex(@"^tcp://(?<server>[^/]+)/(?<base>[^/?#]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex TcpRegex();

    // srv\buh, srv:1541\buh, srv1,srv2:1541\buh — без диска и без «\\» в начале.
    [GeneratedRegex(@"^(?<server>[^\\/:,\s]+(:\d+)?(,[^\\/:,\s]+(:\d+)?)*)\\(?<base>[^\\/]+)$")]
    private static partial Regex ServerAndBaseRegex();

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRootRegex();

    // ru_RU, en_US — язык веб-клиента в адресе. Двухбуквенные «ru», «ut» не трогаем: так часто называют публикации.
    [GeneratedRegex(@"^[a-z]{2}_[A-Z]{2}$")]
    private static partial Regex ClientLanguageRegex();
}
