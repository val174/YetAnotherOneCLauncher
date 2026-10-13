using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using YetAnotherOneCLauncher.Core.IO;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Exchange;

/// <summary>Перед чем сделана копия списка.</summary>
public enum ListBackupReason
{
    /// <summary>Перед загрузкой из файла (JSON или .v8i).</summary>
    Import,

    /// <summary>Перед восстановлением другой копии.</summary>
    Restore,
}

/// <summary>Настройки лаунчера для баз на момент копии (пароли — только ключи хранилища, как в настройках).</summary>
public sealed record ListBackupSettings
{
    public List<InfoBaseRef> Favorites { get; init; } = [];

    public List<PlatformVersionOverride> PlatformOverrides { get; init; } = [];

    public List<InfoBaseLaunchProfile> InfoBaseProfiles { get; init; } = [];

    public List<FolderLaunchProfile> FolderProfiles { get; init; } = [];

    public static ListBackupSettings From(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ListBackupSettings
        {
            Favorites = [.. settings.Favorites],
            PlatformOverrides = [.. settings.PlatformOverrides],
            InfoBaseProfiles = [.. settings.InfoBaseProfiles],
            FolderProfiles = [.. settings.FolderProfiles],
        };
    }

    /// <summary>Вернуть настройки баз к состоянию копии.</summary>
    public void ApplyTo(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Favorites = [.. Favorites];
        settings.PlatformOverrides = [.. PlatformOverrides];
        settings.InfoBaseProfiles = [.. InfoBaseProfiles];
        settings.FolderProfiles = [.. FolderProfiles];
    }
}

/// <summary>Описание копии: файл <c>ibases-…json</c> рядом с копией списка <c>ibases-…v8i</c>.</summary>
public sealed record ListBackupInfo
{
    public DateTimeOffset CreatedAt { get; init; }

    public ListBackupReason Reason { get; init; }

    /// <summary>Имя файла, из которого шла загрузка.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Режим загрузки: «добавить» или «заменить».</summary>
    public ImportMode? Mode { get; init; }

    /// <summary>Баз и групп в копии.</summary>
    public int EntryCount { get; init; }

    public ListBackupSettings Settings { get; init; } = new();
}

/// <summary>Копия личного списка в каталоге копий.</summary>
/// <param name="ListPath">Файл копии списка (.v8i).</param>
/// <param name="Info">Описание.</param>
public sealed record ListBackup(string ListPath, ListBackupInfo Info);

/// <summary>
/// Копии личного списка перед загрузкой из файлов и перед восстановлением: каталог <c>list-backups</c> в данных лаунчера,
/// хранятся последние <see cref="MaxBackups"/>.
/// </summary>
public sealed class ListBackupStore
{
    public const int MaxBackups = 10;

    private const string Prefix = "ibases-";
    private const string TimeFormat = "yyyy-MM-dd_HH-mm-ss";

    private static readonly ListBackupJsonContext Json = new(new JsonSerializerOptions(ListBackupJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private readonly TimeProvider _time;

    public ListBackupStore(string directory, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        _time = time ?? TimeProvider.System;
    }

    public string Directory { get; }

    /// <summary>Сохраняет копию текущего списка; лишние старые копии удаляются.</summary>
    /// <param name="listContent">Содержимое ibases.v8i; <c>null</c> — файла нет (копия — пустой список).</param>
    public async Task<ListBackup> CreateAsync(
        byte[]? listContent,
        ListBackupReason reason,
        LauncherSettings settings,
        string? sourceFile = null,
        ImportMode? mode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        System.IO.Directory.CreateDirectory(Directory);
        var now = _time.GetLocalNow();
        var content = listContent ?? [];
        var info = new ListBackupInfo
        {
            CreatedAt = now,
            Reason = reason,
            SourceFile = sourceFile,
            Mode = mode,
            EntryCount = content.Length == 0
                ? 0
                : V8iDocument.Parse(content).Sections.Count(s => !string.IsNullOrWhiteSpace(s.Name)),
            Settings = ListBackupSettings.From(settings),
        };

        // Одно имя на секунду; если копий за секунду несколько — с номером.
        var stem = Prefix + now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var name = stem;
        for (var i = 2; File.Exists(Path.Combine(Directory, name + ".v8i")); i++)
        {
            name = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        var listPath = Path.Combine(Directory, name + ".v8i");
        await AtomicFileWriter.WriteAllBytesAsync(listPath, content, backupPath: null, cancellationToken).ConfigureAwait(false);
        await AtomicFileWriter.WriteAllBytesAsync(
            Path.ChangeExtension(listPath, ".json"),
            JsonSerializer.SerializeToUtf8Bytes(info, Json.ListBackupInfo),
            backupPath: null,
            cancellationToken).ConfigureAwait(false);
        Prune();
        return new ListBackup(listPath, info);
    }

    /// <summary>Копии, начиная с последней. Копии без описания или с повреждённым описанием пропускаются.</summary>
    public IReadOnlyList<ListBackup> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var result = new List<ListBackup>();
        foreach (var listPath in System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.v8i"))
        {
            var infoPath = Path.ChangeExtension(listPath, ".json");
            try
            {
                if (File.Exists(infoPath)
                    && JsonSerializer.Deserialize(File.ReadAllBytes(infoPath), Json.ListBackupInfo) is { } info)
                {
                    result.Add(new ListBackup(listPath, info with { Settings = info.Settings ?? new ListBackupSettings() }));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Чужой или повреждённый файл в каталоге копий — не показываем.
            }
        }

        return [.. result.OrderByDescending(b => b.Info.CreatedAt).ThenByDescending(b => b.ListPath, StringComparer.Ordinal)];
    }

    public static Task<byte[]> ReadListAsync(ListBackup backup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return File.ReadAllBytesAsync(backup.ListPath, cancellationToken);
    }

    private void Prune()
    {
        foreach (var old in List().Skip(MaxBackups))
        {
            try
            {
                File.Delete(old.ListPath);
                File.Delete(Path.ChangeExtension(old.ListPath, ".json"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Не удалось — попробуем в следующий раз.
            }
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ListBackupInfo))]
internal sealed partial class ListBackupJsonContext : JsonSerializerContext;
