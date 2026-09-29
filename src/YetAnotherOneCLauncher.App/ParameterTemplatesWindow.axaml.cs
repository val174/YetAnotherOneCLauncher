using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Свои шаблоны параметров. В полях ввода: Enter — добавить или применить изменение, Esc — отменить изменение.
/// В таблице: F2 или двойной щелчок — изменить свой параметр, Del — удалить.
/// </summary>
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
        EditButton.Click += (_, _) => FocusParameter();
        ApplyButton.Click += (_, _) => NewParameterBox.Focus();

        foreach (var box in new[] { NewParameterBox, NewDescriptionBox })
        {
            // Туннелем: Esc иначе закроет всё окно (кнопка «Отмена»), а нужно отменить только изменение.
            box.AddHandler(KeyDownEvent, (_, e) => OnFormKeyDown(templates, e), RoutingStrategies.Tunnel);
        }

        RowsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && templates.DeleteCommand.CanExecute(null))
            {
                templates.DeleteCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && templates.EditCommand.CanExecute(null))
            {
                templates.EditCommand.Execute(null);
                FocusParameter();
                e.Handled = true;
            }
        };
        RowsList.DoubleTapped += (_, _) =>
        {
            if (templates.EditCommand.CanExecute(null))
            {
                templates.EditCommand.Execute(null);
                FocusParameter();
            }
        };

        Opened += (_, _) => NewParameterBox.Focus();
    }

    private void OnFormKeyDown(ParameterTemplatesViewModel templates, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && templates.ApplyCommand.CanExecute(null))
        {
            templates.ApplyCommand.Execute(null);
            NewParameterBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && templates.IsEditing)
        {
            templates.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusParameter()
    {
        NewParameterBox.Focus();
        NewParameterBox.SelectAll();
    }
}
