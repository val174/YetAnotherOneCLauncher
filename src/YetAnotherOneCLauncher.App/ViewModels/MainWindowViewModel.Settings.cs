using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Окно «Настройки» и горячие клавиши.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Действующие сочетания клавиш (с переопределениями из настроек).</summary>
    [ObservableProperty]
    public partial HotKeyMap HotKeys { get; private set; } = HotKeyMap.Default;

    /// <summary>Текущие значения — для черновика окна настроек.</summary>
    internal SettingsValues CurrentSettings => new()
    {
        AfterLaunchIndex = AfterLaunchIndex,
        MinimizeToTray = MinimizeToTray,
        SingleInstance = SingleInstance,
        UseThickClientForFileBases = UseThickClientForFileBases,
        CheckAvailability = CheckAvailability,
        UpdateModeIndex = UpdateModeIndex,
        ShowEdtProjects = ShowEdtProjects,
        ThemeIndex = ThemeIndex,
        DarkShadeIndex = DarkShadeIndex,
        LightShadeIndex = LightShadeIndex,
        IconStyleIndex = IconStyleIndex,
        ShowDetails = ShowDetails,
        ShowRowLaunchButtons = ShowRowLaunchButtons,
        RowLaunchPlacementIndex = RowLaunchPlacementIndex,
        ShowSideLaunchButtons = ShowSideLaunchButtons,
        TwoLineRows = TwoLineRows,
        HighlightRunning = HighlightRunning,
        RowStripesIndex = RowStripesIndex,
        HotKeys = HotKeys,
        ParameterTemplates = [.. _settings.Settings.ParameterTemplates],
        AdminTools = [.. _settings.Settings.AdminTools],
    };

    /// <summary>Окно настроек; изменения применяются, только если нажато «Сохранить».</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var settings = new SettingsViewModel(CurrentSettings, ShowAboutCommand, _toolIcons, _files)
        {
            CheckForUpdatesCommand = CanCheckForUpdates ? CheckForUpdatesCommand : null,
            UpdateStatusText = UpdateStatusText,
        };
        if (await _dialogs.EditSettingsAsync(settings))
        {
            ApplySettings(settings.Result);
            StatusText = "Настройки сохранены.";
        }
    }

    /// <summary>Применить и сохранить значения из окна настроек.</summary>
    internal void ApplySettings(SettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Каждое свойство само записывает себя в настройки и применяется (тема, значки, проверка доступности…).
        AfterLaunchIndex = values.AfterLaunchIndex;
        MinimizeToTray = values.MinimizeToTray;
        SingleInstance = values.SingleInstance;
        UseThickClientForFileBases = values.UseThickClientForFileBases;
        CheckAvailability = values.CheckAvailability;
        UpdateModeIndex = values.UpdateModeIndex;
        ShowEdtProjects = values.ShowEdtProjects;
        ThemeIndex = values.ThemeIndex;
        DarkShadeIndex = values.DarkShadeIndex;
        LightShadeIndex = values.LightShadeIndex;
        IconStyleIndex = values.IconStyleIndex;
        ShowDetails = values.ShowDetails;
        ShowRowLaunchButtons = values.ShowRowLaunchButtons;
        RowLaunchPlacementIndex = values.RowLaunchPlacementIndex;
        ShowSideLaunchButtons = values.ShowSideLaunchButtons;
        TwoLineRows = values.TwoLineRows;
        HighlightRunning = values.HighlightRunning;
        RowStripesIndex = values.RowStripesIndex;

        if (!values.HotKeys.SameAs(HotKeys))
        {
            HotKeys = values.HotKeys;
            _settings.Settings.Ui.HotKeys = values.HotKeys.ToOverrides();
            _settings.RequestSave();
        }

        if (!values.ParameterTemplates.SequenceEqual(_settings.Settings.ParameterTemplates))
        {
            _settings.Settings.ParameterTemplates = [.. values.ParameterTemplates];
            _settings.RequestSave();
        }

        if (!values.AdminTools.SequenceEqual(_settings.Settings.AdminTools))
        {
            _settings.Settings.AdminTools = [.. values.AdminTools];
            _settings.RequestSave();
        }

        // Свои значки, от которых отказались (и загруженные, но не сохранённые), больше не нужны.
        _toolIcons?.RemoveUnused(_settings.Settings.AdminTools);
    }
}
