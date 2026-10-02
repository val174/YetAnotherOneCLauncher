using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Настройки. Пока строка горячих клавиш ждёт нажатия, клавиши перехватываются раньше всех (туннелем):
/// нажатое сочетание назначается, Esc отменяет ввод и не закрывает окно.
/// </summary>
public partial class SettingsWindow : Window
{
    // Нужен дизайнеру XAML.
    public SettingsWindow()
        : this(new SettingsViewModel(new SettingsValues()))
    {
    }

    public SettingsWindow(SettingsViewModel settings)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = settings;
        SaveButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        AddHandler(KeyDownEvent, (_, e) => OnCaptureKeyDown(settings, e), RoutingStrategies.Tunnel);
    }

    private static void OnCaptureKeyDown(SettingsViewModel settings, KeyEventArgs e)
    {
        if (settings.CapturingRow is not { } row)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            settings.CancelCapture();
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
        {
            return; // ждём основную клавишу
        }

        // Буква — по физической клавише: Ctrl+Й в русской раскладке запишется как Ctrl+Q.
        var qwerty = e.PhysicalKey == PhysicalKey.None ? Key.None : e.PhysicalKey.ToQwertyKey();
        var key = qwerty is >= Key.A and <= Key.Z ? qwerty : e.Key;
        settings.TryAssign(row, new KeyGesture(key, e.KeyModifiers & ~KeyModifiers.Meta));
    }
}
