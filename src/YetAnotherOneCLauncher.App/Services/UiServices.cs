using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Вопросы и сообщения пользователю.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string question, string acceptText);

    Task ShowMessageAsync(string title, string text);

    /// <summary>Однострочный ввод (например, имя папки); <c>null</c> — отмена.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText);

    /// <summary>Многострочный текст (запись .v8i вручную); <c>null</c> — отмена.</summary>
    Task<string?> EditTextAsync(string title, string hint, string text);

    /// <summary>Форма базы; <c>true</c> — пользователь нажал «Сохранить» и данные прошли проверку.</summary>
    Task<bool> EditInfoBaseAsync(InfoBaseEditorViewModel editor);

    /// <summary>Параметры запуска; <c>true</c> — пользователь подтвердил, данные прошли проверку.</summary>
    Task<bool> EditLaunchParametersAsync(LaunchParametersViewModel parameters);

    /// <summary>Окно «Кэш баз»; закрывается пользователем.</summary>
    Task ShowCacheManagerAsync(CacheManagerViewModel cache);
}

/// <summary>Выбор файлов и каталогов.</summary>
public interface IFileDialogService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> OpenListFileAsync(string title);

    /// <summary>Выбор файла по маскам, например <c>*.epf</c>.</summary>
    Task<string?> OpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns);

    Task<string?> SaveListFileAsync(string title, string suggestedName);
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
public sealed class DesktopUiServices : IDialogService, IFileDialogService, IClipboardService, IWindowService, IThemeService
{
    private static readonly FilePickerFileType ListFileType = new("Список информационных баз 1С")
    {
        Patterns = ["*.v8i"],
    };

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Владелец диалога — активное окно: из формы базы окно параметров открывается поверх формы.</summary>
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.LastOrDefault(w => w.IsActive)
        ?? MainWindow;

    public Task<bool> ConfirmAsync(string title, string question, string acceptText) =>
        Owner is { } owner ? MessageDialog.AskAsync(owner, title, question, acceptText) : Task.FromResult(false);

    public Task ShowMessageAsync(string title, string text) =>
        Owner is { } owner ? MessageDialog.ShowAsync(owner, title, text) : Task.CompletedTask;

    public Task<string?> PromptAsync(string title, string label, string initialText) =>
        Owner is { } owner ? InputDialog.PromptAsync(owner, title, label, initialText) : Task.FromResult<string?>(null);

    public Task<string?> EditTextAsync(string title, string hint, string text) =>
        Owner is { } owner ? InputDialog.EditTextAsync(owner, title, hint, text) : Task.FromResult<string?>(null);

    public Task<bool> EditInfoBaseAsync(InfoBaseEditorViewModel editor) =>
        Owner is { } owner ? new InfoBaseEditorWindow(editor).ShowDialog<bool>(owner) : Task.FromResult(false);

    public Task<bool> EditLaunchParametersAsync(LaunchParametersViewModel parameters) =>
        Owner is { } owner ? new LaunchParametersWindow(parameters).ShowDialog<bool>(owner) : Task.FromResult(false);

    public Task ShowCacheManagerAsync(CacheManagerViewModel cache) =>
        Owner is { } owner ? new CacheManagerWindow(cache).ShowDialog(owner) : Task.CompletedTask;

    public async Task<string?> OpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns)
    {
        if (Owner?.StorageProvider is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [.. patterns] }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner?.StorageProvider is not { } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenListFileAsync(string title)
    {
        if (Owner?.StorageProvider is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [ListFileType, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveListFileAsync(string title, string suggestedName)
    {
        if (Owner?.StorageProvider is not { } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "v8i",
            FileTypeChoices = [ListFileType],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

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
