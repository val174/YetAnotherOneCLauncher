using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Проекты 1C:EDT в поиске, режим «Проекты 1C:EDT», кнопка EDT в строках, выключатель в настройках.</summary>
public class EdtSearchAndModeTests
{
    private static async Task<ViewModelFixture> LoadedAsync()
    {
        var fixture = new ViewModelFixture();
        fixture.Edt.Catalog = FakeEdt.Sample();
        await fixture.LoadAsync();
        return fixture;
    }

    private static List<string> Names(MainWindowViewModel vm) =>
        [.. vm.ListItems.Select(i => string.Concat(i.NameSegments.Select(s => s.Text)))];

    [Fact]
    public async Task Search_finds_projects_after_bases_by_name_or_workspace()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;

        vm.SearchText = "торг";
        var project = Assert.IsType<EdtListItemViewModel>(Assert.Single(vm.ListItems));
        Assert.Equal("Торговля", project.Project.Name);
        Assert.Equal([new TextSegment("Торг", true), new TextSegment("овля", false)], project.NameSegments);
        Assert.True(vm.IsEdtProjectSelected); // единственная найденная строка выделена
        Assert.Null(vm.SelectedInfoBase);

        vm.SearchText = "edt tools"; // по пути рабочей области: D:\edt\tools
        Assert.Equal(["Инструменты"], Names(vm));

        vm.SearchText = "бух"; // базы — первыми; проектов с «бух» нет
        Assert.All(vm.ListItems, i => Assert.IsType<BaseListItemViewModel>(i));
    }

    [Fact]
    public async Task Edt_mode_lists_only_projects_and_searches_in_them()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        Assert.True(vm.IsEdtModeAvailable);

        vm.IsEdtProjectsMode = true;

        Assert.False(vm.ShowTree);
        Assert.Equal(["Архив", "Инструменты", "Торговля"], Names(vm));
        Assert.Equal("Показаны проекты 1C:EDT.", vm.StatusText);
        Assert.False(vm.CanEditList); // добавлять базы и папки — только во «Всех базах»

        vm.SearchText = "арх";
        Assert.Equal(["Архив"], Names(vm));

        vm.SearchText = "нет такого";
        Assert.True(vm.ShowNothingFound);
    }

    [Fact]
    public async Task Row_button_opens_project_of_row_and_linked_base()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        var tools = vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.IsEdtProjects).Children.OfType<EdtProjectNodeViewModel>().Single(p => p.Name == "Инструменты");

        // Кнопка в строке проекта — этот проект, даже если выделено другое.
        await vm.OpenInEdtCommand.ExecuteAsync(tools);
        Assert.Equal(@"C:\EDT\2025.2\1cedt\1cedt.exe", Assert.Single(fixture.Processes.OpenedPrograms).Path);

        // У базы кнопка есть, только если база связана с проектом.
        var zup = vm.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом");
        Assert.False(zup.HasEdtProject);
        Assert.False(vm.OpenInEdtCommand.CanExecute(zup));
        vm.SetEdtProject(zup, FakeEdt.Sample().Projects[0]);
        Assert.Equal("Торговля", zup.EdtProjectName);
        Assert.Equal("1C:EDT — Торговля", zup.EdtButtonToolTip);
        await vm.OpenInEdtCommand.ExecuteAsync(zup);
        Assert.Equal(@"C:\EDT\2025.1\1cedt\1cedt.exe", fixture.Processes.OpenedPrograms[^1].Path);
    }

    [Fact]
    public async Task Switching_edt_off_hides_group_search_mode_and_buttons()
    {
        using var fixture = await LoadedAsync();
        var vm = fixture.ViewModel;
        var zup = vm.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом");
        vm.SetEdtProject(zup, FakeEdt.Sample().Projects[0]);
        vm.IsEdtProjectsMode = true;

        fixture.Dialogs.SettingsEditor = settings =>
        {
            Assert.True(settings.ShowEdtProjects);
            settings.ShowEdtProjects = false;
            return true;
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.False(fixture.Settings.Settings.Edt.ShowProjects);
        Assert.True(vm.IsAllBasesMode); // режима «Проекты 1C:EDT» больше нет
        Assert.False(vm.IsEdtModeAvailable);
        Assert.False(vm.IsEdtEnabled);
        Assert.DoesNotContain(vm.TreeItems, n => n is FolderNodeViewModel { IsEdtProjects: true });
        Assert.Null(zup.EdtProjectName); // кнопки в строке нет
        Assert.False(vm.OpenInEdtCommand.CanExecute(zup));
        vm.SearchText = "торг";
        Assert.Empty(vm.ListItems);

        // Связь не потеряна: включили — снова есть.
        vm.ShowEdtProjects = true;
        Assert.Equal("Торговля", zup.EdtProjectName);
    }

    [AvaloniaFact]
    public async Task Toolbar_segment_and_row_buttons_render()
    {
        using var fixture = new ViewModelFixture();
        fixture.Edt.Catalog = FakeEdt.Sample();
        if (Environment.GetEnvironmentVariable("YAOCL_EDT_ICON") is { } iconPath && File.Exists(iconPath))
        {
            fixture.Edt.Icon = new Avalonia.Media.Imaging.Bitmap(iconPath);
        }

        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;
        Assert.True(window.FindControl<RadioButton>("EdtProjectsButton")!.IsEffectivelyVisible);

        // База со связанным проектом: в строке — кнопка EDT рядом с «Конфигуратором».
        var buh = vm.TreeItems.OfType<FolderNodeViewModel>().First(f => f.IsRegularFolder).Children.OfType<BaseNodeViewModel>().First();
        vm.SetEdtProject(buh.Base, FakeEdt.Sample().Projects[0]);
        vm.SelectedTreeItem = buh;
        MainWindowTests.Render();
        var tree = window.FindControl<TreeView>("CatalogTree")!;
        Assert.Contains(tree.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("edt") && b.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "49-edt-row-button");

        vm.IsEdtProjectsMode = true;
        MainWindowTests.Render();
        vm.SelectedListItem = vm.ListItems[2];
        MainWindowTests.Render();
        MainWindowTests.Snapshot(window, "49-edt-mode");

        vm.IsAllBasesMode = true;
        vm.SearchText = "т";
        MainWindowTests.Render();
        MainWindowTests.Snapshot(window, "49-edt-search");
        window.Close();
    }
}
