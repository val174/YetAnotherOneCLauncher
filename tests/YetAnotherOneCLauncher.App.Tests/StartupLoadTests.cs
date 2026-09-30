using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Catalog;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Фоновая загрузка при старте: списки и платформы читаются до окна, окно забирает готовое.</summary>
public class StartupLoadTests
{
    [Fact]
    public async Task Load_reads_catalog_and_platforms_in_background()
    {
        using var fixture = new ViewModelFixture();
        var load = CatalogLoad.Start(new InfoBaseCatalogLoader(), new FakePaths(fixture.ListPath), new FakeLocator(ViewModelFixture.Installations));

        var platforms = await load.Platforms;
        var catalog = await load.Catalog;

        Assert.Equal(ViewModelFixture.Installations.Count, platforms.Installations.Count);
        Assert.Contains(catalog.InfoBases, b => b.Name == "Бухгалтерия предприятия");
    }

    [Fact]
    public async Task Startup_load_is_taken_once_by_first_reload()
    {
        using var fixture = new ViewModelFixture();
        var startup = new StartupCatalog(
            new InfoBaseCatalogLoader(),
            new FakePaths(fixture.ListPath),
            new FakeLocator(ViewModelFixture.Installations),
            NullLogger<CatalogLoad>.Instance);
        Assert.Null(startup.Take()); // не начинали — забирать нечего

        startup.Begin();
        startup.Begin(); // повторный вызов не начинает вторую загрузку
        var load = startup.Take();
        Assert.NotNull(load);
        Assert.Null(startup.Take()); // забирается один раз, дальше загрузки обычные
        Assert.Contains((await load.Catalog).InfoBases, b => b.Name == "Копия бухгалтерии");
    }
}
