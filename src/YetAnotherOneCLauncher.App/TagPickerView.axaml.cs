using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>Выбор метки во всплывающем окне: Enter в поле ставит введённую метку.</summary>
public partial class TagPickerView : UserControl
{
    public TagPickerView()
    {
        InitializeComponent();
        FilterBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is TagPickerViewModel picker)
            {
                picker.PickCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>Открыли всплывающее окно: список заново, курсор — в поле.</summary>
    public void Activate()
    {
        (DataContext as TagPickerViewModel)?.Refresh();
        Dispatcher.UIThread.Post(() => FilterBox.Focus());
    }
}
