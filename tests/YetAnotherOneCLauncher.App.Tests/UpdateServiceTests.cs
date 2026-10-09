using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Updates;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Установка обновления: скачивание, проверка размера и SHA-256, замена файла программы, остатки.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private static readonly byte[] NewBuild = "новая версия лаунчера"u8.ToArray();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "yaocl-update-" + Guid.NewGuid().ToString("N"));

    public UpdateServiceTests() => Directory.CreateDirectory(_directory);

    private string Exe => Path.Combine(_directory, UpdateChecker.WindowsAssetName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static UpdateCheckResult.Available Update(byte[] content, string? sha256 = "auto", long? size = null)
    {
        var hash = sha256 == "auto" ? Convert.ToHexStringLower(SHA256.HashData(content)) : sha256;
        var asset = new GitHubReleaseAsset(UpdateChecker.WindowsAssetName, size ?? content.Length, hash, new Uri("https://example.test/YetAnotherOneCLauncher.exe"));
        var release = new GitHubRelease("0.2.0", "0.2.0", string.Empty, null, "main", false, false, null, [asset]);
        return new UpdateCheckResult.Available(ReleaseVersion.Parse("0.2.0"), release, asset);
    }

    private UpdateService Service(byte[] served, string? executablePath = "exe")
    {
        var http = new HttpClient(new Served(served));
        return new UpdateService(http, NullLogger<UpdateService>.Instance, ReleaseVersion.Parse("0.1.0"),
            executablePath == "exe" ? Exe : executablePath, UpdateChecker.WindowsAssetName);
    }

    [Fact]
    public async Task Verified_download_replaces_program_file()
    {
        File.WriteAllText(Exe, "старая версия");
        var progress = new List<double>();

        var result = await Service(NewBuild).InstallAsync(Update(NewBuild), new SyncProgress(progress.Add), TestContext.Current.CancellationToken);

        Assert.Equal("0.2.0", Assert.IsType<UpdateInstallResult.Installed>(result).Version.ToString());
        Assert.Equal(NewBuild, File.ReadAllBytes(Exe));
        Assert.Equal(1.0, progress[^1]);
        Assert.False(File.Exists(Exe + ".download"));
        if (OperatingSystem.IsWindows())
        {
            // Запущенный exe не удалить — он переименован и уберётся при следующем запуске.
            Assert.Equal("старая версия", File.ReadAllText(Exe + ".old"));
        }

        UpdateService.CleanupLeftovers(Exe);
        Assert.False(File.Exists(Exe + ".old"));
        Assert.True(File.Exists(Exe));
    }

    [Fact]
    public async Task Wrong_checksum_or_size_keeps_program_file()
    {
        File.WriteAllText(Exe, "старая версия");

        var badHash = await Service(NewBuild).InstallAsync(Update(NewBuild, sha256: new string('0', 64)), null, TestContext.Current.CancellationToken);
        Assert.Contains("SHA-256", Assert.IsType<UpdateInstallResult.Failed>(badHash).Reason, StringComparison.Ordinal);

        var truncated = await Service(NewBuild[..5]).InstallAsync(Update(NewBuild), null, TestContext.Current.CancellationToken);
        Assert.Contains("не полностью", Assert.IsType<UpdateInstallResult.Failed>(truncated).Reason, StringComparison.Ordinal);

        Assert.Equal("старая версия", File.ReadAllText(Exe));
        Assert.False(File.Exists(Exe + ".download"));
        Assert.False(File.Exists(Exe + ".old"));
    }

    [Fact]
    public async Task Unpublished_build_cannot_install()
    {
        var service = Service(NewBuild, executablePath: null);

        Assert.False(service.CanInstall);
        Assert.Contains("не из опубликованного файла", service.CannotInstallReason, StringComparison.Ordinal);
        Assert.IsType<UpdateInstallResult.Failed>(await service.InstallAsync(Update(NewBuild), null, TestContext.Current.CancellationToken));
        Assert.False(service.StartUpdatedVersion());
    }

    [Fact]
    public void Current_version_comes_from_assembly_without_commit_hash()
    {
        var version = UpdateService.CurrentVersionOf(typeof(UpdateService).Assembly);
        Assert.Null(version.PreRelease);
        Assert.True(version >= ReleaseVersion.Parse("0.1.0"));
    }

    [Fact]
    public void Waits_only_for_given_previous_process()
    {
        // Нет аргумента или процесс уже закрыт — не ждём.
        var started = DateTime.UtcNow;
        UpdateService.WaitForPreviousInstance([], TimeSpan.FromSeconds(5));
        UpdateService.WaitForPreviousInstance([UpdateService.AfterUpdateArgument, "999999"], TimeSpan.FromSeconds(5));
        UpdateService.WaitForPreviousInstance([UpdateService.AfterUpdateArgument, "не число"], TimeSpan.FromSeconds(5));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    private sealed class Served(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    /// <summary>Progress&lt;T&gt; сообщает через контекст синхронизации — в тесте нужен немедленный вызов.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
