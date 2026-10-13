using Avalonia.Controls;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Новая метка»; закрывается с <c>true</c>, если наименование задано и нажато «Создать».</summary>
public partial class NewTagWindow : Window
{
    // Нужен дизайнеру XAML.
    public NewTagWindow()
        : this(new NewTagViewModel([], TagColor.Amber, "Метка будет поставлена: «Бухгалтерия»."))
    {
    }

    public NewTagWindow(NewTagViewModel tag)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = tag;

        OkButton.Click += (_, _) =>
        {
            if (tag.Validate())
            {
                Close(true);
            }
        };
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) => NameBox.Focus();
    }
}
