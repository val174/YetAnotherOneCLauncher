using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public async Task Builds_tree_with_folders_and_selects_nothing()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        // Без OrderInTree папки и базы идут вперемешку по имени — как у штатного стартера.
        Assert.True(vm.ShowTree);
        Assert.Equal(new[] { "Копия бухгалтерии", "Рабочие", "Розница (тест)" }, vm.TreeItems.Select(n => n.Name));
        var folder = Assert.IsType<FolderNodeViewModel>(vm.TreeItems[1]);
        Assert.True(folder.IsExpanded);
        Assert.Equal(2, folder.Children.Count);
        Assert.Null(vm.SelectedInfoBase);
        Assert.Contains("Баз: 4", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(3, vm.PlatformCount);
    }

    [Fact]
    public async Task Search_shows_ranked_list_selects_best_match_and_highlights()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        vm.SearchText = ",e["; // «бух» в английской раскладке

        Assert.True(vm.ShowList);
        Assert.Equal(new[] { "Бухгалтерия предприятия", "Копия бухгалтерии" }, vm.ListItems.Select(i => i.Base.Name));
        Assert.Equal("Бухгалтерия предприятия", vm.SelectedInfoBase?.Name);
        Assert.Equal(new TextSegment("Бух", true), vm.ListItems[0].NameSegments[0]);
        Assert.False(vm.ShowNothingFound);

        vm.SearchText = "нет такой базы";
        Assert.Empty(vm.ListItems);
        Assert.True(vm.ShowNothingFound);
        Assert.Null(vm.SelectedInfoBase);

        vm.ClearSearchCommand.Execute(null);
        Assert.True(vm.ShowTree);
    }

    [Fact]
    public async Task Favorites_appear_first_in_tree_and_list_and_boost_search()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        vm.ToggleFavoriteCommand.Execute(fixture.Base("Копия бухгалтерии"));

        var favorites = Assert.IsType<FolderNodeViewModel>(vm.TreeItems[0]);
        Assert.Equal(FolderKind.Favorites, favorites.Kind);
        Assert.Equal("Копия бухгалтерии", Assert.Single(favorites.Children).Name);
        Assert.True(fixture.Base("Копия бухгалтерии").IsFavorite);
        Assert.Single(fixture.Settings.Settings.Favorites);

        vm.IsTreeMode = false;
        Assert.Equal("Копия бухгалтерии", vm.ListItems[0].Base.Name);

        // Совпадение с начала имени («Бухгалтерия…») важнее избранного («Копия бухгалтерии»).
        vm.SearchText = "бух";
        Assert.Equal("Бухгалтерия предприятия", vm.ListItems[0].Base.Name);
    }

    [Fact]
    public async Task Launch_starts_process_records_history_and_updates_recent()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";

        await vm.LaunchEnterpriseCommand.ExecuteAsync(null);

        var command = Assert.Single(fixture.Processes.Started);
        Assert.EndsWith("1cv8c.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal(new[] { "ENTERPRISE", "/S", @"srv-1c\zup" }, command.Arguments);
        Assert.Equal(1, fixture.Base("Зарплата и управление персоналом").LaunchCount);
        Assert.Contains("Предприятие", fixture.Base("Зарплата и управление персоналом").LastLaunchText, StringComparison.Ordinal);

        vm.SearchText = string.Empty;
        var recent = Assert.IsType<FolderNodeViewModel>(vm.TreeItems[0]);
        Assert.Equal(FolderKind.Recent, recent.Kind);
        Assert.Equal("Зарплата и управление персоналом", Assert.Single(recent.Children).Name);
        Assert.Equal(0, fixture.Shell.MinimizeCount);
    }

    [Fact]
    public async Task Designer_launch_and_after_launch_action()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.AfterLaunchIndex = (int)AfterLaunchAction.Minimize;

        await vm.LaunchDesignerCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        var command = Assert.Single(fixture.Processes.Started);
        Assert.EndsWith("1cv8.exe", command.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal("DESIGNER", command.Arguments[0]);
        Assert.Equal("8.3.27.2130", command.Platform.Version.ToString()); // Version=8.3 — последняя 8.3
        Assert.Equal(1, fixture.Shell.MinimizeCount);
        Assert.Equal(LaunchMode.Designer, fixture.Settings.Settings.History.Single().Mode);
    }

    [Fact]
    public async Task Missing_version_asks_and_declining_does_not_launch_or_complain()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Dialogs.ConfirmAnswer = false;

        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Копия бухгалтерии")); // Version=8.3.22

        Assert.Contains("8.3.22", Assert.Single(fixture.Dialogs.Questions), StringComparison.Ordinal);
        Assert.Empty(fixture.Processes.Started);
        Assert.Empty(fixture.Dialogs.Messages);
        Assert.Empty(fixture.Settings.Settings.History);

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Копия бухгалтерии"));
        Assert.Equal("8.3.27.2130", Assert.Single(fixture.Processes.Started).Platform.Version.ToString());
    }

    [Fact]
    public async Task Web_client_opens_browser_and_errors_are_shown()
    {
        using var fixture = new ViewModelFixture("""
            [Веб]
            Connect=ws="https://web.example/retail";
            App=WebClient
            [Файловая в веб-клиенте]
            Connect=File="C:\B";
            App=WebClient
            """);
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Веб"));
        Assert.Equal("https://web.example/retail", Assert.Single(fixture.Processes.OpenedUrls).OriginalString);

        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Файловая в веб-клиенте"));
        Assert.Contains("Веб-клиент", Assert.Single(fixture.Dialogs.Messages), StringComparison.Ordinal);
        Assert.Empty(fixture.Processes.Started);
    }

    [Fact]
    public async Task Platform_choice_overrides_version_and_survives_reselection()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "бухгалтерия предприятия";
        Assert.Equal("Как в списке баз", vm.SelectedPlatformChoice?.Label);

        vm.SelectedPlatformChoice = vm.PlatformChoices.Single(c => c.Version == "8.3.24.1667");
        await vm.LaunchEnterpriseCommand.ExecuteAsync(null);

        Assert.Equal("8.3.24.1667", Assert.Single(fixture.Processes.Started).Platform.Version.ToString());
        Assert.Equal("8.3.24.1667", fixture.Base("Бухгалтерия предприятия").PlatformVersionOverride);

        vm.SearchText = "зуп";
        Assert.Equal("Как в списке баз", vm.SelectedPlatformChoice?.Label);
        vm.SearchText = "бухгалтерия предприятия";
        Assert.Equal("8.3.24.1667", vm.SelectedPlatformChoice?.Version);
    }

    [Fact]
    public async Task View_settings_are_stored()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var ui = fixture.Settings.Settings.Ui;

        vm.IsTreeMode = false;
        vm.ThemeIndex = (int)ThemeMode.Dark;
        vm.ShowDetails = false;
        vm.UseThickClientForFileBases = true;
        Folder(vm, "Рабочие").IsExpanded = false;

        Assert.Equal(CatalogViewMode.List, ui.ViewMode);
        Assert.Equal(ThemeMode.Dark, ui.Theme);
        Assert.Equal(ThemeMode.Dark, fixture.Shell.AppliedTheme);
        Assert.False(ui.ShowDetails);
        Assert.True(fixture.Settings.Settings.Launch.UseThickClientForFileBasesByDefault);
        Assert.Equal(["/Рабочие"], ui.CollapsedFolders);

        // После перезагрузки свёрнутая папка остаётся свёрнутой.
        await fixture.LoadAsync();
        Assert.False(Folder(vm, "Рабочие").IsExpanded);
    }

    [Fact]
    public async Task Copy_connection_string_and_open_folder()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var copy = fixture.Base("Копия бухгалтерии");

        await vm.CopyConnectionStringCommand.ExecuteAsync(copy);
        await vm.OpenBaseFolderCommand.ExecuteAsync(copy);

        Assert.Equal("File=\"C:\\Bases\\BuhCopy\";", fixture.Shell.ClipboardText);
        Assert.Equal(@"C:\Bases\BuhCopy", Assert.Single(fixture.Processes.OpenedFolders));
        Assert.False(vm.OpenBaseFolderCommand.CanExecute(fixture.Base("Зарплата и управление персоналом")));
    }

    [Fact]
    public async Task Reload_keeps_selection()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.IsTreeMode = false;
        vm.SelectedListItem = vm.ListItems.Single(i => i.Base.Name == "Розница (тест)");

        await fixture.LoadAsync();

        Assert.Equal("Розница (тест)", vm.SelectedInfoBase?.Name);
    }

    private static FolderNodeViewModel Folder(MainWindowViewModel vm, string name) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == name);
}
