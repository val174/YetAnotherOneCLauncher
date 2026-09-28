using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace YetAnotherOneCLauncher.App;

/// <summary>Простое модальное окно с вопросом или сообщением. Во Avalonia стандартного MessageBox нет.</summary>
internal sealed class MessageDialog : Window
{
    private readonly CheckBox? _option;

    private MessageDialog(string title, string text, string acceptText, string? cancelText, string? optionText = null, bool optionChecked = false)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var accept = new Button { Content = acceptText, IsDefault = true, MinWidth = 90 };
        accept.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { accept },
        };

        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 90 };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children = { new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
        };
        if (optionText is not null)
        {
            _option = new CheckBox
            {
                Name = "OptionCheckBox",
                IsChecked = optionChecked,
                Content = new TextBlock { Text = optionText, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            };
            content.Children.Add(_option);
        }

        content.Children.Add(buttons);
        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
    }

    public static Task<bool> AskAsync(Window owner, string title, string question, string acceptText) =>
        new MessageDialog(title, question, acceptText, "Отмена").ShowDialog<bool>(owner);

    public static Task ShowAsync(Window owner, string title, string text) =>
        new MessageDialog(title, text, "OK", cancelText: null).ShowDialog<bool>(owner);

    /// <summary>Вопрос с флажком; возвращает ответ и состояние флажка.</summary>
    public static async Task<(bool Accepted, bool Option)> AskWithOptionAsync(
        Window owner, string title, string question, string acceptText, string optionText, bool optionChecked)
    {
        var dialog = new MessageDialog(title, question, acceptText, "Отмена", optionText, optionChecked);
        var accepted = await dialog.ShowDialog<bool>(owner);
        return (accepted, dialog._option?.IsChecked == true);
    }
}
