using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using YetAnotherOneCLauncher.Core.Availability;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Text;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Веб-сервис списков: HTTP-обмен без сети.</summary>
public class WebInfoBaseListClientTests
{
    private const string ListText = "[Веб-база]\r\nConnect=Srvr=\"srv\";Ref=\"web\";\r\n";

    [Fact]
    public async Task First_request_uses_zero_ids_and_follows_returned_url()
    {
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath switch
        {
            "/list/WebCommonInfoBases/CheckInfoBases" => Json("""{"root":{"InfoBasesChanged":"true","URL":"/auth/WebCommonInfoBases"}}"""),
            "/auth/WebCommonInfoBases/GetInfoBases" => Json("""{"root":{"ClientID":"c-1","InfoBasesCheckCode":"v-1","InfoBases":""" + System.Text.Json.JsonSerializer.Serialize(ListText) + "}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = new WebInfoBaseListClient(new HttpClient(handler));

        var result = await client.FetchAsync("http://host/list/", state: null, hasCachedList: false);

        var changed = Assert.IsType<WebListResult.Changed>(result);
        Assert.Equal(ListText, changed.V8iText);
        Assert.Equal(new WebListState("c-1", "v-1"), changed.State);
        Assert.Equal(
            "http://host/list/WebCommonInfoBases/CheckInfoBases?ClientID=00000000-0000-0000-0000-000000000000&InfoBasesCheckCode=00000000-0000-0000-0000-000000000000",
            handler.Requests[0]);
        Assert.StartsWith("http://host/auth/WebCommonInfoBases/GetInfoBases?", handler.Requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unchanged_list_is_not_downloaded_again()
    {
        var handler = new FakeHttp(_ => Json("""{"root":{"InfoBasesChanged":false,"URL":""}}"""));
        var client = new WebInfoBaseListClient(new HttpClient(handler));

        var result = await client.FetchAsync("https://host/list", new WebListState("c-1", "v-1"), hasCachedList: true);

        Assert.IsType<WebListResult.NotChanged>(result);
        Assert.Contains("ClientID=c-1&InfoBasesCheckCode=v-1", Assert.Single(handler.Requests), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Values_wrapped_by_1c_serializer_are_accepted()
    {
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath.EndsWith("CheckInfoBases", StringComparison.Ordinal)
            ? Json("""{"InfoBasesChanged":{"#type":"jxs:boolean","#value":true}}""")
            : Json("""{"root":{"InfoBases":{"#value":"[A]\nConnect=File=\"C:\\A\";"},"InfoBasesCheckCode":"x"}}"""));
        var client = new WebInfoBaseListClient(new HttpClient(handler));

        var result = Assert.IsType<WebListResult.Changed>(await client.FetchAsync("http://host/s", null, false));

        Assert.StartsWith("[A]", result.V8iText, StringComparison.Ordinal);
        Assert.EndsWith("/s/WebCommonInfoBases/GetInfoBases?ClientID=00000000-0000-0000-0000-000000000000&InfoBasesCheckCode=00000000-0000-0000-0000-000000000000", handler.Requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hand_built_json_with_raw_line_breaks_is_accepted()
    {
        // Как в ответе, собранном склейкой строк: переводы строк списка не экранированы.
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath.EndsWith("CheckInfoBases", StringComparison.Ordinal)
            ? Json("{\"root\": {\"InfoBasesChanged\": \"true\", \"URL\": \"/s/WebCommonInfoBases\"}}")
            : Json("{\"root\": {\"ClientID\": \"c\", \"InfoBasesCheckCode\": \"v\", \"InfoBases\": \"[A]\r\nConnect=File=\\\"C:\\\\A\\\";\r\n\tx\"}}"));
        var client = new WebInfoBaseListClient(new HttpClient(handler));

        var result = Assert.IsType<WebListResult.Changed>(await client.FetchAsync("http://host/s", null, false));

        Assert.Equal("[A]\r\nConnect=File=\"C:\\A\";\r\n\tx", result.V8iText);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Http_errors_become_failures(HttpStatusCode status, bool authentication)
    {
        var client = new WebInfoBaseListClient(new HttpClient(new FakeHttp(_ => new HttpResponseMessage(status))));

        var failed = Assert.IsType<WebListResult.Failed>(await client.FetchAsync("http://host/s", null, false));

        Assert.Equal(authentication, failed.AuthenticationRequired);
    }

    [Theory]
    [InlineData("не адрес")]
    [InlineData("ftp://host/s")]
    public async Task Bad_address_is_reported(string url)
    {
        var client = new WebInfoBaseListClient(new HttpClient(new FakeHttp(_ => throw new InvalidOperationException())));

        Assert.IsType<WebListResult.Failed>(await client.FetchAsync(url, null, false));
    }

    [Fact]
    public async Task Network_error_and_non_json_are_failures()
    {
        var down = new WebInfoBaseListClient(new HttpClient(new FakeHttp(_ => throw new HttpRequestException("connection refused"))));
        var html = new WebInfoBaseListClient(new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") })));

        Assert.Contains("connection refused", Assert.IsType<WebListResult.Failed>(await down.FetchAsync("http://h/s", null, false)).Message, StringComparison.Ordinal);
        Assert.Contains("не JSON", Assert.IsType<WebListResult.Failed>(await html.FetchAsync("http://h/s", null, false)).Message, StringComparison.Ordinal);
    }

    internal static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Загрузка с копиями общих списков, веб-сервисом и постепенной выдачей.</summary>
public class CachedCatalogLoadingTests
{
    [Fact]
    public async Task Unavailable_common_list_is_shown_from_cache_with_date()
    {
        using var temp = new TempDirectory();
        var commonPath = temp.Combine("common.v8i");
        var cfgPath = temp.Combine("1cestart.cfg");
        await File.WriteAllTextAsync(cfgPath, $"CommonInfoBases={commonPath}\r\n");
        await WriteV8iAsync(commonPath, "[Общая]\r\nConnect=File=\"C:\\Common\";\r\n");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.Zero));
        var loader = new InfoBaseCatalogLoader(cache: new ListCache(temp.Combine("cache")), time: time);
        var sources = new CatalogSources(temp.Combine("ibases.v8i"), [cfgPath]);

        var first = await loader.LoadAsync(sources);
        Assert.False(first.InfoBases.Single().Source.IsFromCache);

        File.Delete(commonPath); // сетевой диск отвалился
        var second = await loader.LoadAsync(sources);

        var infoBase = Assert.Single(second.InfoBases);
        Assert.Equal("Общая", infoBase.Name);
        Assert.Equal(time.GetUtcNow(), infoBase.Source.CachedAt);
        Assert.True(infoBase.IsReadOnly);
        Assert.Contains(second.Warnings, w => w.Message.Contains("Показана сохранённая копия на", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Progress_shows_personal_and_cached_lists_before_network()
    {
        using var temp = new TempDirectory();
        var commonPath = temp.Combine("common.v8i");
        var cfgPath = temp.Combine("1cestart.cfg");
        var personalPath = temp.Combine("ibases.v8i");
        await File.WriteAllTextAsync(cfgPath, $"CommonInfoBases={commonPath}\r\n");
        await WriteV8iAsync(commonPath, "[Общая]\r\nConnect=File=\"C:\\Common\";\r\n");
        await WriteV8iAsync(personalPath, "[Личная]\r\nConnect=File=\"C:\\Mine\";\r\n");
        var cache = new ListCache(temp.Combine("cache"));
        var sources = new CatalogSources(personalPath, [cfgPath]);
        await new InfoBaseCatalogLoader(cache: cache).LoadAsync(sources); // заполнить кэш

        await WriteV8iAsync(commonPath, "[Общая]\r\nConnect=File=\"C:\\Common\";\r\n[Новая общая]\r\nConnect=File=\"C:\\New\";\r\n");
        var reports = new List<InfoBaseCatalog>();
        var final = await new InfoBaseCatalogLoader(cache: cache).LoadAsync(sources, new SyncProgress<InfoBaseCatalog>(reports.Add));

        Assert.Equal(2, reports.Count);
        Assert.Equal(new[] { "Личная", "Общая" }, reports[0].InfoBases.Select(b => b.Name));
        Assert.True(reports[0].Lists[1].IsPending);
        Assert.True(reports[0].InfoBases[1].Source.IsFromCache);
        Assert.Equal(new[] { "Личная", "Общая", "Новая общая" }, final.InfoBases.Select(b => b.Name));
        Assert.False(final.Lists[1].IsPending);
    }

    [Fact]
    public async Task Web_service_list_is_loaded_cached_and_reused_when_unchanged()
    {
        using var temp = new TempDirectory();
        var cfgPath = temp.Combine("1cestart.cfg");
        await File.WriteAllTextAsync(cfgPath, "InternetService=http://host/list\r\n");
        var changed = true;
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath.EndsWith("CheckInfoBases", StringComparison.Ordinal)
            ? WebInfoBaseListClientTests.Json("""{"root":{"InfoBasesChanged":""" + (changed ? "true" : "false") + "}}")
            : WebInfoBaseListClientTests.Json("""{"root":{"ClientID":"c-9","InfoBasesCheckCode":"v-9","InfoBases":"[С веб-сервиса]\r\nConnect=Srvr=\"srv\";Ref=\"w\";\r\n"}}"""));
        var cache = new ListCache(temp.Combine("cache"));
        var loader = new InfoBaseCatalogLoader(cache: cache, web: new WebInfoBaseListClient(new HttpClient(handler)));
        var sources = new CatalogSources(temp.Combine("ibases.v8i"), [cfgPath]);

        var first = await loader.LoadAsync(sources);
        Assert.Equal("С веб-сервиса", first.InfoBases.Single().Name);
        Assert.Equal(ListSourceKind.InternetService, first.InfoBases.Single().Source.Kind);

        changed = false;
        handler.Requests.Clear();
        var second = await loader.LoadAsync(sources);

        Assert.Equal("С веб-сервиса", second.InfoBases.Single().Name);
        Assert.False(second.InfoBases.Single().Source.IsFromCache); // сервис подтвердил, что копия актуальна
        Assert.Contains("ClientID=c-9&InfoBasesCheckCode=v-9", Assert.Single(handler.Requests), StringComparison.Ordinal);
        Assert.DoesNotContain(second.Warnings, w => w.Level == CatalogWarningLevel.Warning);
    }

    [Fact]
    public async Task Without_web_client_internet_service_is_only_mentioned()
    {
        using var temp = new TempDirectory();
        var cfgPath = temp.Combine("1cestart.cfg");
        await File.WriteAllTextAsync(cfgPath, "InternetService=http://host/list\r\n");

        var catalog = await new InfoBaseCatalogLoader().LoadAsync(new CatalogSources(temp.Combine("ibases.v8i"), [cfgPath]));

        Assert.Contains(catalog.Warnings, w => w.Level == CatalogWarningLevel.Info && w.Location == "http://host/list");
        Assert.Single(catalog.Lists);
    }

    [Fact]
    public async Task Corrupted_cache_is_ignored()
    {
        using var temp = new TempDirectory();
        var cache = new ListCache(temp.Path);
        var source = new ListSource(ListSourceKind.Common, @"\\srv\share\list.v8i");
        await cache.SaveAsync(source, "[A]\r\nConnect=File=\"C:\\A\";\r\n", new CachedListInfo { SavedAt = DateTimeOffset.UnixEpoch });
        Assert.NotNull(await cache.LoadAsync(source));

        await File.WriteAllTextAsync(Path.Combine(temp.Path, ListCache.KeyOf(source) + ".json"), "{ битый");

        Assert.Null(await cache.LoadAsync(source));
        Assert.Null(await cache.LoadAsync(source with { Location = @"\\srv\share\other.v8i" }));
    }

    private static Task WriteV8iAsync(string path, string text) =>
        File.WriteAllBytesAsync(path, TextFileCodec.Encode(text, TextFormat.V8iDefault));
}

public class AvailabilityCheckerTests
{
    [Theory]
    [InlineData("srv1c", "srv1c", 1541)]
    [InlineData("srv1c:1641", "srv1c", 1641)]
    [InlineData("tcp://srv1c:1541", "srv1c", 1541)]
    [InlineData(" srv-a:2541, srv-b:1541 ", "srv-a", 2541)]
    [InlineData("[fe80::1]:1541", "fe80::1", 1541)]
    [InlineData("[fe80::1]", "fe80::1", 1541)]
    public void Server_address_is_parsed(string server, string host, int port)
    {
        Assert.Equal((host, port), AvailabilityChecker.ParseServer(server));
    }

    [Theory]
    [InlineData("")]
    [InlineData("srv:порт")]
    [InlineData(":1541")]
    [InlineData("srv:70000")]
    public void Bad_server_address_is_null(string server)
    {
        Assert.Null(AvailabilityChecker.ParseServer(server));
    }

    [Fact]
    public async Task Each_address_is_probed_once_and_results_are_reported()
    {
        var probe = new FakeProbe { Directories = { @"C:\Here" }, Endpoints = { "srv:1541" } };
        var checker = new AvailabilityChecker(probe);
        var bases = new[]
        {
            InfoBase("Connect=File=\"C:\\Here\";", "ID=1"),
            InfoBase("Connect=File=\"C:\\Gone\";", "ID=2"),
            InfoBase("Connect=Srvr=\"srv\";Ref=\"a\";", "ID=3"),
            InfoBase("Connect=Srvr=\"srv:1541\";Ref=\"b\";", "ID=4"),
            InfoBase("Connect=Srvr=\"down\";Ref=\"c\";", "ID=5"),
            InfoBase("Connect=ws=\"https://web.example/buh\";", "ID=6"),
            InfoBase("Connect=Что-то=\"x\";", "ID=7"),
        };
        var reported = new List<string>();

        var results = await checker.CheckAsync(bases, new SyncProgress<KeyValuePair<string, AvailabilityResult>>(r => reported.Add(r.Key)));

        AvailabilityStatus StatusOf(int i) => results[bases[i].IdentityKey].Status;
        Assert.Equal(AvailabilityStatus.Available, StatusOf(0));
        Assert.Equal(AvailabilityStatus.Unavailable, StatusOf(1));
        Assert.Equal(AvailabilityStatus.Available, StatusOf(2));
        Assert.Equal(AvailabilityStatus.Available, StatusOf(3));
        Assert.Equal(AvailabilityStatus.Unavailable, StatusOf(4));
        Assert.Equal("down:1541 не отвечает", results[bases[4].IdentityKey].Message);
        Assert.Equal(AvailabilityStatus.Unavailable, StatusOf(5));
        Assert.Equal(AvailabilityStatus.Unknown, StatusOf(6));
        Assert.Equal(1, probe.Calls["srv:1541"]); // две базы на одном сервере — одна проверка
        Assert.Equal(1, probe.Calls["web.example:443"]);
        Assert.Equal(7, reported.Count);
    }

    [Fact]
    public async Task Hanging_probe_is_cut_by_timeout()
    {
        var checker = new AvailabilityChecker(new FakeProbe { Hang = true }, TimeSpan.FromMilliseconds(50));

        var result = (await checker.CheckAsync([InfoBase("Connect=File=\"\\\\dead\\share\";")])).Single().Value;

        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
        Assert.Contains("нет ответа", result.Message, StringComparison.Ordinal);
    }

    private sealed class FakeProbe : IAvailabilityProbe
    {
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Endpoints { get; } = [];

        public Dictionary<string, int> Calls { get; } = [];

        public bool Hang { get; init; }

        public async Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
        {
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }

            return Directories.Contains(path);
        }

        public Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            var key = $"{host}:{port}";
            lock (Calls)
            {
                Calls[key] = Calls.GetValueOrDefault(key) + 1;
            }

            return Task.FromResult(Endpoints.Contains(key));
        }
    }
}

/// <summary>HTTP без сети: ответ задаёт тест, запросы запоминаются.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
        }

        return Task.FromResult(respond(request));
    }
}

/// <summary>Progress без SynchronizationContext: вызывает обработчик сразу.</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Lock _gate = new();

    public void Report(T value)
    {
        lock (_gate)
        {
            handler(value);
        }
    }
}
