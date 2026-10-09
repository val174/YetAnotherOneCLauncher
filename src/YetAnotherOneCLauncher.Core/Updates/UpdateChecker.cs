namespace YetAnotherOneCLauncher.Core.Updates;

/// <summary>Итог проверки обновлений.</summary>
public abstract record UpdateCheckResult
{
    private UpdateCheckResult()
    {
    }

    /// <summary>Новее установленной версии релизов нет.</summary>
    public sealed record UpToDate(ReleaseVersion Current) : UpdateCheckResult;

    /// <summary>Есть новая версия и файл для этой ОС.</summary>
    public sealed record Available(ReleaseVersion Version, GitHubRelease Release, GitHubReleaseAsset Asset) : UpdateCheckResult;

    /// <summary>Проверить не удалось (сеть, лимит GitHub).</summary>
    public sealed record Failed(string Reason) : UpdateCheckResult;
}

/// <summary>
/// Выбор обновления среди релизов GitHub: только опубликованные (не черновики, не предварительные) релизы ветки
/// <see cref="Branch"/>, новее текущей версии и с файлом программы для этой ОС; из подходящих — самый новый.
/// </summary>
public static class UpdateChecker
{
    /// <summary>Обновления берутся из релизов этой ветки.</summary>
    public const string Branch = "main";

    public const string WindowsAssetName = "YetAnotherOneCLauncher.exe";
    public const string LinuxAssetName = "YetAnotherOneCLauncher";

    /// <summary>Имя файла программы в релизе для этой ОС; <c>null</c> — для ОС сборок нет.</summary>
    public static string? AssetNameForCurrentOs() =>
        OperatingSystem.IsWindows() ? WindowsAssetName : OperatingSystem.IsLinux() ? LinuxAssetName : null;

    public static async Task<UpdateCheckResult> CheckAsync(
        GitHubReleaseClient client, ReleaseVersion current, string assetName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return await client.GetReleasesAsync(cancellationToken).ConfigureAwait(false) switch
        {
            ReleaseListResult.Loaded loaded => Select(loaded.Releases, current, assetName),
            ReleaseListResult.Failed failed => new UpdateCheckResult.Failed(failed.Reason),
            _ => new UpdateCheckResult.Failed("Неизвестный ответ."),
        };
    }

    public static UpdateCheckResult Select(IEnumerable<GitHubRelease> releases, ReleaseVersion current, string assetName)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(current);
        var best = releases
            .Where(r => !r.IsDraft && !r.IsPrerelease && string.Equals(r.TargetCommitish, Branch, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Release: r, Version: ReleaseVersion.TryParse(r.Tag, out var v) ? v : null,
                Asset: r.Assets.FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase))))
            .Where(c => c.Version is not null && c.Version.PreRelease is null && c.Version > current && c.Asset is not null)
            .OrderByDescending(c => c.Version)
            .FirstOrDefault();

        return best.Release is null
            ? new UpdateCheckResult.UpToDate(current)
            : new UpdateCheckResult.Available(best.Version!, best.Release, best.Asset!);
    }
}
