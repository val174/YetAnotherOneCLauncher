using System.Net;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Core.Updates;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Проверка обновлений: версии, разбор релизов GitHub, выбор релиза ветки main.</summary>
public class UpdateCheckerTests
{
    private static IReadOnlyList<GitHubRelease> Releases() =>
        ((ReleaseListResult.Loaded)GitHubReleaseClient.Parse(Fixtures.Read("github_releases.json"))).Releases;

    [Theory]
    [InlineData("0.2.0", 0, 2, 0, null)]
    [InlineData("v0.2.0", 0, 2, 0, null)]
    [InlineData("V1.10", 1, 10, 0, null)]
    [InlineData("0.3.0-beta.1", 0, 3, 0, "beta.1")]
    [InlineData("0.1.0+4368cfb0123", 0, 1, 0, null)] // версия сборки с хешем коммита
    public void Parses_versions(string text, int major, int minor, int patch, string? preRelease)
    {
        var version = ReleaseVersion.Parse(text);
        Assert.Equal((major, minor, patch, preRelease), (version.Major, version.Minor, version.Patch, version.PreRelease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.x")]
    [InlineData("1.0-")]
    public void Rejects_non_versions(string text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Theory]
    [InlineData("0.2.0", "0.1.9", 1)]
    [InlineData("0.10.0", "0.9.0", 1)] // числа, а не строки
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)] // предварительная младше
    [InlineData("1.0.0.1", "1.0.0", 1)]
    public void Compares_versions(string left, string right, int sign) =>
        Assert.Equal(sign, Math.Sign(ReleaseVersion.Parse(left).CompareTo(ReleaseVersion.Parse(right))));

    [Fact]
    public void Parses_release_fields_and_sha256_digest()
    {
        var release = Releases().Single(r => r.Tag == "v0.2.0");

        Assert.Equal("YAOL v 0.2.0", release.Name);
        Assert.Equal("main", release.TargetCommitish);
        Assert.Equal(new Uri("https://github.com/val174/YetAnotherOneCLauncher/releases/tag/v0.2.0"), release.PageUrl);
        Assert.Equal(2, release.Assets.Count);
        var exe = release.Assets[0];
        Assert.Equal(66982828, exe.Size);
        Assert.Equal("4e56c62a4438086bb9f6f8c87c8b8d5a0d2a1f729c6634b026cf8f75b8d03daa", exe.Sha256); // в нижнем регистре
        Assert.Null(Releases().Single(r => r.Tag == "0.1.5").Assets[0].Sha256); // не sha256 — нет хеша
    }

    [Fact]
    public void Picks_newest_published_main_release_with_file_for_os()
    {
        // Черновик 0.4.0, предварительный 0.3.0-beta.1 и 0.2.5 из develop не подходят.
        var result = Assert.IsType<UpdateCheckResult.Available>(
            UpdateChecker.Select(Releases(), ReleaseVersion.Parse("0.1.0"), UpdateChecker.WindowsAssetName));

        Assert.Equal("0.2.0", result.Version.ToString());
        Assert.Equal("v0.2.0", result.Release.Tag);
        Assert.Equal(UpdateChecker.WindowsAssetName, result.Asset.Name);
    }

    [Fact]
    public void Release_without_file_for_os_is_skipped()
    {
        // Без 0.2.0: для Windows подходит 0.1.5, а для Linux у 0.1.5 файла нет — обновления нет.
        var windows = Assert.IsType<UpdateCheckResult.Available>(
            UpdateChecker.Select(Releases().Where(r => r.Tag != "v0.2.0"), ReleaseVersion.Parse("0.1.0"), UpdateChecker.WindowsAssetName));
        Assert.Equal("0.1.5", windows.Version.ToString());

        Assert.IsType<UpdateCheckResult.UpToDate>(
            UpdateChecker.Select(Releases().Where(r => r.Tag != "v0.2.0"), ReleaseVersion.Parse("0.1.0"), UpdateChecker.LinuxAssetName));
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public void Same_or_newer_installed_version_is_up_to_date(string current) =>
        Assert.IsType<UpdateCheckResult.UpToDate>(UpdateChecker.Select(Releases(), ReleaseVersion.Parse(current), UpdateChecker.WindowsAssetName));

    [Fact]
    public async Task Client_requests_github_api_and_reports_limits()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, Fixtures.Read("github_releases.json"));
        using var http = new HttpClient(handler);
        var result = await UpdateChecker.CheckAsync(new GitHubReleaseClient(http), ReleaseVersion.Parse("0.1.0"), UpdateChecker.WindowsAssetName);

        Assert.IsType<UpdateCheckResult.Available>(result);
        Assert.Equal("https://api.github.com/repos/val174/YetAnotherOneCLauncher/releases?per_page=20", handler.Request!.RequestUri!.ToString());
        Assert.Contains("application/vnd.github+json", handler.Request.Headers.Accept.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(handler.Request.Headers.UserAgent);

        using var limited = new StubHandler(HttpStatusCode.Forbidden, "{}"u8.ToArray());
        using var limitedHttp = new HttpClient(limited);
        var failed = Assert.IsType<UpdateCheckResult.Failed>(
            await UpdateChecker.CheckAsync(new GitHubReleaseClient(limitedHttp), ReleaseVersion.Parse("0.1.0"), UpdateChecker.WindowsAssetName));
        Assert.Contains("ограничил", failed.Reason, StringComparison.Ordinal);

        Assert.IsType<ReleaseListResult.Failed>(GitHubReleaseClient.Parse("не json"u8.ToArray()));
        Assert.IsType<ReleaseListResult.Failed>(GitHubReleaseClient.Parse("{\"message\":\"Not Found\"}"u8.ToArray()));
    }

    [Fact]
    public async Task Network_error_is_failure_not_exception()
    {
        using var handler = new StubHandler(new HttpRequestException("нет сети"));
        using var http = new HttpClient(handler);
        var result = await UpdateChecker.CheckAsync(new GitHubReleaseClient(http), ReleaseVersion.Parse("0.1.0"), UpdateChecker.WindowsAssetName);
        Assert.Contains("нет сети", Assert.IsType<UpdateCheckResult.Failed>(result).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_is_due_daily_unless_disabled()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        Assert.True(new UpdateSettings().IsCheckDue(now)); // ни разу не проверяли; по умолчанию — «Только проверка»
        Assert.False(new UpdateSettings { LastCheck = now.AddHours(-23) }.IsCheckDue(now));
        Assert.True(new UpdateSettings { LastCheck = now.AddHours(-25) }.IsCheckDue(now));
        Assert.True(new UpdateSettings { LastCheck = now.AddDays(3) }.IsCheckDue(now)); // часы перевели назад
        Assert.False(new UpdateSettings { Mode = UpdateMode.Disabled }.IsCheckDue(now));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body = [];
        private readonly Exception? _error;

        public StubHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception error) => _error = error;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return _error is not null
                ? Task.FromException<HttpResponseMessage>(_error)
                : Task.FromResult(new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) });
        }
    }
}
