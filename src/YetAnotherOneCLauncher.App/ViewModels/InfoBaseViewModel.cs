using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public string ClientText => InfoBase.App switch
    {
        ClientApp.ThinClient => "Тонкий клиент",
        ClientApp.ThickClient => "Толстый клиент",
        ClientApp.WebClient => "Веб-клиент",
        _ => "Выбирать автоматически",
    };

    public string ListVersionText => InfoBase.Version ?? "не указана";

    public string FolderText => InfoBase.FolderPath;

    public string SourceText => InfoBase.IsReadOnly ? "Общий список (только чтение)" : "Личный список";

    /// <summary>Файл или адрес списка — для подсказки.</summary>
    public string SourceLocation => InfoBase.Source.Location;

    public string? Id => InfoBase.Id;

    public bool IsFileBase => InfoBase.ConnectionKind == ConnectionKind.File;

    /// <summary>Строка под именем в списке: папка и подключение.</summary>
    public string Subtitle => InfoBase.FolderPath == FolderPaths.Root
        ? ConnectionText
        : $"{InfoBase.FolderPath}  ·  {ConnectionText}";

    [ObservableProperty]
    public partial bool IsFavorite { get; private set; }

    [ObservableProperty]
    public partial string LastLaunchText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int LaunchCount { get; private set; }

    /// <summary>Версия платформы, выбранная в лаунчере; <c>null</c> — как в списке.</summary>
    [ObservableProperty]
    public partial string? PlatformVersionOverride { get; private set; }

    /// <summary>Перечитать избранное, историю и выбранную версию из настроек.</summary>
    public void Refresh()
    {
        IsFavorite = _userData.IsFavorite(InfoBase);
        LaunchCount = _userData.LaunchCount(InfoBase);
        PlatformVersionOverride = _userData.PlatformVersionOverride(InfoBase);

        var last = _userData.LastLaunch(InfoBase);
        LastLaunchText = last is null
            ? "не запускалась из лаунчера"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{last.LaunchedAt.ToLocalTime():dd.MM.yyyy HH:mm} ({(last.Mode == LaunchMode.Designer ? "Конфигуратор" : "Предприятие")})");
    }

    public override string ToString() => Name;
}
