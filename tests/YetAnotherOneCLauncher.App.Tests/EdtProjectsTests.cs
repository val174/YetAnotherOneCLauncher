using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Edt;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Проекты 1C:EDT: группа в дереве, открытие проекта, версия EDT, связь базы с проектом.</summary>
public class EdtProjectsTests
{
    private const string ZupId = "00000000-0000-0000-0000-000000000002";

    private static async Task<ViewModelFixture> LoadedAsync(Action<FakeEdt>? setup = null)
    {
        var fixture = new ViewModelFixture();
        fixture.Edt.Catalog = FakeEdt.Sample();
        setup?.Invoke(fixture.Edt);
        await fixture.LoadAsync();
        return fixture;
    }

    private static FolderNodeViewModel? Group(MainWindowViewModel vm) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().SingleOrDefault(f => f.IsEdtProjects);

    private static EdtProjectNodeViewModel Project(MainWindowViewModel vm, string name) =>
        Group(vm)!.Children.OfType<EdtProjectNodeViewModel>().Single(p => p.Name == name);

    [Fact]
    public async Task Group_is_under_favorites_with_projects_by_name()
    {
        using var fixture = await LoadedAsync(edt => edt.OpenWorkspaces.Add(@"D:\edt\tools"));
        var vm = fixture.ViewModel;

        var group = Group(vm)!;
        var regular = vm.TreeItems.OfType<FolderNodeViewModel>().First(f => f.IsRegularFolder);
        Assert.True(vm.TreeItems.IndexOf(group) < vm.TreeItems.IndexOf(regular)); // над папками списка
        Assert.Equal(["Архив", "Инструменты", "Торговля"], group.Children.Select(c => c.Name));

        var trade = Project(vm, "Торговля");
        Assert.Equal(("EDT 2025.1", "Java 17", false), (trade.VersionText, trade.JavaText, trade.IsVersionMissing));
        var old = Project(vm, "Архив");
        Assert.True(old.IsVersionMissing); // версии проекта нет — откроется в самой новой
        Assert.Equal("EDT 2025.2", old.VersionText);
        Assert.Contains("сконвертирован", old.VersionToolTip, StringComparison.Ordinal);
        Assert.True(Project(vm, "Инструменты").IsOpen);
        Assert.False(trade.IsOpen);
    }

    [Fact]
    public async Task No_edt_start_or_switched_off_means_no_group()
    {
        using var empty = new ViewModelFixture();
        await empty.LoadAsync();
        Assert.Null(Group(empty.ViewModel));

        using var fixture = await LoadedAsync();
        fixture.ViewModel.ShowEdtProjects = false;
        Assert.Null(Group(fixture.ViewModel));
        Assert.False(fixture.Settings.Settings.Edt.ShowProjects);
        fixture.ViewModel.ShowEdtProjects = true;
        Assert.NotNull(Group(fixture.ViewModel));
    }

    [Fact]
    public async Task Selected_project_opens_in_its_edt_version_with_java()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Project(vm, "Торговля");

        Assert.True(vm.IsEdtProjectSelected);
        Assert.Null(vm.SelectedInfoBase);
        Assert.Equal("Свойства проекта 1C:EDT", vm.PropertiesHeaderText);
        Assert.False(vm.ShowNoSelectionHint);
        Assert.Equal(
            "\"C:\\EDT\\2025.1\\1cedt\\1cedt.exe\" -data \"D:\\edt\\trade\" -vm \"C:\\jdk\\axiom-jdk-full-17.0.16+12-x86_64\\bin\\javaw.exe\"",
            vm.SelectedEdtCommandLine);

        await vm.OpenInEdtCommand.ExecuteAsync(null);

        var (path, arguments) = Assert.Single(fixture.Processes.OpenedPrograms);
        Assert.Equal(@"C:\EDT\2025.1\1cedt\1cedt.exe", path);
        Assert.Equal("-data \"D:\\edt\\trade\" -vm \"C:\\jdk\\axiom-jdk-full-17.0.16+12-x86_64\\bin\\javaw.exe\"", arguments);
        Assert.Contains("открывается в 1C:EDT 2025.1", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_project_is_not_launched_twice()
    {
        using var fixture = await LoadedAsync(edt => edt.OpenWorkspaces.Add(@"D:\edt\trade"));
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Project(vm, "Торговля");

        await vm.OpenInEdtCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Processes.OpenedPrograms);
        Assert.Equal("Проект «Торговля» уже открыт в 1C:EDT.", Assert.Single(fixture.Dialogs.Messages));
    }

    [Fact]
    public async Task Missing_version_is_chosen_once_with_conversion_warning()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Project(vm, "Архив");
        fixture.Dialogs.ChoiceAnswer = 1; // 1C:EDT 2025.1 (новые — первыми)

        await vm.OpenInEdtCommand.ExecuteAsync(null);

        var (question, options) = Assert.Single(fixture.Dialogs.Choices);
        Assert.Contains("сконвертировать", question, StringComparison.Ordinal);
        Assert.Equal(["1C:EDT 2025.2", "1C:EDT 2025.1"], options);
        Assert.Equal(@"C:\EDT\2025.1\1cedt\1cedt.exe", Assert.Single(fixture.Processes.OpenedPrograms).Path);
        Assert.Equal("p-2025", fixture.Settings.Settings.Edt.ProjectInstallations["pr-old"]);

        // Второй раз — без вопроса, в выбранной версии.
        await vm.OpenInEdtCommand.ExecuteAsync(null);
        Assert.Single(fixture.Dialogs.Choices);
        Assert.Equal(2, fixture.Processes.OpenedPrograms.Count);
    }

    [Fact]
    public async Task Base_linked_in_form_gets_edt_button_and_project_lists_it()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            Assert.True(editor.ShowEdtProjectField);
            Assert.Equal("Не выбран", editor.EdtProjectText);
            fixture.Dialogs.ChoiceAnswer = 2; // «Торговля» — третий по названию (как в дереве)
            editor.ChooseEdtProjectCommand.Execute(null);
            return true;
        };

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal("Торговля", shown!.EdtProjectText);
        var profile = fixture.Settings.UserData.LaunchProfile(vm.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").InfoBase)!;
        Assert.Equal(("pr-trade", @"D:\edt\trade"), (profile.EdtProjectId, profile.EdtWorkspace));

        vm.SearchText = "зуп";
        Assert.True(vm.HasSelectedBaseEdtProject);
        Assert.Equal("1C:EDT — Торговля", vm.SelectedBaseEdtButtonText);
        Assert.Equal("Торговля", vm.SelectedBaseEdtProjectText);
        await vm.OpenInEdtCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\EDT\2025.1\1cedt\1cedt.exe", Assert.Single(fixture.Processes.OpenedPrograms).Path);

        vm.SearchText = string.Empty;
        vm.SelectedTreeItem = Project(vm, "Торговля");
        Assert.Equal("Зарплата и управление персоналом", vm.SelectedEdtProjectBases);
    }

    [Fact]
    public async Task Edt_own_binding_is_suggested_and_link_can_be_cleared()
    {
        using var fixture = await LoadedAsync(edt => edt.Bindings[@"D:\edt\tools"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ZupId });
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Project(vm, "Инструменты");
        Assert.Equal("Зарплата и управление персоналом", vm.SelectedEdtProjectBases); // EDT сам связал

        vm.SearchText = "зуп";
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.True(editor.ShowEdtSuggestion);
            Assert.Equal("1C:EDT связывает эту базу с проектом «Инструменты».", editor.EdtSuggestionText);
            editor.AcceptEdtSuggestionCommand.Execute(null);
            Assert.False(editor.ShowEdtSuggestion);
            return true;
        };
        await vm.EditCommand.ExecuteAsync(null);
        vm.SearchText = "зуп";
        Assert.Equal("1C:EDT — Инструменты", vm.SelectedBaseEdtButtonText);

        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.True(editor.ClearEdtProjectCommand.CanExecute(null));
            editor.ClearEdtProjectCommand.Execute(null);
            return true;
        };
        await vm.EditCommand.ExecuteAsync(null);
        vm.SearchText = "зуп";
        Assert.False(vm.HasSelectedBaseEdtProject);
        Assert.Null(fixture.Settings.UserData.LaunchProfile(vm.SelectedInfoBase!.InfoBase)); // пустой профиль удалён
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Tree_group_project_panel_and_form_render(string theme)
    {
        using var fixture = new ViewModelFixture();
        fixture.Edt.Catalog = FakeEdt.Sample();
        fixture.Edt.OpenWorkspaces.Add(@"D:\edt\tools");
        // Значок EDT для снимков — только если указан файл (у тестов своего значка нет: он с компьютера пользователя).
        if (Environment.GetEnvironmentVariable("YAOCL_EDT_ICON") is { } iconPath && File.Exists(iconPath))
        {
            fixture.Edt.Icon = new Avalonia.Media.Imaging.Bitmap(iconPath);
        }

        var window = await MainWindowTests.OpenAsync(fixture);
        window.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var vm = fixture.ViewModel;

        vm.SelectedTreeItem = Project(vm, "Торговля");
        MainWindowTests.Render();
        Assert.True(window.FindControl<Button>("OpenInEdtButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<ScrollViewer>("EdtProjectPanel")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("LaunchEnterpriseButton")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "48-edt-project-" + theme);

        vm.SelectedTreeItem = vm.TreeItems.OfType<FolderNodeViewModel>().First(f => f.IsRegularFolder).Children.OfType<BaseNodeViewModel>().First();
        vm.SetEdtProject(vm.SelectedInfoBase!, FakeEdt.Sample().Projects[0]);
        MainWindowTests.Render();
        Assert.True(window.FindControl<Button>("BaseEdtButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("LaunchEnterpriseButton")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "48-edt-base-link-" + theme);
        window.Close();

        if (theme == "light")
        {
            var editor = new InfoBaseEditorViewModel(new Core.Editing.InfoBaseDraft { Name = "База", FilePath = @"C:\B" }, [], isNew: false, new FakeFiles())
            {
                EdtProjects = FakeEdt.Sample().Projects,
                InitialEdtProjectId = "pr-trade",
                SuggestedEdtProject = FakeEdt.Sample().Projects[2],
            };
            var form = new InfoBaseEditorWindow(editor);
            form.Show();
            MainWindowTests.Render();
            Assert.Equal("Торговля", form.FindControl<TextBox>("EdtProjectBox")!.Text);
            Assert.True(form.FindControl<Border>("EdtSuggestion")!.IsEffectivelyVisible);
            MainWindowTests.Snapshot(form, "48-edt-form");
            form.Close();
        }
    }
}
