using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class LaunchPlannerTests
{
    private static readonly PlatformInstallation[] Installed =
    [
        Installation("8.3.27.2130"),
        Installation("8.3.24.1667"),
        Installation("8.3.18.1741", PlatformArchitecture.X86, thin: false),
    ];

    [Fact]
    public void File_base_in_enterprise_uses_thin_client()
    {
        var command = Run(InfoBase("Connect=File=\"C:\\Bases\\Buh\";"), LaunchMode.Enterprise);

        Assert.EndsWith("1cv8c.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal(new[] { "ENTERPRISE", "/F", @"C:\Bases\Buh" }, command.Arguments);
        Assert.Equal("8.3.27.2130", command.Platform.Version.ToString());
    }

    [Fact]
    public void Server_base_in_designer_uses_thick_client()
    {
        var command = Run(InfoBase("Connect=Srvr=\"srv1c:1541\";Ref=\"buh\";"), LaunchMode.Designer);

        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal(new[] { "DESIGNER", "/S", @"srv1c:1541\buh" }, command.Arguments);
    }

    [Fact]
    public void Web_base_in_thin_client_uses_ws_switch()
    {
        var command = Run(InfoBase("Connect=ws=\"https://host/buh\";"), LaunchMode.Enterprise);

        Assert.Equal(new[] { "ENTERPRISE", "/WS", "https://host/buh" }, command.Arguments);
    }

    [Fact]
    public void Unusual_connection_string_is_passed_whole()
    {
        var command = Run(InfoBase("Connect=ws=\"https://host/buh\";wsn=\"user\";"), LaunchMode.Enterprise);

        Assert.Equal(new[] { "ENTERPRISE", "/IBConnectionString", "ws=\"https://host/buh\";wsn=\"user\";" }, command.Arguments);
        Assert.Equal(
            "ENTERPRISE /IBConnectionString \"ws=\"\"https://host/buh\"\";wsn=\"\"user\"\";\"",
            command.ToWindowsArguments());
    }

    [Fact]
    public void Thick_client_from_app_key()
    {
        var command = Run(InfoBase("Connect=File=\"C:\\B\";", "App=ThickClient"), LaunchMode.Enterprise);

        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_override_wins_over_app_key()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";", "App=ThickClient"), LaunchMode.Enterprise)
        {
            ClientOverride = ClientApp.ThinClient,
        };

        var plan = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, Installed, null));
        Assert.EndsWith("1cv8c.exe", plan.Command.ExecutablePath, StringComparison.Ordinal);
    }

    [Fact]
    public void File_bases_can_default_to_thick_client()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";"), LaunchMode.Enterprise);
        var plan = LaunchPlanner.Plan(request, Installed, null, new LaunchOptions { UseThickClientForFileBasesByDefault = true });

        Assert.EndsWith("1cv8.exe", Assert.IsType<LaunchPlan.Run>(plan).Command.ExecutablePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_client_falls_back_to_thick_when_version_has_no_thin_client()
    {
        // 8.3.18 установлена только с толстым клиентом.
        var command = Run(InfoBase("Connect=File=\"C:\\B\";", "Version=8.3.18"), LaunchMode.Enterprise);

        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal("8.3.18.1741", command.Platform.Version.ToString());
    }

    [Fact]
    public void Explicit_thin_client_does_not_fall_back_to_thick()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";", "Version=8.3.18", "App=ThinClient"), LaunchMode.Enterprise);

        var plan = Assert.IsType<LaunchPlan.ConfirmFallback>(LaunchPlanner.Plan(request, Installed, null));
        Assert.EndsWith("1cv8c.exe", plan.Command.ExecutablePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_version_asks_for_confirmation()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";", "Version=8.3.22"), LaunchMode.Enterprise);

        var plan = Assert.IsType<LaunchPlan.ConfirmFallback>(LaunchPlanner.Plan(request, Installed, null));
        Assert.Contains("8.3.22", plan.Question, StringComparison.Ordinal);
        Assert.Contains("8.3.27.2130", plan.Question, StringComparison.Ordinal);
        Assert.Equal("8.3.27.2130", plan.Command.Platform.Version.ToString());
    }

    [Fact]
    public void Starter_default_version_is_used()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";"), LaunchMode.Enterprise);

        var plan = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, Installed, "8.3.24"));
        Assert.Equal("8.3.24.1667", plan.Command.Platform.Version.ToString());
    }

    [Fact]
    public void Nothing_installed_fails_with_explanation()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";"), LaunchMode.Designer);

        var plan = Assert.IsType<LaunchPlan.Failed>(LaunchPlanner.Plan(request, [], null));
        Assert.Contains("1cv8", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_connection_fails()
    {
        var request = new LaunchRequest(InfoBase("Connect="), LaunchMode.Enterprise);

        Assert.IsType<LaunchPlan.Failed>(LaunchPlanner.Plan(request, Installed, null));
    }

    [Fact]
    public void Web_client_opens_browser_for_published_base()
    {
        var request = new LaunchRequest(InfoBase("Connect=ws=\"https://host/buh\";", "App=WebClient"), LaunchMode.Enterprise);

        var plan = Assert.IsType<LaunchPlan.OpenInBrowser>(LaunchPlanner.Plan(request, [], null));
        Assert.Equal("https://host/buh", plan.Url.OriginalString);
    }

    [Fact]
    public void Web_client_is_not_possible_for_file_base()
    {
        var request = new LaunchRequest(InfoBase("Connect=File=\"C:\\B\";", "App=WebClient"), LaunchMode.Enterprise);

        Assert.IsType<LaunchPlan.Failed>(LaunchPlanner.Plan(request, Installed, null));
    }

    [Fact]
    public void Designer_ignores_web_client_setting()
    {
        var command = Run(InfoBase("Connect=ws=\"https://host/buh\";", "App=WebClient"), LaunchMode.Designer);

        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Adds_windows_authentication_speed_and_credentials()
    {
        var infoBase = InfoBase("Connect=Srvr=\"srv\";Ref=\"buh\";", "WA=0", "ClientConnectionSpeed=low");
        var request = new LaunchRequest(infoBase, LaunchMode.Enterprise) { UserName = "Иванов", Password = "p@ss word" };

        var command = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, Installed, null)).Command;

        Assert.Equal(
            new[] { "ENTERPRISE", "/S", @"srv\buh", "/WA-", "/O", "Low", "/N", "Иванов", "/P", "p@ss word" },
            command.Arguments);
        Assert.DoesNotContain("p@ss", command.ToDisplayString(), StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Designer_does_not_get_connection_speed()
    {
        var command = Run(InfoBase("Connect=File=\"C:\\B\";", "WA=1", "ClientConnectionSpeed=Normal"), LaunchMode.Designer);

        Assert.Equal(new[] { "DESIGNER", "/F", @"C:\B", "/WA+" }, command.Arguments);
    }

    [Fact]
    public void Additional_parameters_go_last_and_extra_arguments_after_them()
    {
        var infoBase = InfoBase("Connect=File=\"C:\\B\";", "AdditionalParameters=/DisableStartupMessages /C\"a b\"");
        var request = new LaunchRequest(infoBase, LaunchMode.Enterprise) { ExtraArguments = ["/L", "en us"] };

        var command = Assert.IsType<LaunchPlan.Run>(LaunchPlanner.Plan(request, Installed, null)).Command;

        Assert.Equal("ENTERPRISE /F C:\\B /DisableStartupMessages /C\"a b\" /L \"en us\"", command.ToWindowsArguments());
        Assert.Equal(
            new[] { "ENTERPRISE", "/F", @"C:\B", "/DisableStartupMessages", "/Ca b", "/L", "en us" },
            command.ToArgumentVector());
    }

    [Fact]
    public void Display_string_quotes_executable_and_masks_raw_password()
    {
        var infoBase = InfoBase("Connect=File=\"C:\\B\";", "AdditionalParameters=/N admin /P secret");

        var display = Run(infoBase, LaunchMode.Enterprise).ToDisplayString();

        Assert.StartsWith("\"" + Path.Combine("Program Files", "1cv8", "8.3.27.2130"), display, StringComparison.Ordinal);
        Assert.EndsWith("/N admin /P ***", display, StringComparison.Ordinal);
    }

    private static LaunchCommand Run(InfoBase infoBase, LaunchMode mode)
    {
        var plan = LaunchPlanner.Plan(new LaunchRequest(infoBase, mode), Installed, null);
        return Assert.IsType<LaunchPlan.Run>(plan).Command;
    }
}
