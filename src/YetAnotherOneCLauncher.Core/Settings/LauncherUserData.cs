using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>
/// Избранное, история и выбранные версии платформы поверх <see cref="LauncherSettings"/>.
/// Сопоставляет записи с базами по <see cref="InfoBaseRef.Matches"/>.
/// </summary>
public sealed class LauncherUserData
{
    /// <summary>Сколько записей истории хранить.</summary>
    public const int MaxHistoryEntries = 500;

    private readonly TimeProvider _time;

    public LauncherUserData(LauncherSettings settings, TimeProvider? time = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _time = time ?? TimeProvider.System;
    }

    public LauncherSettings Settings { get; }

    public bool IsFavorite(InfoBase infoBase) => Settings.Favorites.Exists(f => f.Matches(infoBase));

    /// <returns>Новое состояние: <c>true</c> — база в избранном.</returns>
    public bool SetFavorite(InfoBase infoBase, bool favorite)
    {
        Settings.Favorites.RemoveAll(f => f.Matches(infoBase));
        if (favorite)
        {
            Settings.Favorites.Add(InfoBaseRef.From(infoBase));
        }

        return favorite;
    }

    public void RecordLaunch(InfoBase infoBase, LaunchMode mode)
    {
        Settings.History.Add(new LaunchHistoryEntry
        {
            InfoBase = InfoBaseRef.From(infoBase),
            Mode = mode,
            LaunchedAt = _time.GetUtcNow(),
        });

        var excess = Settings.History.Count - MaxHistoryEntries;
        if (excess > 0)
        {
            Settings.History.RemoveRange(0, excess);
        }
    }

    public LaunchHistoryEntry? LastLaunch(InfoBase infoBase) =>
        Settings.History.FindLast(h => h.InfoBase.Matches(infoBase));

    public int LaunchCount(InfoBase infoBase) => Settings.History.Count(h => h.InfoBase.Matches(infoBase));

    /// <summary>Недавно запускавшиеся базы из <paramref name="available"/>, начиная с последней.</summary>
    public IReadOnlyList<InfoBase> Recent(IEnumerable<InfoBase> available, int count)
    {
        var bases = available.ToList();
        var result = new List<InfoBase>();
        for (var i = Settings.History.Count - 1; i >= 0 && result.Count < count; i--)
        {
            var entry = Settings.History[i];
            var infoBase = bases.Find(entry.InfoBase.Matches);
            if (infoBase is not null && !result.Contains(infoBase))
            {
                result.Add(infoBase);
            }
        }

        return result;
    }

    public string? PlatformVersionOverride(InfoBase infoBase) =>
        Settings.PlatformOverrides.Find(o => o.InfoBase.Matches(infoBase))?.Version;

    /// <param name="infoBase">База.</param>
    /// <param name="version"><c>null</c> — вернуть версию из списка баз.</param>
    public void SetPlatformVersionOverride(InfoBase infoBase, string? version)
    {
        Settings.PlatformOverrides.RemoveAll(o => o.InfoBase.Matches(infoBase));
        if (!string.IsNullOrWhiteSpace(version))
        {
            Settings.PlatformOverrides.Add(new PlatformVersionOverride { InfoBase = InfoBaseRef.From(infoBase), Version = version.Trim() });
        }
    }

    public InfoBaseLaunchProfile? LaunchProfile(InfoBase infoBase) =>
        Settings.InfoBaseProfiles.Find(p => p.InfoBase.Matches(infoBase));

    /// <summary>Сохраняет профиль базы; пустой профиль удаляется.</summary>
    public void SetLaunchProfile(InfoBase infoBase, InfoBaseLaunchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Settings.InfoBaseProfiles.RemoveAll(p => p.InfoBase.Matches(infoBase));
        profile = profile with
        {
            InfoBase = InfoBaseRef.From(infoBase),
            Parameters = NullIfBlank(profile.Parameters),
            UserName = NullIfBlank(profile.UserName),
        };
        if (!profile.IsEmpty)
        {
            Settings.InfoBaseProfiles.Add(profile);
        }
    }

    public string? FolderParameters(string folderPath)
    {
        var path = FolderPaths.Normalize(folderPath);
        return Settings.FolderProfiles.Find(p => SamePath(p.FolderPath, path))?.Parameters;
    }

    /// <param name="folderPath">Папка.</param>
    /// <param name="parameters">Пусто — убрать параметры папки.</param>
    public void SetFolderParameters(string folderPath, string? parameters)
    {
        var path = FolderPaths.Normalize(folderPath);
        Settings.FolderProfiles.RemoveAll(p => SamePath(p.FolderPath, path));
        if (NullIfBlank(parameters) is { } text)
        {
            Settings.FolderProfiles.Add(new FolderLaunchProfile { FolderPath = path, Parameters = text });
        }
    }

    /// <summary>Папку переименовали или перенесли в лаунчере — параметры переходят вместе с ней и вложенными.</summary>
    public void MoveFolderParameters(string oldPath, string newPath)
    {
        var from = FolderPaths.Normalize(oldPath);
        var to = FolderPaths.Normalize(newPath);
        for (var i = 0; i < Settings.FolderProfiles.Count; i++)
        {
            var path = Settings.FolderProfiles[i].FolderPath;
            if (SamePath(path, from))
            {
                Settings.FolderProfiles[i] = Settings.FolderProfiles[i] with { FolderPath = to };
            }
            else if (path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase))
            {
                Settings.FolderProfiles[i] = Settings.FolderProfiles[i] with { FolderPath = to + path[from.Length..] };
            }
        }
    }

    /// <summary>
    /// Параметры лаунчера для базы в порядке применения: папки от верхней к вложенной, затем сама база.
    /// </summary>
    public IReadOnlyList<string> ParameterChain(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        var result = new List<string>();
        var path = FolderPaths.Root;
        foreach (var segment in FolderPaths.Split(infoBase.FolderPath))
        {
            path = FolderPaths.Combine(path, segment);
            if (NullIfBlank(FolderParameters(path)) is { } folderParameters)
            {
                result.Add(folderParameters);
            }
        }

        if (NullIfBlank(LaunchProfile(infoBase)?.Parameters) is { } own)
        {
            result.Add(own);
        }

        return result;
    }

    /// <summary>Свои и встроенные шаблоны: свои — первыми.</summary>
    public IReadOnlyList<ParameterTemplate> ParameterTemplates() => [.. Settings.ParameterTemplates, .. ParameterLibrary.BuiltIn];

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
