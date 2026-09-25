using Avalonia.Controls;
using Avalonia.Input;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.App;

/// <summary>Параметры запуска. Закрывается с <c>true</c>, только если данные прошли проверку.</summary>
public partial class LaunchParametersWindow : Window
{
    // Нужен дизайнеру XAML.
    public LaunchParametersWindow()
    {
        InitializeComponent();
    }

    public LaunchParametersWindow(LaunchParametersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        void Accept(LaunchMode? mode)
        {
            if (viewModel.TryAccept(mode))
            {
                Close(true);
            }
        }

        EnterpriseButton.Click += (_, _) => Accept(LaunchMode.Enterprise);
        DesignerButton.Click += (_, _) => Accept(LaunchMode.Designer);
        SaveButton.Click += (_, _) => Accept(null);
        CancelButton.Click += (_, _) => Close(false);

        // Enter сохраняет форму базы и папки; при разовом запуске — запускает Предприятие (IsDefault),
        // Ctrl+Enter — Конфигуратор, как в главном окне.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if (!viewModel.IsOneOff)
            {
                Accept(null);
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control)
            {
                Accept(LaunchMode.Designer);
                e.Handled = true;
            }
        };

        Opened += (_, _) => ParametersBox.Focus();
    }
}
