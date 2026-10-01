using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Добавление базы, как в штатном стартере: сначала выбор варианта — добавить существующую, создать из шаблона
/// или создать без конфигурации; для новой базы — параметры создания (в каталоге или на сервере 1С:Предприятия).
/// </summary>
public sealed partial class InfoBaseEditorViewModel
{
    private static readonly DbmsType[] DbmsTypes = [DbmsType.MSSQLServer, DbmsType.PostgreSQL, DbmsType.IBMDB2, DbmsType.OracleDatabase];
    private static readonly SecureConnectionLevel[] SecureLevels = [SecureConnectionLevel.Off, SecureConnectionLevel.ConnectionOnly, SecureConnectionLevel.Always];

    /// <summary>
    /// Создаёт базу (запускает платформу и ждёт её): задаёт главное окно. Возвращает текст ошибки или <c>null</c>,
    /// если база создана. <c>null</c> вместо функции — создавать нельзя, форма сразу для существующей базы.
    /// </summary>
    public Func<InfoBaseCreation, PlatformInstallation, Task<string?>>? Creator
    {
        get;
        init
        {
            field = value;
            ShowModePage = IsNew && value is not null;
        }
    }

    /// <summary>Можно ли создать новую базу (новая запись и задан <see cref="Creator"/>).</summary>
    public bool CanCreate => IsNew && Creator is not null;

    /// <summary>Платформы с 1cv8 для создания базы: первая — по умолчанию (задаёт главное окно).</summary>
    public IReadOnlyList<PlatformInstallation> CreationPlatforms
    {
        get;
        init
        {
            field = value;
            SelectedCreationPlatform = value.Count > 0 ? value[0] : null;
        }
    } = [];

    [ObservableProperty]
    public partial PlatformInstallation? SelectedCreationPlatform { get; set; }

    /// <summary>Найденные шаблоны; «Выбрать файл…» добавляет выбранный файл в начало.</summary>
    public ObservableCollection<ConfigurationTemplate> Templates { get; } = [];

    /// <summary>Начальный список шаблонов (каталоги шаблонов 1С).</summary>
    public IReadOnlyList<ConfigurationTemplate> FoundTemplates
    {
        init
        {
            foreach (var template in value)
            {
                Templates.Add(template);
            }
        }
    }

    public bool HasTemplates => Templates.Count > 0;

    [ObservableProperty]
    public partial ConfigurationTemplate? SelectedTemplate { get; set; }

    partial void OnSelectedTemplateChanged(ConfigurationTemplate? value)
    {
        // Название по умолчанию — последний уровень дерева шаблонов («Бухгалтерия предприятия»).
        if (value is not null && string.IsNullOrWhiteSpace(Name))
        {
            Name = value.Catalog.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        }
    }

    // --- Страница выбора варианта ---

    /// <summary>Показана первая страница — выбор варианта добавления.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm), nameof(Title), nameof(AcceptText))]
    public partial bool ShowModePage { get; private set; }

    public bool ShowForm => !ShowModePage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsExistingMode), nameof(IsTemplateMode), nameof(IsEmptyMode), nameof(IsCreateMode), nameof(Title), nameof(AcceptText),
        nameof(ShowExistingFields), nameof(ShowCreateServerFields), nameof(ShowExistingServerFields))]
    public partial InfoBaseAddMode AddMode { get; set; }

    // Положения переключателя на первой странице: установка в true выбирает вариант.
    public bool IsExistingMode
    {
        get => AddMode == InfoBaseAddMode.Existing;
        set => SelectMode(InfoBaseAddMode.Existing, value);
    }

    public bool IsTemplateMode
    {
        get => AddMode == InfoBaseAddMode.FromTemplate;
        set => SelectMode(InfoBaseAddMode.FromTemplate, value);
    }

    public bool IsEmptyMode
    {
        get => AddMode == InfoBaseAddMode.Empty;
        set => SelectMode(InfoBaseAddMode.Empty, value);
    }

    /// <summary>Создаётся новая база (шаблон или пустая), а не добавляется существующая.</summary>
    public bool IsCreateMode => CanCreate && AddMode != InfoBaseAddMode.Existing;

    /// <summary>Поля, которые нужны только существующей базе (веб-сервер и т. п.).</summary>
    public bool ShowExistingFields => !IsCreateMode;

    public bool ShowExistingServerFields => IsServer && !IsCreateMode;

    public bool ShowCreateServerFields => IsServer && IsCreateMode;

    /// <summary>Подпись главной кнопки: «Далее», «Создать» или «Сохранить».</summary>
    public string AcceptText => ShowModePage ? "Далее >" : IsCreateMode ? "Создать" : "Сохранить";

    /// <summary>Кнопка «Назад» — на форме, если до неё была страница выбора варианта.</summary>
    public bool CanGoBack => CanCreate && !ShowModePage;

    private void SelectMode(InfoBaseAddMode mode, bool selected)
    {
        if (selected)
        {
            AddMode = mode;
        }
    }

    /// <summary>С первой страницы — к форме.</summary>
    [RelayCommand]
    private void Next()
    {
        ShowModePage = false;
        OnPropertyChanged(nameof(CanGoBack));
        if (IsCreateMode)
        {
            // Создать можно только в каталоге или на сервере 1С:Предприятия.
            if (KindIndex > 1)
            {
                KindIndex = 0;
            }

            if (string.IsNullOrWhiteSpace(FilePath))
            {
                FilePath = SuggestFilePath();
            }
        }
    }

    /// <summary>С формы — обратно к выбору варианта.</summary>
    [RelayCommand]
    private void Back()
    {
        Errors = string.Empty;
        ShowModePage = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    // --- Параметры создания ---

    public IReadOnlyList<string> CreationKindNames { get; } = ["На данном компьютере или в локальной сети", "На сервере 1С:Предприятия"];

    public IReadOnlyList<string> SecureConnectionNames { get; } = ["Выключено", "Только соединение", "Постоянно"];

    public IReadOnlyList<string> DbmsNames { get; } = ["MS SQL Server", "PostgreSQL", "IBM DB2", "Oracle Database"];

    public IReadOnlyList<string> DateOffsetNames { get; } = ["0", "2000"];

    public IReadOnlyList<InfoBaseLocale> Locales { get; } = InfoBaseCreation.Locales;

    [ObservableProperty]
    public partial int SecureConnectionIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDateOffsetEnabled))]
    public partial int DbmsIndex { get; set; }

    /// <summary>Смещение дат — только для MS SQL Server.</summary>
    public bool IsDateOffsetEnabled => DbmsIndex == 0;

    [ObservableProperty]
    public partial string DatabaseServer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabaseName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabaseUser { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabasePassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DateOffsetIndex { get; set; }

    [ObservableProperty]
    public partial bool CreateDatabase { get; set; } = true;

    [ObservableProperty]
    public partial bool BlockScheduledJobs { get; set; }

    [ObservableProperty]
    public partial bool DisableLocalSpeechToText { get; set; }

    [ObservableProperty]
    public partial InfoBaseLocale? Locale { get; set; } = InfoBaseCreation.Locales[0];

    /// <summary>Идёт создание базы: форма недоступна.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    partial void OnKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowCreateServerFields));
        OnPropertyChanged(nameof(ShowExistingServerFields));
    }

    partial void OnDatabaseNameChanged(string oldValue, string newValue)
    {
        // Имя базы данных по умолчанию — как имя базы в кластере: пока его не меняли, оно следует за именем базы данных.
        if (IsCreateMode && (string.IsNullOrWhiteSpace(InfobaseName) || InfobaseName == oldValue))
        {
            InfobaseName = newValue;
        }
    }

    /// <summary>Параметры создания из полей формы.</summary>
    public InfoBaseCreation BuildCreation() => new()
    {
        Kind = KindIndex == 1 ? ConnectionKind.Server : ConnectionKind.File,
        FilePath = FilePath.Trim(),
        Server = Server.Trim(),
        InfobaseName = InfobaseName.Trim(),
        SecureConnection = SecureLevels[Math.Clamp(SecureConnectionIndex, 0, SecureLevels.Length - 1)],
        Dbms = DbmsTypes[Math.Clamp(DbmsIndex, 0, DbmsTypes.Length - 1)],
        DatabaseServer = DatabaseServer.Trim(),
        DatabaseName = DatabaseName.Trim(),
        DatabaseUser = DatabaseUser.Trim(),
        DatabasePassword = DatabasePassword,
        DateOffset = DateOffsetIndex == 1 ? 2000 : 0,
        CreateDatabase = CreateDatabase,
        BlockScheduledJobs = BlockScheduledJobs,
        DisableLocalSpeechToText = DisableLocalSpeechToText,
        Locale = Locale?.Code ?? InfoBaseCreation.DefaultLocale,
        TemplatePath = AddMode == InfoBaseAddMode.FromTemplate ? SelectedTemplate?.Path ?? string.Empty : null,
    };

    /// <summary>
    /// Проверяет форму и создаёт базу. При успехе заполняет <see cref="Result"/> (запись для списка баз);
    /// при ошибке — <see cref="Errors"/>, в том числе ответ платформы.
    /// </summary>
    public async Task<bool> TryCreateAsync()
    {
        if (!IsCreateMode || Creator is null || IsBusy)
        {
            return false;
        }

        if (!TryAccept())
        {
            return false;
        }

        var draft = Result!;
        var creation = BuildCreation();
        var errors = creation.Validate().ToList();
        if (SelectedCreationPlatform is null)
        {
            errors.Add("Не найдена платформа 1С с конфигуратором (1cv8) — создать базу нечем.");
        }

        if (errors.Count > 0)
        {
            Errors = string.Join(Environment.NewLine, errors);
            Result = null;
            return false;
        }

        IsBusy = true;
        Errors = string.Empty;
        try
        {
            if (await Creator(creation, SelectedCreationPlatform!) is { } failure)
            {
                Errors = failure;
                Result = null;
                return false;
            }
        }
        finally
        {
            IsBusy = false;
        }

        Result = draft;
        return true;
    }

    [RelayCommand]
    private async Task BrowseTemplateAsync()
    {
        if (await _files.OpenFileAsync("Шаблон информационной базы", "Конфигурация или выгрузка 1С", ["*.cf", "*.dt"]) is { } path)
        {
            var template = new ConfigurationTemplate(path, System.IO.Path.GetFileName(path), string.Empty);
            Templates.Insert(0, template);
            OnPropertyChanged(nameof(HasTemplates));
            SelectedTemplate = template;
        }
    }

    /// <summary>Каталог новой файловой базы по умолчанию — как у штатного стартера: «Документы\InfoBase», «InfoBase1»…</summary>
    private static string SuggestFilePath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            return string.Empty;
        }

        for (var i = 0; i < 1000; i++)
        {
            var path = System.IO.Path.Combine(documents, i == 0 ? "InfoBase" : $"InfoBase{i}");
            if (!Directory.Exists(path))
            {
                return path;
            }
        }

        return System.IO.Path.Combine(documents, "InfoBase");
    }
}
