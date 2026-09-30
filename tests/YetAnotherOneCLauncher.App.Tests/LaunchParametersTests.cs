using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Параметры запуска у базы и папки, разовый запуск, пароли в хранилище ОС.</summary>
public class LaunchParametersTests
{
    private const string Buh = "Бухгалтерия предприятия";

    [Fact]
    public async Task Base_settings_save_user_and_password_to_store_not_to_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        SelectBase(fixture, Buh);

        fixture.Dialogs.LaunchParameters = form =>
        {
            form.Parameters = "/DisableStartupMessages";
            form.UserName = "Бухгалтер";
            form.Password = "секрет 1";
            form.SavePassword = true;
            return (true, null);
        };
        await vm.EditLaunchSettingsCommand.ExecuteAsync(null);

        var profile = Assert.Single(fixture.Settings.Settings.InfoBaseProfiles);
        Assert.Equal("Бухгалтер", profile.UserName);
        Assert.NotNull(profile.PasswordKey);
        Assert.Equal(("Бухгалтер", "секрет 1"), fixture.Credentials.Entries[profile.PasswordKey]);
        Assert.Equal("Бухгалтер (пароль сохранён)", fixture.Base(Buh).UserText);
        Assert.Equal("/DisableStartupMessages", fixture.Base(Buh).LaunchParametersText);

        // Пароль не попадает в файл настроек.
        var store = new SettingsStore(Path.Combine(fixture.Directory, "settings"));
        await store.SaveAsync(fixture.Settings.Settings, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("секрет", await File.ReadAllTextAsync(store.FilePath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        await vm.LaunchEnterpriseCommand.ExecuteAsync(null);

        var command = Assert.Single(fixture.Processes.Started);
        Assert.Equal(new[] { "/N", "Бухгалтер", "/P", "секрет 1" }, command.Arguments.TakeLast(4));
        Assert.Equal("/DisableStartupMessages", command.RawArguments);
        Assert.DoesNotContain("секрет", command.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_user_without_new_password_removes_old_password()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        SelectBase(fixture, Buh);
        await SaveProfileAsync(fixture, "Первый", "111");
        var oldKey = fixture.Settings.Settings.InfoBaseProfiles[0].PasswordKey!;

        fixture.Dialogs.LaunchParameters = form =>
        {
            Assert.True(form.HasSavedPassword);
            Assert.Equal(string.Empty, form.Password);
            form.UserName = "Второй";
            return (true, null);
        };
        await fixture.ViewModel.EditLaunchSettingsCommand.ExecuteAsync(null);

        var profile = Assert.Single(fixture.Settings.Settings.InfoBaseProfiles);
        Assert.Equal("Второй", profile.UserName);
        Assert.Null(profile.PasswordKey);
        Assert.False(fixture.Credentials.Entries.ContainsKey(oldKey));
        Assert.Contains("сохранённый пароль удалён", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unchecking_save_password_deletes_it_but_keeps_user()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        SelectBase(fixture, Buh);
        await SaveProfileAsync(fixture, "Первый", "111");

        fixture.Dialogs.LaunchParameters = form =>
        {
            form.SavePassword = false;
            return (true, null);
        };
        await fixture.ViewModel.EditLaunchSettingsCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Credentials.Entries);
        Assert.Equal("Первый", fixture.Settings.Settings.InfoBaseProfiles.Single().UserName);
        Assert.Equal("Первый", fixture.Base(Buh).UserText);
    }

    [Fact]
    public async Task Save_password_is_refused_when_store_is_unavailable()
    {
        using var fixture = new ViewModelFixture();
        fixture.Credentials.UnavailableReason = "Нет secret-tool.";
        await fixture.LoadAsync();
        SelectBase(fixture, Buh);

        LaunchParametersViewModel? shown = null;
        fixture.Dialogs.LaunchParameters = form =>
        {
            shown = form;
            form.UserName = "Первый";
            form.Password = "111";
            form.SavePassword = true;
            return (true, null);
        };
        await fixture.ViewModel.EditLaunchSettingsCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.False(shown.CanSavePassword);
        Assert.Contains("Нет secret-tool.", shown.Errors, StringComparison.Ordinal);
        Assert.Empty(fixture.Settings.Settings.InfoBaseProfiles);
    }

    [Fact]
    public async Task Missing_saved_password_does_not_block_launch()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        SelectBase(fixture, Buh);
        await SaveProfileAsync(fixture, "Первый", "111");
        fixture.Credentials.Entries.Clear(); // запись удалили в диспетчере учётных данных

        await fixture.ViewModel.LaunchEnterpriseCommand.ExecuteAsync(null);

        var command = Assert.Single(fixture.Processes.Started);
        Assert.Equal(new[] { "/N", "Первый" }, command.Arguments.TakeLast(2));
        Assert.Contains("не найден", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Folder_parameters_apply_to_nested_bases_and_follow_rename()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Folder(vm, "Рабочие");

        LaunchParametersViewModel? form = null;
        fixture.Dialogs.LaunchParameters = f =>
        {
            form = f;
            f.SelectedTemplate = f.Templates.Single(t => t.Text == "/UC");
            f.TemplateValue = "42";
            f.InsertTemplateCommand.Execute(null);
            return (true, null);
        };
        await vm.EditLaunchSettingsCommand.ExecuteAsync(null);

        Assert.Equal(LaunchParametersKind.Folder, form!.Kind);
        Assert.False(form.ShowCredentials);
        Assert.Equal("/UC 42", fixture.Settings.UserData.FolderParameters("/Рабочие"));
        Assert.Equal("/UC 42", fixture.Base(Buh).LaunchParametersText);
        Assert.Equal("не заданы", fixture.Base("Копия бухгалтерии").LaunchParametersText);

        fixture.Dialogs.PromptAnswer = "Работа";
        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        await vm.EditCommand.ExecuteAsync(null);

        Assert.Null(fixture.Settings.UserData.FolderParameters("/Рабочие"));
        Assert.Equal("/UC 42", fixture.Settings.UserData.FolderParameters("/Работа"));
        Assert.Equal("/UC 42", fixture.Base(Buh).LaunchParametersText);
    }

    [Fact]
    public async Task One_off_launch_adds_parameters_last_and_can_override_user_and_client()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Settings.UserData.SetFolderParameters("/Рабочие", "/L ru");
        SelectBase(fixture, Buh);
        await SaveProfileAsync(fixture, "Первый", "111", parameters: "/DisableStartupMessages");

        LaunchParametersViewModel? form = null;
        fixture.Dialogs.LaunchParameters = f =>
        {
            form = f;
            f.Parameters = "/ClearCache";
            f.ClientIndex = 2; // толстый клиент
            return (true, LaunchMode.Enterprise);
        };
        await vm.LaunchWithParametersCommand.ExecuteAsync(null);

        Assert.Equal("/L ru /DisableStartupMessages", form!.InheritedText);
        Assert.Equal("Первый", form.UserName);
        var command = Assert.Single(fixture.Processes.Started);
        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal("/L ru /DisableStartupMessages /ClearCache", command.RawArguments);
        Assert.Equal(new[] { "/N", "Первый", "/P", "111" }, command.Arguments.TakeLast(4));

        // Другой пользователь при разовом запуске — сохранённый пароль не подставляется.
        fixture.Dialogs.LaunchParameters = f =>
        {
            f.UserName = "Гость";
            return (true, LaunchMode.Designer);
        };
        await vm.LaunchWithParametersCommand.ExecuteAsync(null);

        var designer = fixture.Processes.Started[1];
        Assert.Equal("DESIGNER", designer.Arguments[0]);
        Assert.Equal(new[] { "/N", "Гость" }, designer.Arguments.TakeLast(2));
        Assert.Equal("Первый", fixture.Settings.Settings.InfoBaseProfiles.Single().UserName); // профиль не изменился
    }

    [Fact]
    public async Task Cancelled_one_off_form_does_not_launch()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        SelectBase(fixture, Buh);

        await fixture.ViewModel.LaunchWithParametersCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Processes.Started);
        Assert.Single(fixture.Dialogs.LaunchParameterForms);
    }

    [Fact]
    public void Password_without_user_is_rejected_and_template_needs_value()
    {
        var form = new LaunchParametersViewModel(LaunchParametersKind.OneOff, "База", ParameterLibrary.BuiltIn, [], new FakeFiles())
        {
            Password = "x",
        };

        Assert.False(form.TryAccept(LaunchMode.Enterprise));
        Assert.Contains("без пользователя", form.Errors, StringComparison.Ordinal);

        form.SelectedTemplate = ParameterLibrary.BuiltIn.Single(t => t.Text == "/Execute");
        Assert.True(form.TemplateNeedsFile);
        form.InsertTemplateCommand.Execute(null);
        Assert.Equal(string.Empty, form.Parameters);
        Assert.Contains("нужно значение", form.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_preselected_and_parameters_can_be_cleared()
    {
        var form = new LaunchParametersViewModel(LaunchParametersKind.InfoBase, "База", ParameterLibrary.BuiltIn, [], new FakeFiles())
        {
            Parameters = "/UC 42",
        };

        Assert.Null(form.SelectedTemplate);
        Assert.Equal(string.Empty, form.TemplateDescription);
        Assert.False(form.InsertTemplateCommand.CanExecute(null));
        Assert.True(form.HasParameters);

        form.ClearParametersCommand.Execute(null);
        Assert.Equal(string.Empty, form.Parameters);
        Assert.False(form.HasParameters);

        form.SelectedTemplate = ParameterLibrary.BuiltIn[0];
        Assert.True(form.InsertTemplateCommand.CanExecute(null));
    }

    [Fact]
    public void Own_parameters_are_added_without_slash_and_only_they_can_be_deleted()
    {
        // Старый свой шаблон (с «/», без описания): «/» не показывается, в колонке «Описание» — его название.
        var form = new ParameterTemplatesViewModel([new ParameterTemplate { Name = "Тестовый вход", Text = "/N Тест" }]);
        Assert.Equal(1 + ParameterLibrary.BuiltIn.Count, form.Rows.Count);
        Assert.Equal(("N Тест", "Тестовый вход", true), (form.Rows[0].Parameter, form.Rows[0].Description, form.Rows[0].IsCustom));
        Assert.DoesNotContain(form.Rows, r => r.Parameter.StartsWith('/')); // и у встроенных
        Assert.False(form.ApplyCommand.CanExecute(null)); // без параметра добавлять нечего
        Assert.Equal(("Новый параметр", "Добавить"), (form.FormTitle, form.ApplyText));

        form.NewParameter = "  /L en "; // «/» по привычке — отбрасывается
        form.NewDescription = "Английский интерфейс";
        form.ApplyCommand.Execute(null);
        var added = form.Rows[1]; // после своих, перед встроенными
        Assert.Equal(("L en", "Английский интерфейс", true), (added.Parameter, added.Description, added.IsCustom));
        Assert.Equal("L en", added.Template.Text); // хранится без «/»
        Assert.Equal("Английский интерфейс", added.Template.Name); // так он называется в списке шаблонов
        Assert.Same(added, form.SelectedRow);
        Assert.Equal((string.Empty, string.Empty), (form.NewParameter, form.NewDescription));

        form.NewParameter = "clearcache"; // уже есть среди встроенных
        form.ApplyCommand.Execute(null);
        Assert.Contains("встроенных", form.ErrorText, StringComparison.Ordinal);
        Assert.False(form.SelectedRow!.IsCustom);
        Assert.False(form.DeleteCommand.CanExecute(null)); // встроенный не удаляется и не изменяется
        Assert.False(form.EditCommand.CanExecute(null));

        form.SelectedRow = form.Rows[0];
        form.DeleteCommand.Execute(null);
        Assert.Equal(["L en"], form.CustomTemplates.Select(t => t.Text));
        Assert.Same(form.Rows[0], form.SelectedRow); // выделение — на оставшийся свой
    }

    [Fact]
    public void Own_parameter_is_edited_in_place()
    {
        var form = new ParameterTemplatesViewModel(
        [
            ParameterLibrary.Custom("N Тест", "Тестовый вход"),
            ParameterLibrary.Custom("L en", "Английский интерфейс"),
        ]);
        form.SelectedRow = form.Rows[0];
        form.EditCommand.Execute(null);
        Assert.Equal(("N Тест", "Тестовый вход"), (form.NewParameter, form.NewDescription));
        Assert.Equal(("Изменение параметра", "Применить"), (form.FormTitle, form.ApplyText));
        Assert.False(form.DeleteCommand.CanExecute(null)); // изменяемый не удаляется

        form.NewParameter = "L en"; // занят другим своим
        form.ApplyCommand.Execute(null);
        Assert.Contains("уже добавлен", form.ErrorText, StringComparison.Ordinal);
        Assert.True(form.IsEditing);

        form.NewParameter = "N Тест"; // тот же параметр — не повтор
        form.NewDescription = "Вход тестировщика";
        form.ApplyCommand.Execute(null);
        Assert.False(form.IsEditing);
        Assert.Equal(("N Тест", "Вход тестировщика"), (form.Rows[0].Parameter, form.Rows[0].Description)); // на прежнем месте
        Assert.Equal(["N Тест", "L en"], form.CustomTemplates.Select(t => t.Text));

        form.SelectedRow = form.Rows[1];
        form.EditCommand.Execute(null);
        form.NewDescription = "не то";
        form.CancelEditCommand.Execute(null);
        Assert.False(form.IsEditing);
        Assert.Equal("Английский интерфейс", form.Rows[1].Description);
        Assert.Equal((string.Empty, string.Empty), (form.NewParameter, form.NewDescription));
    }

    [Fact]
    public void Slash_is_added_when_parameter_is_chosen_for_launch()
    {
        var own = ParameterLibrary.Custom("N Тест", "Тестовый вход");
        Assert.Equal("/N Тест", own.Format(null));
        Assert.Equal("/UC 123", ParameterLibrary.BuiltIn.Single(t => t.Text == "/UC").Format("123"));
        Assert.Equal("/N", ParameterLibrary.Custom("N", null).Name); // без описания в списке шаблонов — с «/»

        var form = new LaunchParametersViewModel(LaunchParametersKind.OneOff, "База", [own], [], new FakeFiles()) { SelectedTemplate = own };
        Assert.StartsWith("/N Тест — ", form.TemplateDescription, StringComparison.Ordinal);
        form.InsertTemplateCommand.Execute(null);
        Assert.Equal("/N Тест", form.Parameters);
    }

    [Fact]
    public async Task Own_templates_are_saved_only_on_save()
    {
        using var fixture = new ViewModelFixture();
        fixture.Dialogs.SettingsEditor = settings =>
        {
            settings.Templates.NewParameter = "/N Тест";
            settings.Templates.NewDescription = "Тестовый вход";
            settings.Templates.ApplyCommand.Execute(null);
            Assert.Equal(("Шаблоны параметров*", "Настройки*"), (settings.TemplatesHeader, settings.Title));
            return false; // «Отменить»
        };
        await fixture.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Settings.Settings.ParameterTemplates);

        fixture.Dialogs.SettingsEditor = settings =>
        {
            settings.Templates.NewParameter = "/N Тест";
            settings.Templates.NewDescription = "Тестовый вход";
            settings.Templates.ApplyCommand.Execute(null);
            return true; // «Сохранить»
        };
        await fixture.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        var saved = Assert.Single(fixture.Settings.Settings.ParameterTemplates);
        Assert.Equal(("Тестовый вход", "N Тест", "Тестовый вход"), (saved.Name, saved.Text, saved.Description));
        Assert.Equal(saved, fixture.Settings.UserData.ParameterTemplates()[0]); // первым — в списке шаблонов окна параметров
    }

    private static void SelectBase(ViewModelFixture fixture, string name)
    {
        fixture.ViewModel.SelectedTreeItem = Folder(fixture.ViewModel, "Рабочие").Children
            .OfType<BaseNodeViewModel>()
            .Concat(fixture.ViewModel.TreeItems.OfType<BaseNodeViewModel>())
            .First(n => n.Name == name);
        Assert.Equal(name, fixture.ViewModel.SelectedInfoBase?.Name);
    }

    private static FolderNodeViewModel Folder(MainWindowViewModel vm, string name) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == name);

    private static async Task SaveProfileAsync(ViewModelFixture fixture, string user, string password, string parameters = "")
    {
        fixture.Dialogs.LaunchParameters = form =>
        {
            form.UserName = user;
            form.Password = password;
            form.Parameters = parameters;
            form.SavePassword = true;
            return (true, null);
        };
        await fixture.ViewModel.EditLaunchSettingsCommand.ExecuteAsync(null);
        fixture.Dialogs.LaunchParameters = _ => (false, null);
    }
}
