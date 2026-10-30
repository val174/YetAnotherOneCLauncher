using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

public class TagOrderingTests
{
    [Fact]
    public void Editor_moves_sorts_and_applies_order_without_losing_selection()
    {
        var settings = new LauncherSettings();
        var data = new LauncherUserData(settings);
        data.EnsureTag("Бета");
        data.EnsureTag("Альфа");
        data.EnsureTag("Гамма");
        var editor = new TagsEditorViewModel(data.TagCatalog(), new Dictionary<string, int>());
        var selected = editor.SelectedRow!;
        Assert.False(editor.MoveUpCommand.CanExecute(null));
        editor.MoveDownCommand.Execute(null);
        Assert.Same(selected, editor.SelectedRow);
        Assert.Equal("Бета", editor.Rows[0].Name);
        Assert.Equal("Альфа", data.TagCatalog()[0].Name);
        editor.ApplyTo(data);
        Assert.Equal(editor.Rows.Select(r => r.Name), data.TagCatalog().Select(t => t.Name));
        editor.SortAlphabeticallyCommand.Execute(null);
        Assert.Same(selected, editor.SelectedRow);
        Assert.Equal(new[] { "Альфа", "Бета", "Гамма" }, editor.Rows.Select(r => r.Name));
        // Пока пользователь не нажал OK, сохранённый порядок прежний.
        Assert.Equal("Бета", data.TagCatalog()[0].Name);
    }

    [Fact]
    public async Task Custom_order_survives_settings_round_trip()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var data = new LauncherUserData(new LauncherSettings());
            data.EnsureTag("Бета");
            data.EnsureTag("Альфа");
            data.ReorderTags(["Бета", "Альфа"]);
            await store.SaveAsync(data.Settings, TestContext.Current.CancellationToken);
            var loaded = new LauncherUserData((await store.LoadAsync(TestContext.Current.CancellationToken)).Settings);
            Assert.Equal(new[] { "Бета", "Альфа" }, loaded.TagCatalog().Select(t => t.Name));
            loaded.RenameTag("Бета", "Ярлык");
            loaded.EnsureTag("Новая");
            Assert.Equal(new[] { "Ярлык", "Альфа", "Новая" }, loaded.TagCatalog().Select(t => t.Name));
            loaded.DeleteTag("Альфа");
            Assert.Equal(new[] { "Ярлык", "Новая" }, loaded.TagCatalog().Select(t => t.Name));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
