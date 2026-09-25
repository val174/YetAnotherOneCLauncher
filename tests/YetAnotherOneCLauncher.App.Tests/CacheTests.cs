using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Platform.Linux;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Кэш баз: размеры в окне, очистка одной базы, «Очистить и запустить», окно «Кэш баз».</summary>
public class CacheTests
{
    private const string BuhId = "00000000-0000-0000-0000-000000000001";
    private const string OrphanId = "99999999-0000-0000-0000-000000000009";

    [Fact]
    public async Task Cache_sizes_are_shown_after_load()
    {
        using var fixture = await LoadAsync();

        Assert.Equal("2 КБ (и настройки 100 Б)", fixture.Base("Бухгалтерия предприятия").CacheText);
        Assert.True(fixture.Base("Бухгалтерия предприятия").HasCache);
        Assert.Equal("нет", fixture.Base("Копия бухгалтерии").CacheText);
        Assert.StartsWith("Кэш: ", fixture.ViewModel.CacheSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_base_cache_moves_local_part_to_recycle_bin()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        fixture.CacheUsage.Processes.Add("1cv8c (PID 42)");
        var target = fixture.Base("Бухгалтерия предприятия");

        await fixture.ViewModel.ClearCacheCommand.ExecuteAsync(target);

        var question = Assert.Single(fixture.Dialogs.Questions);
        Assert.Contains("кэш «Бухгалтерия предприятия» — 2 КБ", question, StringComparison.Ordinal);
        Assert.Contains("1cv8c (PID 42)", question, StringComparison.Ordinal);
        Assert.DoesNotContain("Roaming", question, StringComparison.Ordinal);
        Assert.Equal(new[] { Path.Combine(fixture.LocalCacheRoot, BuhId) }, fixture.RecycleBin.Recycled);
        Assert.True(Directory.Exists(Path.Combine(fixture.RoamingCacheRoot, BuhId))); // настройки не тронуты
        Assert.Equal("нет (и настройки 100 Б)", fixture.Base("Бухгалтерия предприятия").CacheText);
        Assert.StartsWith("Освобождено 2 КБ", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roaming_and_permanent_delete_follow_settings()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        fixture.Settings.Settings.Cache.IncludeRoaming = true;
        fixture.Settings.Settings.Cache.DeletePermanently = true;

        await fixture.ViewModel.ClearCacheCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        Assert.Contains("Roaming", fixture.Dialogs.Questions.Single(), StringComparison.Ordinal);
        Assert.Empty(fixture.RecycleBin.Recycled);
        Assert.False(Directory.Exists(Path.Combine(fixture.LocalCacheRoot, BuhId)));
        Assert.False(Directory.Exists(Path.Combine(fixture.RoamingCacheRoot, BuhId)));
        Assert.Equal("нет", fixture.Base("Бухгалтерия предприятия").CacheText);
    }

    [Fact]
    public async Task Busy_cache_is_kept_and_launch_asks_first()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        fixture.CacheUsage.InUse.Add(Path.Combine(fixture.LocalCacheRoot, BuhId));

        await fixture.ViewModel.ClearCacheAndLaunchCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        Assert.True(Directory.Exists(Path.Combine(fixture.LocalCacheRoot, BuhId)));
        Assert.Contains("Не удалены", fixture.Dialogs.Messages.Single(), StringComparison.Ordinal);
        Assert.Contains("уже открыта", fixture.Dialogs.Questions[1], StringComparison.Ordinal);
        Assert.Single(fixture.Processes.Started); // пользователь согласился запустить
    }

    [Fact]
    public async Task Clear_and_launch_starts_base_after_cleaning()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;

        await fixture.ViewModel.ClearCacheAndLaunchCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        Assert.Single(fixture.RecycleBin.Recycled);
        Assert.Single(fixture.Dialogs.Questions);
        Assert.Equal("ENTERPRISE", Assert.Single(fixture.Processes.Started).Arguments[0]);
    }

    [Fact]
    public async Task Declined_confirmation_changes_nothing()
    {
        using var fixture = await LoadAsync();

        await fixture.ViewModel.ClearCacheAndLaunchCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        Assert.Empty(fixture.RecycleBin.Recycled);
        Assert.Empty(fixture.Processes.Started);
    }

    [Fact]
    public async Task Cache_manager_selects_orphans_and_cleans_them()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        CacheManagerViewModel? shown = null;
        fixture.Dialogs.CacheManager = async manager =>
        {
            shown = manager;
            Assert.Equal(2, manager.Rows.Count);
            Assert.Contains("без хозяина: 1 (5 КБ)", manager.TotalText, StringComparison.Ordinal);
            Assert.False(manager.CleanCommand.CanExecute(null));

            manager.SelectOrphansCommand.Execute(null);
            Assert.Equal("Выбрано: 1, к удалению 5 КБ", manager.SelectedText);
            await manager.CleanCommand.ExecuteAsync(null);
            manager.IncludeRoaming = true;
        };

        await fixture.ViewModel.OpenCacheManagerCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.False(Directory.Exists(Path.Combine(fixture.LocalCacheRoot, OrphanId)));
        Assert.Equal("Бухгалтерия предприятия", Assert.Single(shown.Rows).Name);
        Assert.True(fixture.Settings.Settings.Cache.IncludeRoaming); // выбор в окне запоминается
    }

    [Fact]
    public void Windows_probe_detects_open_file_and_keeps_directory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Только Windows.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "yaocl-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "vrs"));
        var file = Path.Combine(directory, "vrs", "1Cv8.1CL");
        File.WriteAllBytes(file, [1, 2, 3]);
        try
        {
            var probe = new WindowsCacheUsageProbe();
            using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                Assert.True(probe.IsDirectoryInUse(directory));
            }

            Assert.False(probe.IsDirectoryInUse(directory));
            Assert.True(File.Exists(file)); // после проверки каталог на месте и с прежним именем
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Freedesktop_trash_moves_directory_and_writes_info()
    {
        var root = Path.Combine(Path.GetTempPath(), "yaocl-trash-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "кэш баз", BuhId);
        var trash = Path.Combine(root, "Trash");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "x");
            var bin = new FreedesktopTrash(trash);

            bin.MoveToRecycleBin(source);
            Directory.CreateDirectory(source);
            bin.MoveToRecycleBin(source); // то же имя — второй экземпляр получает суффикс

            Assert.False(Directory.Exists(source));
            Assert.True(File.Exists(Path.Combine(trash, "files", BuhId, "a.txt")));
            Assert.True(Directory.Exists(Path.Combine(trash, "files", BuhId + ".2")));
            var info = File.ReadAllText(Path.Combine(trash, "info", BuhId + ".trashinfo"));
            Assert.StartsWith("[Trash Info]\nPath=", info, StringComparison.Ordinal);
            Assert.Contains("%D0%BA%D1%8D%D1%88%20%D0%B1%D0%B0%D0%B7", info, StringComparison.Ordinal); // «кэш баз»
            Assert.Contains("\nDeletionDate=", info, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Windows_recycle_bin_round_trip_when_enabled()
    {
        // Кладёт каталог в настоящую корзину пользователя — только по явной просьбе.
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("YAOCL_RECYCLE_TEST") != "1")
        {
            Assert.Skip("Задайте YAOCL_RECYCLE_TEST=1, чтобы проверить настоящую корзину Windows.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "yaocl-recycle-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.txt"), "x");

        new WindowsRecycleBin().MoveToRecycleBin(directory);

        Assert.False(Directory.Exists(directory));
    }

    private static async Task<ViewModelFixture> LoadAsync()
    {
        var fixture = new ViewModelFixture();
        fixture.AddCache(BuhId, 2048);
        fixture.AddCache(BuhId, 100, roaming: true);
        fixture.AddCache(OrphanId, 5 * 1024);
        Directory.CreateDirectory(Path.Combine(fixture.LocalCacheRoot, "tmplts"));
        await fixture.LoadAsync();
        await fixture.ViewModel.CacheScanTask;
        return fixture;
    }
}
