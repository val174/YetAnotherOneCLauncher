using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Tests;

public class PersonalListStoreTests
{
    [Fact]
    public async Task First_write_makes_backup_later_writes_do_not_overwrite_it()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ibases.v8i");
        var original = Fixtures.Read("ibases_sample.v8i");
        await File.WriteAllBytesAsync(path, original);
        using var store = new PersonalListStore(path);

        await store.EditAsync(d => PersonalListEditor.AddFolder(d, "/", "Первая"));
        await store.EditAsync(d => PersonalListEditor.AddFolder(d, "/", "Вторая"));

        Assert.Equal(original, await File.ReadAllBytesAsync(store.BackupPath));
        var saved = V8iDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { "Первая", "Вторая" }, saved.Sections.TakeLast(2).Select(s => s.Name));
        Assert.Equal(await store.CurrentFingerprintAsync(), store.LastWrittenFingerprint);
    }

    [Fact]
    public async Task Missing_list_is_created_without_backup()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("1C", "1CEStart", "ibases.v8i");
        using var store = new PersonalListStore(path);

        var document = await store.EditAsync(d => PersonalListEditor.AddBase(d, new InfoBaseDraft { Name = "Первая", FilePath = @"C:\B" }));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(store.BackupPath));
        Assert.Equal("Первая", Assert.Single(document.Sections).Name);

        // Как у штатного стартера: UTF-8 с BOM.
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(path))[..3]);
    }

    [Fact]
    public async Task Change_by_another_program_during_edit_is_not_lost()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ibases.v8i");
        await File.WriteAllBytesAsync(path, Fixtures.Read("ibases_sample.v8i"));
        using var store = new PersonalListStore(path);
        var attempts = 0;

        await store.EditAsync(document =>
        {
            attempts++;
            if (attempts == 1)
            {
                // Пока мы готовим правку, стартер 1С дописывает свою запись.
                var other = V8iDocument.Parse(File.ReadAllBytes(path));
                PersonalListEditor.AddFolder(other, "/", "От стартера");
                File.WriteAllBytes(path, other.ToBytes());
            }

            PersonalListEditor.AddFolder(document, "/", "От лаунчера");
        });

        var saved = V8iDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(2, attempts);
        Assert.Contains(saved.Sections, s => s.Name == "От стартера");
        Assert.Contains(saved.Sections, s => s.Name == "От лаунчера");
    }

    [Fact]
    public async Task Failed_edit_leaves_file_untouched()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ibases.v8i");
        var original = Fixtures.Read("ibases_sample.v8i");
        await File.WriteAllBytesAsync(path, original);
        using var store = new PersonalListStore(path);

        await Assert.ThrowsAsync<ListEditException>(() =>
            store.EditAsync(d => PersonalListEditor.AddFolder(d, "/", "Бухгалтерия")));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(store.BackupPath));
        Assert.Null(store.LastWrittenFingerprint);
    }

    [Fact]
    public async Task Round_trip_through_store_without_changes_is_byte_exact()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ibases.v8i");
        var original = Fixtures.Read("ibases_sample.v8i");
        await File.WriteAllBytesAsync(path, original);
        using var store = new PersonalListStore(path);

        await store.EditAsync(_ => { });

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
}
