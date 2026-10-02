using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Значения настроек, которые редактирует окно «Настройки».</summary>
public sealed record SettingsValues
{
    // Общие.
    public int AfterLaunchIndex { get; init; }

    public bool MinimizeToTray { get; init; }

    public bool SingleInstance { get; init; }

    public bool UseThickClientForFileBases { get; init; }

    public bool CheckAvailability { get; init; }

    // Внешний вид.
    public int ThemeIndex { get; init; }

    public int IconStyleIndex { get; init; }

    public bool ShowDetails { get; init; }

    public bool ShowRowLaunchButtons { get; init; } = true;

    public int RowLaunchPlacementIndex { get; init; }

    public bool ShowSideLaunchButtons { get; init; } = true;

    public bool TwoLineRows { get; init; }

    public bool HighlightRunning { get; init; } = true;

    // Горячие клавиши.
    public HotKeyMap HotKeys { get; init; } = HotKeyMap.Default;

    // Свои шаблоны параметров.
    public IReadOnlyList<ParameterTemplate> ParameterTemplates { get; init; } = [];

    // Средства администрирования.
    public IReadOnlyList<AdminTool> AdminTools { get; init; } = [];
}

/// <summary>Строка вкладки «Горячие клавиши».</summary>
public sealed partial class HotKeyRowViewModel : ObservableObject
{
    public HotKeyRowViewModel(HotKeyDefinition definition, KeyGesture? gesture)
    {
        Definition = definition;
        Gesture = gesture;
    }

    public HotKeyDefinition Definition { get; }

    public string Title => Definition.Title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText), nameof(IsDefault))]
    public partial KeyGesture? Gesture { get; set; }

    /// <summary>Ждёт нажатия нового сочетания.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText))]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public string GestureText => IsCapturing ? "Нажмите сочетание…" : HotKeyMap.Format(Gesture);

    public bool IsDefault => Gesture is { } g && g.Key == Definition.Default.Key && g.KeyModifiers == Definition.Default.KeyModifiers;

    public string DefaultText => "По умолчанию: " + HotKeyMap.Format(Definition.Default);
}

/// <summary>
/// Окно «Настройки»: вкладки «Общие», «Внешний вид», «Горячие клавиши», «Шаблоны параметров», «Средства администрирования». Правки копятся в черновике и применяются
/// кнопкой «Сохранить»; пока есть несохранённые — в заголовке окна и у вкладки «*».
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsValues _original;

    public SettingsViewModel(
        SettingsValues original, ICommand? showAbout = null, IAdminToolIconSource? toolIcons = null, IFileDialogService? files = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
        ShowAboutCommand = showAbout;

        AfterLaunchIndex = original.AfterLaunchIndex;
        MinimizeToTray = original.MinimizeToTray;
        SingleInstance = original.SingleInstance;
        UseThickClientForFileBases = original.UseThickClientForFileBases;
        CheckAvailability = original.CheckAvailability;
        ThemeIndex = original.ThemeIndex;
        IconStyleIndex = original.IconStyleIndex;
        ShowDetails = original.ShowDetails;
        ShowRowLaunchButtons = original.ShowRowLaunchButtons;
        RowLaunchPlacementIndex = original.RowLaunchPlacementIndex;
        ShowSideLaunchButtons = original.ShowSideLaunchButtons;
        TwoLineRows = original.TwoLineRows;
        HighlightRunning = original.HighlightRunning;
        foreach (var definition in HotKeyMap.Definitions)
        {
            var row = new HotKeyRowViewModel(definition, original.HotKeys[definition.Command]);
            row.PropertyChanged += OnHotKeyRowChanged;
            HotKeyRows.Add(row);
        }

        Templates = new ParameterTemplatesViewModel(original.ParameterTemplates);
        Templates.Rows.CollectionChanged += (_, _) => RaiseDirty();
        AdminTools = new AdminToolsViewModel(original.AdminTools, toolIcons, files);
        AdminTools.Rows.CollectionChanged += (_, _) => RaiseDirty();
        PropertyChanged += OnOwnPropertyChanged;
    }

    public IReadOnlyList<string> AfterLaunchNames { get; } = ["Ничего не делать", "Свернуть окно", "Закрыть лаунчер"];

    public IReadOnlyList<string> ThemeNames { get; } = ["Как в системе", "Светлая", "Тёмная"];

    public IReadOnlyList<string> IconStyleNames { get; } = ["Стиль 1", "Стиль 2"];

    /// <summary>В порядке <see cref="RowLaunchPlacement"/>.</summary>
    public IReadOnlyList<string> RowLaunchPlacementNames { get; } = ["Справа, у края колонки наименования", "Слева от наименования"];

    public ICommand? ShowAboutCommand { get; }

    // --- Общие ---
    [ObservableProperty]
    public partial int AfterLaunchIndex { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool SingleInstance { get; set; }

    [ObservableProperty]
    public partial bool UseThickClientForFileBases { get; set; }

    [ObservableProperty]
    public partial bool CheckAvailability { get; set; }

    // --- Внешний вид ---
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int IconStyleIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    [ObservableProperty]
    public partial bool ShowRowLaunchButtons { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowLaunchHint))]
    public partial int RowLaunchPlacementIndex { get; set; }

    public string RowLaunchHint => RowLaunchPlacementIndex == (int)RowLaunchPlacement.Left
        ? "Одна кнопка ▶ в каждой строке, видна всегда: щелчок открывает меню — 1С: Предприятие, Конфигуратор или запуск с параметрами."
        : "Появляются у строки под указателем мыши и у выделенной.";

    [ObservableProperty]
    public partial bool ShowSideLaunchButtons { get; set; }

    [ObservableProperty]
    public partial bool TwoLineRows { get; set; }

    [ObservableProperty]
    public partial bool HighlightRunning { get; set; }

    // --- Горячие клавиши ---
    public ObservableCollection<HotKeyRowViewModel> HotKeyRows { get; } = [];

    /// <summary>Строка, которая ждёт нажатия; <c>null</c> — ни одна.</summary>
    public HotKeyRowViewModel? CapturingRow => HotKeyRows.FirstOrDefault(r => r.IsCapturing);

    // --- Шаблоны параметров ---

    /// <summary>Вкладка «Шаблоны параметров»: таблица параметров, свои добавляются, изменяются и удаляются.</summary>
    public ParameterTemplatesViewModel Templates { get; }

    // --- Средства администрирования ---

    /// <summary>Вкладка «Средства администрирования»: таблица «Инструменты».</summary>
    public AdminToolsViewModel AdminTools { get; }

    // --- Изменения ---
    public bool IsGeneralDirty =>
        AfterLaunchIndex != _original.AfterLaunchIndex
        || MinimizeToTray != _original.MinimizeToTray
        || SingleInstance != _original.SingleInstance
        || UseThickClientForFileBases != _original.UseThickClientForFileBases
        || CheckAvailability != _original.CheckAvailability;

    public bool IsAppearanceDirty =>
        ThemeIndex != _original.ThemeIndex
        || IconStyleIndex != _original.IconStyleIndex
        || ShowDetails != _original.ShowDetails
        || ShowRowLaunchButtons != _original.ShowRowLaunchButtons
        || RowLaunchPlacementIndex != _original.RowLaunchPlacementIndex
        || ShowSideLaunchButtons != _original.ShowSideLaunchButtons
        || TwoLineRows != _original.TwoLineRows
        || HighlightRunning != _original.HighlightRunning;

    public bool IsHotKeysDirty => !CurrentHotKeys.SameAs(_original.HotKeys);

    public bool IsTemplatesDirty => !Templates.CustomTemplates.SequenceEqual(_original.ParameterTemplates);

    public bool IsAdminToolsDirty => !AdminTools.Tools.SequenceEqual(_original.AdminTools);

    public bool IsDirty => IsGeneralDirty || IsAppearanceDirty || IsHotKeysDirty || IsTemplatesDirty || IsAdminToolsDirty;

    public string Title => IsDirty ? "Настройки*" : "Настройки";

    public string GeneralHeader => IsGeneralDirty ? "Общие*" : "Общие";

    public string AppearanceHeader => IsAppearanceDirty ? "Внешний вид*" : "Внешний вид";

    public string HotKeysHeader => IsHotKeysDirty ? "Горячие клавиши*" : "Горячие клавиши";

    public string TemplatesHeader => IsTemplatesDirty ? "Шаблоны параметров*" : "Шаблоны параметров";

    public string AdminToolsHeader => IsAdminToolsDirty ? "Средства администрирования*" : "Средства администрирования";

    public HotKeyMap CurrentHotKeys =>
        HotKeyRows.Aggregate(HotKeyMap.Default, (map, row) => map.With(row.Definition.Command, row.Gesture));

    /// <summary>Что сохранить (кнопка «Сохранить»).</summary>
    public SettingsValues Result => new()
    {
        AfterLaunchIndex = AfterLaunchIndex,
        MinimizeToTray = MinimizeToTray,
        SingleInstance = SingleInstance,
        UseThickClientForFileBases = UseThickClientForFileBases,
        CheckAvailability = CheckAvailability,
        ThemeIndex = ThemeIndex,
        IconStyleIndex = IconStyleIndex,
        ShowDetails = ShowDetails,
        ShowRowLaunchButtons = ShowRowLaunchButtons,
        RowLaunchPlacementIndex = RowLaunchPlacementIndex,
        ShowSideLaunchButtons = ShowSideLaunchButtons,
        TwoLineRows = TwoLineRows,
        HighlightRunning = HighlightRunning,
        HotKeys = CurrentHotKeys,
        ParameterTemplates = Templates.CustomTemplates,
        AdminTools = AdminTools.Tools,
    };

    /// <summary>Ждать нового сочетания для строки (остальные перестают ждать).</summary>
    [RelayCommand]
    private void StartCapture(HotKeyRowViewModel? row)
    {
        foreach (var other in HotKeyRows)
        {
            other.IsCapturing = ReferenceEquals(other, row);
            other.ErrorText = string.Empty;
        }
    }

    public void CancelCapture()
    {
        foreach (var row in HotKeyRows)
        {
            row.IsCapturing = false;
        }
    }

    /// <summary>
    /// Назначить нажатое сочетание строке, которая ждёт. Нельзя назначить сочетание, мешающее набору текста,
    /// и уже занятое другой командой — строка покажет почему.
    /// </summary>
    /// <returns><c>false</c> — сочетание не принято.</returns>
    public bool TryAssign(HotKeyRowViewModel row, KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(gesture);
        if (HotKeyMap.Validate(gesture, row.Definition.Scope) is { } problem)
        {
            row.ErrorText = problem;
            return false;
        }

        var owner = HotKeyRows.FirstOrDefault(r => !ReferenceEquals(r, row)
            && r.Gesture is { } g && g.Key == gesture.Key && g.KeyModifiers == gesture.KeyModifiers);
        if (owner is not null)
        {
            row.ErrorText = $"{HotKeyMap.Format(gesture)} уже назначено команде «{owner.Title}».";
            return false;
        }

        row.Gesture = gesture;
        row.IsCapturing = false;
        row.ErrorText = string.Empty;
        return true;
    }

    [RelayCommand]
    private void ClearHotKey(HotKeyRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        CancelCapture();
        row.Gesture = null;
        row.ErrorText = string.Empty;
    }

    /// <summary>Вернуть сочетание по умолчанию; если его заняла другая команда — у той оно снимается.</summary>
    [RelayCommand]
    private void ResetHotKey(HotKeyRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var gesture = row.Definition.Default;
        foreach (var other in HotKeyRows.Where(r => !ReferenceEquals(r, row)
                     && r.Gesture is { } g && g.Key == gesture.Key && g.KeyModifiers == gesture.KeyModifiers))
        {
            other.Gesture = null;
        }

        row.Gesture = gesture;
        row.IsCapturing = false;
        row.ErrorText = string.Empty;
    }

    [RelayCommand]
    private void ResetAllHotKeys()
    {
        foreach (var row in HotKeyRows)
        {
            row.Gesture = row.Definition.Default;
            row.IsCapturing = false;
            row.ErrorText = string.Empty;
        }
    }

    private void OnHotKeyRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotKeyRowViewModel.Gesture))
        {
            OnPropertyChanged(nameof(IsHotKeysDirty));
            RaiseDirty();
        }
    }

    private void OnOwnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsDirty) or nameof(Title) or nameof(GeneralHeader) or nameof(AppearanceHeader)
            or nameof(HotKeysHeader) or nameof(TemplatesHeader) or nameof(IsTemplatesDirty) or nameof(AdminToolsHeader) or nameof(IsAdminToolsDirty) or nameof(IsGeneralDirty) or nameof(IsAppearanceDirty) or nameof(IsHotKeysDirty) or nameof(RowLaunchHint))
        {
            return;
        }

        RaiseDirty();
    }

    private void RaiseDirty()
    {
        OnPropertyChanged(nameof(IsGeneralDirty));
        OnPropertyChanged(nameof(IsAppearanceDirty));
        OnPropertyChanged(nameof(IsHotKeysDirty));
        OnPropertyChanged(nameof(IsTemplatesDirty));
        OnPropertyChanged(nameof(IsAdminToolsDirty));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(GeneralHeader));
        OnPropertyChanged(nameof(AppearanceHeader));
        OnPropertyChanged(nameof(HotKeysHeader));
        OnPropertyChanged(nameof(TemplatesHeader));
        OnPropertyChanged(nameof(AdminToolsHeader));
    }
}
