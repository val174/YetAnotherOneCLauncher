using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Вопросы и сообщения пользователю.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string question, string acceptText);

    Task ShowMessageAsync(string title, string text);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>Управление главным окном из ViewModel.</summary>
public interface IWindowService
{
    void Minimize();

    void Close();
}

public interface IThemeService
{
    void Apply(ThemeMode mode);
}

/// <summary>Реализации для настольного приложения Avalonia: работают с текущим главным окном.</summary>
public sealed class DesktopUiServices : IDialogService, IClipboardService, IWindowService, IThemeService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public Task<bool> ConfirmAsync(string title, string question, string acceptText) =>
        MainWindow is { } owner ? MessageDialog.AskAsync(owner, title, question, acceptText) : Task.FromResult(false);

    public Task ShowMessageAsync(string title, string text) =>
        MainWindow is { } owner ? MessageDialog.ShowAsync(owner, title, text) : Task.CompletedTask;

    public async Task SetTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(MainWindow)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public void Minimize()
    {
        if (MainWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    public void Close() => MainWindow?.Close();

    public void Apply(ThemeMode mode)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
