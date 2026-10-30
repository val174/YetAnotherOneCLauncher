using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Метки баз и быстрый отбор по ним (задача #25).</summary>
public class TagsTests
{
    private const string Buh = "Бухгалтерия предприятия";
    private const string Zup = "Зарплата и управление персоналом";
    private const string Copy = "Копия бухгалтерии";

    private static FolderNodeViewModel Folder(MainWindowViewModel vm, string name) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == name);

    private static BaseNodeViewModel Node(MainWindowViewModel vm, string name) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().SelectMany(f => f.Children).Concat(vm.TreeItems)
            .OfType<BaseNodeViewModel>().First(b => b.Name == name);

    private static List<string> ListNames(MainWindowViewModel vm) =>
        [.. vm.ListItems.OfType<BaseListItemViewModel>().Select(i => i.Base.Name)];

    /// <summary>Пункт подменю «Метки» по имени.</summary>
    private static TagMenuItemViewModel MenuItem(MainWindowViewModel vm, string header) =>
        vm.TagMenuItems.Single(i => i.Header == header);

    private static async Task<ViewModelFixture> TaggedAsync()
    {
        var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        foreach (var (name, tags) in new[] { (Buh, new[] { "прод", "бух" }), (Zup, ["прод"]), (Copy, ["тест", "бух"]) })
        {
            vm.SelectedTreeItem = Node(vm, name);
            foreach (var tag in tags)
            {
                vm.AddTagCommand.Execute(tag);
            }
        }

        return fixture;
    }

    [Fact]
    public async Task Reordering_tag_bar_preserves_filters_and_assignments()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        var source = vm.TagFilters[0];
        var target = vm.TagFilters[^1];
        vm.ToggleTagFilterCommand.Execute(source);
        var before = ListNames(vm);
        vm.MoveTag(source.Name, target.Name, true);
        Assert.Equal(source.Name, vm.TagFilters[^1].Name);
        Assert.True(vm.TagFilters[^1].IsSelected);
        Assert.Equal(before, ListNames(vm));
    }

    [Fact]
    public async Task Context_menu_creates_and_toggles_tag_on_base()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Zup);
        Assert.True(vm.HasTagTarget);
        Assert.False(vm.HasTags);
        Assert.Equal(["Новая метка…", "Управление метками…"], vm.TagMenuItems.Select(i => i.Header));

        fixture.Dialogs.NewTagEditor = form =>
        {
            Assert.Equal("Метка будет поставлена: «Зарплата и управление персоналом».", form.TargetText);
            Assert.Equal(TagColor.Red, form.Color); // первый цвет по кругу
            Assert.False(form.Validate());
            Assert.Equal("Укажите наименование метки.", form.ErrorText);
            form.Name = "  прод ";
            Assert.Null(form.ErrorText);
            Assert.Equal("прод", form.PreviewName);
            form.ColorIndex = (int)TagColor.Purple;
            Assert.Equal(TagColor.Purple, form.PreviewColor);
            return true;
        };
        await vm.NewTagCommand.ExecuteAsync(null);
        Assert.Equal(TagColor.Purple, fixture.Settings.UserData.Tag("прод")!.Color);

        var zup = fixture.Base(Zup);
        Assert.Equal(["прод"], zup.AssignedTags);
        Assert.Equal("прод", Assert.Single(zup.RowTags).Name);
        Assert.Contains("Метка «прод» поставлена: «Зарплата и управление персоналом». Метка создана.", vm.StatusText, StringComparison.Ordinal);
        Assert.True(vm.HasTags);
        Assert.True(vm.IsTagBarVisible); // строка меток по умолчанию включена
        Assert.Equal("прод · 1", Assert.Single(vm.TagFilters).Text);
        Assert.True(MenuItem(vm, "прод").IsChecked);
        Assert.Equal("-", vm.TagMenuItems[1].Header); // разделитель перед командами

        MenuItem(vm, "прод").Command!.Execute(MenuItem(vm, "прод").CommandParameter);

        Assert.Empty(zup.AssignedTags);
        Assert.False(MenuItem(vm, "прод").IsChecked);
        Assert.False(vm.HasTags); // метка в каталоге осталась, но ни у одной базы её нет
        Assert.Contains("снята", vm.StatusText, StringComparison.Ordinal);
        Assert.NotNull(fixture.Settings.UserData.Tag("прод"));
    }

    [Fact]
    public async Task New_tag_with_existing_name_keeps_its_color_and_cancel_does_nothing()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        var color = fixture.Settings.UserData.Tag("тест")!.Color;
        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        fixture.Dialogs.NewTagEditor = form =>
        {
            Assert.Equal("Метка будет поставлена: 2 базы.", form.TargetText);
            form.Name = "ТЕСТ";
            Assert.True(form.IsExisting);
            Assert.Equal("тест", form.PreviewName);
            Assert.Equal(color, form.PreviewColor);
            form.Color = color == TagColor.Pink ? TagColor.Blue : TagColor.Pink; // выбор цвета скрыт и не действует
            return true;
        };

        await vm.NewTagCommand.ExecuteAsync(null);

        Assert.Equal(color, fixture.Settings.UserData.Tag("тест")!.Color);
        Assert.Contains("тест", fixture.Base(Buh).AssignedTags);
        Assert.Contains("тест", fixture.Base(Zup).AssignedTags);
        Assert.DoesNotContain("Метка создана", vm.StatusText, StringComparison.Ordinal);

        fixture.Dialogs.NewTagEditor = form =>
        {
            form.Name = "отменённая";
            return false;
        };
        await vm.NewTagCommand.ExecuteAsync(null);
        Assert.Null(fixture.Settings.UserData.Tag("отменённая"));
    }

    [Fact]
    public async Task Folder_target_tags_all_its_bases_and_second_toggle_removes_from_all()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Buh);
        vm.AddTagCommand.Execute("прод");

        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        Assert.False(MenuItem(vm, "прод").IsChecked); // есть не у всех баз папки
        vm.ToggleTagCommand.Execute("прод");

        Assert.Equal(["прод"], fixture.Base(Buh).AssignedTags);
        Assert.Equal(["прод"], fixture.Base(Zup).AssignedTags);
        Assert.Contains("2 базы", vm.StatusText, StringComparison.Ordinal);
        Assert.True(MenuItem(vm, "прод").IsChecked);

        vm.ToggleTagCommand.Execute("прод");
        Assert.Empty(fixture.Base(Buh).AssignedTags);
        Assert.Empty(fixture.Base(Zup).AssignedTags);
    }

    [Fact]
    public async Task Filter_bar_shows_bases_with_all_selected_tags_in_flat_list()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        Assert.Equal(["бух · 2", "прод · 2", "тест · 1"], vm.TagFilters.Select(t => t.Text));
        Assert.True(vm.ShowTree);

        vm.ToggleTagFilterCommand.Execute(vm.TagFilters.Single(t => t.Name == "прод"));

        Assert.True(vm.HasTagFilter);
        Assert.False(vm.ShowTree); // при отборе — плоский список, как при поиске
        Assert.Equal([Buh, Zup], ListNames(vm));
        Assert.True(vm.TagFilters.Single(t => t.Name == "прод").IsSelected);
        Assert.Contains("Отбор по меткам: прод — найдено 2", vm.StatusText, StringComparison.Ordinal);

        // Вторая метка — условие «И».
        vm.ToggleTagFilterCommand.Execute(vm.TagFilters.Single(t => t.Name == "бух"));
        Assert.Equal([Buh], ListNames(vm));

        // Поиск — среди отобранных.
        vm.SearchText = "зарплата";
        Assert.Empty(ListNames(vm));
        Assert.True(vm.ShowNothingFound);
        vm.SearchText = string.Empty;

        vm.ClearTagFilterCommand.Execute(null);
        Assert.False(vm.HasTagFilter);
        Assert.True(vm.ShowTree);
        Assert.All(vm.TagFilters, t => Assert.False(t.IsSelected));
    }

    [Fact]
    public async Task Filter_works_inside_favorites_mode()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        fixture.Settings.UserData.SetFavorite(fixture.Base(Copy).InfoBase, true);
        fixture.Settings.UserData.SetFavorite(fixture.Base(Zup).InfoBase, true);
        fixture.Base(Copy).Refresh();
        fixture.Base(Zup).Refresh();
        vm.IsFavoritesMode = true;

        vm.ToggleTagFilterCommand.Execute(vm.TagFilters.Single(t => t.Name == "бух"));

        Assert.Equal([Copy], ListNames(vm));
    }

    [Fact]
    public async Task Hash_words_in_search_filter_by_tag_prefix()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;

        vm.SearchText = "#пр";
        Assert.Equal([Buh, Zup], ListNames(vm));

        vm.SearchText = "#пр бухг";
        Assert.Equal([Buh], ListNames(vm));

        vm.SearchText = "#нет";
        Assert.Empty(ListNames(vm));
        Assert.Equal("Ничего не найдено", vm.EmptyListText);
    }

    [Fact]
    public async Task Hiding_tag_bar_clears_filter_and_is_remembered()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.ToggleTagFilterCommand.Execute(vm.TagFilters[0]);

        vm.ToggleTagBarCommand.Execute(null);

        Assert.False(vm.ShowTagBar);
        Assert.False(vm.IsTagBarVisible);
        Assert.False(vm.HasTagFilter);
        Assert.True(vm.ShowTree);
        Assert.False(fixture.Settings.Settings.Ui.ShowTagBar);
    }

    [Fact]
    public async Task Removing_last_base_of_selected_tag_drops_it_from_filter()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.ToggleTagFilterCommand.Execute(vm.TagFilters.Single(t => t.Name == "тест"));
        vm.SelectedListItem = vm.ListItems.Single();

        vm.RemoveTagCommand.Execute("тест");

        Assert.False(vm.HasTagFilter);
        Assert.DoesNotContain(vm.TagFilters, t => t.Name == "тест");
    }

    [Fact]
    public async Task Manage_window_renames_recolors_hides_and_deletes()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        fixture.Dialogs.TagsEditor = editor =>
        {
            Assert.Equal(["бух", "прод", "тест"], editor.Rows.Select(r => r.Name));
            Assert.Equal(["2", "2", "1"], editor.Rows.Select(r => r.CountText));
            var prod = editor.Rows.Single(r => r.Name == "прод");
            prod.Name = "боевые";
            prod.ColorIndex = (int)TagColor.Blue;
            editor.Rows.Single(r => r.Name == "бух").ShowInRow = false;
            editor.SelectedRow = editor.Rows.Single(r => r.Name == "тест");
            editor.DeleteCommand.Execute(null);
            editor.CreateCommand.Execute(null);
            Assert.Equal("Новая метка", editor.SelectedRow!.Name);
            Assert.True(editor.Validate());
            return true;
        };

        await vm.ManageTagsCommand.ExecuteAsync(null);

        Assert.Equal(["бух", "боевые"], fixture.Base(Buh).AssignedTags);
        Assert.Equal(["боевые"], fixture.Base(Buh).RowTags.Select(t => t.Name)); // «бух» не показывается в строке
        Assert.Equal(TagColor.Blue, fixture.Base(Zup).Tags.Single().Color);
        Assert.Equal(["бух"], fixture.Base(Copy).AssignedTags);
        Assert.Null(fixture.Settings.UserData.Tag("тест"));
        Assert.NotNull(fixture.Settings.UserData.Tag("Новая метка"));
        Assert.Equal(["бух · 2", "боевые · 2"], vm.TagFilters.Select(t => t.Text));
    }

    [Fact]
    public async Task Manage_window_swaps_names_and_rejects_duplicates_and_blanks()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        fixture.Dialogs.TagsEditor = editor =>
        {
            var bux = editor.Rows.Single(r => r.Name == "бух");
            var prod = editor.Rows.Single(r => r.Name == "прод");
            bux.Name = "ПРОД";
            Assert.False(editor.Validate());
            Assert.Equal("Метка «прод» уже есть.", editor.ErrorText);
            bux.Name = " ";
            Assert.False(editor.Validate());
            Assert.Equal("Укажите наименование метки.", editor.ErrorText);
            bux.Name = "прод";
            prod.Name = "бух";
            Assert.True(editor.Validate());
            Assert.Null(editor.ErrorText);
            return true;
        };

        await vm.ManageTagsCommand.ExecuteAsync(null);

        // Имена поменялись местами; позиции меток сохранены.
        Assert.Equal(["прод", "бух"], fixture.Base(Buh).AssignedTags);
        Assert.Equal(["бух"], fixture.Base(Zup).AssignedTags);
        Assert.Equal(["прод", "тест"], fixture.Base(Copy).AssignedTags);
    }

    [Fact]
    public async Task Cancelled_manage_window_changes_nothing()
    {
        using var fixture = await TaggedAsync();
        fixture.Dialogs.TagsEditor = editor =>
        {
            editor.Rows[0].Name = "другое";
            return false;
        };

        await fixture.ViewModel.ManageTagsCommand.ExecuteAsync(null);

        Assert.NotNull(fixture.Settings.UserData.Tag("бух"));
    }

    [Fact]
    public async Task Duplicate_copies_tags()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Zup);
        fixture.Dialogs.ConfirmAnswer = true;

        await vm.DuplicateCommand.ExecuteAsync(null);

        Assert.Equal(["прод"], fixture.Base(Zup + "_копия").AssignedTags);
        Assert.Equal("прод · 3", vm.TagFilters.Single(t => t.Name == "прод").Text);
    }

    [Fact]
    public async Task Editor_field_shows_chips_removes_and_picks_tags()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Zup);
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            var chip = Assert.Single(editor.TagChips);
            Assert.Equal(("прод", fixture.Settings.UserData.Tag("прод")!.Color), (chip.Name, chip.Color));
            Assert.Equal(["бух", "тест"], editor.TagPicker.Available.Select(t => t.Name)); // поставленных нет
            Assert.False(editor.IsTagsChanged);

            editor.TagPicker.PickCommand.Execute("тест");
            editor.RemoveTagCommand.Execute("прод");
            editor.TagPicker.Filter = "новая";
            Assert.True(editor.TagPicker.CanCreate);
            Assert.Equal("Enter — создать метку «новая»", editor.TagPicker.CreateText);
            editor.TagPicker.PickCommand.Execute(null);
            Assert.Equal(["тест", "новая"], editor.Tags);
            Assert.Equal(TagColor.Gray, editor.TagChips[1].Color); // ещё не создана — серая
            Assert.Empty(editor.TagPicker.Filter);
            return true;
        };

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(["новая", "тест"], fixture.Base(Zup).AssignedTags);
        Assert.Contains(vm.TagFilters, t => t.Text == "новая · 1");
    }

    [Fact]
    public async Task Details_picker_lists_missing_tags_filters_and_picks()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Zup);
        var picker = vm.TagPicker;
        var picked = 0;
        picker.Picked += (_, _) => picked++;
        Assert.Equal(["бух", "тест"], picker.Available.Select(t => t.Name));

        picker.Filter = "ес";
        Assert.Equal(["тест"], picker.Available.Select(t => t.Name));
        Assert.False(picker.CanCreate);
        Assert.Equal("Enter — поставить «тест»", picker.CreateText);
        picker.PickCommand.Execute(null); // Enter: единственная подходящая
        Assert.Equal(["прод", "тест"], fixture.Base(Zup).AssignedTags);
        Assert.Equal(1, picked);
        Assert.Equal(["бух"], picker.Available.Select(t => t.Name));

        picker.Filter = "ПРОД"; // уже стоит — Enter ничего не делает
        Assert.False(picker.HasEnterHint);
        picker.PickCommand.Execute(null);
        Assert.Equal(1, picked);

        picker.PickCommand.Execute("бух"); // щелчок по метке
        Assert.Equal(["бух", "прод", "тест"], fixture.Base(Zup).AssignedTags);
        Assert.False(picker.HasAvailable);

        // Другая база — список по её меткам.
        vm.SelectedTreeItem = Node(vm, Copy);
        Assert.Equal(["прод"], picker.Available.Select(t => t.Name));
    }

    [Fact]
    public async Task Row_chip_cross_removes_tag_from_its_base()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Node(vm, Zup);
        var chip = fixture.Base(Buh).RowTags.Single(t => t.Name == "прод");
        Assert.Same(fixture.Base(Buh), chip.Owner);

        vm.RemoveRowTagCommand.Execute(chip);

        Assert.Equal(["бух"], fixture.Base(Buh).AssignedTags);
        Assert.Equal(["прод"], fixture.Base(Zup).AssignedTags); // выделенная база не тронута
        Assert.Contains("Метка «прод» снята: «Бухгалтерия предприятия».", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tags_are_saved_to_settings()
    {
        using var fixture = await TaggedAsync();

        var set = fixture.Settings.Settings.InfoBaseTags.Single(t => t.InfoBase.Name == Zup);
        Assert.Equal(["прод"], set.Tags);
        Assert.Equal("00000000-0000-0000-0000-000000000002", set.InfoBase.Id);
    }

    [AvaloniaFact]
    public async Task Tag_bar_row_chips_properties_and_tags_window_are_shown()
    {
        using var fixture = await TaggedAsync();
        var vm = fixture.ViewModel;
        var window = await MainWindowTests.OpenAsync(fixture);

        Assert.True(window.FindControl<Button>("TagBarButton")!.IsVisible);
        Assert.True(window.FindControl<DockPanel>("TagBar")!.IsVisible);
        Assert.Equal(3, window.FindControl<ItemsControl>("TagFilterList")!.ItemCount);
        Assert.Equal("Метки", window.FindControl<MenuItem>("TagsMenu")!.Header);
        MainWindowTests.Snapshot(window, "52-tags-tree");

        vm.SelectedTreeItem = Node(vm, Buh);
        vm.ToggleTagFilterCommand.Execute(vm.TagFilters.Single(t => t.Name == "прод"));
        vm.SelectedListItem = vm.ListItems[0];
        MainWindowTests.Render();
        Assert.True(window.FindControl<Button>("ClearTagFilterButton")!.IsVisible);
        Assert.Equal(2, window.FindControl<ItemsControl>("SelectedTagsList")!.ItemCount);
        MainWindowTests.Snapshot(window, "52-tags-filter");

        // В одну строку метки — сразу за наименованием; в две — во второй строке.
        bool Shown(string kind) => window.GetVisualDescendants().OfType<ItemsControl>()
            .Any(i => i.Classes.Contains("rowTags") && i.Classes.Contains("secondLine") == (kind == "second") && i.IsEffectivelyVisible);
        Assert.True(Shown("inline"));
        Assert.False(Shown("second"));
        vm.TwoLineRows = true;
        MainWindowTests.Render();
        Assert.False(Shown("inline"));
        Assert.True(Shown("second"));
        MainWindowTests.Snapshot(window, "52-tags-two-line");
        vm.TwoLineRows = false;

        vm.ToggleTagBarCommand.Execute(null);
        MainWindowTests.Render();
        Assert.False(window.FindControl<DockPanel>("TagBar")!.IsVisible);
        window.Close();

        var counts = fixture.Settings.UserData.TagCatalog().ToDictionary(t => t.Name, _ => 1);
        var dialog = new TagsWindow(new TagsEditorViewModel(fixture.Settings.UserData.TagCatalog(), counts));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, dialog.FindControl<ListBox>("TagsList")!.ItemCount);
        Assert.Equal(7, dialog.FindControl<ListBox>("ColorList")!.ItemCount);
        Assert.Equal("бух", dialog.FindControl<TextBox>("NameBox")!.Text);
        MainWindowTests.Snapshot(dialog, "52-tags-window");
        dialog.Close();

        var editorModel = new InfoBaseEditorViewModel(
            new Core.Editing.InfoBaseDraft { Name = "Бухгалтерия", Kind = Core.Parsing.ConnectionKind.File, FilePath = @"C:\B" }, [], isNew: false, new FakeFiles())
        {
            TagCatalog = fixture.Settings.UserData.TagCatalog(),
            InitialTags = ["бух", "прод"],
        };
        var editorWindow = new InfoBaseEditorWindow(editorModel);
        editorWindow.Show();
        MainWindowTests.Render();
        Assert.Equal(2, editorWindow.FindControl<ItemsControl>("TagChipsList")!.ItemCount);
        Assert.Equal("+ Добавить", editorWindow.FindControl<Button>("AddTagButton")!.Content);
        MainWindowTests.Snapshot(editorWindow, "52-tags-editor");
        editorWindow.Close();

        var pickerView = new TagPickerView { DataContext = editorModel.TagPicker };
        var pickerWindow = new Window { Content = new Border { Padding = new Avalonia.Thickness(12), Child = pickerView }, SizeToContent = SizeToContent.WidthAndHeight };
        pickerWindow.Show();
        editorModel.TagPicker.Filter = "т";
        MainWindowTests.Render();
        Assert.Equal(1, pickerView.FindControl<ItemsControl>("AvailableList")!.ItemCount);
        Assert.Equal("Enter — поставить «тест»", pickerView.FindControl<TextBlock>("CreateHint")!.Text);
        editorModel.TagPicker.Filter = string.Empty;
        MainWindowTests.Snapshot(pickerWindow, "52-tag-picker");
        pickerWindow.Close();

        var form = new NewTagViewModel(fixture.Settings.UserData.TagCatalog(), TagColor.Amber, "Метка будет поставлена: «Копия бухгалтерии».") { Name = "клиент А" };
        var newTag = new NewTagWindow(form);
        newTag.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(newTag.FindControl<ListBox>("ColorList")!.IsEffectivelyVisible);
        Assert.Equal((int)TagColor.Amber, newTag.FindControl<ListBox>("ColorList")!.SelectedIndex);
        MainWindowTests.Snapshot(newTag, "52-new-tag");
        form.Name = "прод";
        Dispatcher.UIThread.RunJobs();
        Assert.False(newTag.FindControl<ListBox>("ColorList")!.IsEffectivelyVisible);
        Assert.True(newTag.FindControl<TextBlock>("ExistingText")!.IsVisible);
        MainWindowTests.Snapshot(newTag, "52-new-tag-existing");
        newTag.Close();
    }
}
