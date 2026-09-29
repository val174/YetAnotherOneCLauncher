using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Правка личного списка из окна: форма, папки, удаление, порядок, конфликты, импорт и выгрузка.</summary>
public class EditingTests
{
    [Fact]
    public async Task Add_base_writes_file_and_selects_new_base()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.Equal("/Рабочие", editor.Folder); // папка выделена — новая база туда
            editor.Name = "Новая база";
            editor.KindIndex = 1;
            editor.Server = "srv2";
            editor.InfobaseName = "new_db";
            return true;
        };

        await vm.AddBaseCommand.ExecuteAsync(null);

        var section = fixture.SavedList().Sections.Single(s => s.Name == "Новая база");
        Assert.Equal("Srvr=\"srv2\";Ref=\"new_db\";", section.Get("Connect"));
        Assert.Equal("/Рабочие", section.Get("Folder"));
        Assert.Equal("Новая база", vm.SelectedInfoBase?.Name);
        Assert.Contains(vm.InfoBases, b => b.Name == "Новая база");
        Assert.True(File.Exists(fixture.Store.BackupPath));
    }

    [Fact]
    public async Task Invalid_form_is_not_saved()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var before = File.ReadAllBytes(fixture.ListPath);
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            editor.Name = "Без пути";
            return true; // «Сохранить», но каталог не указан
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));
        Assert.True(shown!.HasErrors);
        Assert.Contains("каталог", shown.Errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Architecture_choice_in_editor_is_saved_to_list()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            editor.ArchitectureIndex = 1; // 32 бита (x86)
            return true;
        };

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(5, shown!.ArchitectureNames.Count);
        Assert.Equal("x86", fixture.SavedList().Sections.Single(s => s.Name == "Зарплата и управление персоналом").Get("AppArch"));
        Assert.Equal(Core.Model.AppArchitecture.X86, vm.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").InfoBase.Architecture);

        // При следующем открытии выбор показан.
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            return false;
        };
        vm.SearchText = "зуп";
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal(1, shown.ArchitectureIndex);
    }

    [Fact]
    public async Task Edit_base_changes_only_its_lines()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.Version = "8.3.27";
            editor.AppIndex = 1; // тонкий клиент
            return true;
        };
        var before = File.ReadAllText(fixture.ListPath);

        await vm.EditCommand.ExecuteAsync(null);

        var after = File.ReadAllText(fixture.ListPath);
        var removed = before.Split("\r\n").Except(after.Split("\r\n")).ToList();
        var added = after.Split("\r\n").Except(before.Split("\r\n")).ToList();
        Assert.Empty(removed);
        Assert.Equal(new[] { "App=ThinClient", "Version=8.3.27" }, added.Order());
        Assert.Equal("Зарплата и управление персоналом", vm.SelectedInfoBase?.Name);
        Assert.Equal("8.3.27", vm.SelectedInfoBase?.InfoBase.Version);
    }

    [Fact]
    public async Task Folder_add_rename_delete()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        fixture.Dialogs.PromptAnswer = "Архив";
        await vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal("/Архив", vm.SelectedFolder?.Path);

        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        fixture.Dialogs.PromptAnswer = "Боевые";
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal("/Боевые", vm.SelectedFolder?.Path);
        Assert.All(fixture.SavedList().Sections.Where(s => s.Name is "Бухгалтерия предприятия" or "Зарплата и управление персоналом"),
            s => Assert.Equal("/Боевые", s.Get("Folder")));

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Contains("2 записи", fixture.Dialogs.Questions[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Бухгалтерия предприятия");
        Assert.Equal(3, fixture.SavedList().Sections.Count); // Копия, Розница, Архив
    }

    [Fact]
    public async Task Delete_base_asks_and_respects_no()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "розница";
        var before = File.ReadAllBytes(fixture.ListPath);

        fixture.Dialogs.ConfirmAnswer = false;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Розница (тест)");
        Assert.DoesNotContain(vm.InfoBases, b => b.Name == "Розница (тест)");
    }

    [Fact]
    public async Task Change_by_another_program_is_a_conflict_then_list_is_reloaded()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "розница";

        // Стартер 1С поменял эту же запись после того, как лаунчер загрузил список.
        var external = fixture.SavedList();
        external.Sections.Single(s => s.Name == "Розница (тест)").Set("Version", "8.3.99");
        File.WriteAllBytes(fixture.ListPath, external.ToBytes());

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("изменена другой программой", fixture.Dialogs.Messages.Single(), StringComparison.Ordinal);
        Assert.Contains(fixture.SavedList().Sections, s => s.Name == "Розница (тест)"); // не удалена
        Assert.Equal("8.3.99", vm.InfoBases.Single(b => b.Name == "Розница (тест)").InfoBase.Version); // перечитано

        // После обновления удаление проходит.
        vm.SearchText = string.Empty;
        vm.SearchText = "розница";
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.DoesNotContain(fixture.SavedList().Sections, s => s.Name == "Розница (тест)");
    }

    [Fact]
    public async Task External_change_is_picked_up_but_own_write_is_ignored()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        fixture.Dialogs.PromptAnswer = "Своя";
        await vm.AddFolderCommand.ExecuteAsync(null);
        var status = vm.StatusText;
        await vm.HandleListFileChangedAsync(ListFileKind.PersonalList);
        Assert.Equal(status, vm.StatusText); // своя запись — не «чужое изменение»

        var external = fixture.SavedList();
        PersonalListEditor.AddBase(external, new InfoBaseDraft { Name = "От стартера", FilePath = @"D:\S" });
        File.WriteAllBytes(fixture.ListPath, external.ToBytes());
        await vm.HandleListFileChangedAsync(ListFileKind.PersonalList);

        Assert.Contains(vm.InfoBases, b => b.Name == "От стартера");
        Assert.Contains("другой программой", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_up_and_down_changes_order()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var root = () => vm.TreeItems.Select(n => n.Name).ToList();
        Assert.True(vm.IsSortedByName);
        Assert.Equal(new[] { "Рабочие", "Копия бухгалтерии", "Розница (тест)" }, root());

        // Перестановка при сортировке по наименованию: видимый порядок записывается в список баз,
        // и дальше показывается свой порядок — с перестановкой.
        vm.SelectedTreeItem = vm.TreeItems.Single(n => n.Name == "Розница (тест)");
        await vm.MoveUpCommand.ExecuteAsync(null);
        Assert.False(vm.IsSortedByName);
        Assert.Equal(Core.Catalog.CatalogSortMode.Custom, fixture.Settings.Settings.Ui.SortMode);
        Assert.Equal(new[] { "Рабочие", "Розница (тест)", "Копия бухгалтерии" }, root());

        await vm.MoveUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Розница (тест)", "Рабочие", "Копия бухгалтерии" }, root());
        Assert.Equal("Розница (тест)", vm.SelectedInfoBase?.Name);

        // Порядок в файле: сохранится и после перезапуска, и в штатном стартере.
        var saved = fixture.SavedList().Sections.Where(s => s.Get("Folder") == "/").OrderBy(s => double.Parse(s.Get("OrderInTree")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new[] { "Розница (тест)", "Рабочие", "Копия бухгалтерии" }, saved.Select(s => s.Name));

        // Вложенная папка тоже получила алфавитный порядок — при своём порядке она выглядит как раньше.
        Assert.Equal(new[] { "Бухгалтерия предприятия", "Зарплата и управление персоналом" }, Folder(vm, "Рабочие").Children.Select(n => n.Name));
    }

    [Fact]
    public async Task Selected_folder_can_be_sorted_by_name_in_custom_order()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.ToggleSortCommand.Execute(null); // свой порядок
        var children = () => Folder(vm, "Рабочие").Children.Select(n => n.Name).ToList();

        // Переставим базы в папке, чтобы порядок стал не алфавитным.
        vm.SelectedTreeItem = Folder(vm, "Рабочие").Children.Single(n => n.Name == "Зарплата и управление персоналом");
        await vm.MoveUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Зарплата и управление персоналом", "Бухгалтерия предприятия" }, children());
        var root = vm.TreeItems.Select(n => n.Name).ToList();

        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        Assert.True(vm.SortFolderByNameCommand.CanExecute(null));
        await vm.SortFolderByNameCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Бухгалтерия предприятия", "Зарплата и управление персоналом" }, children());
        Assert.Equal(root, vm.TreeItems.Select(n => n.Name)); // остальные папки не тронуты
        Assert.False(vm.IsSortedByName); // режим прежний — свой порядок
        Assert.Contains("упорядочены по наименованию", vm.StatusText, StringComparison.Ordinal);

        // У выделенной базы упорядочивается папка, в которой она лежит.
        vm.SelectedTreeItem = vm.TreeItems.OfType<BaseNodeViewModel>().First();
        Assert.True(vm.SortFolderByNameCommand.CanExecute(null));

        vm.ShowRecentCommand.Execute(null);
        Assert.False(vm.SortFolderByNameCommand.CanExecute(null));
    }

    [Fact]
    public async Task Drag_base_into_folder_and_onto_favorites()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var copy = vm.TreeItems.OfType<BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");

        await vm.MoveNodeAsync(copy, Folder(vm, "Рабочие"));

        Assert.Equal("/Рабочие", fixture.SavedList().Sections.Single(s => s.Name == "Копия бухгалтерии").Get("Folder"));
        Assert.Equal(3, Folder(vm, "Рабочие").Children.Count);

        vm.ToggleFavoriteCommand.Execute(vm.InfoBases.Single(b => b.Name == "Розница (тест)")); // появится «Избранное»
        var favorites = vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Kind == FolderKind.Favorites);
        var moved = Folder(vm, "Рабочие").Children.OfType<BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");
        await vm.MoveNodeAsync(moved, favorites);

        Assert.True(vm.InfoBases.Single(b => b.Name == "Копия бухгалтерии").IsFavorite);
    }

    [Fact]
    public async Task Edit_as_text_keeps_unknown_keys()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "розница";
        fixture.Dialogs.TextEditor = text => text + Environment.NewLine + "MyCustomKey=42";

        await vm.EditAsTextCommand.ExecuteAsync(null);

        Assert.Equal("42", fixture.SavedList().Sections.Single(s => s.Name == "Розница (тест)").Get("MyCustomKey"));
    }

    [Fact]
    public async Task Common_list_base_is_read_only_but_can_be_copied()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadWithCommonListAsync("""
            [Общая база]
            Connect=Srvr="shared";Ref="common";
            ID=cccc0000-0000-0000-0000-000000000001
            Folder=/
            """);
        var vm = fixture.ViewModel;
        vm.SearchText = "общая";
        Assert.True(vm.SelectedInfoBase!.InfoBase.IsReadOnly);

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Contains("общего списка", fixture.Dialogs.Messages.Single(), StringComparison.Ordinal);

        await vm.EditCommand.ExecuteAsync(null); // предлагает копию — соглашаемся
        Assert.Contains(fixture.SavedList().Sections, s => s.Name == "Общая база (копия)");
        Assert.Equal("Общая база (копия)", vm.SelectedInfoBase?.Name);
    }

    [Fact]
    public async Task Import_and_export()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        var importPath = Path.Combine(fixture.Directory, "import.v8i");
        File.WriteAllText(importPath, """
            [Импортированная]
            Connect=File="E:\Imported";
            [Дубль]
            Connect=Srvr="srv-1c";Ref="zup";
            ID=00000000-0000-0000-0000-000000000002
            """.ReplaceLineEndings("\r\n"));
        fixture.Files.OpenAnswer = importPath;
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("добавлено записей: 1; пропущено (уже есть в списке): 1", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains(vm.InfoBases, b => b.Name == "Импортированная");

        vm.SelectedTreeItem = Folder(vm, "Рабочие");
        var exportPath = Path.Combine(fixture.Directory, "export.v8i");
        fixture.Files.SaveAnswer = exportPath;
        await vm.ExportCommand.ExecuteAsync(null);

        var exported = V8iDocument.Parse(File.ReadAllBytes(exportPath));
        Assert.Equal(new[] { "Рабочие", "Бухгалтерия предприятия", "Зарплата и управление персоналом" }, exported.Sections.Select(s => s.Name));
    }

    private static FolderNodeViewModel Folder(MainWindowViewModel vm, string name) =>
        vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == name);
}
