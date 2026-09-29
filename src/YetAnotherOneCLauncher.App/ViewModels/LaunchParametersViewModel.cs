using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.App.ViewModels;

public enum LaunchParametersKind
{
    /// <summary>Параметры, пользователь и пароль базы — сохраняются в настройках лаунчера.</summary>
    InfoBase,

    /// <summary>Параметры папки — действуют на вложенные базы.</summary>
    Folder,

    /// <summary>Разовый запуск: ничего не сохраняется.</summary>
    OneOff,

    /// <summary>
    /// Из формы базы: параметры попадают в поле формы и сохраняются в ibases.v8i (действуют и в штатном стартере),
    /// пользователь и пароль — в лаунчере.
    /// </summary>
    ListEntry,
}

/// <summary>Форма параметров запуска: у базы, у папки или разово («Запустить с параметрами…»).</summary>
public sealed partial class LaunchParametersViewModel : ObservableObject
{
    private static readonly ClientApp?[] Clients = [null, ClientApp.ThinClient, ClientApp.ThickClient, ClientApp.WebClient];

    private readonly IFileDialogService _files;

    /// <param name="kind">Где применяются параметры.</param>
    /// <param name="subject">Имя базы или путь папки — для заголовка.</param>
    /// <param name="templates">Шаблоны: свои и встроенные.</param>
    /// <param name="inherited">Что уже применяется до этих параметров (AdditionalParameters, папки) — для справки.</param>
    /// <param name="files">Выбор файла для шаблонов с путём.</param>
    public LaunchParametersViewModel(
        LaunchParametersKind kind,
        string subject,
        IReadOnlyList<ParameterTemplate> templates,
        IReadOnlyList<string> inherited,
        IFileDialogService files)
    {
        Kind = kind;
        _files = files;
        Templates = templates;
        InheritedText = string.Join(" ", inherited.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        Title = kind switch
        {
            LaunchParametersKind.Folder => $"Параметры запуска папки «{subject}»",
            LaunchParametersKind.OneOff => $"Запуск «{subject}» с параметрами",
            _ => $"Параметры запуска «{subject}»",
        };
    }

    public LaunchParametersKind Kind { get; }

    public string Title { get; }

    public bool IsOneOff => Kind == LaunchParametersKind.OneOff;

    public bool IsFolder => Kind == LaunchParametersKind.Folder;

    /// <summary>Пользователь и пароль — у базы и при разовом запуске.</summary>
    public bool ShowCredentials => Kind != LaunchParametersKind.Folder;

    public string ParametersHint => Kind switch
    {
        LaunchParametersKind.Folder => "Действуют на все базы в папке и во вложенных папках.",
        LaunchParametersKind.OneOff => "Только для этого запуска, добавляются после сохранённых.",
        LaunchParametersKind.ListEntry => "Сохраняются в списке баз (ibases.v8i) и действуют также в штатном стартере 1С. Пользователь и пароль хранятся в лаунчере.",
        _ => "Добавляются после параметров папок. Список баз при этом не меняется.",
    };

    /// <summary>Уже применяемые параметры (до этих).</summary>
    public string InheritedText { get; }

    public bool HasInherited => InheritedText.Length > 0;

    public IReadOnlyList<ParameterTemplate> Templates { get; }

    public IReadOnlyList<string> ClientNames { get; } = ["Как указано у базы", "Тонкий клиент", "Толстый клиент", "Веб-клиент"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParameters))]
    public partial string Parameters { get; set; } = string.Empty;

    public bool HasParameters => !string.IsNullOrEmpty(Parameters);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateNeedsValue), nameof(TemplateNeedsFile), nameof(TemplateDescription))]
    [NotifyCanExecuteChangedFor(nameof(InsertTemplateCommand))]
    public partial ParameterTemplate? SelectedTemplate { get; set; }

    public bool TemplateNeedsValue => SelectedTemplate?.Value is ParameterValueKind.Text or ParameterValueKind.File;

    public bool TemplateNeedsFile => SelectedTemplate?.Value == ParameterValueKind.File;

    public string TemplateDescription => SelectedTemplate is { } template
        ? string.IsNullOrWhiteSpace(template.Description) ? template.CommandText : $"{template.CommandText} — {char.ToLower(template.Description[0], System.Globalization.CultureInfo.CurrentCulture)}{template.Description[1..]}"
        : string.Empty;

    [ObservableProperty]
    public partial string TemplateValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Пароль уже сохранён в хранилище ОС: пустое поле пароля означает «не менять».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordPlaceholder))]
    public partial bool HasSavedPassword { get; set; }

    public string PasswordPlaceholder => HasSavedPassword ? "сохранён; пусто — не менять" : string.Empty;

    /// <summary>Хранить пароль в хранилище ОС (только для базы).</summary>
    [ObservableProperty]
    public partial bool SavePassword { get; set; }

    /// <summary>Почему пароль нельзя сохранить; <c>null</c> — можно.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword), nameof(ShowSavePassword))]
    public partial string? SavePasswordUnavailableReason { get; set; }

    public bool CanSavePassword => SavePasswordUnavailableReason is null;

    public bool ShowSavePassword => Kind is LaunchParametersKind.InfoBase or LaunchParametersKind.ListEntry;

    /// <summary>Версии платформы для разового запуска; пусто — поле не показывается.</summary>
    public IReadOnlyList<PlatformChoice> PlatformChoices { get; init; } = [];

    public bool ShowPlatform => IsOneOff && PlatformChoices.Count > 0;

    [ObservableProperty]
    public partial PlatformChoice? SelectedPlatformChoice { get; set; }

    /// <summary>Сохранить выбранную платформу для базы: дальше она используется и при обычном запуске.</summary>
    [ObservableProperty]
    public partial bool RememberPlatform { get; set; }

    [ObservableProperty]
    public partial int ClientIndex { get; set; }

    /// <summary>Клиент для разового запуска; <c>null</c> — как у базы.</summary>
    public ClientApp? ClientOverride => Clients[Math.Clamp(ClientIndex, 0, Clients.Length - 1)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string Errors { get; private set; } = string.Empty;

    public bool HasErrors => Errors.Length > 0;

    /// <summary>Режим разового запуска, выбранный кнопкой; заполняется в <see cref="TryAccept"/>.</summary>
    public LaunchMode? Mode { get; private set; }

    /// <summary>Проверяет данные формы.</summary>
    /// <param name="mode">Для разового запуска — режим; для остальных не нужен.</param>
    public bool TryAccept(LaunchMode? mode = null)
    {
        var errors = new List<string>();
        if (Password.Length > 0 && string.IsNullOrWhiteSpace(UserName))
        {
            errors.Add("Пароль указан без пользователя.");
        }

        if (SavePassword && !CanSavePassword)
        {
            errors.Add(SavePasswordUnavailableReason!);
        }

        Errors = string.Join(Environment.NewLine, errors);
        Mode = mode;
        return errors.Count == 0;
    }

    /// <summary>Очистить поле параметров (кнопка ✕ в поле).</summary>
    [RelayCommand]
    private void ClearParameters() => Parameters = string.Empty;

    /// <summary>Вставить выбранный шаблон в конец параметров. Шаблон выбирает пользователь — по умолчанию ничего не выбрано.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedTemplate))]
    private void InsertTemplate()
    {
        if (SelectedTemplate is not { } template)
        {
            return;
        }

        if (template.Value != ParameterValueKind.None && string.IsNullOrWhiteSpace(TemplateValue))
        {
            Errors = $"Для «{template.Name}» нужно значение.";
            return;
        }

        Errors = string.Empty;
        Parameters = ParameterLibrary.Append(Parameters, template.Format(TemplateValue));
        TemplateValue = string.Empty;
    }

    private bool HasSelectedTemplate() => SelectedTemplate is not null;

    [RelayCommand]
    private async Task BrowseTemplateFileAsync()
    {
        if (await _files.OpenFileAsync("Внешняя обработка", "Внешняя обработка 1С", ["*.epf", "*.erf"]) is { } path)
        {
            TemplateValue = path;
        }
    }
}
