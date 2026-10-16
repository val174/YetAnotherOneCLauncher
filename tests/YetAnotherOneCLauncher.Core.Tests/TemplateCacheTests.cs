using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Кэш шаблонов конфигураций (задача #29).</summary>
public sealed class TemplateCacheTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("yaocl-templates-");

    private string CachePath => Path.Combine(_directory.FullName, "data", TemplateCache.FileName);

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Templates_round_trip_with_roots_and_time()
    {
        var at = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.FromHours(3));
        ConfigurationTemplate[] templates =
        [
            new("Бухгалтерия/Бухгалтерия предприятия", "3.0.150.20", @"C:\t\acc\1cv8.cf", @"C:\t\acc\1cv8.dt"),
            new("Демо", string.Empty, null, @"C:\t\demo\1cv8.dt"),
        ];
        var cache = new TemplateCache(CachePath);

        await cache.SaveAsync(TemplateCacheData.From(templates, [@"C:\t", @"\\srv\tmplts\"], at));
        var loaded = await cache.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(at, loaded.UpdatedAt);
        Assert.Equal(templates, loaded.ToTemplates());
        Assert.True(loaded.SameRoots([@"\\SRV\tmplts", @"c:\t\"])); // без учёта регистра, порядка и «\» в конце
        Assert.False(loaded.SameRoots([@"C:\t"]));
    }

    [Fact]
    public async Task Missing_or_damaged_cache_is_null_and_empty_entries_are_skipped()
    {
        var cache = new TemplateCache(CachePath);
        Assert.Null(await cache.LoadAsync());

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, "{ не json");
        Assert.Null(await cache.LoadAsync());

        await File.WriteAllTextAsync(CachePath, """{ "templates": [ { "catalog": "Без файлов" }, null, { "catalog": "А", "configurationPath": "C:\\a.cf" } ] }""");
        var loaded = await cache.LoadAsync();
        Assert.Equal("А", Assert.Single(loaded!.ToTemplates()).Catalog);
    }
}
