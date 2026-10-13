using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.IO;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Exchange;

/// <summary>Что сохранять в файл вместе со списком баз.</summary>
/// <param name="LaunchParameters">Параметры запуска баз и групп.</param>
/// <param name="BaseSettings">Избранное, версия платформы, проект 1C:EDT.</param>
/// <param name="UserNames">Имена пользователей.</param>
public sealed record ExportOptions(bool LaunchParameters = true, bool BaseSettings = true, bool UserNames = false)
{
    public static ExportOptions From(ListExchangeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ExportOptions(settings.IncludeLaunchParameters, settings.IncludeBaseSettings, settings.IncludeUserNames);
    }
}

/// <summary>Как загружать файл в личный список.</summary>
public enum ImportMode
{
    /// <summary>Добавить базы и группы, которых ещё нет.</summary>
    Add,

    /// <summary>Заменить личный список содержимым файла.</summary>
    Replace,
}

/// <summary>Файл не удалось прочитать как список баз лаунчера.</summary>
public sealed class InfoBaseListFileException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Сохранение личного списка в JSON и загрузка обратно.</summary>
public static class InfoBaseListExchange
{
    private static readonly InfoBaseListJsonContext Json = new(new JsonSerializerOptions(InfoBaseListJsonContext.Default.Options)
    {
        // Русские названия — читаемыми буквами, а не \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>Ключи .v8i, у которых в файле свои поля; порядок — как у штатного стартера.</summary>
    private static readonly string[] KeyOrder =
    [
        V8iKeys.Connect, V8iKeys.Id, V8iKeys.OrderInList, V8iKeys.Folder, V8iKeys.OrderInTree, V8iKeys.External,
        V8iKeys.ClientConnectionSpeed, V8iKeys.App, V8iKeys.WindowsAuthentication, V8iKeys.Version, V8iKeys.DefaultApp,
        V8iKeys.AdditionalParameters, V8iKeys.AppArch,
    ];

    private static readonly HashSet<string> MappedKeys = new(
        KeyOrder.Where(k => k != V8iKeys.External), StringComparer.OrdinalIgnoreCase);

    private static readonly ListSource Personal = new(ListSourceKind.Personal, string.Empty);

    /// <summary>Файл из записей личного списка и настроек лаунчера для них.</summary>
    /// <param name="sections">Записи личного списка: базы и группы.</param>
    /// <param name="userData">Настройки лаунчера; <c>null</c> — сохранить только список.</param>
    /// <param name="options">Что сохранять вместе со списком.</param>
    /// <param name="wholeList">Сохраняется весь список (тогда и параметры корня списка).</param>
    /// <param name="appVersion">Версия лаунчера.</param>
    /// <param name="exportedAt">Время сохранения.</param>
    public static InfoBaseListFile Export(
        IEnumerable<V8iSection> sections,
        LauncherUserData? userData,
        ExportOptions options,
        bool wholeList,
        string? appVersion = null,
        DateTimeOffset? exportedAt = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(options);
        var folders = new List<ExportedFolder>();
        var bases = new List<ExportedInfoBase>();
        foreach (var section in sections.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
        {
            if (V8iSections.IsFolder(section))
            {
                folders.Add(ExportFolder(section));
            }
            else
            {
                bases.Add(ExportBase(section, userData, options));
            }
        }

        var folderParameters = new List<ExportedFolderParameters>();
        if (userData is not null && options.LaunchParameters)
        {
            // Параметры сохранённых групп; корня списка ("/") — только вместе со всем списком.
            var paths = folders.Select(f => f.Path);
            if (wholeList)
            {
                paths = paths.Prepend(FolderPaths.Root);
            }

            folderParameters.AddRange(paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => (Path: p, Parameters: userData.FolderParameters(p)))
                .Where(p => !string.IsNullOrWhiteSpace(p.Parameters))
                .Select(p => new ExportedFolderParameters { Folder = p.Path, Parameters = p.Parameters! }));
        }

        return new InfoBaseListFile
        {
            AppVersion = appVersion,
            ExportedAt = exportedAt,
            Folders = folders,
            FolderParameters = folderParameters,
            InfoBases = bases,
            Tags = ExportTags(bases, userData),
        };
    }

    public static byte[] Serialize(InfoBaseListFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return JsonSerializer.SerializeToUtf8Bytes(file, Json.InfoBaseListFile);
    }

    public static Task SaveAsync(InfoBaseListFile file, string path, CancellationToken cancellationToken = default) =>
        AtomicFileWriter.WriteAllBytesAsync(path, Serialize(file), backupPath: null, cancellationToken);

    /// <summary>Разбирает и проверяет файл.</summary>
    /// <exception cref="InfoBaseListFileException">Не файл списка баз, более новая версия формата или ошибка в записях.</exception>
    public static InfoBaseListFile Parse(ReadOnlySpan<byte> json)
    {
        InfoBaseListFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, Json.InfoBaseListFile);
        }
        catch (JsonException ex)
        {
            throw new InfoBaseListFileException($"Файл повреждён или это не JSON: {ex.Message}", ex);
        }

        if (file is null || !string.Equals(file.Format, InfoBaseListFile.FormatName, StringComparison.Ordinal))
        {
            throw new InfoBaseListFileException("Это не файл списка баз YetAnotherOneCLauncher.");
        }

        if (file.FormatVersion > InfoBaseListFile.CurrentFormatVersion)
        {
            throw new InfoBaseListFileException(
                $"Файл сохранён более новой версией лаунчера{(file.AppVersion is { } v ? $" ({v})" : string.Empty)}. Обновите лаунчер.");
        }

        var folders = file.Folders ?? [];
        var bases = file.InfoBases ?? [];
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i] is null || string.IsNullOrWhiteSpace(bases[i].Name) || string.IsNullOrWhiteSpace(bases[i].Connection))
            {
                throw new InfoBaseListFileException($"В базе №{i + 1} не указано название или строка подключения.");
            }
        }

        if (folders.Any(f => f is null || FolderPaths.Normalize(f.Path) == FolderPaths.Root))
        {
            throw new InfoBaseListFileException("У одной из групп не указан путь.");
        }

        return file with
        {
            Folders = folders,
            FolderParameters = file.FolderParameters?.Where(p => p is not null).ToList() ?? [],
            InfoBases = bases,
        };
    }

    public static async Task<InfoBaseListFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));

    /// <summary>Записи .v8i из файла: сначала группы, затем базы — в порядке файла.</summary>
    public static V8iDocument ToDocument(InfoBaseListFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var document = new V8iDocument();
        foreach (var folder in file.Folders)
        {
            var path = FolderPaths.Normalize(folder.Path);
            var segments = FolderPaths.Split(path);
            var parent = "/" + string.Join('/', segments[..^1]);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [V8iKeys.Id] = folder.Id,
                [V8iKeys.OrderInList] = FormatOrder(folder.OrderInList),
                [V8iKeys.Folder] = FolderPaths.Normalize(parent),
                [V8iKeys.OrderInTree] = FormatOrder(folder.OrderInTree),
            };
            document.Sections.Add(BuildSection(segments[^1], values, folder.OtherKeys));
        }

        foreach (var infoBase in file.InfoBases)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [V8iKeys.Connect] = infoBase.Connection.Trim(),
                [V8iKeys.Id] = infoBase.Id,
                [V8iKeys.OrderInList] = FormatOrder(infoBase.OrderInList),
                [V8iKeys.Folder] = FolderPaths.Normalize(infoBase.Folder),
                [V8iKeys.OrderInTree] = FormatOrder(infoBase.OrderInTree),
                [V8iKeys.ClientConnectionSpeed] = infoBase.ClientConnectionSpeed,
                [V8iKeys.App] = infoBase.App,
                [V8iKeys.WindowsAuthentication] = infoBase.WindowsAuthentication switch
                {
                    true => "1",
                    false => "0",
                    null => null,
                },
                [V8iKeys.Version] = infoBase.Version,
                [V8iKeys.DefaultApp] = infoBase.DefaultApp,
                [V8iKeys.AdditionalParameters] = infoBase.AdditionalParameters,
                [V8iKeys.AppArch] = infoBase.Architecture,
            };
            document.Sections.Add(BuildSection(infoBase.Name.Trim(), values, infoBase.OtherKeys));
        }

        return document;
    }

    /// <summary>Что получится при загрузке — для окна подтверждения. Список не меняется.</summary>
    /// <param name="document">Текущий личный список.</param>
    /// <param name="file">Файл.</param>
    /// <param name="mode">Режим загрузки.</param>
    /// <param name="takenNames">Названия баз в других списках.</param>
    public static ImportResult Preview(V8iDocument document, InfoBaseListFile file, ImportMode mode, IEnumerable<string>? takenNames = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var copy = V8iDocument.Parse(document.ToText());
        return Import(copy, file, mode, takenNames, added: null);
    }

    /// <summary>
    /// Загружает файл в личный список. «Добавить» — как загрузка из .v8i: базы, которые уже есть (по ID или подключению),
    /// базы с занятым названием и существующие группы пропускаются. «Заменить» — прежние записи удаляются,
    /// базы с названием из других списков всё равно пропускаются.
    /// </summary>
    /// <param name="added">Сюда попадают добавленные записи (для переноса настроек).</param>
    public static ImportResult Import(
        V8iDocument document,
        InfoBaseListFile file,
        ImportMode mode,
        IEnumerable<string>? takenNames,
        ICollection<V8iSection>? added)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(file);
        if (mode == ImportMode.Replace)
        {
            document.Sections.Clear();
        }

        return PersonalListEditor.Import(document, ToDocument(file), takenNames, added);
    }

    /// <summary>
    /// Переносит настройки лаунчера из файла на добавленные базы и группы. Пароли не переносятся.
    /// </summary>
    /// <param name="file">Файл.</param>
    /// <param name="added">Добавленные записи (<see cref="Import"/>).</param>
    /// <param name="userData">Настройки лаунчера.</param>
    /// <param name="replaceFolderParameters">Перезаписывать параметры групп, которые уже заданы.</param>
    /// <returns>Сколько баз получили настройки.</returns>
    public static int ApplySettings(
        InfoBaseListFile file, IEnumerable<V8iSection> added, LauncherUserData userData, bool replaceFolderParameters)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(userData);
        var addedBases = added.Where(s => !V8iSections.IsFolder(s)).Select(s => new InfoBase(s, Personal)).ToList();
        ImportTags(file, userData);
        var count = 0;
        foreach (var exported in file.InfoBases)
        {
            if (exported.Launcher is not { } settings || FindAdded(exported, addedBases) is not { } infoBase)
            {
                continue;
            }

            if (settings.Favorite is { } favorite)
            {
                userData.SetFavorite(infoBase, favorite);
            }

            if (settings.PlatformVersion is not null)
            {
                userData.SetPlatformVersionOverride(infoBase, settings.PlatformVersion);
            }

            // Пароль (PasswordKey) остаётся пустым: в файле его нет, а чужой ключ хранилища не годится.
            var profile = userData.LaunchProfile(infoBase) ?? new InfoBaseLaunchProfile();
            userData.SetLaunchProfile(infoBase, profile with
            {
                Parameters = settings.Parameters ?? profile.Parameters,
                UserName = settings.UserName ?? profile.UserName,
                EdtProjectId = settings.EdtProjectId ?? profile.EdtProjectId,
                EdtWorkspace = settings.EdtWorkspace ?? profile.EdtWorkspace,
            });
            if (settings.Tags is { Count: > 0 } tags)
            {
                userData.SetTags(infoBase, [.. userData.TagsOf(infoBase), .. tags]);
            }

            count++;
        }

        foreach (var folder in file.FolderParameters)
        {
            if (replaceFolderParameters || userData.FolderParameters(folder.Folder) is null)
            {
                userData.SetFolderParameters(folder.Folder, folder.Parameters);
            }
        }

        return count;
    }

    /// <summary>Каталог меток — только тех, что есть у сохранённых баз.</summary>
    private static List<ExportedTag>? ExportTags(List<ExportedInfoBase> bases, LauncherUserData? userData)
    {
        var used = bases.SelectMany(b => b.Launcher?.Tags ?? []).ToHashSet(TagNames.Comparer);
        if (userData is null || used.Count == 0)
        {
            return null;
        }

        return
        [
            .. userData.TagCatalog().Where(t => used.Contains(t.Name)).Select(t => new ExportedTag
            {
                Name = t.Name,
                Color = t.Color.ToString().ToLowerInvariant(),
                ShowInRow = t.ShowInRow ? null : false,
            }),
        ];
    }

    /// <summary>Метки из файла попадают в каталог; цвет уже известной метки не меняется.</summary>
    private static void ImportTags(InfoBaseListFile file, LauncherUserData userData)
    {
        foreach (var exported in file.Tags ?? [])
        {
            if (userData.Tag(exported.Name) is not null || TagNames.Normalize(exported.Name) is not { } name)
            {
                continue;
            }

            var color = Enum.TryParse<TagColor>(exported.Color, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : (TagColor?)null;
            var tag = userData.EnsureTag(name, color)!;
            if (exported.ShowInRow is false)
            {
                userData.SetTagAppearance(tag.Name, tag.Color, showInRow: false);
            }
        }
    }

    private static InfoBase? FindAdded(ExportedInfoBase exported, List<InfoBase> addedBases)
    {
        var id = exported.Id?.Trim();
        var connectionKey = "conn:" + ConnectionString.Parse(exported.Connection).ToNormalizedKey();
        return addedBases.Find(b => id is not null && b.Id is { } baseId
            ? string.Equals(id, baseId.Trim(), StringComparison.OrdinalIgnoreCase)
            : b.ConnectionKey == connectionKey && PersonalListEditor.NameComparer.Equals(b.Name, exported.Name.Trim()));
    }

    private static ExportedFolder ExportFolder(V8iSection section)
    {
        var folder = new InfoBaseFolder(section, Personal);
        var other = OtherKeys(section, out var orderInList, out var orderInTree);
        return new ExportedFolder
        {
            Path = folder.FullPath,
            Id = NullIfBlank(section.Get(V8iKeys.Id)),
            OrderInList = orderInList,
            OrderInTree = orderInTree,
            OtherKeys = other,
        };
    }

    private static ExportedInfoBase ExportBase(V8iSection section, LauncherUserData? userData, ExportOptions options)
    {
        var infoBase = new InfoBase(section, Personal);
        var other = OtherKeys(section, out var orderInList, out var orderInTree);
        var wa = section.Get(V8iKeys.WindowsAuthentication)?.Trim();
        bool? windowsAuthentication = wa switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };
        if (windowsAuthentication is null && !string.IsNullOrEmpty(wa))
        {
            other ??= [];
            other[V8iKeys.WindowsAuthentication] = wa;
        }

        return new ExportedInfoBase
        {
            Id = NullIfBlank(section.Get(V8iKeys.Id)),
            Name = section.Name.Trim(),
            Folder = infoBase.FolderPath,
            Connection = section.Get(V8iKeys.Connect)?.Trim() ?? string.Empty,
            OrderInList = orderInList,
            OrderInTree = orderInTree,
            App = NullIfBlank(section.Get(V8iKeys.App)),
            Version = NullIfBlank(section.Get(V8iKeys.Version)),
            Architecture = NullIfBlank(section.Get(V8iKeys.AppArch)),
            WindowsAuthentication = windowsAuthentication,
            DefaultApp = NullIfBlank(section.Get(V8iKeys.DefaultApp)),
            ClientConnectionSpeed = NullIfBlank(section.Get(V8iKeys.ClientConnectionSpeed)),
            AdditionalParameters = NullIfBlank(section.Get(V8iKeys.AdditionalParameters)),
            OtherKeys = other,
            Launcher = userData is null ? null : ExportSettings(infoBase, userData, options),
        };
    }

    private static ExportedLaunchSettings? ExportSettings(InfoBase infoBase, LauncherUserData userData, ExportOptions options)
    {
        var profile = userData.LaunchProfile(infoBase);
        var settings = new ExportedLaunchSettings
        {
            Favorite = options.BaseSettings && userData.IsFavorite(infoBase) ? true : null,
            Parameters = options.LaunchParameters ? NullIfBlank(profile?.Parameters) : null,
            UserName = options.UserNames ? NullIfBlank(profile?.UserName) : null,
            PlatformVersion = options.BaseSettings ? NullIfBlank(userData.PlatformVersionOverride(infoBase)) : null,
            EdtProjectId = options.BaseSettings ? NullIfBlank(profile?.EdtProjectId) : null,
            EdtWorkspace = options.BaseSettings ? NullIfBlank(profile?.EdtWorkspace) : null,
            Tags = options.BaseSettings && userData.TagsOf(infoBase) is { Count: > 0 } tags ? tags : null,
        };
        return settings.IsEmpty ? null : settings;
    }

    /// <summary>Ключи без своих полей в файле; порядок записи разбирается в числа, нечисловой — остаётся ключом.</summary>
    private static Dictionary<string, string>? OtherKeys(V8iSection section, out double? orderInList, out double? orderInTree)
    {
        orderInList = ParseOrder(section.Get(V8iKeys.OrderInList), out var rawList);
        orderInTree = ParseOrder(section.Get(V8iKeys.OrderInTree), out var rawTree);
        var other = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in section.Lines.Where(l => l.IsKeyValue && !MappedKeys.Contains(l.Key!)))
        {
            other.TryAdd(line.Key!, line.Value ?? string.Empty);
        }

        if (rawList is not null)
        {
            other[V8iKeys.OrderInList] = rawList;
        }

        if (rawTree is not null)
        {
            other[V8iKeys.OrderInTree] = rawTree;
        }

        return other.Count == 0 ? null : other;
    }

    private static double? ParseOrder(string? value, out string? raw)
    {
        raw = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            return number;
        }

        raw = value.Trim();
        return null;
    }

    private static string? FormatOrder(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static V8iSection BuildSection(string name, Dictionary<string, string?> values, Dictionary<string, string>? otherKeys)
    {
        var section = new V8iSection(name);
        var other = otherKeys ?? [];
        foreach (var key in KeyOrder)
        {
            // Ключ из «прочих» (например, нечисловой порядок) важнее пустого поля.
            var value = values.GetValueOrDefault(key);
            if (string.IsNullOrWhiteSpace(value) && TryGet(other, key, out var raw))
            {
                value = raw;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                section.Set(key, value.Trim());
            }
        }

        foreach (var (key, value) in other)
        {
            if (!string.IsNullOrWhiteSpace(key) && !KeyOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                section.Set(key.Trim(), value ?? string.Empty);
            }
        }

        return section;
    }

    private static bool TryGet(Dictionary<string, string> values, string key, out string? value)
    {
        foreach (var (k, v) in values)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
