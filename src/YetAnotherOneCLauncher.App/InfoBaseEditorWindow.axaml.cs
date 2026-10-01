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
            if (!viewModel.ShowForm)
            {
                viewModel.NextCommand.Execute(null);
                FocusPage(viewModel);
            }
            else if (viewModel.IsCreateMode ? await viewModel.TryCreateAsync() : viewModel.TryAccept())
            {
                Close(true);
            }
        };
        BackButton.Click += (_, _) =>
        {
            viewModel.BackCommand.Execute(null);
            FocusPage(viewModel);
        };
        CancelButton.Click += (_, _) => Close(false);
        // Пока платформа создаёт базу, окно не закрывается: результат нужно дождаться.
        Closing += (_, e) => e.Cancel |= viewModel.IsBusy;
        Opened += (_, _) => FocusPage(viewModel);
    }

    /// <summary>Фокус — на первое поле текущего шага.</summary>
    private void FocusPage(InfoBaseEditorViewModel viewModel)
    {
        if (viewModel.ShowModePage)
        {
            ExistingModeButton.Focus();
        }
        else if (viewModel.ShowTemplatePage)
        {
            TemplateSearchBox.Focus();
        }
        else
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }
}
