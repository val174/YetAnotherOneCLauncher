using System.Text;
using Microsoft.Extensions.Time.Testing;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Копии личного списка перед загрузкой из файлов и восстановление.</summary>
public sealed class ListBackupStoreTests : IDisposable
{
    private const string List = "[Бухгалтерия]\r\nConnect=File=\"C:\\Bases\\Buh\";\r\n[Склад]\r\nConnect=Srvr=\"srv\";Ref=\"sklad\";\r\n";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("yaocl-backups-");

    private string BackupsDirectory => Path.Combine(_directory.FullName, "list-backups");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Backup_keeps_exact_list_bytes_description_and_settings()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 15, 2, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var store = new ListBackupStore(BackupsDirectory, time);
        var settings = new LauncherSettings
        {
            Favorites = [new InfoBaseRef { ConnectionKey = "conn:x", Name = "Склад" }],
            FolderProfiles = [new FolderLaunchProfile { FolderPath = "/A", Parameters = "/Debug" }],
        };
        var bytes = Encoding.UTF8.GetBytes(List);

        var backup = await store.CreateAsync(bytes, ListBackupReason.Import, settings, "work.json", ImportMode.Replace);

        Assert.Equal(Path.Combine(BackupsDirectory, "ibases-2026-10-13_06-15-02.v8i"), backup.ListPath);
        Assert.Equal(bytes, await ListBackupStore.ReadListAsync(backup));
        var listed = Assert.Single(store.List());
        Assert.Equal(backup.ListPath, listed.ListPath);
        Assert.Equal(ListBackupReason.Import, listed.Info.Reason);
        Assert.Equal("work.json", listed.Info.SourceFile);
        Assert.Equal(ImportMode.Replace, listed.Info.Mode);
        Assert.Equal(2, listed.Info.EntryCount);
        Assert.Equal(settings.Favorites, listed.Info.Settings.Favorites);
        Assert.Equal(settings.FolderProfiles, listed.Info.Settings.FolderProfiles);
        Assert.Contains("\"reason\": \"Import\"", await File.ReadAllTextAsync(Path.ChangeExtension(backup.ListPath, ".json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_last_ten_backups_are_kept_newest_first()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero));
        var store = new ListBackupStore(BackupsDirectory, time);
        for (var i = 0; i < 12; i++)
        {
            await store.CreateAsync(Encoding.UTF8.GetBytes($"[База {i}]\r\nConnect=File=\"C:\\{i}\";\r\n"), ListBackupReason.Import, new LauncherSettings());
            time.Advance(TimeSpan.FromMinutes(1));
        }

        var backups = store.List();

        Assert.Equal(ListBackupStore.MaxBackups, backups.Count);
        Assert.Contains("[База 11]", Encoding.UTF8.GetString(await ListBackupStore.ReadListAsync(backups[0])), StringComparison.Ordinal);
        Assert.Contains("[База 2]", Encoding.UTF8.GetString(await ListBackupStore.ReadListAsync(backups[^1])), StringComparison.Ordinal);
        Assert.Equal(20, Directory.GetFiles(BackupsDirectory).Length); // список и описание у каждой
    }

    [Fact]
    public async Task Backups_within_one_second_get_distinct_names_and_missing_list_is_empty_backup()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero));
        var store = new ListBackupStore(BackupsDirectory, time);

        var first = await store.CreateAsync(null, ListBackupReason.Import, new LauncherSettings());
        var second = await store.CreateAsync(null, ListBackupReason.Restore, new LauncherSettings());

        Assert.NotEqual(first.ListPath, second.ListPath);
        Assert.Equal(0, first.Info.EntryCount);
        Assert.Empty(await ListBackupStore.ReadListAsync(first));
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Damaged_descriptions_are_skipped_and_missing_directory_is_empty()
    {
        var store = new ListBackupStore(BackupsDirectory);
        Assert.Empty(store.List());

        Directory.CreateDirectory(BackupsDirectory);
        File.WriteAllText(Path.Combine(BackupsDirectory, "ibases-x.v8i"), List);
        File.WriteAllText(Path.Combine(BackupsDirectory, "ibases-x.json"), "{ не json");
        File.WriteAllText(Path.Combine(BackupsDirectory, "ibases-y.v8i"), List); // без описания

        Assert.Empty(store.List());
    }

    [Fact]
    public void Settings_snapshot_replaces_settings_of_bases_only()
    {
        var snapshot = ListBackupSettings.From(new LauncherSettings
        {
            Favorites = [new InfoBaseRef { ConnectionKey = "conn:old", Name = "Старая" }],
        });
        var current = new LauncherSettings
        {
            Favorites = [new InfoBaseRef { ConnectionKey = "conn:new", Name = "Новая" }],
            InfoBaseProfiles = [new InfoBaseLaunchProfile { Parameters = "/New" }],
            ParameterTemplates = [new Launching.ParameterTemplate()],
        };

        snapshot.ApplyTo(current);

        Assert.Equal("Старая", Assert.Single(current.Favorites).Name);
        Assert.Empty(current.InfoBaseProfiles);
        Assert.Single(current.ParameterTemplates); // общие настройки не трогаются
    }

    [Fact]
    public async Task Replace_writes_list_as_is_and_keeps_previous_in_bak()
    {
        var path = Path.Combine(_directory.FullName, "ibases.v8i");
        await File.WriteAllTextAsync(path, "[Текущая]\r\nConnect=File=\"C:\\Now\";\r\n");
        using var store = new PersonalListStore(path);
        var content = Encoding.UTF8.GetBytes(List);

        var document = await store.ReplaceAsync(content);

        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.Equal(["Бухгалтерия", "Склад"], document.Sections.Where(s => s.Name.Length > 0).Select(s => s.Name));
        Assert.Contains("[Текущая]", await File.ReadAllTextAsync(store.BackupPath), StringComparison.Ordinal);
        Assert.Equal(await store.CurrentFingerprintAsync(), store.LastWrittenFingerprint); // своя запись — не чужое изменение
        Assert.Equal(content, await store.ReadContentAsync());
    }
}
