using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Кнопка «…» у дополнительных параметров в форме базы открывает окно «Параметры запуска».</summary>
public class EditorLaunchParametersTests
{
    [Fact]
    public async Task Parameters_go_to_list_and_credentials_to_launcher_after_form_is_saved()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.ViewModel.SelectedTreeItem = fixture.ViewModel.TreeItems.OfType<BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");

        LaunchParametersViewModel? shown = null;
        fixture.Dialogs.LaunchParameters = form =>
        {
            shown = form;
            form.SelectedTemplate = form.Templates.Single(t => t.Text == "/ClearCache");
            form.InsertTemplateCommand.Execute(null);
            form.UserName = "Бухгалтер";
            form.Password = "секрет";
            form.SavePassword = true;
            return (true, null);
        };
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.True(editor.CanEditLaunchParameters);
            editor.AdditionalParameters = "/DisableStartupMessages";
            editor.EditLaunchParametersCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal("/DisableStartupMessages /ClearCache", editor.AdditionalParameters);
            Assert.Empty(fixture.Credentials.Entries); // до сохранения формы ничего не записано
            return true;
        };

        await fixture.ViewModel.EditCommand.ExecuteAsync(null);

        Assert.Equal(LaunchParametersKind.ListEntry, shown!.Kind);
        Assert.True(shown.ShowSavePassword);
        Assert.Contains("ibases.v8i", shown.ParametersHint, StringComparison.Ordinal);
        Assert.Equal("/DisableStartupMessages /ClearCache", fixture.SavedList().Sections.Single(s => s.Name == "Копия бухгалтерии").Get("AdditionalParameters"));
        var profile = Assert.Single(fixture.Settings.Settings.InfoBaseProfiles);
        Assert.Equal("Бухгалтер", profile.UserName);
        Assert.Null(profile.Parameters); // параметры — в списке баз, а не в лаунчере
        Assert.Equal(("Бухгалтер", "секрет"), fixture.Credentials.Entries[profile.PasswordKey!]);
        Assert.Equal("Бухгалтер (пароль сохранён)", fixture.Base("Копия бухгалтерии").UserText);
    }

    [Fact]
    public async Task Cancelled_form_saves_nothing()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.ViewModel.SelectedTreeItem = fixture.ViewModel.TreeItems.OfType<BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");
        fixture.Dialogs.LaunchParameters = form =>
        {
            form.UserName = "Бухгалтер";
            form.Password = "секрет";
            form.SavePassword = true;
            return (true, null);
        };
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.EditLaunchParametersCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            return false;
        };

        await fixture.ViewModel.EditCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Settings.Settings.InfoBaseProfiles);
        Assert.Empty(fixture.Credentials.Entries);
    }

    [Fact]
    public async Task New_base_gets_user_from_parameters_window()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.Dialogs.LaunchParameters = form =>
        {
            Assert.Equal("Параметры запуска «Новая»", form.Title);
            form.UserName = "Администратор";
            return (true, null);
        };
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.Name = "Новая";
            editor.FilePath = @"C:\Bases\New";
            editor.EditLaunchParametersCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            return true;
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        Assert.Equal("Администратор", fixture.Base("Новая").UserText);
        Assert.Null(Assert.Single(fixture.Settings.Settings.InfoBaseProfiles).PasswordKey);
    }
}
