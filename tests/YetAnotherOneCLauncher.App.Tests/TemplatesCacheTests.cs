using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Шаблоны — из кэша; каталоги просматриваются только по «Обновить» (задача #29).</summary>
public class TemplatesCacheTests
{
    private static string CachePath(ViewModelFixture fixture) => System.IO.Path.Combine(fixture.Directory, TemplateCache.FileName);

    /// <summary>Открыть форму новой базы, перейти к шагу шаблона и выполнить <paramref name="step"/>.</summary>
    private static async Task OnTemplatePageAsync(ViewModelFixture fixture, Action<InfoBaseEditorViewModel> step)
    {
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.IsTemplateMode = true;
            editor.NextCommand.Execute(null);
            Assert.True(editor.ShowTemplatePage);
            step(editor);
            return false;
        };
        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Templates_are_scanned_only_on_refresh_and_then_shown_from_cache()
    {
        using var fixture = new ViewModelFixture();
        var (cf, _) = CreateInfoBaseTests.AddTemplate(fixture, "3_0_150_20", "3.0.150.20", withDump: false);
        await fixture.LoadAsync();

        await OnTemplatePageAsync(fixture, editor =>
        {
            Assert.Empty(editor.TemplateGroups);
            Assert.True(editor.ShowTemplatesNeverLoaded);
            Assert.Equal("Список шаблонов ещё не загружался.", editor.TemplatesStatusText);
            Assert.True(editor.RefreshTemplatesCommand.CanExecute(null));

            editor.RefreshTemplatesCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Equal(cf, Assert.Single(Assert.Single(editor.TemplateGroups).Items).Template.Path);
            Assert.StartsWith("Список обновлён ", editor.TemplatesStatusText, StringComparison.Ordinal);
            Assert.False(editor.IsRefreshingTemplates);
        });
        Assert.True(File.Exists(CachePath(fixture)));

        // Новый шаблон в каталоге — в форме его нет, пока не нажать «Обновить»: показывается кэш.
        CreateInfoBaseTests.AddTemplate(fixture, "3_0_151_1", "3.0.151.1", withDump: false);
        await OnTemplatePageAsync(fixture, editor =>
        {
            Assert.Equal(["3.0.150.20"], editor.TemplateGroups.Single().Items.Select(i => i.Text));
            Assert.NotNull(editor.TemplatesUpdatedAt);
            Assert.False(editor.TemplateRootsChanged);
            editor.RefreshTemplatesCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(["3.0.151.1", "3.0.150.20"], editor.TemplateGroups.Single().Items.Select(i => i.Text));
        });
    }

    [Fact]
    public async Task Selected_template_that_no_longer_exists_is_reported_and_blocks_next()
    {
        using var fixture = new ViewModelFixture();
        var (cf, _) = CreateInfoBaseTests.AddTemplate(fixture, "3_0_150_20", "3.0.150.20", withDump: false);
        await fixture.LoadAsync();
        await OnTemplatePageAsync(fixture, editor => editor.RefreshTemplatesCommand.ExecuteAsync(null).GetAwaiter().GetResult());
        File.Delete(cf);

        await OnTemplatePageAsync(fixture, editor =>
        {
            editor.SelectedTemplateNode = editor.TemplateGroups[0];
            WaitFor(() => editor.IsSelectedTemplateMissing);
            Assert.Contains("больше нет", editor.Errors, StringComparison.Ordinal);
            Assert.Contains(cf, editor.Errors, StringComparison.Ordinal);
            Assert.Contains("Нажмите «Обновить»", editor.Errors, StringComparison.Ordinal);

            editor.NextCommand.Execute(null);
            Assert.True(editor.ShowTemplatePage); // дальше не пускает
            Assert.Contains("больше нет", editor.Errors, StringComparison.Ordinal);

            // После обновления пропавшего шаблона в списке нет, выбор снят.
            editor.RefreshTemplatesCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Empty(editor.TemplateGroups);
            Assert.Null(editor.SelectedTemplate);
            Assert.True(editor.ShowNoTemplatesFound);
        });
    }

    [Fact]
    public async Task Changed_template_locations_suggest_refresh()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        await new TemplateCache(CachePath(fixture)).SaveAsync(
            TemplateCacheData.From([], [@"D:\Другой каталог"], DateTimeOffset.Now), TestContext.Current.CancellationToken);

        await OnTemplatePageAsync(fixture, editor =>
        {
            Assert.True(editor.TemplateRootsChanged);
            Assert.Contains("обновите список", editor.TemplatesStatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Refresh_runs_in_background_keeps_browsed_files_and_selection()
    {
        var pending = new TaskCompletionSource<TemplateCacheData>();
        var cached = new ConfigurationTemplate("Торговля", "1.0", @"C:\t\1cv8.cf", null);
        var files = new FakeFiles { OpenAnswer = @"C:\Мои\base.dt" };
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, files)
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            FoundTemplates = [cached],
            TemplatesUpdatedAt = DateTimeOffset.Now,
            TemplateRefresher = () => pending.Task,
            TemplateFileExists = _ => true,
        };
        await editor.BrowseTemplateCommand.ExecuteAsync(null);
        editor.SelectTemplate(editor.TemplateGroups.SelectMany(g => g.Items).Single(i => i.Template.Catalog == "Торговля").Template);

        var refreshing = editor.RefreshTemplatesCommand.ExecuteAsync(null);

        Assert.True(editor.IsRefreshingTemplates);
        Assert.False(editor.RefreshTemplatesCommand.CanExecute(null)); // второй раз не запускается
        pending.SetResult(TemplateCacheData.From([cached with { Version = "1.0" }, new ConfigurationTemplate("Склад", "2.0", @"C:\s\1cv8.cf", null)], [], DateTimeOffset.Now));
        await refreshing;

        Assert.False(editor.IsRefreshingTemplates);
        Assert.Equal(["base.dt", "Склад", "Торговля"], editor.TemplateGroups.Select(g => g.Name).Order(StringComparer.Ordinal)); // свой файл остался
        Assert.Equal(@"C:\t\1cv8.cf", editor.SelectedTemplate?.Path); // выбор сохранён
    }

    [Fact]
    public void Editing_existing_base_has_no_refresh()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft { Name = "А" }, [], isNew: false, new FakeFiles());

        Assert.False(editor.CanRefreshTemplates);
        Assert.False(editor.RefreshTemplatesCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Window_shows_spinner_while_refreshing()
    {
        var pending = new TaskCompletionSource<TemplateCacheData>();
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            CreationPlatforms = [ViewModelFixture.Installations[0]],
            TemplateRefresher = () => pending.Task,
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        editor.IsTemplateMode = true;
        editor.NextCommand.Execute(null);
        MainWindowTests.Render();
        Assert.True(window.FindControl<Button>("RefreshTemplatesButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<TextBlock>("TemplatesNeverLoadedText")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "54-templates-not-loaded");

        _ = editor.RefreshTemplatesCommand.ExecuteAsync(null);
        MainWindowTests.Render();
        var refreshing = window.FindControl<StackPanel>("TemplatesRefreshing")!;
        Assert.True(refreshing.IsEffectivelyVisible);
        Assert.Single(refreshing.Children.OfType<Arc>());
        Assert.Equal("Выполняется обновление списка шаблонов", refreshing.Children.OfType<TextBlock>().Single().Text);
        Assert.False(window.FindControl<Button>("RefreshTemplatesButton")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<TextBlock>("TemplatesNeverLoadedText")!.IsVisible);
        MainWindowTests.Snapshot(window, "54-templates-refreshing");

        pending.SetResult(TemplateCacheData.From([new ConfigurationTemplate("Торговля/Розница", "3.0", @"C:\t\1cv8.cf", null)], [], DateTimeOffset.Now));
        MainWindowTests.Render();
        Assert.False(refreshing.IsVisible);
        Assert.Equal(1, window.FindControl<TreeView>("TemplatesTree")!.ItemCount);
        MainWindowTests.Snapshot(window, "54-templates-refreshed");
        window.Close();
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition(), "Условие не выполнилось за отведённое время.");
    }
}
