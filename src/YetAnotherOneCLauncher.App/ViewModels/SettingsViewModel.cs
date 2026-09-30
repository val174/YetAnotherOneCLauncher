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

    public string PuskUrl { get; init; } = string.Empty;

    // Внешний вид.
    public int ThemeIndex { get; init; }

    public int IconStyleIndex { get; init; }

    public bool ShowDetails { get; init; }

    // Горячие клавиши.
    public HotKeyMap HotKeys { get; init; } = HotKeyMap.Default;

    // Свои шаблоны параметров.
    public IReadOnlyList<ParameterTemplate> ParameterTemplates { get; init; } = [];
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
/// Окно «Настройки»: вкладки «Общие», «Внешний вид», «Горячие клавиши», «Шаблоны параметров». Правки копятся в черновике и применяются
/// кнопкой «Сохранить»; пока есть несохранённые — в заголовке окна и у вкладки «*».
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsValues _original;

    public SettingsViewModel(SettingsValues original, ICommand? showAbout = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
        ShowAboutCommand = showAbout;

        AfterLaunchIndex = original.AfterLaunchIndex;
        MinimizeToTray = original.MinimizeToTray;
        SingleInstance = original.SingleInstance;
        UseThickClientForFileBases = original.UseThickClientForFileBases;
        CheckAvailability = original.CheckAvailability;
        PuskUrl = original.PuskUrl;
        ThemeIndex = original.ThemeIndex;
        IconStyleIndex = original.IconStyleIndex;
        ShowDetails = original.ShowDetails;
        foreach (var definition in HotKeyMap.Definitions)
        {
            var row = new HotKeyRowViewModel(definition, original.HotKeys[definition.Command]);
            row.PropertyChanged += OnHotKeyRowChanged;
            HotKeyRows.Add(row);
        }

        Templates = new ParameterTemplatesViewModel(original.ParameterTemplates);
        Templates.Rows.CollectionChanged += (_, _) => RaiseDirty();
        PropertyChanged += OnOwnPropertyChanged;
    }

    public IReadOnlyList<string> AfterLaunchNames { get; } = ["Ничего не делать", "Свернуть окно", "Закрыть лаунчер"];

    public IReadOnlyList<string> ThemeNames { get; } = ["Как в системе", "Светлая", "Тёмная"];

    public IReadOnlyList<string> IconStyleNames { get; } = ["Стиль 1", "Стиль 2"];

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPuskUrlInvalid))]
    public partial string PuskUrl { get; set; } = string.Empty;

    public bool IsPuskUrlInvalid => !string.IsNullOrWhiteSpace(PuskUrl) && NetworkSettings.ParseWebUrl(PuskUrl) is null;

    // --- Внешний вид ---
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int IconStyleIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    // --- Горячие клавиши ---
    public ObservableCollection<HotKeyRowViewModel> HotKeyRows { get; } = [];

    /// <summary>Строка, которая ждёт нажатия; <c>null</c> — ни одна.</summary>
    public HotKeyRowViewModel? CapturingRow => HotKeyRows.FirstOrDefault(r => r.IsCapturing);

    // --- Шаблоны параметров ---

    /// <summary>Вкладка «Шаблоны параметров»: таблица параметров, свои добавляются, изменяются и удаляются.</summary>
    public ParameterTemplatesViewModel Templates { get; }

    // --- Изменения ---
    public bool IsGeneralDirty =>
        AfterLaunchIndex != _original.AfterLaunchIndex
        || MinimizeToTray != _original.MinimizeToTray
        || SingleInstance != _original.SingleInstance
        || UseThickClientForFileBases != _original.UseThickClientForFileBases
        || CheckAvailability != _original.CheckAvailability
        || !string.Equals(PuskUrl.Trim(), _original.PuskUrl.Trim(), StringComparison.Ordinal);

    public bool IsAppearanceDirty =>
        ThemeIndex != _original.ThemeIndex || IconStyleIndex != _original.IconStyleIndex || ShowDetails != _original.ShowDetails;

    public bool IsHotKeysDirty => !CurrentHotKeys.SameAs(_original.HotKeys);

    public bool IsTemplatesDirty => !Templates.CustomTemplates.SequenceEqual(_original.ParameterTemplates);

    public bool IsDirty => IsGeneralDirty || IsAppearanceDirty || IsHotKeysDirty || IsTemplatesDirty;

    public string Title => IsDirty ? "Настройки*" : "Настройки";

    public string GeneralHeader => IsGeneralDirty ? "Общие*" : "Общие";

    public string AppearanceHeader => IsAppearanceDirty ? "Внешний вид*" : "Внешний вид";

    public string HotKeysHeader => IsHotKeysDirty ? "Горячие клавиши*" : "Горячие клавиши";

    public string TemplatesHeader => IsTemplatesDirty ? "Шаблоны параметров*" : "Шаблоны параметров";

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
        PuskUrl = PuskUrl.Trim(),
        ThemeIndex = ThemeIndex,
        IconStyleIndex = IconStyleIndex,
        ShowDetails = ShowDetails,
        HotKeys = CurrentHotKeys,
        ParameterTemplates = Templates.CustomTemplates,
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
            or nameof(HotKeysHeader) or nameof(TemplatesHeader) or nameof(IsTemplatesDirty) or nameof(IsGeneralDirty) or nameof(IsAppearanceDirty) or nameof(IsHotKeysDirty)
            or nameof(IsPuskUrlInvalid))
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
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(GeneralHeader));
        OnPropertyChanged(nameof(AppearanceHeader));
        OnPropertyChanged(nameof(HotKeysHeader));
        OnPropertyChanged(nameof(TemplatesHeader));
    }
}
