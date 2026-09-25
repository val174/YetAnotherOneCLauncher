using Avalonia.Controls;
using Avalonia.Input;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Кэш баз». Пробел отмечает выделенную строку.</summary>
public partial class CacheManagerWindow : Window
{
    // Нужен дизайнеру XAML.
    public CacheManagerWindow()
    {
        InitializeComponent();
    }

    public CacheManagerWindow(CacheManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();
        RowsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && RowsList.SelectedItem is CacheRowViewModel row)
            {
                row.IsSelected = !row.IsSelected;
                e.Handled = true;
            }
        };
    }
}
