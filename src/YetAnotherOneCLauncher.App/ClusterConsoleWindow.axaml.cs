using Avalonia.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App;

/// <summary>Консоль кластера серверов: выбор версии платформы и переход в «ПУСК».</summary>
public partial class ClusterConsoleWindow : Window
{
    // Нужен дизайнеру XAML.
    public ClusterConsoleWindow()
        : this(new ClusterConsoleViewModel([], new NoClusterConsole(), null!, null))
    {
    }

    public ClusterConsoleWindow(ClusterConsoleViewModel console)
    {
        InitializeComponent();
        DataContext = console;
        console.CloseRequested += (_, _) => Close();
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => VersionsList.Focus();
    }
}
