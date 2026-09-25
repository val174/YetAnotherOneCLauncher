using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Settings;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class LaunchParametersTests
{
    [Fact]
    public void Parameters_are_applied_in_order_list_folders_base_one_off()
    {
        var data = new LauncherUserData(new LauncherSettings());
        var infoBase = InfoBase("Connect=File=\"C:\\B\";", "Folder=/Работа/Архив", "AdditionalParameters=/DisableStartupMessages");
        data.SetFolderParameters("/Работа", "/L ru");
        data.SetFolderParameters("/Работа/Архив", "/UseHwLicenses-");
        data.SetFolderParameters("/Другая", "/ClearCache");
        data.SetLaunchProfile(infoBase, new InfoBaseLaunchProfile { Parameters = "/C \"режим теста\"" });

        var request = new LaunchRequest(infoBase, LaunchMode.Enterprise)
        {
            ParameterFragments = [.. data.ParameterChain(infoBase), "/UC 42"],
            ExtraArguments = ["/VL", "ru_RU"],
        };
        var command = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, [Installation("8.3.27.2130")], null)).Command;

        Assert.Equal(
            "ENTERPRISE /F C:\\B /DisableStartupMessages /L ru /UseHwLicenses- /C \"режим теста\" /UC 42 /VL ru_RU",
            command.ToWindowsArguments());
        Assert.Equal(
            new[] { "ENTERPRISE", "/F", @"C:\B", "/DisableStartupMessages", "/L", "ru", "/UseHwLicenses-", "/C", "режим теста", "/UC", "42", "/VL", "ru_RU" },
            command.ToArgumentVector());
    }

    [Fact]
    public void User_and_password_go_to_arguments_and_password_is_masked()
    {
        var infoBase = InfoBase("Connect=Srvr=\"srv\";Ref=\"buh\";");
        var request = new LaunchRequest(infoBase, LaunchMode.Designer) { UserName = "Иванов И.", Password = "p@ss word", ParameterFragments = ["/P raw"] };

        var command = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, [Installation("8.3.27.2130")], null)).Command;

        Assert.Equal(new[] { "DESIGNER", "/S", @"srv\buh", "/N", "Иванов И.", "/P", "p@ss word" }, command.Arguments);
        var display = command.ToDisplayString();
        Assert.DoesNotContain("p@ss", display, StringComparison.Ordinal);
        Assert.DoesNotContain("raw", display, StringComparison.Ordinal);
        Assert.EndsWith("/N \"Иванов И.\" /P *** /P ***", display, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_profile_is_removed()
    {
        var data = new LauncherUserData(new LauncherSettings());
        var infoBase = InfoBase("Connect=File=\"C:\\B\";", "ID=1");

        data.SetLaunchProfile(infoBase, new InfoBaseLaunchProfile { UserName = " admin ", Parameters = "  " });
        var profile = Assert.Single(data.Settings.InfoBaseProfiles);
        Assert.Equal("admin", profile.UserName);
        Assert.Null(profile.Parameters);
        Assert.Equal("1", profile.InfoBase.Id);

        data.SetLaunchProfile(infoBase, new InfoBaseLaunchProfile { UserName = "" });
        Assert.Empty(data.Settings.InfoBaseProfiles);
    }

    [Fact]
    public void Folder_parameters_move_with_renamed_folder_and_subfolders()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetFolderParameters("/Работа", "/L ru");
        data.SetFolderParameters("Работа/Архив/", "/ClearCache");
        data.SetFolderParameters("/Работа2", "/UC 1");

        data.MoveFolderParameters("/Работа", "/Проекты/Работа");

        Assert.Equal("/L ru", data.FolderParameters("/Проекты/Работа"));
        Assert.Equal("/ClearCache", data.FolderParameters("/проекты/работа/архив"));
        Assert.Equal("/UC 1", data.FolderParameters("/Работа2"));
        Assert.Null(data.FolderParameters("/Работа"));

        data.SetFolderParameters("/Работа2", " ");
        Assert.Null(data.FolderParameters("/Работа2"));
    }

    [Theory]
    [InlineData(ParameterValueKind.None, "x", "/ClearCache")]
    [InlineData(ParameterValueKind.Text, "123", "/ClearCache 123")]
    [InlineData(ParameterValueKind.File, @"C:\Мои обработки\a.epf", "/ClearCache \"C:\\Мои обработки\\a.epf\"")]
    [InlineData(ParameterValueKind.Text, " ", "/ClearCache")]
    public void Template_formats_value(ParameterValueKind kind, string value, string expected)
    {
        var template = new ParameterTemplate { Text = "/ClearCache", Value = kind };

        Assert.Equal(expected, template.Format(value));
    }

    [Fact]
    public void Append_joins_with_single_space()
    {
        Assert.Equal("/A", ParameterLibrary.Append(null, " /A "));
        Assert.Equal("/A /B", ParameterLibrary.Append("/A  ", "/B"));
    }

    [Fact]
    public void Own_templates_come_before_built_in()
    {
        var data = new LauncherUserData(new LauncherSettings { ParameterTemplates = [new ParameterTemplate { Name = "Мой", Text = "/N Тест" }] });

        var templates = data.ParameterTemplates();

        Assert.Equal("Мой", templates[0].Name);
        Assert.Equal(ParameterLibrary.BuiltIn.Count + 1, templates.Count);
        Assert.All(ParameterLibrary.BuiltIn, t => Assert.StartsWith("/", t.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Profiles_survive_settings_round_trip_without_computed_properties()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = new LauncherSettings
        {
            InfoBaseProfiles = [new InfoBaseLaunchProfile { InfoBase = new InfoBaseRef { Id = "1", ConnectionKey = "c" }, UserName = "admin", PasswordKey = "k1", Parameters = "/L ru" }],
            FolderProfiles = [new FolderLaunchProfile { FolderPath = "/Работа", Parameters = "/ClearCache" }],
            ParameterTemplates = [new ParameterTemplate { Name = "Мой", Text = "/UC", Value = ParameterValueKind.Text }],
        };

        await store.SaveAsync(settings);
        var text = await File.ReadAllTextAsync(store.FilePath);
        var loaded = (await store.LoadAsync()).Settings;

        Assert.DoesNotContain("isEmpty", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(settings.InfoBaseProfiles, loaded.InfoBaseProfiles);
        Assert.Equal(settings.FolderProfiles, loaded.FolderProfiles);
        Assert.Equal(settings.ParameterTemplates, loaded.ParameterTemplates);
    }
}
