using Avalonia.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Форма базы. Закрывается с <c>true</c>, только если данные прошли проверку.</summary>
public partial class InfoBaseEditorWindow : Window
{
    // Нужен дизайнеру XAML.
    public InfoBaseEditorWindow()
    {
        InitializeComponent();
    }

    public InfoBaseEditorWindow(InfoBaseEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        SaveButton.Click += (_, _) =>
        {
            if (viewModel.TryAccept())
            {
                Close(true);
            }
        };
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}
