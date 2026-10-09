using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Разовые параметры из «Запустить с параметрами…».</summary>
/// <param name="Parameters">Дописываются после сохранённых.</param>
/// <param name="UserName">Пользователь; пусто — без <c>/N</c>.</param>
/// <param name="Password">Пароль; пусто — сохранённый, если пользователь тот же.</param>
/// <param name="ClientOverride">Клиент вместо указанного у базы.</param>
/// <param name="Platform">Платформа для этого запуска; <c>null</c> — как сохранено для базы.</param>
internal sealed record OneOffLaunch(string Parameters, string UserName, string Password, ClientApp? ClientOverride, PlatformChoice? Platform = null)
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
            // Выбор в «Запустить с параметрами» важнее сохранённого; «Как в списке баз» — без замены версии.
            PlatformVersionOverride = oneOff?.Platform is { } platform ? platform.Version : target.PlatformVersionOverride,
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

    /// <summary>Запуск с разовыми параметрами, пользователем, клиентом или платформой (F6): режим выбирается в окне.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchWithParametersAsync(InfoBaseViewModel? target) => LaunchWithParametersAsync(target, presetMode: null);

    /// <summary>«Запустить с параметрами» у кнопки «1С: Предприятие»: в окне вместо выбора режима — «Продолжить».</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchEnterpriseWithParametersAsync(InfoBaseViewModel? target) => LaunchWithParametersAsync(target, LaunchMode.Enterprise);

    /// <summary>«Запустить с параметрами» у кнопки «Конфигуратор»: в окне вместо выбора режима — «Продолжить».</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchDesignerWithParametersAsync(InfoBaseViewModel? target) => LaunchWithParametersAsync(target, LaunchMode.Designer);

    private async Task LaunchWithParametersAsync(InfoBaseViewModel? target, LaunchMode? presetMode)
    {
        target ??= SelectedInfoBase;
        if (target is null)
        {
            return;
        }

        var choices = PlatformChoicesFor(target);
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
            PlatformChoices = choices,
            SelectedPlatformChoice = choices.Find(c => c.Version == target.PlatformVersionOverride) ?? choices[0],
            PresetMode = presetMode,
        };

        if (!await _dialogs.EditLaunchParametersAsync(editor) || editor.Mode is not { } mode)
        {
            return;
        }

        if (editor.RememberPlatform)
        {
            _settings.UserData.SetPlatformVersionOverride(target.InfoBase, editor.SelectedPlatformChoice?.Version);
            _settings.RequestSave();
            target.Refresh();
            UpdatePlatformColumn([target]);
        }

        await LaunchAsync(target, mode, new OneOffLaunch(editor.Parameters, editor.UserName, editor.Password, editor.ClientOverride, editor.SelectedPlatformChoice));
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

        StatusText = await SaveLaunchProfileAsync(target, editor, editor.Parameters) ?? $"Параметры запуска «{target.Name}» сохранены.";
    }

    /// <summary>Пользователь и пароль из окна параметров — в профиль базы и хранилище ОС.</summary>
    /// <param name="target">База.</param>
    /// <param name="form">Подтверждённое окно параметров.</param>
    /// <param name="parameters">Параметры лаунчера для базы (из формы базы они не меняются).</param>
    /// <returns>Сообщение, если с паролем что-то пошло не так.</returns>
    private async Task<string?> SaveLaunchProfileAsync(InfoBaseViewModel target, LaunchParametersViewModel form, string? parameters)
    {
        var profile = _settings.UserData.LaunchProfile(target.InfoBase) ?? new InfoBaseLaunchProfile();
        var userName = NullIfBlank(form.UserName);
        var (passwordKey, message) = await UpdateSavedPasswordAsync(target.Name, profile, userName, form);
        _settings.UserData.SetLaunchProfile(target.InfoBase, profile with
        {
            Parameters = parameters,
            UserName = userName,
            PasswordKey = passwordKey,
        });
        _settings.RequestSave();
        target.Refresh();
        return message;
    }

    /// <summary>Форма базы: кнопка «…» у дополнительных параметров открывает окно «Параметры запуска».</summary>
    /// <param name="templates">Для новой базы — найденные шаблоны; тогда в форме можно и создать базу.</param>
    private InfoBaseEditorViewModel CreateBaseEditor(
        InfoBaseDraft draft, bool isNew, InfoBaseViewModel? existing, IReadOnlyList<ConfigurationTemplate>? templates = null) =>
        new(draft, AllFolderPaths(), isNew, _files)
        {
            PlatformVersions = PlatformVersionChoices(),
            LaunchParametersEditor = editor => EditListEntryParametersAsync(editor, existing),
            GroupChooser = _dialogs.ChooseGroupAsync,
            GroupNamePrompt = parent => _dialogs.PromptAsync(
                "Новая группа", parent == FolderPaths.Root ? "Имя группы:" : $"Имя группы внутри «{parent.TrimStart('/')}»:", string.Empty),
            Creator = isNew && templates is not null ? CreateInfoBaseAsync : null,
            // Названия других баз: совпадать с ними название не должно (у изменяемой — кроме её самой).
            ExistingNames = OtherBaseNames(existing),
            CreationPlatforms = isNew ? CreationPlatforms() : [],
            FoundTemplates = templates ?? [],
            // Проект 1C:EDT: связь из профиля базы и подсказка — проект, с которым базу связал сам EDT.
            EdtProjects = EdtProjectChoices,
            EdtProjectChooser = names => _dialogs.ChooseAsync("Проект 1C:EDT", "Выберите проект 1C:EDT для этой базы:", names),
            InitialEdtProjectId = existing is null ? null : EdtProjectOf(existing)?.Id,
            SuggestedEdtProject = SuggestedEdtProject(existing?.InfoBase),
        };

    /// <summary>Ветки установленных платформ («8.3», «8.5») и сами версии — от новых к старым.</summary>
    private List<string> PlatformVersionChoices()
    {
        var versions = _installations.Select(i => i.Version).Distinct().OrderDescending().ToList();
        return
        [
            .. versions.Select(v => $"{v.Major}.{v.Minor}").Distinct(),
            .. versions.Select(v => v.ToString()),
        ];
    }

    /// <summary>
    /// То же окно, что «Параметры запуска…» в главном окне. Параметры возвращаются в поле формы (ibases.v8i),
    /// пользователь и пароль ждут сохранения формы.
    /// </summary>
    private async Task EditListEntryParametersAsync(InfoBaseEditorViewModel editor, InfoBaseViewModel? existing)
    {
        var profile = existing is null ? null : _settings.UserData.LaunchProfile(existing.InfoBase);
        var pending = editor.LaunchSettings;
        var form = new LaunchParametersViewModel(
            LaunchParametersKind.ListEntry,
            string.IsNullOrWhiteSpace(editor.Name) ? "новая база" : editor.Name.Trim(),
            _settings.UserData.ParameterTemplates(),
            [],
            _files)
        {
            Parameters = editor.AdditionalParameters,
            UserName = pending?.UserName ?? profile?.UserName ?? string.Empty,
            Password = pending?.Password ?? string.Empty,
            HasSavedPassword = profile?.PasswordKey is not null,
            SavePassword = pending?.SavePassword ?? profile?.PasswordKey is not null,
            SavePasswordUnavailableReason = _credentials is null ? "Хранилище паролей не поддерживается." : _credentials.UnavailableReason,
        };

        if (await _dialogs.EditLaunchParametersAsync(form))
        {
            editor.AdditionalParameters = form.Parameters;
            editor.LaunchSettings = form;
        }
    }

    /// <summary>После сохранения формы — пользователь и пароль, заданные в окне параметров.</summary>
    private async Task ApplyPendingLaunchSettingsAsync(InfoBaseEditorViewModel editor, string? selectionKey)
    {
        // Связь с проектом 1C:EDT — в профиле сохранённой базы.
        if (editor.IsEdtProjectChanged && _bases.FirstOrDefault(b => b.InfoBase.IdentityKey == selectionKey) is { } linked)
        {
            SetEdtProject(linked, editor.EdtProject);
        }

        if (editor.LaunchSettings is not { } form
            || _bases.FirstOrDefault(b => b.InfoBase.IdentityKey == selectionKey) is not { } saved)
        {
            return;
        }

        var parameters = _settings.UserData.LaunchProfile(saved.InfoBase)?.Parameters;
        if (await SaveLaunchProfileAsync(saved, form, parameters) is { } message)
        {
            StatusText = message;
        }
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

    /// <summary>
    /// Колонка «Платформа»: какую версию выберет запуск в режиме 1С: Предприятие — тот же планировщик, что и при запуске.
    /// </summary>
    private void UpdatePlatformColumn(IEnumerable<InfoBaseViewModel> bases)
    {
        var options = _settings.Settings.Launch.ToLaunchOptions();
        foreach (var target in bases)
        {
            var request = new LaunchRequest(target.InfoBase, LaunchMode.Enterprise) { PlatformVersionOverride = target.PlatformVersionOverride };
            switch (LaunchPlanner.Plan(request, _installations, _starterDefaultVersion, options))
            {
                case LaunchPlan.Run run:
                    target.SetPlatform(run.Command.Platform.Version.ToString(), PlatformSource(target, run.Command), missing: false);
                    break;

                case LaunchPlan.ConfirmFallback fallback:
                    var mask = target.PlatformVersionOverride ?? target.InfoBase.Version ?? _starterDefaultVersion;
                    target.SetPlatform($"{mask} — нет", fallback.Question, missing: true);
                    break;

                case LaunchPlan.OpenInBrowser:
                    target.SetPlatform("браузер", "Веб-клиент открывается в браузере", missing: false);
                    break;

                case LaunchPlan.Failed failed:
                    target.SetPlatform("—", failed.Message, missing: true);
                    break;
            }
        }
    }

    private string PlatformSource(InfoBaseViewModel target, LaunchCommand command)
    {
        var client = Path.GetFileNameWithoutExtension(command.ExecutablePath);
        var source = target.PlatformVersionOverride is { } chosen ? $"выбрана в лаунчере ({chosen})"
            : target.InfoBase.Version is { } version ? $"по версии в списке баз ({version})"
            : _starterDefaultVersion is { } starter ? $"по версии по умолчанию из 1cestart.cfg ({starter})"
            : "самая новая из установленных";
        return $"{command.Platform} {client}: {source}";
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Хранилище паролей")]
    private static partial void LogCredentialFailed(ILogger logger, Exception exception);
}
