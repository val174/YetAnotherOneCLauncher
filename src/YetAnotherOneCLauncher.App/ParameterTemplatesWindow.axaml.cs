using Avalonia.Controls;
using Avalonia.Input;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Свои шаблоны параметров. Enter в полях нового параметра — добавить, Del в таблице — удалить свой.</summary>
public partial class ParameterTemplatesWindow : Window
{
    // Нужен дизайнеру XAML.
    public ParameterTemplatesWindow()
        : this(new ParameterTemplatesViewModel([]))
    {
    }

    public ParameterTemplatesWindow(ParameterTemplatesViewModel templates)
    {
        InitializeComponent();
        DataContext = templates;

        SaveButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);

        foreach (var box in new[] { NewParameterBox, NewDescriptionBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && templates.AddCommand.CanExecute(null))
                {
                    templates.AddCommand.Execute(null);
                    NewParameterBox.Focus();
                    e.Handled = true;
                }
            };
        }

        RowsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && templates.DeleteCommand.CanExecute(null))
            {
                templates.DeleteCommand.Execute(null);
                e.Handled = true;
            }
        };

        Opened += (_, _) => NewParameterBox.Focus();
    }
}
