using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Удаление базы из списка с флажком «Удалить временные файлы информационной базы».</summary>
public class DeleteWithCacheTests
{
    private const string CopyId = "00000000-0000-0000-0000-000000000003"; // «Копия бухгалтерии»

    [Fact]
    public async Task Flag_is_on_by_default_and_removes_base_cache()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(null);

        var (text, isChecked) = Assert.Single(fixture.Dialogs.Options);
        Assert.True(isChecked);
        Assert.Equal("Удалить временные файлы информационной базы (4 КБ)", text);
        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
        Assert.Equal(new[] { Path.Combine(fixture.LocalCacheRoot, CopyId) }, fixture.RecycleBin.Recycled);
        Assert.True(Directory.Exists(Path.Combine(fixture.RoamingCacheRoot, CopyId))); // настройки пользователя — только по настройке
        Assert.Single(fixture.Dialogs.Questions); // второго вопроса про кэш нет
        Assert.Contains("удалена из списка", fixture.ViewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Освобождено 4 КБ", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unchecked_flag_keeps_cache()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        fixture.Dialogs.OptionAnswer = false;

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
        Assert.Empty(fixture.RecycleBin.Recycled);
        Assert.True(Directory.Exists(Path.Combine(fixture.LocalCacheRoot, CopyId)));
    }

    [Fact]
    public async Task Cancel_deletes_nothing()
    {
        using var fixture = await LoadAsync();

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Contains(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
        Assert.Empty(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task Cache_of_open_base_is_kept_and_reported()
    {
        using var fixture = await LoadAsync();
        fixture.Dialogs.ConfirmAnswer = true;
        fixture.CacheUsage.InUse.Add(Path.Combine(fixture.LocalCacheRoot, CopyId));

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
        Assert.True(Directory.Exists(Path.Combine(fixture.LocalCacheRoot, CopyId)));
        Assert.Contains("Не удалены", fixture.Dialogs.Messages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Base_without_cache_is_deleted_without_size_in_flag()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        await fixture.ViewModel.CacheScanTask;
        Select(fixture, "Копия бухгалтерии");
        fixture.Dialogs.ConfirmAnswer = true;

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal("Удалить временные файлы информационной базы", fixture.Dialogs.Options.Single().Text);
        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
    }

    private static async Task<ViewModelFixture> LoadAsync()
    {
        var fixture = new ViewModelFixture();
        fixture.AddCache(CopyId, 4096);
        fixture.AddCache(CopyId, 100, roaming: true);
        await fixture.LoadAsync();
        await fixture.ViewModel.CacheScanTask;
        Select(fixture, "Копия бухгалтерии");
        return fixture;
    }

    private static void Select(ViewModelFixture fixture, string name) =>
        fixture.ViewModel.SelectedTreeItem = fixture.ViewModel.TreeItems.OfType<BaseNodeViewModel>().Single(n => n.Name == name);
}
