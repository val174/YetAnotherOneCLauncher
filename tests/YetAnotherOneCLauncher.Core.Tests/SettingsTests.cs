using Microsoft.Extensions.Time.Testing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Core.Settings;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public async Task Missing_file_gives_defaults_without_warning()
    {
        using var temp = new TempDirectory();

        var result = await new SettingsStore(temp.Path).LoadAsync();

        Assert.Null(result.Warning);
        Assert.Empty(result.Settings.Favorites);
        Assert.Equal(ThemeMode.System, result.Settings.Ui.Theme);
    }

    [Fact]
    public async Task Round_trip_keeps_everything()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Combine("nested"));
        var settings = new LauncherSettings
        {
            Favorites = [new InfoBaseRef { Id = "id-1", ConnectionKey = "conn:x", Name = "База" }],
            History = [new LaunchHistoryEntry { InfoBase = new InfoBaseRef { ConnectionKey = "conn:y", Name = "Y" }, Mode = LaunchMode.Designer, LaunchedAt = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.FromHours(3)) }],
            PlatformOverrides = [new PlatformVersionOverride { InfoBase = new InfoBaseRef { ConnectionKey = "conn:z" }, Version = "8.3.27" }],
            Ui = new UiSettings
            {
                Theme = ThemeMode.Dark,
                ViewMode = CatalogViewMode.List,
                AfterLaunch = AfterLaunchAction.Minimize,
                Window = new WindowPlacement { X = 10, Y = 20, Width = 800, Height = 600, IsMaximized = true },
                CollapsedFolders = ["/Архив"],
                ShowDetails = false,
            },
            Launch = new LaunchSettings { PreferredArchitecture = PlatformArchitecture.X86, UseThickClientForFileBasesByDefault = true },
        };

        await store.SaveAsync(settings);
        var loaded = (await store.LoadAsync()).Settings;

        Assert.Equal(settings.Favorites, loaded.Favorites);
        Assert.Equal(settings.History, loaded.History);
        Assert.Equal(settings.PlatformOverrides, loaded.PlatformOverrides);
        Assert.Equal(ThemeMode.Dark, loaded.Ui.Theme);
        Assert.Equal(CatalogViewMode.List, loaded.Ui.ViewMode);
        Assert.Equal(AfterLaunchAction.Minimize, loaded.Ui.AfterLaunch);
        Assert.Equal(settings.Ui.Window, loaded.Ui.Window);
        Assert.Equal(["/Архив"], loaded.Ui.CollapsedFolders);
        Assert.False(loaded.Ui.ShowDetails);
        Assert.Equal(PlatformArchitecture.X86, loaded.Launch.PreferredArchitecture);
        Assert.True(loaded.Launch.UseThickClientForFileBasesByDefault);
    }

    [Fact]
    public async Task File_is_readable_json_with_enum_names()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);

        await store.SaveAsync(new LauncherSettings { Ui = new UiSettings { Theme = ThemeMode.Dark } });
        var text = await File.ReadAllTextAsync(store.FilePath);

        Assert.Contains("\"theme\": \"Dark\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupted_file_is_moved_aside_and_defaults_are_used()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        await File.WriteAllTextAsync(store.FilePath, "{ \"favorites\": [ oops");

        var result = await store.LoadAsync();

        Assert.NotNull(result.Warning);
        Assert.Empty(result.Settings.Favorites);
        Assert.False(File.Exists(store.FilePath));
        Assert.True(File.Exists(store.FilePath + ".bad"));
    }

    [Theory]
    [InlineData("Duotone", IconStyle.Outline)] // двухтоновые значки убраны
    [InlineData("Plate", IconStyle.Plate)]
    [InlineData("7", IconStyle.Outline)]
    public async Task Removed_icon_style_is_read_as_default(string saved, IconStyle expected)
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var value = int.TryParse(saved, out _) ? saved : $"\"{saved}\"";
        await File.WriteAllTextAsync(store.FilePath, $$"""{ "ui": { "iconStyle": {{value}} } }""");

        var result = await store.LoadAsync();

        Assert.Null(result.Warning);
        Assert.Equal(expected, result.Settings.Ui.IconStyle);
    }

    [Fact]
    public async Task Nulls_and_unknown_properties_are_tolerated()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        await File.WriteAllTextAsync(store.FilePath, """
            {
              // комментарии допустимы
              "favorites": null,
              "ui": { "collapsedFolders": null, "futureOption": 42 },
              "somethingNew": true,
            }
            """);

        var result = await store.LoadAsync();

        Assert.Null(result.Warning);
        Assert.Empty(result.Settings.Favorites);
        Assert.Empty(result.Settings.Ui.CollapsedFolders);
    }
}

public class LauncherUserDataTests
{
    [Fact]
    public void Favorite_is_found_by_id_even_if_connection_changed()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetFavorite(InfoBase("Connect=File=\"C:\\Old\";", "ID=AAAA-1"), favorite: true);

        Assert.True(data.IsFavorite(InfoBase("Connect=File=\"C:\\New\";", "ID=aaaa-1")));
        Assert.False(data.IsFavorite(InfoBase("Connect=File=\"C:\\Old\";", "ID=bbbb-2")));
    }

    [Fact]
    public void Favorite_is_found_by_connection_when_id_was_lost()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetFavorite(InfoBase("Connect=Srvr=\"srv\";Ref=\"buh\";", "ID=AAAA-1"), favorite: true);

        Assert.True(data.IsFavorite(InfoBase("Connect=Srvr=\"SRV\";Ref=\"Buh\";")));
    }

    [Fact]
    public void Toggling_favorite_does_not_duplicate()
    {
        var data = new LauncherUserData(new LauncherSettings());
        var infoBase = InfoBase("Connect=File=\"C:\\A\";");

        data.SetFavorite(infoBase, true);
        data.SetFavorite(infoBase, true);
        Assert.Single(data.Settings.Favorites);

        data.SetFavorite(infoBase, false);
        Assert.False(data.IsFavorite(infoBase));
    }

    [Fact]
    public void History_gives_recent_distinct_bases_last_launch_and_count()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
        var data = new LauncherUserData(new LauncherSettings(), time);
        var a = InfoBase("Connect=File=\"C:\\A\";");
        var b = InfoBase("Connect=File=\"C:\\B\";");
        var c = InfoBase("Connect=File=\"C:\\C\";");
        var gone = InfoBase("Connect=File=\"C:\\Gone\";");

        data.RecordLaunch(a, LaunchMode.Enterprise);
        time.Advance(TimeSpan.FromMinutes(1));
        data.RecordLaunch(gone, LaunchMode.Enterprise);
        time.Advance(TimeSpan.FromMinutes(1));
        data.RecordLaunch(b, LaunchMode.Enterprise);
        time.Advance(TimeSpan.FromMinutes(1));
        data.RecordLaunch(a, LaunchMode.Designer);

        Assert.Equal(new[] { a, b }, data.Recent([a, b, c], 10));
        Assert.Equal(new[] { a }, data.Recent([a, b, c], 1));
        Assert.Equal(2, data.LaunchCount(a));
        Assert.Equal(0, data.LaunchCount(c));
        var last = data.LastLaunch(a)!;
        Assert.Equal(LaunchMode.Designer, last.Mode);
        Assert.Equal(time.GetUtcNow(), last.LaunchedAt);
    }

    [Fact]
    public void History_is_limited()
    {
        var data = new LauncherUserData(new LauncherSettings());
        var infoBase = InfoBase("Connect=File=\"C:\\A\";");

        for (var i = 0; i < LauncherUserData.MaxHistoryEntries + 10; i++)
        {
            data.RecordLaunch(infoBase, LaunchMode.Enterprise);
        }

        Assert.Equal(LauncherUserData.MaxHistoryEntries, data.Settings.History.Count);
    }

    [Fact]
    public void Platform_override_is_stored_and_used_by_planner()
    {
        var data = new LauncherUserData(new LauncherSettings());
        var infoBase = InfoBase("Connect=File=\"C:\\A\";", "Version=8.3.27");
        data.SetPlatformVersionOverride(infoBase, "8.3.24");

        var request = new LaunchRequest(infoBase, LaunchMode.Enterprise) { PlatformVersionOverride = data.PlatformVersionOverride(infoBase) };
        var plan = LaunchPlanner.Plan(request, [Installation("8.3.27.2130"), Installation("8.3.24.1667")], null);

        Assert.Equal("8.3.24.1667", Assert.IsType<LaunchPlan.Run>(plan).Command.Platform.Version.ToString());

        data.SetPlatformVersionOverride(infoBase, null);
        Assert.Null(data.PlatformVersionOverride(infoBase));
    }
}
