using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Вкладка «Шаблоны параметров» окна настроек. В полях ввода: Enter — добавить или применить изменение,
/// Esc — отменить изменение. В таблице: F2 или двойной щелчок — изменить свой параметр, Del — удалить.
/// </summary>
public partial class ParameterTemplatesView : UserControl
{
    public ParameterTemplatesView()
    {
        InitializeComponent();

        EditButton.Click += (_, _) => FocusParameter();
        ApplyButton.Click += (_, _) => NewParameterBox.Focus();

        foreach (var box in new[] { NewParameterBox, NewDescriptionBox })
        {
            // Туннелем: Esc иначе закроет всё окно настроек (кнопка «Отменить»), а нужно отменить только изменение.
            box.AddHandler(KeyDownEvent, OnFormKeyDown, RoutingStrategies.Tunnel);
        }

        RowsList.KeyDown += (_, e) =>
        {
            if (Templates is not { } templates)
            {
                return;
            }

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
            if (Templates is { } templates && templates.EditCommand.CanExecute(null))
            {
                templates.EditCommand.Execute(null);
                FocusParameter();
            }
        };
    }

    private ParameterTemplatesViewModel? Templates => DataContext as ParameterTemplatesViewModel;

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (Templates is not { } templates)
        {
            return;
        }

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
