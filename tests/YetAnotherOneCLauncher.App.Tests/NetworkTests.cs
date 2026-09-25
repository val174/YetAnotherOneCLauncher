using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Доступность баз и копии общих списков в окне.</summary>
public class NetworkTests
{
    [Fact]
    public async Task Unavailable_bases_are_marked_after_load_and_survive_rebuild()
    {
        using var fixture = new ViewModelFixture();
        fixture.Availability.MissingDirectories.Add(@"C:\Bases\BuhCopy");
        fixture.Availability.DeadHosts.Add("web.example");
        await fixture.LoadAsync();
        await fixture.ViewModel.AvailabilityTask;
        var vm = fixture.ViewModel;

        Assert.True(fixture.Base("Копия бухгалтерии").IsUnavailable);
        Assert.Equal(@"недоступна: каталог базы не найден: C:\Bases\BuhCopy", fixture.Base("Копия бухгалтерии").AvailabilityText);
        Assert.True(fixture.Base("Розница (тест)").IsUnavailable);
        Assert.Equal("доступна (srv-1c:1541 отвечает)", fixture.Base("Бухгалтерия предприятия").AvailabilityText);
        Assert.Equal(2, vm.UnavailableCount);
        Assert.True(vm.HasUnavailable);

        // Избранное перестраивает дерево и создаёт новые строки — отметки остаются.
        vm.ToggleFavoriteCommand.Execute(fixture.Base("Копия бухгалтерии"));
        Assert.True(fixture.Base("Копия бухгалтерии").IsUnavailable);
    }

    [Fact]
    public async Task Turning_check_off_clears_marks_and_is_saved()
    {
        using var fixture = new ViewModelFixture();
        fixture.Availability.MissingDirectories.Add(@"C:\Bases\BuhCopy");
        await fixture.LoadAsync();
        await fixture.ViewModel.AvailabilityTask;

        fixture.ViewModel.CheckAvailability = false;

        Assert.False(fixture.Base("Копия бухгалтерии").IsUnavailable);
        Assert.Equal("не проверялась", fixture.Base("Копия бухгалтерии").AvailabilityText);
        Assert.False(fixture.ViewModel.HasUnavailable);
        Assert.False(fixture.Settings.Settings.Network.CheckAvailability);

        fixture.ViewModel.CheckAvailability = true;
        await fixture.ViewModel.AvailabilityTask;
        Assert.True(fixture.Base("Копия бухгалтерии").IsUnavailable);
    }

    [Fact]
    public void Base_from_saved_copy_says_so()
    {
        var section = V8iDocument.Parse("[Общая]\r\nConnect=File=\"C:\\A\";\r\n").Sections.Single(s => s.Name.Length > 0);
        var savedAt = new DateTimeOffset(2026, 9, 20, 9, 15, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 20)));
        var fromCopy = new InfoBaseViewModel(
            new InfoBase(section, new ListSource(ListSourceKind.Common, @"\\srv\share\list.v8i") { CachedAt = savedAt }),
            new LauncherUserData(new LauncherSettings()));
        var fromWeb = new InfoBaseViewModel(
            new InfoBase(section, new ListSource(ListSourceKind.InternetService, "http://host/list")),
            new LauncherUserData(new LauncherSettings()));

        Assert.True(fromCopy.IsFromCache);
        Assert.Equal("Общий список — список недоступен, показана копия на 20.09.2026 09:15", fromCopy.SourceText);
        Assert.Equal("Веб-сервис списков (только чтение)", fromWeb.SourceText);
        Assert.False(fromWeb.IsFromCache);
    }
}
