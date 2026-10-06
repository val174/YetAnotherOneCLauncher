using Avalonia.Controls;
using Avalonia.Input;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Выбор группы» из формы базы; закрывается с <c>true</c>, если группа выбрана.</summary>
public partial class GroupPickerWindow : Window
{
    // Нужен дизайнеру XAML.
    public GroupPickerWindow()
        : this(new GroupPickerViewModel(["/Рабочие/Отчёты", "/Архив"], "/Рабочие"))
    {
    }

    public GroupPickerWindow(GroupPickerViewModel picker)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = picker;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        // Двойной щелчок по группе — выбрать её.
        GroupsTree.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control { DataContext: GroupNodeViewModel } && picker.SelectedGroup is not null)
            {
                Close(true);
            }
        };
        Opened += (_, _) =>
        {
            if (GroupsTree.TreeContainerFromItem(picker.SelectedGroup!) is { } item)
            {
                item.Focus(NavigationMethod.Directional);
            }
            else
            {
                GroupsTree.Focus();
            }
        };
    }
}
