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

        // По умолчанию — по наименованию: сначала папки, затем базы.
        Assert.True(vm.ShowTree);
        Assert.True(vm.IsSortedByName);
        Assert.Equal(new[] { "Рабочие", "Копия бухгалтерии", "Розница (тест)" }, vm.TreeItems.Select(n => n.Name));
        var folder = Assert.IsType<FolderNodeViewModel>(vm.TreeItems[0]);
        Assert.True(folder.IsExpanded);
        Assert.Equal(2, folder.Children.Count);
        Assert.Null(vm.SelectedInfoBase);
        Assert.Contains("Баз: 4", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(3, vm.PlatformCount);
    }

    [Fact]
    public async Task Sort_header_switches_between_name_and_custom_order_and_is_saved()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        vm.ToggleSortCommand.Execute(null);

        // Свой порядок: без OrderInTree папки и базы вперемешку по имени — как у штатного стартера.
        Assert.False(vm.IsSortedByName);
        Assert.Equal(new[] { "Копия бухгалтерии", "Рабочие", "Розница (тест)" }, vm.TreeItems.Select(n => n.Name));
        Assert.Equal(Core.Catalog.CatalogSortMode.Custom, fixture.Settings.Settings.Ui.SortMode);

        vm.ToggleSortCommand.Execute(null);
        Assert.Equal(new[] { "Рабочие", "Копия бухгалтерии", "Розница (тест)" }, vm.TreeItems.Select(n => n.Name));
        Assert.Equal(Core.Catalog.CatalogSortMode.Name, fixture.Settings.Settings.Ui.SortMode);
    }

    [Fact]
    public async Task Recent_mode_shows_only_recent_bases_and_forbids_adding_and_deleting()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        vm.ShowRecentCommand.Execute(null);
        Assert.True(vm.IsRecentMode);
        Assert.True(vm.ShowList);
        Assert.Empty(vm.ListItems);
        Assert.True(vm.ShowNothingFound);
        Assert.Equal("Недавних запусков пока нет", vm.EmptyListText);
        Assert.False(vm.ToggleViewModeCommand.CanExecute(null));

        vm.ShowAllBasesCommand.Execute(null);
        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Зарплата и управление персоналом"));
        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));
        vm.ShowRecentCommand.Execute(null);

        // Свежие сверху; выделена первая, с ней работает всё, кроме добавления, удаления и перестановки.
        Assert.Equal(new[] { "Бухгалтерия предприятия", "Зарплата и управление персоналом" }, vm.ListItems.Select(i => i.Base.Name));
        Assert.Equal("Бухгалтерия предприятия", vm.SelectedInfoBase?.Name);
        Assert.False(vm.CanEditList);
        Assert.False(vm.AddBaseCommand.CanExecute(null));
        Assert.False(vm.AddFolderCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.True(vm.EditCommand.CanExecute(null));
        Assert.True(vm.ToggleFavoriteCommand.CanExecute(null));
        Assert.True(vm.LaunchDesignerCommand.CanExecute(null));

        // Запуск из недавних поднимает базу наверх.
        await vm.LaunchEnterpriseCommand.ExecuteAsync(fixture.Base("Зарплата и управление персоналом"));
        Assert.Equal("Зарплата и управление персоналом", vm.ListItems[0].Base.Name);

        // Поиск — только среди недавних.
        vm.SearchText = "розница";
        Assert.Empty(vm.ListItems);
        Assert.Equal("Ничего не найдено", vm.EmptyListText);
        vm.SearchText = "зуп";
        Assert.Equal("Зарплата и управление персоналом", Assert.Single(vm.ListItems).Base.Name);
        vm.SearchText = string.Empty;

        vm.ShowAllBasesCommand.Execute(null);
        Assert.False(vm.IsRecentMode);
        Assert.True(vm.ShowTree);
        Assert.True(vm.CanEditList);
        Assert.True(vm.AddBaseCommand.CanExecute(null));
        Assert.DoesNotContain(vm.TreeItems, n => n.Name == "Недавние");
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
        Assert.DoesNotContain(vm.TreeItems, n => n.Name == "Недавние"); // недавние — отдельный режим, не папка
        vm.ShowRecentCommand.Execute(null);
        Assert.Equal("Зарплата и управление персоналом", Assert.Single(vm.ListItems).Base.Name);
        Assert.Equal(0, fixture.Shell.MinimizeCount);
    }

    [Fact]
    public void Single_instance_and_tray_settings_are_saved()
    {
        using var fixture = new ViewModelFixture();
        var ui = fixture.Settings.Settings.Ui;
        Assert.False(ui.SingleInstance); // по умолчанию выключены
        Assert.False(ui.MinimizeToTray);

        fixture.ViewModel.SingleInstance = true;
        fixture.ViewModel.MinimizeToTray = true;

        Assert.True(ui.SingleInstance);
        Assert.True(ui.MinimizeToTray);
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
    public async Task Platform_is_chosen_in_launch_with_parameters_once_or_remembered()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "бухгалтерия предприятия";
        LaunchParametersViewModel? form = null;

        // Разово: версия только для этого запуска.
        fixture.Dialogs.LaunchParameters = f =>
        {
            form = f;
            Assert.True(f.ShowPlatform);
            Assert.Equal("Как в списке баз", f.SelectedPlatformChoice?.Label);
            f.SelectedPlatformChoice = f.PlatformChoices.Single(c => c.Version == "8.3.24.1667");
            return (true, LaunchMode.Enterprise);
        };
        await vm.LaunchWithParametersCommand.ExecuteAsync(null);
        Assert.Equal("8.3.24.1667", fixture.Processes.Started[0].Platform.Version.ToString());
        Assert.Null(fixture.Base("Бухгалтерия предприятия").PlatformVersionOverride);

        await vm.LaunchEnterpriseCommand.ExecuteAsync(null);
        Assert.Equal("8.3.27.2130", fixture.Processes.Started[1].Platform.Version.ToString());

        // Запомнить: версия действует и при обычном запуске, в окне выбрана она же.
        fixture.Dialogs.LaunchParameters = f =>
        {
            f.SelectedPlatformChoice = f.PlatformChoices.Single(c => c.Version == "8.3.24.1667");
            f.RememberPlatform = true;
            return (true, LaunchMode.Enterprise);
        };
        await vm.LaunchWithParametersCommand.ExecuteAsync(null);
        Assert.Equal("8.3.24.1667", fixture.Base("Бухгалтерия предприятия").PlatformVersionOverride);
        Assert.Equal("8.3.24.1667", fixture.Base("Бухгалтерия предприятия").PlatformText);

        await vm.LaunchEnterpriseCommand.ExecuteAsync(null);
        Assert.Equal("8.3.24.1667", fixture.Processes.Started[3].Platform.Version.ToString());

        fixture.Dialogs.LaunchParameters = f =>
        {
            Assert.Equal("8.3.24.1667", f.SelectedPlatformChoice?.Version);
            f.SelectedPlatformChoice = f.PlatformChoices[0]; // «Как в списке баз»
            f.RememberPlatform = true;
            return (true, LaunchMode.Enterprise);
        };
        await vm.LaunchWithParametersCommand.ExecuteAsync(null);
        Assert.Null(fixture.Base("Бухгалтерия предприятия").PlatformVersionOverride);
        Assert.Equal("8.3.27.2130", fixture.Processes.Started[4].Platform.Version.ToString());
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
