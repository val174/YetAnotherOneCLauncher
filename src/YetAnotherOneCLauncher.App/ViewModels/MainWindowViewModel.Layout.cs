using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Раскладка главного окна (задача #31): панель команд сверху — в одной строке с поиском, меню и кнопками окна —
/// или слева, вертикально; левую можно скрыть кнопкой в левом верхнем углу.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Где панель команд: 0 — сверху, 1 — слева (<see cref="ToolbarPlacement"/>; настройка «Внешний вид»).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolbarLeft), nameof(IsToolbarTop), nameof(IsSideToolbarVisible))]
    public partial int ToolbarPlacementIndex { get; set; }

    public bool IsToolbarLeft => ToolbarPlacementIndex == (int)ToolbarPlacement.Left;

    public bool IsToolbarTop => !IsToolbarLeft;

    /// <summary>Панель слева показана (кнопка в левом верхнем углу; запоминается).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSideToolbarVisible))]
    public partial bool ShowSideToolbar { get; set; } = true;

    /// <summary>Видна панель слева: она выбрана и не скрыта.</summary>
    public bool IsSideToolbarVisible => IsToolbarLeft && ShowSideToolbar;

    partial void OnToolbarPlacementIndexChanged(int value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ToolbarPlacement = value == (int)ToolbarPlacement.Left ? ToolbarPlacement.Left : ToolbarPlacement.Top;
        _settings.RequestSave();
    }

    partial void OnShowSideToolbarChanged(bool value)
    {
        if (_suppressSettingsSync)
        {
            return;
        }

        _settings.Settings.Ui.ShowSideToolbar = value;
        _settings.RequestSave();
    }

    /// <summary>Кнопка в левом верхнем углу: скрыть или показать панель команд слева.</summary>
    [RelayCommand]
    private void ToggleSideToolbar() => ShowSideToolbar = !ShowSideToolbar;
}
