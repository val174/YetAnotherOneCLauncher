using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Форма добавления или изменения базы.</summary>
public sealed partial class InfoBaseEditorViewModel : ObservableObject
{
    private static readonly ConnectionKind[] Kinds = [ConnectionKind.File, ConnectionKind.Server, ConnectionKind.Web];
    private static readonly ClientApp[] Apps = [ClientApp.Auto, ClientApp.ThinClient, ClientApp.ThickClient, ClientApp.WebClient];

    private readonly IFileDialogService _files;
    private readonly InfoBaseDraft _original;

    public InfoBaseEditorViewModel(InfoBaseDraft draft, IEnumerable<string> folders, bool isNew, IFileDialogService files)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _original = draft;
        _files = files;
        IsNew = isNew;
        Folders = folders.Prepend(FolderPaths.Root).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();

        Name = draft.Name;
        KindIndex = Math.Max(0, Array.IndexOf(Kinds, draft.Kind));
        FilePath = draft.FilePath;
        Server = draft.Server;
        InfobaseName = draft.InfobaseName;
        WebUrl = draft.WebUrl;
        Folder = draft.FolderPath;
        AppIndex = Math.Max(0, Array.IndexOf(Apps, draft.App));
        Version = draft.Version ?? string.Empty;
        ArchitectureIndex = Math.Max(0, Array.IndexOf(Architectures, draft.Architecture));
        // Нет ключа WA — 1С по умолчанию пробует аутентификацию Windows; показываем так же.
        WindowsAuthentication = draft.WindowsAuthentication ?? true;
        AdditionalParameters = draft.AdditionalParameters ?? string.Empty;
    }

    public bool IsNew { get; }

    public string Title => ShowModePage
        ? "Добавление информационной базы"
        : ShowTemplatePage
            ? "Выбор шаблона информационной базы"
        : IsCreateMode
            ? AddMode == InfoBaseAddMode.FromTemplate ? "Создание информационной базы из шаблона" : "Создание информационной базы без конфигурации"
            : IsNew ? "Новая информационная база" : "Изменение информационной базы";

    public IReadOnlyList<string> KindNames { get; } = ["На этом компьютере или в локальной сети", "На сервере 1С:Предприятия", "На веб-сервере"];

    public IReadOnlyList<string> AppNames { get; } = ["Выбирать автоматически", "Тонкий клиент", "Толстый клиент", "Веб-клиент"];

    /// <summary>Разрядность клиента — как в свойствах базы штатного стартера (ключ AppArch).</summary>
    public IReadOnlyList<string> ArchitectureNames { get; } =
        ["Как в настройках лаунчера", "32 бита (x86)", "64 бита (x86-64)", "Предпочтительно 32 бита", "Предпочтительно 64 бита"];

    private static readonly AppArchitecture[] Architectures =
        [AppArchitecture.Auto, AppArchitecture.X86, AppArchitecture.X64, AppArchitecture.PreferX86, AppArchitecture.PreferX64];

    public IReadOnlyList<string> Folders { get; }

    /// <summary>
    /// Варианты для поля «Версия платформы»: ветки («8.3») и найденные версии, новые — первыми.
    /// Задаёт главное окно; вписать версию вручную можно всегда.
    /// </summary>
    public IReadOnlyList<string> PlatformVersions { get; init; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFile), nameof(IsServer), nameof(IsWeb))]
    public partial int KindIndex { get; set; }

    public bool IsFile => KindIndex == 0;

    public bool IsServer => KindIndex == 1;

    public bool IsWeb => KindIndex == 2;

    [ObservableProperty]
    public partial string FilePath { get; set; }

    [ObservableProperty]
    public partial string Server { get; set; }

    [ObservableProperty]
    public partial string InfobaseName { get; set; }

    [ObservableProperty]
    public partial string WebUrl { get; set; }

    /// <summary>Группа (папка списка), например «/Рабочие/Отчёты»; «/» — без группы, в корне списка.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderText), nameof(FolderPathTip), nameof(HasGroup))]
    [NotifyCanExecuteChangedFor(nameof(ClearGroupCommand))]
    public partial string Folder { get; set; }

    /// <summary>Группа для показа в форме, как в панели свойств: только своя («Отчёты») или «Не выбрана».</summary>
    public string FolderText =>
        FolderPaths.Split(Folder) is { Length: > 0 } segments ? segments[^1] : GroupPickerViewModel.NoGroupText;

    /// <summary>Полный путь вложенной группы — подсказка к полю («Рабочие / Отчёты»); иначе <c>null</c>.</summary>
    public string? FolderPathTip => FolderPaths.Split(Folder) is { Length: > 1 } segments ? string.Join(" / ", segments) : null;

    public bool HasGroup => FolderPaths.Normalize(Folder) != FolderPaths.Root;

    /// <summary>Очистить поле «Группа»: база — в корне списка.</summary>
    [RelayCommand(CanExecute = nameof(HasGroup))]
    private void ClearGroup() => Folder = FolderPaths.Root;

    /// <summary>
    /// Открывает окно выбора группы; <c>true</c> — группа выбрана. Задаёт главное окно;
    /// <c>null</c> — кнопка выбора недоступна.
    /// </summary>
    public Func<GroupPickerViewModel, Task<bool>>? GroupChooser { get; init; }

    /// <summary>Запрос имени новой группы в окне выбора (получает путь родительской); <c>null</c> — создавать нельзя.</summary>
    public Func<string, Task<string?>>? GroupNamePrompt { get; init; }

    public bool CanChooseGroup => GroupChooser is not null;

    [RelayCommand(CanExecute = nameof(CanChooseGroup))]
    private async Task ChooseGroupAsync()
    {
        var picker = new GroupPickerViewModel(Folders, Folder, GroupNamePrompt);
        if (await GroupChooser!(picker))
        {
            Folder = picker.SelectedPath;
        }
    }

    [ObservableProperty]
    public partial int AppIndex { get; set; }

    [ObservableProperty]
    public partial string Version { get; set; }

    [ObservableProperty]
    public partial int ArchitectureIndex { get; set; }

    [ObservableProperty]
    public partial bool WindowsAuthentication { get; set; }

    [ObservableProperty]
    public partial string AdditionalParameters { get; set; }

    /// <summary>Ошибки проверки — показываются в форме.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string Errors { get; private set; } = string.Empty;

    public bool HasErrors => Errors.Length > 0;

    /// <summary>Данные формы; заполняется в <see cref="TryAccept"/>.</summary>
    public InfoBaseDraft? Result { get; private set; }

    /// <summary>Проверяет данные; при успехе заполняет <see cref="Result"/>.</summary>
    public bool TryAccept()
    {
        var draft = _original with
        {
            Name = Name.Trim(),
            Kind = Kinds[Math.Clamp(KindIndex, 0, Kinds.Length - 1)],
            FilePath = FilePath.Trim(),
            Server = Server.Trim(),
            InfobaseName = InfobaseName.Trim(),
            WebUrl = WebUrl.Trim(),
            FolderPath = FolderPaths.Normalize(Folder),
            App = Apps[Math.Clamp(AppIndex, 0, Apps.Length - 1)],
            Version = string.IsNullOrWhiteSpace(Version) ? null : Version.Trim(),
            Architecture = Architectures[Math.Clamp(ArchitectureIndex, 0, Architectures.Length - 1)],
            // Ключа не было и флажок не меняли — ключ не добавляем, чтобы не изменить поведение запуска.
            WindowsAuthentication = _original.WindowsAuthentication is null && WindowsAuthentication ? null : WindowsAuthentication,
            AdditionalParameters = string.IsNullOrWhiteSpace(AdditionalParameters) ? null : AdditionalParameters.Trim(),
        };

        var errors = draft.Validate();
        Errors = string.Join(Environment.NewLine, errors);
        Result = errors.Count == 0 ? draft : null;
        return Result is not null;
    }

    /// <summary>
    /// Открывает окно «Параметры запуска» (то же, что в главном окне): задаёт главное окно.
    /// <c>null</c> — кнопка «…» у поля параметров недоступна.
    /// </summary>
    public Func<InfoBaseEditorViewModel, Task>? LaunchParametersEditor { get; init; }

    public bool CanEditLaunchParameters => LaunchParametersEditor is not null;

    /// <summary>
    /// Что пользователь подтвердил в окне параметров: пользователь и пароль применяются только после
    /// сохранения формы. <c>null</c> — окно не открывали.
    /// </summary>
    public LaunchParametersViewModel? LaunchSettings { get; set; }

    [RelayCommand(CanExecute = nameof(CanEditLaunchParameters))]
    private Task EditLaunchParametersAsync() => LaunchParametersEditor?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await _files.PickFolderAsync("Каталог информационной базы") is { } path)
        {
            FilePath = path;
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
            }
        }
    }
}
