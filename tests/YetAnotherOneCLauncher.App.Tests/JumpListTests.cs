using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Список переходов Windows: последние базы и их запуск.</summary>
public class JumpListTests
{
    [Theory]
    [InlineData("id:00000000-0000-0000-0000-000000000001")]
    [InlineData("""conn:file="c:\bases\база с пробелом";""")]
    public void Launch_argument_round_trips_without_quotes_or_spaces(string key)
    {
        var argument = LaunchArgument.Format(key);

        Assert.StartsWith("--launch=", argument, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', argument);
        Assert.DoesNotContain('"', argument);
        Assert.Equal(key, LaunchArgument.Parse(["--other", argument]));
    }

    [Fact]
    public void Broken_or_missing_argument_is_ignored()
    {
        Assert.Null(LaunchArgument.Parse([]));
        Assert.Null(LaunchArgument.Parse(["--launch=@@@"]));
        Assert.Null(LaunchArgument.Parse(["--launch="]));
    }

    [Fact]
    public async Task Jump_list_holds_last_five_launched_bases()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        Assert.Empty(fixture.JumpList.Recent); // ещё ничего не запускали
        fixture.Dialogs.ConfirmAnswer = true; // у «Копии бухгалтерии» нет своей 8.3.22 — согласиться на другую версию

        foreach (var name in new[] { "Копия бухгалтерии", "Бухгалтерия предприятия", "Зарплата и управление персоналом", "Копия бухгалтерии" })
        {
            await fixture.ViewModel.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base(name));
        }

        Assert.Equal(
            new[] { "Копия бухгалтерии", "Зарплата и управление персоналом", "Бухгалтерия предприятия" },
            fixture.JumpList.Recent.Select(e => e.Title));
        var first = fixture.JumpList.Recent[0];
        Assert.Equal(fixture.Base("Копия бухгалтерии").InfoBase.IdentityKey, LaunchArgument.Parse([first.Arguments]));
        Assert.EndsWith("1cv8c.exe", first.IconPath, StringComparison.Ordinal);
        Assert.Equal(@"C:\Bases\BuhCopy", first.Description);
    }

    [Fact]
    public async Task Request_from_jump_list_launches_base_in_enterprise_mode()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();

        await fixture.ViewModel.LaunchFromJumpListAsync(fixture.Base("Зарплата и управление персоналом").InfoBase.IdentityKey);

        Assert.Equal("ENTERPRISE", Assert.Single(fixture.Processes.Started).Arguments[0]);
    }

    [Fact]
    public async Task Startup_launch_waits_for_catalog()
    {
        using var fixture = new ViewModelFixture(startupLaunchKey: "id:00000000-0000-0000-0000-000000000002");
        Assert.Empty(fixture.Processes.Started);

        await fixture.LoadAsync();

        await WaitAsync(() => fixture.Processes.Started.Count == 1);
        Assert.Contains(@"srv-1c\zup", string.Join(' ', fixture.Processes.Started[0].Arguments), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_base_is_reported()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();

        await fixture.ViewModel.LaunchFromJumpListAsync("id:нет-такой");

        Assert.Empty(fixture.Processes.Started);
        Assert.Contains("не найдена", Assert.Single(fixture.Dialogs.Messages), StringComparison.Ordinal);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
