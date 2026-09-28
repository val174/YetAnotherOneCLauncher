using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Колонки списка баз: платформа, режим запуска, дата последнего запуска.</summary>
public class ColumnsTests
{
    [Fact]
    public async Task Platform_column_shows_version_that_launch_would_use()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();

        var buh = fixture.Base("Бухгалтерия предприятия"); // Version=8.3 -> самая новая 8.3
        Assert.Equal("8.3.27.2130", buh.PlatformText);
        Assert.False(buh.IsPlatformMissing);
        Assert.Contains("по версии в списке баз (8.3)", buh.PlatformToolTip, StringComparison.Ordinal);

        var copy = fixture.Base("Копия бухгалтерии"); // Version=8.3.22 не установлена
        Assert.Equal("8.3.22 — нет", copy.PlatformText);
        Assert.True(copy.IsPlatformMissing);

        Assert.Equal("8.5.1.1150", fixture.Base("Зарплата и управление персоналом").PlatformText); // без версии — самая новая

        // Версия, запомненная в «Запустить с параметрами», сразу видна в колонке.
        fixture.Dialogs.LaunchParameters = f =>
        {
            f.SelectedPlatformChoice = f.PlatformChoices.Single(c => c.Version == "8.3.24.1667");
            f.RememberPlatform = true;
            return (true, LaunchMode.Enterprise);
        };
        await fixture.ViewModel.LaunchWithParametersCommand.ExecuteAsync(copy);
        Assert.Equal("8.3.24.1667", copy.PlatformText);
        Assert.False(copy.IsPlatformMissing);
        Assert.Contains("выбрана в лаунчере", copy.PlatformToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mode_and_last_launch_columns()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var zup = fixture.Base("Зарплата и управление персоналом");

        Assert.Equal("Авто", zup.ClientShortText);
        Assert.Equal(string.Empty, zup.LastLaunchShortText);

        await fixture.ViewModel.LaunchEnterpriseCommand.ExecuteAsync(zup);

        Assert.Matches(@"^\d\d\.\d\d\.\d{4} \d\d:\d\d$", fixture.Base("Зарплата и управление персоналом").LastLaunchShortText);
        Assert.Equal(LaunchMode.Enterprise, fixture.Settings.Settings.History.Single().Mode);
    }
}
