using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace YetAnotherOneCLauncher.Core.Updates;

/// <summary>Файл релиза GitHub.</summary>
/// <param name="Sha256">Хеш SHA-256 (hex, нижний регистр) из поля <c>digest</c>; <c>null</c> — GitHub его не указал.</param>
public sealed record GitHubReleaseAsset(string Name, long Size, string? Sha256, Uri DownloadUrl);

/// <summary>Релиз GitHub — только поля, нужные для обновления.</summary>
/// <param name="TargetCommitish">Ветка (или коммит), из которой выпущен релиз.</param>
public sealed record GitHubRelease(
    string Tag,
    string Name,
    string Body,
    Uri? PageUrl,
    string TargetCommitish,
    bool IsDraft,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets);

/// <summary>Итог запроса списка релизов.</summary>
public abstract record ReleaseListResult
{
    private ReleaseListResult()
    {
    }

    public sealed record Loaded(IReadOnlyList<GitHubRelease> Releases) : ReleaseListResult;

    /// <summary>Сеть, лимит запросов GitHub, неожиданный ответ — понятная причина для пользователя.</summary>
    public sealed record Failed(string Reason) : ReleaseListResult;
}

/// <summary>
/// Релизы репозитория лаунчера на GitHub (публичный API, без токена:
/// <c>GET https://api.github.com/repos/{owner}/{repo}/releases</c>). Никогда не бросает исключений, кроме отмены.
/// </summary>
public sealed class GitHubReleaseClient
{
    public const string Owner = "val174";
    public const string Repository = "YetAnotherOneCLauncher";

    /// <summary>Страница релизов — для ссылки «скачать вручную».</summary>
    public static readonly Uri ReleasesPage = new($"https://github.com/{Owner}/{Repository}/releases");

    private static readonly Uri ReleasesApi = new($"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=20");

    private readonly HttpClient _http;

    public GitHubReleaseClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<ReleaseListResult> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (request.Headers.UserAgent.Count == 0 && _http.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                // GitHub отклоняет запросы без User-Agent.
                request.Headers.UserAgent.ParseAdd(Repository);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new ReleaseListResult.Failed("GitHub временно ограничил число запросов — попробуйте позже.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ReleaseListResult.Failed($"GitHub ответил: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // OperationCanceledException без отмены — таймаут HttpClient.
            return new ReleaseListResult.Failed("Нет связи с GitHub: " + ex.Message);
        }
    }

    /// <summary>Разбор ответа <c>/releases</c>: массив релизов; неверный JSON — <see cref="ReleaseListResult.Failed"/>.</summary>
    public static ReleaseListResult Parse(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new ReleaseListResult.Failed("GitHub вернул неожиданный ответ.");
            }

            var releases = new List<GitHubRelease>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (ParseRelease(item) is { } release)
                {
                    releases.Add(release);
                }
            }

            return new ReleaseListResult.Loaded(releases);
        }
        catch (JsonException)
        {
            return new ReleaseListResult.Failed("GitHub вернул неожиданный ответ.");
        }
    }

    private static GitHubRelease? ParseRelease(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || String(item, "tag_name") is not { Length: > 0 } tag)
        {
            return null;
        }

        var assets = new List<GitHubReleaseAsset>();
        if (item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                if (asset.ValueKind == JsonValueKind.Object
                    && String(asset, "name") is { Length: > 0 } name
                    && Uri.TryCreate(String(asset, "browser_download_url"), UriKind.Absolute, out var url))
                {
                    var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                    assets.Add(new GitHubReleaseAsset(name, size, Sha256Of(String(asset, "digest")), url));
                }
            }
        }

        return new GitHubRelease(
            tag,
            String(item, "name") ?? tag,
            String(item, "body") ?? string.Empty,
            Uri.TryCreate(String(item, "html_url"), UriKind.Absolute, out var page) ? page : null,
            String(item, "target_commitish") ?? string.Empty,
            Bool(item, "draft"),
            Bool(item, "prerelease"),
            DateTimeOffset.TryParse(String(item, "published_at"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var published) ? published : null,
            assets);
    }

    /// <summary>«sha256:4e56…» → «4e56…»; другой алгоритм или пусто — <c>null</c>.</summary>
    private static string? Sha256Of(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
