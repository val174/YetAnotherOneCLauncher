using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
            if (e.Source is Control { DataContext: GroupNodeViewModel } && picker.HasSelection)
            {
                Close(true);
            }
        };
        // Щелчок по пустому месту снимает выделение: новая группа тогда создаётся в корне списка.
        GroupsTree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is not Visual source || source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is null)
            {
                picker.SelectedGroup = null;
            }
        }, handledEventsToo: true);
        Opened += (_, _) =>
        {
            if (picker.SelectedGroup is { } selected && GroupsTree.TreeContainerFromItem(selected) is { } item)
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
