using System.Text.Json.Serialization;

namespace YetAnotherOneCLauncher.Core.Exchange;

/// <summary>
/// Файл JSON со списком баз: личный список (базы и группы) и, по выбору, настройки лаунчера для них.
/// Пароли в файл не попадают никогда.
/// </summary>
public sealed record InfoBaseListFile
{
    /// <summary>Признак файла лаунчера в поле <see cref="Format"/>.</summary>
    public const string FormatName = "YetAnotherOneCLauncher.InfoBaseList";

    /// <summary>Версия формата, которую пишет и понимает эта версия лаунчера.</summary>
    public const int CurrentFormatVersion = 1;

    public string Format { get; init; } = FormatName;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Версия лаунчера, сохранившего файл.</summary>
    public string? AppVersion { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }

    public List<ExportedFolder> Folders { get; init; } = [];

    /// <summary>Параметры запуска групп (действуют на вложенные базы).</summary>
    public List<ExportedFolderParameters> FolderParameters { get; init; } = [];

    public List<ExportedInfoBase> InfoBases { get; init; } = [];
}

/// <summary>Группа (папка) личного списка.</summary>
public sealed record ExportedFolder
{
    /// <summary>Полный путь группы: "/Разработка/Тестовые".</summary>
    public string Path { get; init; } = string.Empty;

    public string? Id { get; init; }

    public double? OrderInList { get; init; }

    public double? OrderInTree { get; init; }

    /// <summary>Прочие ключи записи .v8i — переносятся как есть.</summary>
    public Dictionary<string, string>? OtherKeys { get; init; }
}

public sealed record ExportedFolderParameters
{
    public string Folder { get; init; } = string.Empty;

    public string Parameters { get; init; } = string.Empty;
}

/// <summary>База личного списка: ключи записи .v8i и настройки лаунчера для неё.</summary>
public sealed record ExportedInfoBase
{
    public string? Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Группа: "/" — корень списка.</summary>
    public string Folder { get; init; } = "/";

    /// <summary>Строка подключения в том виде, как в .v8i.</summary>
    public string Connection { get; init; } = string.Empty;

    public double? OrderInList { get; init; }

    public double? OrderInTree { get; init; }

    public string? App { get; init; }

    public string? Version { get; init; }

    public string? Architecture { get; init; }

    public bool? WindowsAuthentication { get; init; }

    public string? DefaultApp { get; init; }

    public string? ClientConnectionSpeed { get; init; }

    public string? AdditionalParameters { get; init; }

    /// <summary>Прочие ключи записи .v8i — переносятся как есть.</summary>
    public Dictionary<string, string>? OtherKeys { get; init; }

    /// <summary>Настройки лаунчера для базы; <c>null</c> — нет или не сохранялись.</summary>
    public ExportedLaunchSettings? Launcher { get; init; }
}

/// <summary>Настройки лаунчера для базы. Пароля здесь нет.</summary>
public sealed record ExportedLaunchSettings
{
    public bool? Favorite { get; init; }

    /// <summary>Параметры запуска базы.</summary>
    public string? Parameters { get; init; }

    public string? UserName { get; init; }

    /// <summary>Версия платформы, выбранная вместо указанной в списке.</summary>
    public string? PlatformVersion { get; init; }

    public string? EdtProjectId { get; init; }

    public string? EdtWorkspace { get; init; }

    [JsonIgnore]
    public bool IsEmpty =>
        Favorite is not true && Parameters is null && UserName is null && PlatformVersion is null
        && EdtProjectId is null && EdtWorkspace is null;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(InfoBaseListFile))]
internal sealed partial class InfoBaseListJsonContext : JsonSerializerContext;
