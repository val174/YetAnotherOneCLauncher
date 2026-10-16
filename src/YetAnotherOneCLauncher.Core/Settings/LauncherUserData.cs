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

    /// <summary>Часы: время запусков и «сколько прошло».</summary>
    public TimeProvider Time => _time;

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

    /// <summary>Метка из каталога по имени (без учёта регистра).</summary>
    public TagDefinition? Tag(string name) => Settings.Tags.Find(t => TagNames.Same(t.Name, name));

    /// <summary>Каталог меток по наименованию.</summary>
    public IReadOnlyList<TagDefinition> TagCatalog() =>
        [.. Settings.Tags.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Метки базы в порядке каталога (по наименованию); имена — как в каталоге.</summary>
    public IReadOnlyList<string> TagsOf(InfoBase infoBase)
    {
        var own = Settings.InfoBaseTags.Find(t => t.InfoBase.Matches(infoBase))?.Tags;
        return own is null or { Count: 0 }
            ? []
            : [.. TagCatalog().Where(t => own.Contains(t.Name, TagNames.Comparer)).Select(t => t.Name)];
    }

    /// <summary>Создаёт метку, если её ещё нет; цвет новой — следующий по кругу.</summary>
    /// <returns>Метка из каталога; <c>null</c> — имя пустое.</returns>
    public TagDefinition? EnsureTag(string name, TagColor? color = null)
    {
        if (TagNames.Normalize(name) is not { } normalized)
        {
            return null;
        }

        if (Tag(normalized) is { } existing)
        {
            return existing;
        }

        var tag = new TagDefinition { Name = normalized, Color = color ?? NextTagColor() };
        Settings.Tags.Add(tag);
        return tag;
    }

    /// <summary>Цвет следующей новой метки: по кругу, кроме серого (его — для «архивных» меток — выбирают сами).</summary>
    public TagColor NextTagColor()
    {
        var palette = Enum.GetValues<TagColor>();
        return palette[1 + (Settings.Tags.Count % (palette.Length - 1))];
    }

    /// <summary>Ставит или снимает метку у базы; новая метка попадает в каталог.</summary>
    public void SetTag(InfoBase infoBase, string name, bool on)
    {
        var tags = TagsOf(infoBase).ToList();
        if (on)
        {
            if (EnsureTag(name) is { } tag && !tags.Contains(tag.Name, TagNames.Comparer))
            {
                tags.Add(tag.Name);
            }
        }
        else
        {
            tags.RemoveAll(t => TagNames.Same(t, name));
        }

        SetTags(infoBase, tags);
    }

    /// <summary>Заменяет метки базы; неизвестные метки попадают в каталог, пустой набор удаляет запись.</summary>
    public void SetTags(InfoBase infoBase, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var tags = names.Select(n => EnsureTag(n)?.Name).OfType<string>().Distinct(TagNames.Comparer).ToList();
        Settings.InfoBaseTags.RemoveAll(t => t.InfoBase.Matches(infoBase));
        if (tags.Count > 0)
        {
            Settings.InfoBaseTags.Add(new InfoBaseTagSet { InfoBase = InfoBaseRef.From(infoBase), Tags = tags });
        }
    }

    /// <summary>Меняет цвет метки и её показ в строках.</summary>
    public void SetTagAppearance(string name, TagColor color, bool showInRow)
    {
        var index = Settings.Tags.FindIndex(t => TagNames.Same(t.Name, name));
        if (index >= 0)
        {
            Settings.Tags[index] = Settings.Tags[index] with { Color = color, ShowInRow = showInRow };
        }
    }

    /// <summary>
    /// Переименовывает метку у всех баз. Если метка с новым именем уже есть, они объединяются (цвет — у той, что была).
    /// </summary>
    /// <returns><c>false</c> — метки нет или новое имя пустое.</returns>
    public bool RenameTag(string oldName, string newName)
    {
        var index = Settings.Tags.FindIndex(t => TagNames.Same(t.Name, oldName));
        if (index < 0 || TagNames.Normalize(newName) is not { } name)
        {
            return false;
        }

        var target = Settings.Tags.FindIndex(t => TagNames.Same(t.Name, name));
        if (target >= 0 && target != index)
        {
            Settings.Tags.RemoveAt(index);
            name = Settings.Tags[target > index ? target - 1 : target].Name;
        }
        else
        {
            Settings.Tags[index] = Settings.Tags[index] with { Name = name };
        }

        for (var i = 0; i < Settings.InfoBaseTags.Count; i++)
        {
            var set = Settings.InfoBaseTags[i];
            if (set.Tags.Contains(oldName, TagNames.Comparer))
            {
                Settings.InfoBaseTags[i] = set with
                {
                    Tags = [.. set.Tags.Select(t => TagNames.Same(t, oldName) ? name : t).Distinct(TagNames.Comparer)],
                };
            }
        }

        return true;
    }

    /// <summary>Удаляет метку из каталога и у всех баз.</summary>
    public void DeleteTag(string name)
    {
        Settings.Tags.RemoveAll(t => TagNames.Same(t.Name, name));
        for (var i = Settings.InfoBaseTags.Count - 1; i >= 0; i--)
        {
            var set = Settings.InfoBaseTags[i];
            if (!set.Tags.Contains(name, TagNames.Comparer))
            {
                continue;
            }

            var rest = set.Tags.Where(t => !TagNames.Same(t, name)).ToList();
            if (rest.Count == 0)
            {
                Settings.InfoBaseTags.RemoveAt(i);
            }
            else
            {
                Settings.InfoBaseTags[i] = set with { Tags = rest };
            }
        }
    }

    /// <summary>Свои и встроенные шаблоны: свои — первыми.</summary>
    public IReadOnlyList<ParameterTemplate> ParameterTemplates() => [.. Settings.ParameterTemplates, .. ParameterLibrary.BuiltIn];

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
