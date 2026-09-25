using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace YetAnotherOneCLauncher.App;

/// <summary>Ввод строки или многострочного текста. Возвращает <c>null</c> при отмене.</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _input;

    private InputDialog(string title, string label, string text, bool multiline)
    {
        Title = title;
        Width = multiline ? 640 : 420;
        SizeToContent = SizeToContent.Height;
        CanResize = multiline;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = new TextBox
        {
            Text = text,
            AcceptsReturn = multiline,
            TextWrapping = TextWrapping.NoWrap,
            Height = multiline ? 320 : double.NaN,
            FontFamily = multiline ? new FontFamily("Consolas, DejaVu Sans Mono, monospace") : FontFamily.Default,
        };

        var ok = new Button { Content = "Сохранить", IsDefault = !multiline, MinWidth = 100, Classes = { "accent" } };
        ok.Click += (_, _) => Close(_input.Text ?? string.Empty);
        var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };

        Opened += (_, _) =>
        {
            _input.Focus();
            if (!multiline)
            {
                _input.SelectAll();
            }
        };
        KeyDown += (_, e) =>
        {
            // В многострочном режиме Enter переносит строку; сохранить — Ctrl+Enter.
            if (multiline && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            {
                Close(_input.Text ?? string.Empty);
                e.Handled = true;
            }
        };
    }

    public static Task<string?> PromptAsync(Window owner, string title, string label, string text) =>
        new InputDialog(title, label, text, multiline: false).ShowDialog<string?>(owner);

    public static Task<string?> EditTextAsync(Window owner, string title, string label, string text) =>
        new InputDialog(title, label, text, multiline: true).ShowDialog<string?>(owner);
}
