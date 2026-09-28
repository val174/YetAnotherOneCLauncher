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

        // Enter сохраняет форму базы и папки; при разовом запуске — запускает 1С: Предприятие (IsDefault).
        // Как в главном окне: F3 — 1С: Предприятие, F4 — Конфигуратор.
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when !viewModel.IsOneOff:
                    Accept(null);
                    e.Handled = true;
                    break;
                case Key.F3 when viewModel.IsOneOff:
                    Accept(LaunchMode.Enterprise);
                    e.Handled = true;
                    break;
                case Key.F4 when viewModel.IsOneOff:
                    Accept(LaunchMode.Designer);
                    e.Handled = true;
                    break;
            }
        };

        Opened += (_, _) => ParametersBox.Focus();
    }
}
