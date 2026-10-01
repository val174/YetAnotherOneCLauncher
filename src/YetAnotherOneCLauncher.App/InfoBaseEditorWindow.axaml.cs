using Avalonia.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Форма базы. Для новой базы сначала выбирается вариант: добавить существующую, создать из шаблона
/// или без конфигурации. Закрывается с <c>true</c>, только если данные прошли проверку (и база создана).
/// </summary>
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

        SaveButton.Click += async (_, _) =>
        {
            if (viewModel.ShowModePage)
            {
                viewModel.NextCommand.Execute(null);
                FocusForm();
            }
            else if (viewModel.IsCreateMode ? await viewModel.TryCreateAsync() : viewModel.TryAccept())
            {
                Close(true);
            }
        };
        BackButton.Click += (_, _) => ExistingModeButton.Focus();
        CancelButton.Click += (_, _) => Close(false);
        // Пока платформа создаёт базу, окно не закрывается: результат нужно дождаться.
        Closing += (_, e) => e.Cancel |= viewModel.IsBusy;
        Opened += (_, _) =>
        {
            if (viewModel.ShowModePage)
            {
                ExistingModeButton.Focus();
            }
            else
            {
                FocusForm();
            }
        };
    }

    private void FocusForm()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }
}
