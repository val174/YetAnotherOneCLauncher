using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Availability;
using YetAnotherOneCLauncher.Core.Cache;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>База для показа: данные из списка плюс избранное и история из настроек лаунчера.</summary>
public sealed partial class InfoBaseViewModel : ObservableObject
{
    private readonly LauncherUserData _userData;

    public InfoBaseViewModel(InfoBase infoBase, LauncherUserData userData)
    {
        InfoBase = infoBase;
        _userData = userData;
        Refresh();
    }

    public InfoBase InfoBase { get; }

    public string Name => InfoBase.Name;

    public string ConnectionText => InfoBase.Connection.ToDisplayString();

    public string ConnectionString => InfoBase.Connection.ToString();

    public string ConnectionKindText => InfoBase.ConnectionKind switch
    {
        ConnectionKind.File => "Файловая",
        ConnectionKind.Server => "Клиент-серверная",
        ConnectionKind.Web => "Через веб-сервер",
        _ => "Не распознан",
    };

    /// <summary>Колонка «Режим запуска»: клиент из ключа <c>App</c>, коротко.</summary>
    public string ClientShortText => InfoBase.App switch
    {
        ClientApp.ThinClient => "Тонкий",
        ClientApp.ThickClient => "Толстый",
        ClientApp.WebClient => "Веб-клиент",
        _ => "Авто",
    };

    /// <summary>Колонка «Платформа»: версия, на которой база запустится в режиме 1С: Предприятие.</summary>
    [ObservableProperty]
    public partial string PlatformText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PlatformToolTip { get; private set; } = string.Empty;

    /// <summary>Нужной версии нет среди установленных.</summary>
    [ObservableProperty]
    public partial bool IsPlatformMissing { get; private set; }

    /// <summary>Колонка «Дата последнего запуска».</summary>
    [ObservableProperty]
    public partial string LastLaunchShortText { get; private set; } = string.Empty;

    public void SetPlatform(string text, string toolTip, bool missing)
    {
        PlatformText = text;
        PlatformToolTip = toolTip;
        IsPlatformMissing = missing;
    }

    public string ClientText => InfoBase.App switch
    {
        ClientApp.ThinClient => "Тонкий клиент",
        ClientApp.ThickClient => "Толстый клиент",
        ClientApp.WebClient => "Веб-клиент",
        _ => "Выбирать автоматически",
    };

    /// <summary>Группа для панели свойств: только та, в которой лежит база («Отчёты», а не «/Рабочие/Отчёты»); в корне — «Не выбрана».</summary>
    public string FolderText => FolderPaths.Split(InfoBase.FolderPath) is { Length: > 0 } segments ? segments[^1] : GroupPickerViewModel.NoGroupText;

    /// <summary>Полный путь вложенной группы — подсказка к ней в панели свойств («Рабочие / Отчёты»); иначе <c>null</c>.</summary>
    public string? FolderPathTip => FolderPaths.Split(InfoBase.FolderPath) is { Length: > 1 } segments ? string.Join(" / ", segments) : null;

    public string SourceText => InfoBase.Source switch
    {
        { Kind: ListSourceKind.Personal } => "Личный список",
        { CachedAt: { } savedAt } => string.Create(
            CultureInfo.CurrentCulture,
            $"{KindName(InfoBase.Source.Kind)} — список недоступен, показана копия на {savedAt.ToLocalTime():dd.MM.yyyy HH:mm}"),
        _ => KindName(InfoBase.Source.Kind) + " (только чтение)",
    };

    public bool IsFromCache => InfoBase.Source.IsFromCache;

    [ObservableProperty]
    public partial string AvailabilityText { get; private set; } = "не проверялась";

    /// <summary>Проверка показала, что база недоступна (каталога нет, сервер не отвечает).</summary>
    [ObservableProperty]
    public partial bool IsUnavailable { get; private set; }

    public void SetAvailability(AvailabilityResult? result)
    {
        IsUnavailable = result?.Status == AvailabilityStatus.Unavailable;
        AvailabilityText = result?.Status switch
        {
            AvailabilityStatus.Available => "доступна (" + result.Message + ")",
            AvailabilityStatus.Unavailable => "недоступна: " + result.Message,
            AvailabilityStatus.Unknown => "проверить нельзя",
            _ => "не проверялась",
        };
    }

    /// <summary>База открыта в 1С текущим пользователем: в командной строке процесса платформы — её адрес или имя.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Подсказка к зелёной точке: в каких клиентах открыта.</summary>
    [ObservableProperty]
    public partial string RunningToolTip { get; private set; } = string.Empty;

    public void SetRunning(IReadOnlyList<string> clients)
    {
        IsRunning = clients.Count > 0;
        RunningToolTip = IsRunning ? "Открыта в 1С: " + string.Join(", ", clients) : string.Empty;
    }

    private static string KindName(ListSourceKind kind) => kind == ListSourceKind.InternetService ? "Веб-сервис списков" : "Общий список";

    /// <summary>Файл или адрес списка — для подсказки.</summary>
    public string SourceLocation => InfoBase.Source.Location;

    public string? Id => InfoBase.Id;

    public bool IsFileBase => InfoBase.ConnectionKind == ConnectionKind.File;

    /// <summary>
    /// Папка базы рядом с именем в плоском списке; у баз в корне — пусто. Путь к базе в строке не показывается:
    /// он в подсказке к имени и в панели подробностей, а поиск по нему работает как прежде.
    /// </summary>
    public string RowFolderText => InfoBase.FolderPath == FolderPaths.Root ? string.Empty : InfoBase.FolderPath;

    public bool HasRowFolder => RowFolderText.Length > 0;

    /// <summary>Связанный проект 1C:EDT (форма базы) — кнопка «1C:EDT» в строке; <c>null</c> — не привязан или проекты выключены.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEdtProject), nameof(EdtButtonToolTip))]
    public partial string? EdtProjectName { get; set; }

    public bool HasEdtProject => EdtProjectName is not null;

    public string EdtButtonToolTip => "1C:EDT — " + EdtProjectName;

    /// <summary>Подсказка к имени в списке: имя и подключение.</summary>
    public string NameToolTip => $"{Name}{Environment.NewLine}{ConnectionText}";

    [ObservableProperty]
    public partial bool IsFavorite { get; private set; }

    [ObservableProperty]
    public partial string LastLaunchText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int LaunchCount { get; private set; }

    /// <summary>Версия платформы, выбранная в лаунчере; <c>null</c> — как в списке.</summary>
    [ObservableProperty]
    public partial string? PlatformVersionOverride { get; private set; }

    /// <summary>Параметры запуска: <c>AdditionalParameters</c> из списка, затем параметры папок и базы из лаунчера.</summary>
    [ObservableProperty]
    public partial string LaunchParametersText { get; private set; } = string.Empty;

    /// <summary>Пользователь 1С из настроек лаунчера и сохранён ли пароль.</summary>
    [ObservableProperty]
    public partial string UserText { get; private set; } = string.Empty;

    /// <summary>Размер кэша базы: локальный и Roaming.</summary>
    [ObservableProperty]
    public partial string CacheText { get; private set; } = "нет";

    [ObservableProperty]
    public partial bool HasCache { get; private set; }

    /// <summary>Каталог программного кэша базы (Local); <c>null</c> — его нет.</summary>
    public string? LocalCachePath { get; private set; }

    /// <summary>Каталог пользовательского кэша — настроек пользователя (Roaming); <c>null</c> — его нет.</summary>
    public string? RoamingCachePath { get; private set; }

    /// <summary>Кэш базы по результатам последнего поиска; <c>null</c> — кэша нет.</summary>
    public void SetCache(CacheOwner? owner)
    {
        HasCache = owner is { Directories.Count: > 0 };
        LocalCachePath = owner?.Directories.FirstOrDefault(d => d.Location == CacheLocation.Local)?.Path;
        RoamingCachePath = owner?.Directories.FirstOrDefault(d => d.Location == CacheLocation.Roaming)?.Path;
        var local = owner?.Directories.Any(d => d.Location == CacheLocation.Local) == true ? ByteSize.Format(owner.LocalBytes) : "нет";
        CacheText = owner?.Directories.Any(d => d.Location == CacheLocation.Roaming) == true
            ? $"{local} (и настройки {ByteSize.Format(owner.RoamingBytes)})"
            : local;
    }

    /// <summary>Перечитать избранное, историю и выбранную версию из настроек.</summary>
    public void Refresh()
    {
        IsFavorite = _userData.IsFavorite(InfoBase);
        LaunchCount = _userData.LaunchCount(InfoBase);
        PlatformVersionOverride = _userData.PlatformVersionOverride(InfoBase);

        var parameters = new[] { InfoBase.AdditionalParameters }.Concat(_userData.ParameterChain(InfoBase))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => OneCCommandLine.MaskPasswords(p!.Trim()));
        LaunchParametersText = string.Join(" ", parameters) is { Length: > 0 } text ? text : "не заданы";

        var profile = _userData.LaunchProfile(InfoBase);
        UserText = profile?.UserName is { } user
            ? profile.PasswordKey is null ? user : user + " (пароль сохранён)"
            : "выбирается при входе";

        var last = _userData.LastLaunch(InfoBase);
        LastLaunchShortText = last is null
            ? string.Empty
            : last.LaunchedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        LastLaunchText = last is null
            ? "не запускалась из лаунчера"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{last.LaunchedAt.ToLocalTime():dd.MM.yyyy HH:mm} ({(last.Mode == LaunchMode.Designer ? "Конфигуратор" : "Предприятие")})");
    }

    public override string ToString() => Name;
}
