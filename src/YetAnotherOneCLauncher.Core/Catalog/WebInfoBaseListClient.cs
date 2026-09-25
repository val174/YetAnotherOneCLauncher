using System.Net;
using System.Text.Json;

namespace YetAnotherOneCLauncher.Core.Catalog;

/// <summary>Что помнит клиент между обращениями к сервису.</summary>
/// <param name="ClientId">Идентификатор, выданный сервисом при первом получении списка.</param>
/// <param name="CheckCode">Код версии полученного списка.</param>
public sealed record WebListState(string ClientId, string CheckCode);

/// <summary>Ответ веб-сервиса списков.</summary>
public abstract record WebListResult
{
    private WebListResult()
    {
    }

    /// <summary>Список не изменился — можно показывать сохранённую копию.</summary>
    public sealed record NotChanged : WebListResult;

    /// <summary>Новый список в формате v8i.</summary>
    public sealed record Changed(string V8iText, WebListState State) : WebListResult;

    /// <summary>Сервис недоступен или ответил ошибкой.</summary>
    public sealed record Failed(string Message, bool AuthenticationRequired = false) : WebListResult;
}

/// <summary>
/// Клиент веб-сервиса общих списков баз (<c>InternetService</c> в 1cestart.cfg, сервис <c>WebCommonInfoBases</c>).
/// </summary>
/// <remarks>
/// Протокол — как у штатного стартера (по описанию в документации 1С и реализациям, работающим со стартером):
/// <list type="number">
/// <item><c>GET {адрес}/WebCommonInfoBases/CheckInfoBases?ClientID=…&amp;InfoBasesCheckCode=…</c> — анонимно;
/// ответ <c>{"root":{"InfoBasesChanged":true,"URL":"/путь/WebCommonInfoBases"}}</c>.</item>
/// <item>Если список изменился — <c>GET {URL}/GetInfoBases?ClientID=…&amp;InfoBasesCheckCode=…</c>
/// (публикация может требовать входа); ответ
/// <c>{"root":{"ClientID":"…","InfoBasesCheckCode":"…","InfoBases":"текст v8i"}}</c>.</item>
/// </list>
/// При первом обращении оба параметра — нулевой GUID. Значения в ответе принимаются и как JSON-типы,
/// и как строки (<c>"true"</c>), и в обёртке <c>{"#value": …}</c>; <c>URL</c> — абсолютный или от корня сервера.
/// </remarks>
public sealed class WebInfoBaseListClient
{
    public const string EmptyId = "00000000-0000-0000-0000-000000000000";
    private const string ServiceName = "WebCommonInfoBases";

    private readonly HttpClient _http;

    public WebInfoBaseListClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <param name="serviceUrl">Адрес из <c>InternetService</c>.</param>
    /// <param name="state">Что сервис выдал в прошлый раз; <c>null</c> — первое обращение.</param>
    /// <param name="hasCachedList">Есть ли сохранённая копия: без неё список запрашивается, даже если «не изменился».</param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task<WebListResult> FetchAsync(
        string serviceUrl,
        WebListState? state,
        bool hasCachedList,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(serviceUrl.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return new WebListResult.Failed($"Некорректный адрес веб-сервиса: {serviceUrl}");
        }

        var clientId = hasCachedList && state is not null ? state.ClientId : EmptyId;
        var checkCode = hasCachedList && state is not null ? state.CheckCode : EmptyId;
        var serviceRoot = Combine(baseUri, ServiceName);

        try
        {
            var check = await GetJsonAsync(Combine(serviceRoot, "CheckInfoBases", clientId, checkCode), cancellationToken).ConfigureAwait(false);
            if (check.Error is { } checkError)
            {
                return checkError;
            }

            var changed = ReadBool(check.Root, "InfoBasesChanged") ?? true;
            if (!changed && hasCachedList)
            {
                return new WebListResult.NotChanged();
            }

            var listRoot = ReadString(check.Root, "URL") is { Length: > 0 } url && Uri.TryCreate(baseUri, url, out var resolved)
                ? resolved
                : serviceRoot;
            var get = await GetJsonAsync(Combine(listRoot, "GetInfoBases", clientId, checkCode), cancellationToken).ConfigureAwait(false);
            if (get.Error is { } getError)
            {
                return getError;
            }

            if (ReadString(get.Root, "InfoBases") is not { } text)
            {
                return new WebListResult.Failed("Веб-сервис не вернул список баз (нет поля InfoBases).");
            }

            var newState = new WebListState(
                ReadString(get.Root, "ClientID") is { Length: > 0 } id ? id : clientId,
                ReadString(get.Root, "InfoBasesCheckCode") ?? string.Empty);
            return new WebListResult.Changed(text, newState);
        }
        catch (HttpRequestException ex)
        {
            return new WebListResult.Failed($"Веб-сервис недоступен: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebListResult.Failed("Веб-сервис не ответил вовремя.");
        }
    }

    private async Task<(JsonElement Root, WebListResult.Failed? Error)> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return (default, new WebListResult.Failed($"Веб-сервис требует входа ({(int)response.StatusCode}): {uri.GetLeftPart(UriPartial.Path)}", AuthenticationRequired: true));
        }

        if (!response.IsSuccessStatusCode)
        {
            return (default, new WebListResult.Failed($"Веб-сервис ответил {(int)response.StatusCode} {response.ReasonPhrase}: {uri.GetLeftPart(UriPartial.Path)}"));
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = ParseLenient(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "root", out var inner))
            {
                root = inner;
            }

            return root.ValueKind == JsonValueKind.Object
                ? (root.Clone(), null)
                : (default, new WebListResult.Failed("Веб-сервис вернул ответ неожиданного вида."));
        }
        catch (JsonException ex)
        {
            return (default, new WebListResult.Failed($"Веб-сервис вернул не JSON: {ex.Message}"));
        }
    }

    /// <summary>
    /// JSON, собранный вручную (в обработчике 1С или скриптом), часто содержит переводы строк списка v8i
    /// прямо внутри строки. Строгий разбор такое отвергает — тогда управляющие символы в строках экранируются.
    /// </summary>
    internal static JsonDocument ParseLenient(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(EscapeControlCharacters(body));
        }
    }

    private static byte[] EscapeControlCharacters(byte[] body)
    {
        var result = new List<byte>(body.Length + 64);
        var inString = false;
        var escaped = false;
        foreach (var b in body)
        {
            if (inString && !escaped && b < 0x20)
            {
                result.AddRange(b switch
                {
                    (byte)'\n' => "\\n"u8.ToArray(),
                    (byte)'\r' => "\\r"u8.ToArray(),
                    (byte)'\t' => "\\t"u8.ToArray(),
                    _ => System.Text.Encoding.ASCII.GetBytes($"\\u{b:x4}"),
                });
                continue;
            }

            result.Add(b);
            if (escaped)
            {
                escaped = false;
            }
            else if (inString && b == (byte)'\\')
            {
                escaped = true;
            }
            else if (b == (byte)'"')
            {
                inString = !inString;
            }
        }

        return [.. result];
    }

    private static Uri Combine(Uri root, string segment) => new(root.AbsoluteUri.TrimEnd('/') + "/" + segment);

    private static Uri Combine(Uri root, string operation, string clientId, string checkCode) =>
        new(Combine(root, operation).AbsoluteUri
            + "?ClientID=" + Uri.EscapeDataString(clientId)
            + "&InfoBasesCheckCode=" + Uri.EscapeDataString(checkCode));

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                // Сериализация 1С может обернуть значение: {"#type": "...", "#value": ...}.
                if (value.ValueKind == JsonValueKind.Object && TryGetProperty(value, "#value", out var wrapped))
                {
                    value = wrapped;
                }

                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            }
            : null;

    private static bool? ReadBool(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => value.GetString()?.Trim().ToUpperInvariant() switch
                {
                    "TRUE" or "1" or "ИСТИНА" => true,
                    "FALSE" or "0" or "ЛОЖЬ" => false,
                    _ => null,
                },
                JsonValueKind.Number => value.GetDouble() != 0,
                _ => null,
            }
            : null;
}
