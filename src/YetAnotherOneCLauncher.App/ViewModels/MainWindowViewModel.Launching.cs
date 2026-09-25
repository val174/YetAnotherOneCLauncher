using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Разовые параметры из «Запустить с параметрами…».</summary>
/// <param name="Parameters">Дописываются после сохранённых.</param>
/// <param name="UserName">Пользователь; пусто — без <c>/N</c>.</param>
/// <param name="Password">Пароль; пусто — сохранённый, если пользователь тот же.</param>
/// <param name="ClientOverride">Клиент вместо указанного у базы.</param>
internal sealed record OneOffLaunch(string Parameters, string UserName, string Password, ClientApp? ClientOverride)
{
    // Сгенерированный ToString записи вывел бы пароль.
    public override string ToString() => nameof(OneOffLaunch);
}

/// <summary>Параметры запуска и учётные данные.</summary>
public sealed partial class MainWindowViewModel
{
    private const string ParametersTitle = "Параметры запуска";

    /// <summary>Запрос запуска: параметры папок и базы, пользователь и пароль из хранилища ОС.</summary>
    /// <returns>Запрос и предупреждение, если пароль прочитать не удалось.</returns>
    private (LaunchRequest Request, string? Warning) BuildRequest(InfoBaseViewModel target, LaunchMode mode, OneOffLaunch? oneOff)
    {
        var infoBase = target.InfoBase;
        var profile = _settings.UserData.LaunchProfile(infoBase);
        var fragments = _settings.UserData.ParameterChain(infoBase).ToList();
        if (!string.IsNullOrWhiteSpace(oneOff?.Parameters))
        {
            fragments.Add(oneOff.Parameters.Trim());
        }

        var userName = oneOff is null ? profile?.UserName : NullIfBlank(oneOff.UserName);
        string? password = null;
        string? warning = null;
        if (!string.IsNullOrEmpty(oneOff?.Password))
        {
            password = oneOff.Password;
        }
        else if (profile?.PasswordKey is { } key && userName is not null
                 && string.Equals(userName, profile.UserName, StringComparison.Ordinal))
        {
            (password, warning) = ReadPassword(key);
        }

        var request = new LaunchRequest(infoBase, mode)
        {
            PlatformVersionOverride = target.PlatformVersionOverride,
            ParameterFragments = fragments,
            UserName = userName,
            Password = password,
            ClientOverride = oneOff?.ClientOverride,
        };
        return (request, warning);
    }

    private (string? Password, string? Warning) ReadPassword(string key)
    {
        if (_credentials is null || _credentials.UnavailableReason is { } reason)
        {
            return (null, "Сохранённый пароль недоступен: " + (_credentials?.UnavailableReason ?? "хранилище паролей не поддерживается."));
        }

        try
        {
            return _credentials.Read(key) is { } password
                ? (password, null)
                : (null, "Сохранённый пароль не найден в хранилище — 1С спросит его при входе.");
        }
        catch (CredentialStoreException ex)
        {
            LogCredentialFailed(_logger, ex);
            return (null, ex.Message);
        }
    }

    /// <summary>Запуск с разовыми параметрами, пользователем или клиентом (Ctrl+Shift+Enter).</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchWithParametersAsync(InfoBaseViewModel? target)
    {
        target ??= SelectedInfoBase;
        if (target is null)
        {
            return;
        }

        var profile = _settings.UserData.LaunchProfile(target.InfoBase);
        var editor = new LaunchParametersViewModel(
            LaunchParametersKind.OneOff,
            target.Name,
            _settings.UserData.ParameterTemplates(),
            [target.InfoBase.AdditionalParameters ?? string.Empty, .. _settings.UserData.ParameterChain(target.InfoBase)],
            _files)
        {
            UserName = profile?.UserName ?? string.Empty,
            HasSavedPassword = profile?.PasswordKey is not null,
        };

        if (!await _dialogs.EditLaunchParametersAsync(editor) || editor.Mode is not { } mode)
        {
            return;
        }

        await LaunchAsync(target, mode, new OneOffLaunch(editor.Parameters, editor.UserName, editor.Password, editor.ClientOverride));
    }

    /// <summary>Параметры запуска выделенной базы (пользователь, пароль) или папки.</summary>
    [RelayCommand(CanExecute = nameof(CanEditLaunchSettings))]
    private async Task EditLaunchSettingsAsync()
    {
        if (SelectedInfoBase is { } target)
        {
            await EditInfoBaseLaunchSettingsAsync(target);
        }
        else if (SelectedFolder is { Kind: FolderKind.Regular } folder)
        {
            await EditFolderLaunchSettingsAsync(folder);
        }
    }

    private bool CanEditLaunchSettings() => SelectedInfoBase is not null || SelectedFolder is { Kind: FolderKind.Regular };

    private async Task EditInfoBaseLaunchSettingsAsync(InfoBaseViewModel target)
    {
        var infoBase = target.InfoBase;
        var profile = _settings.UserData.LaunchProfile(infoBase) ?? new InfoBaseLaunchProfile();
        var folderChain = FolderParameterChain(infoBase.FolderPath);
        var editor = new LaunchParametersViewModel(
            LaunchParametersKind.InfoBase,
            target.Name,
            _settings.UserData.ParameterTemplates(),
            [infoBase.AdditionalParameters ?? string.Empty, .. folderChain],
            _files)
        {
            Parameters = profile.Parameters ?? string.Empty,
            UserName = profile.UserName ?? string.Empty,
            HasSavedPassword = profile.PasswordKey is not null,
            SavePassword = profile.PasswordKey is not null,
            SavePasswordUnavailableReason = _credentials is null ? "Хранилище паролей не поддерживается." : _credentials.UnavailableReason,
        };

        if (!await _dialogs.EditLaunchParametersAsync(editor))
        {
            return;
        }

        var userName = NullIfBlank(editor.UserName);
        var (passwordKey, message) = await UpdateSavedPasswordAsync(target.Name, profile, userName, editor);
        _settings.UserData.SetLaunchProfile(infoBase, profile with
        {
            Parameters = editor.Parameters,
            UserName = userName,
            PasswordKey = passwordKey,
        });
        _settings.RequestSave();
        target.Refresh();
        StatusText = message ?? $"Параметры запуска «{target.Name}» сохранены.";
    }

    /// <summary>Сохраняет, заменяет или удаляет пароль в хранилище ОС.</summary>
    /// <returns>Ключ пароля для профиля и сообщение о том, что пошло не так.</returns>
    private async Task<(string? PasswordKey, string? Message)> UpdateSavedPasswordAsync(
        string baseName,
        InfoBaseLaunchProfile profile,
        string? userName,
        LaunchParametersViewModel editor)
    {
        var key = profile.PasswordKey;
        var userChanged = !string.Equals(userName, profile.UserName, StringComparison.Ordinal);
        try
        {
            if (editor.SavePassword && userName is not null && editor.Password.Length > 0)
            {
                key ??= Guid.NewGuid().ToString("N");
                _credentials!.Write(key, $"1С: {baseName} ({userName})", userName, editor.Password);
                return (key, null);
            }

            // Сохранение выключили, или сменили пользователя, не введя новый пароль: старый пароль — чужой.
            if (key is not null && (!editor.SavePassword || userName is null || userChanged))
            {
                _credentials?.Delete(key);
                var message = editor.SavePassword && userChanged
                    ? $"Пользователь «{baseName}» изменён — сохранённый пароль удалён, введите пароль нового пользователя."
                    : null;
                return (null, message);
            }

            return (key, null);
        }
        catch (CredentialStoreException ex)
        {
            LogCredentialFailed(_logger, ex);
            await _dialogs.ShowMessageAsync(ParametersTitle, ex.Message);
            return (profile.PasswordKey, "Пароль не сохранён: " + ex.Message);
        }
    }

    private async Task EditFolderLaunchSettingsAsync(FolderNodeViewModel folder)
    {
        var editor = new LaunchParametersViewModel(
            LaunchParametersKind.Folder,
            folder.Path,
            _settings.UserData.ParameterTemplates(),
            FolderParameterChain(ParentPath(folder.Path)),
            _files)
        {
            Parameters = _settings.UserData.FolderParameters(folder.Path) ?? string.Empty,
        };

        if (!await _dialogs.EditLaunchParametersAsync(editor))
        {
            return;
        }

        _settings.UserData.SetFolderParameters(folder.Path, editor.Parameters);
        _settings.RequestSave();
        RefreshBases();
        StatusText = string.IsNullOrWhiteSpace(editor.Parameters)
            ? $"Параметры папки «{folder.Name}» убраны."
            : $"Параметры папки «{folder.Name}» сохранены.";
    }

    /// <summary>Свои шаблоны параметров: по строке «Название = параметры».</summary>
    [RelayCommand]
    private async Task EditParameterTemplatesAsync()
    {
        var text = string.Join(
            Environment.NewLine,
            _settings.Settings.ParameterTemplates.Select(t => $"{t.Name} = {t.Text}"));
        var edited = await _dialogs.EditTextAsync(
            "Свои шаблоны параметров",
            "Каждая строка — шаблон: «Название = параметры», например «Тестовый вход = /N Тест /DisableStartupMessages». Сохранить — Ctrl+Enter.",
            text);
        if (edited is null)
        {
            return;
        }

        _settings.Settings.ParameterTemplates = ParseTemplates(edited);
        _settings.RequestSave();
        StatusText = $"Своих шаблонов параметров: {_settings.Settings.ParameterTemplates.Count}.";
    }

    internal static List<ParameterTemplate> ParseTemplates(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                var separator = line.IndexOf('=', StringComparison.Ordinal);
                var name = separator > 0 ? line[..separator].Trim() : line;
                var parameters = separator > 0 ? line[(separator + 1)..].Trim() : line;
                return new ParameterTemplate { Name = name.Length > 0 ? name : parameters, Text = parameters };
            })
            .Where(t => t.Text.Length > 0)
            .ToList();

    /// <summary>Параметры папок от корня до <paramref name="folderPath"/> включительно.</summary>
    private List<string> FolderParameterChain(string folderPath)
    {
        var result = new List<string>();
        var path = FolderPaths.Root;
        foreach (var segment in FolderPaths.Split(folderPath))
        {
            path = FolderPaths.Combine(path, segment);
            if (_settings.UserData.FolderParameters(path) is { } parameters)
            {
                result.Add(parameters);
            }
        }

        return result;
    }

    /// <summary>Папку переименовали или перенесли — её параметры переходят вместе с ней.</summary>
    private void MoveFolderParameters(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.UserData.MoveFolderParameters(oldPath, newPath);
        _settings.RequestSave();
        RefreshBases();
    }

    private void RefreshBases()
    {
        foreach (var infoBase in _bases)
        {
            infoBase.Refresh();
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Хранилище паролей")]
    private static partial void LogCredentialFailed(ILogger logger, Exception exception);
}
