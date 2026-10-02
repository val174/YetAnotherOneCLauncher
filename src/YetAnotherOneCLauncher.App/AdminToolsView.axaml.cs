using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Вкладка «Средства администрирования» окна настроек. В полях ввода: Enter — добавить или применить изменение,
/// Esc — отменить изменение. В таблице: F2 или двойной щелчок — изменить, Del — удалить.
/// Кнопка значка открывает меню: «Автоматически», встроенные значки, «Загрузить свой…».
/// </summary>
public partial class AdminToolsView : UserControl
{
    public AdminToolsView()
    {
        InitializeComponent();

        EditButton.Click += (_, _) => FocusName();
        ApplyButton.Click += (_, _) => NewNameBox.Focus();
        IconButton.Click += (_, _) =>
        {
            if (Tools is { } tools)
            {
                CreateIconMenu(tools).ShowAt(IconButton);
            }
        };

        foreach (var box in new[] { NewNameBox, NewTargetBox })
        {
            // Туннелем: Esc иначе закроет всё окно настроек (кнопка «Отменить»), а нужно отменить только изменение.
            box.AddHandler(KeyDownEvent, OnFormKeyDown, RoutingStrategies.Tunnel);
        }

        RowsList.KeyDown += (_, e) =>
        {
            if (Tools is not { } tools)
            {
                return;
            }

            if (e.Key == Key.Delete && tools.DeleteCommand.CanExecute(null))
            {
                tools.DeleteCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && tools.EditCommand.CanExecute(null))
            {
                tools.EditCommand.Execute(null);
                FocusName();
                e.Handled = true;
            }
        };
        RowsList.DoubleTapped += (_, _) =>
        {
            if (Tools is { } tools && tools.EditCommand.CanExecute(null))
            {
                tools.EditCommand.Execute(null);
                FocusName();
            }
        };
    }

    private AdminToolsViewModel? Tools => DataContext as AdminToolsViewModel;

    /// <summary>Меню выбора значка: «Автоматически», встроенные значки, «Загрузить свой…»; выбранный отмечен.</summary>
    internal static MenuFlyout CreateIconMenu(AdminToolsViewModel tools)
    {
        var menu = new MenuFlyout();
        foreach (var choice in tools.IconChoices)
        {
            menu.Items.Add(new MenuItem
            {
                Header = choice.Title,
                Icon = new AdminToolIconView { Icon = choice.Preview },
                Command = tools.ChooseIconCommand,
                CommandParameter = choice.Icon,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = choice.Icon == tools.NewIcon,
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Загрузить свой…",
            Command = tools.ImportIconCommand,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = Core.Settings.AdminToolIcon.FileName(tools.NewIcon) is not null,
        });
        return menu;
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (Tools is not { } tools)
        {
            return;
        }

        if (e.Key == Key.Enter && tools.ApplyCommand.CanExecute(null))
        {
            tools.ApplyCommand.Execute(null);
            NewNameBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && tools.IsEditing)
        {
            tools.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusName()
    {
        NewNameBox.Focus();
        NewNameBox.SelectAll();
    }
}
