using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>
/// Ссылка на базу из настроек лаунчера. Хранит и <c>ID</c>, и строку подключения:
/// если <c>ID</c> у базы пропал (файл правили вручную), запись находится по подключению.
/// </summary>
public sealed record InfoBaseRef
{
    /// <summary>GUID из ключа <c>ID</c>, если был.</summary>
    public string? Id { get; init; }

    /// <summary><see cref="InfoBase.ConnectionKey"/>.</summary>
    public string ConnectionKey { get; init; } = string.Empty;

    /// <summary>Имя на момент сохранения — для показа, если базы уже нет в списках.</summary>
    public string Name { get; init; } = string.Empty;

    public static InfoBaseRef From(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        return new InfoBaseRef { Id = infoBase.Id?.Trim(), ConnectionKey = infoBase.ConnectionKey, Name = infoBase.Name };
    }

    /// <summary>Если <c>ID</c> есть у обеих — сравнивается он, иначе строка подключения.</summary>
    public bool Matches(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        return Id is not null && infoBase.Id is { } id
            ? string.Equals(Id, id.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals(ConnectionKey, infoBase.ConnectionKey, StringComparison.Ordinal);
    }
}

/// <summary>Запись истории запусков.</summary>
public sealed record LaunchHistoryEntry
{
    public InfoBaseRef InfoBase { get; init; } = new();

    public LaunchMode Mode { get; init; }

    public DateTimeOffset LaunchedAt { get; init; }
}

/// <summary>Версия платформы, выбранная пользователем для базы вместо указанной в списке.</summary>
public sealed record PlatformVersionOverride
{
    public InfoBaseRef InfoBase { get; init; } = new();

    /// <summary>Маска или полная версия, например "8.3.27" или "8.3.27.2130".</summary>
    public string Version { get; init; } = string.Empty;
}

/// <summary>Параметры запуска и пользователь базы — хранятся в лаунчере, список баз не меняется.</summary>
public sealed record InfoBaseLaunchProfile
{
    public InfoBaseRef InfoBase { get; init; } = new();

    /// <summary>Параметры командной строки: дописываются после параметров папок.</summary>
    public string? Parameters { get; init; }

    /// <summary>Пользователь 1С (<c>/N</c>).</summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Ключ пароля в хранилище ОС (Credential Manager, libsecret); <c>null</c> — пароль не сохранён.
    /// Сам пароль в настройках не хранится.
    /// </summary>
    public string? PasswordKey { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Parameters) && string.IsNullOrWhiteSpace(UserName) && PasswordKey is null;
}

/// <summary>Параметры запуска папки: действуют на все базы в ней и во вложенных папках.</summary>
public sealed record FolderLaunchProfile
{
    /// <summary>Путь папки: "/Бухгалтерия/Архив".</summary>
    public string FolderPath { get; init; } = string.Empty;

    public string? Parameters { get; init; }
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Что делать с окном лаунчера после запуска базы.</summary>
public enum AfterLaunchAction
{
    Nothing,
    Minimize,
    Close,
}

public enum CatalogViewMode
{
    Tree,
    List,
}

/// <summary>Положение и размер окна.</summary>
public sealed record WindowPlacement
{
    public int X { get; init; }

    public int Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool IsMaximized { get; init; }
}

public sealed class UiSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public CatalogViewMode ViewMode { get; set; } = CatalogViewMode.Tree;

    public AfterLaunchAction AfterLaunch { get; set; } = AfterLaunchAction.Nothing;

    public WindowPlacement? Window { get; set; }

    /// <summary>Свёрнутые папки дерева (по полному пути). Новые папки раскрыты.</summary>
    public List<string> CollapsedFolders { get; set; } = [];

    public bool ShowDetails { get; set; } = true;
}

public sealed class LaunchSettings
{
    public PlatformArchitecture PreferredArchitecture { get; set; } = PlatformArchitecture.X64;

    public bool UseThickClientForFileBasesByDefault { get; set; }

    public LaunchOptions ToLaunchOptions() => new()
    {
        PreferredArchitecture = PreferredArchitecture,
        UseThickClientForFileBasesByDefault = UseThickClientForFileBasesByDefault,
    };
}

/// <summary>Очистка кэша баз.</summary>
public sealed class CacheSettings
{
    /// <summary>Удалять насовсем, а не в корзину.</summary>
    public bool DeletePermanently { get; set; }

    /// <summary>Удалять и каталог Roaming — локальные настройки пользователя для базы (только Windows).</summary>
    public bool IncludeRoaming { get; set; }
}

/// <summary>Все настройки лаунчера: хранятся в JSON в каталоге приложения.</summary>
public sealed class LauncherSettings
{
    public const int CurrentFormatVersion = 1;

    /// <summary>Версия формата файла — для будущих миграций.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public List<InfoBaseRef> Favorites { get; set; } = [];

    /// <summary>История, новые записи — в конце.</summary>
    public List<LaunchHistoryEntry> History { get; set; } = [];

    public List<PlatformVersionOverride> PlatformOverrides { get; set; } = [];

    public List<InfoBaseLaunchProfile> InfoBaseProfiles { get; set; } = [];

    public List<FolderLaunchProfile> FolderProfiles { get; set; } = [];

    /// <summary>Свои шаблоны параметров — в дополнение к встроенным.</summary>
    public List<ParameterTemplate> ParameterTemplates { get; set; } = [];

    public UiSettings Ui { get; set; } = new();

    public LaunchSettings Launch { get; set; } = new();

    public CacheSettings Cache { get; set; } = new();
}
