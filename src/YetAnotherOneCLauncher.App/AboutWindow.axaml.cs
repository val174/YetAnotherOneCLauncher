using Avalonia.Controls;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «О программе»: название, версия, автор, среда выполнения.</summary>
public partial class AboutWindow : Window
{
    // Нужен дизайнеру XAML.
    public AboutWindow()
        : this(new AboutViewModel())
    {
    }

    public AboutWindow(AboutViewModel about)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = about;
        OkButton.Click += (_, _) => Close();
    }
}
