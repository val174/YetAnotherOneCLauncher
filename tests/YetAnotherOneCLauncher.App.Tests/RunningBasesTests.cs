using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Подсветка запущенных баз: процессы платформы с адресом базы в командной строке.</summary>
public class RunningBasesTests
{
    [Fact]
    public async Task Bases_open_in_1c_are_marked_running_with_clients_in_tooltip()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        Assert.True(vm.HighlightRunning); // включено по умолчанию
        Assert.All(vm.InfoBases, b => Assert.False(b.IsRunning));

        // Адрес — разными ключами и в другом регистре; процессы без базы в командной строке не считаются.
        fixture.CacheUsage.Processes.Add(new PlatformProcess(1, "1cv8c", @"""C:\1cv8\bin\1cv8c.exe"" ENTERPRISE /S ""srv-1c\zup"""));
        fixture.CacheUsage.Processes.Add(new PlatformProcess(2, "1cv8", @"1cv8.exe DESIGNER /S""SRV-1C\Buh_Prod"" /N""Админ"""));
        fixture.CacheUsage.Processes.Add(new PlatformProcess(3, "1cv8c", @"1cv8c.exe ENTERPRISE /IBConnectionString ""Srvr=""""srv-1c"""";Ref=""""buh_prod"""";"""));
        fixture.CacheUsage.Processes.Add(new PlatformProcess(4, "1cv8", "1cv8.exe"));
        await vm.RefreshRunningAsync();

        var buh = fixture.Base("Бухгалтерия предприятия");
        Assert.True(buh.IsRunning);
        Assert.Equal("Открыта в 1С: Конфигуратор, тонкий клиент", buh.RunningToolTip);
        Assert.True(fixture.Base("Зарплата и управление персоналом").IsRunning);
        Assert.False(fixture.Base("Розница (тест)").IsRunning);

        // Процесс закрыли — точка гаснет при следующем чтении.
        fixture.CacheUsage.Processes.RemoveAll(p => p.Id == 1);
        await vm.RefreshRunningAsync();
        Assert.False(fixture.Base("Зарплата и управление персоналом").IsRunning);

        // После перечитывания каталога (новые объекты баз) отметки сохраняются.
        await fixture.LoadAsync();
        Assert.True(fixture.Base("Бухгалтерия предприятия").IsRunning);

        // Подсветку выключили — отметок нет, настройка сохранена; процессы больше не читаются.
        vm.HighlightRunning = false;
        Assert.All(vm.InfoBases, b => Assert.False(b.IsRunning));
        Assert.False(fixture.Settings.Settings.Ui.HighlightRunningBases);
        await vm.RefreshRunningAsync();
        Assert.All(vm.InfoBases, b => Assert.False(b.IsRunning));
    }

    [Fact]
    public async Task Launched_base_is_marked_right_after_launch()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var buh = fixture.Base("Бухгалтерия предприятия");

        // Запущенный процесс появляется вместе с запуском.
        fixture.CacheUsage.Processes.Add(new PlatformProcess(10, "1cv8c", @"1cv8c.exe ENTERPRISE /S ""srv-1c\buh_prod"""));
        await vm.LaunchEnterpriseCommand.ExecuteAsync(buh);

        for (var i = 0; i < 200 && !buh.IsRunning; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(buh.IsRunning);
        Assert.Equal("Открыта в 1С: тонкий клиент", buh.RunningToolTip);
    }

    [Fact]
    public void Highlight_setting_is_edited_on_appearance_tab()
    {
        var settings = new SettingsViewModel(new SettingsValues());
        Assert.True(settings.HighlightRunning);
        settings.HighlightRunning = false;
        Assert.True(settings.IsAppearanceDirty);
        Assert.False(settings.Result.HighlightRunning);
    }
}
