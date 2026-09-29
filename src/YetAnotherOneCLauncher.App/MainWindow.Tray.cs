using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Сворачивание в трей: при включённой настройке в области уведомлений есть значок лаунчера, а свёрнутое окно
/// прячется с панели задач. Щелчок по значку (или повторный запуск лаунчера) возвращает окно.
/// </summary>
public partial class MainWindow
{
    private TrayIcon? _trayIcon;
    private WindowState _restoreState = WindowState.Normal;

    /// <summary>Окно спрятано в трей.</summary>
    internal bool IsInTray { get; private set; }

    /// <summary>Значок в трее (создаётся, когда настройку включают впервые).</summary>
    internal TrayIcon? TrayIcon => _trayIcon;

    /// <summary>Показать окно поверх остальных: из трея, из панели задач или из-под других окон.</summary>
    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        IsInTray = false;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        Activate();
    }

    private void InitTray(MainWindowViewModel viewModel)
    {
        UpdateTrayIcon(viewModel.MinimizeToTray);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.MinimizeToTray))
            {
                UpdateTrayIcon(viewModel.MinimizeToTray);
            }
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty)
            {
                return;
            }

            var state = (WindowState)e.NewValue!;
            if (state != WindowState.Minimized)
            {
                _restoreState = state; // развернуть из трея в том же виде: обычным или на весь экран
            }
            else if (viewModel.MinimizeToTray)
            {
                // Прячем после того, как окно свернулось: скрытие посреди смены состояния Windows может отменить.
                Dispatcher.UIThread.Post(HideToTray);
            }
        };

        Closed += (_, _) => _trayIcon?.Dispose();
    }

    private void HideToTray()
    {
        if (WindowState == WindowState.Minimized && ViewModel?.MinimizeToTray == true)
        {
            IsInTray = true;
            Hide();
        }
    }

    private void UpdateTrayIcon(bool visible)
    {
        if (_trayIcon is null)
        {
            if (!visible)
            {
                return;
            }

            _trayIcon = CreateTrayIcon();
        }

        _trayIcon.IsVisible = visible;
    }

    private TrayIcon CreateTrayIcon()
    {
        var open = new NativeMenuItem("Открыть лаунчер");
        open.Click += (_, _) => BringToFront();
        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) => Close();

        var icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://YetAnotherOneCLauncher/Assets/app.ico"))),
            ToolTipText = Title,
            Menu = [open, new NativeMenuItemSeparator(), exit],
        };
        icon.Clicked += (_, _) => BringToFront();
        return icon;
    }
}
