using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Группа базы в форме: показ «Не выбрана», окно выбора группы с деревом и созданием новой.</summary>
public class GroupPickerTests
{
    [Fact]
    public void Editor_shows_group_as_text_and_root_as_not_selected()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft { FolderPath = "/" }, [], isNew: true, new FakeFiles());
        Assert.Equal("Не выбрана", editor.FolderText);
        editor.Folder = "/Рабочие/Отчёты";
        Assert.Equal("Отчёты", editor.FolderText); // только своя группа, путь — в подсказке
        Assert.Equal("Рабочие / Отчёты", editor.FolderPathTip);

        Assert.True(editor.ClearGroupCommand.CanExecute(null));
        editor.ClearGroupCommand.Execute(null);
        Assert.Equal("/", editor.Folder);
        Assert.Equal("Не выбрана", editor.FolderText);
        Assert.False(editor.HasGroup);
        Assert.False(editor.ClearGroupCommand.CanExecute(null));
        Assert.False(editor.ChooseGroupCommand.CanExecute(null)); // окно выбора не задано
    }

    [Theory]
    [InlineData("/", "Не выбрана", null)]
    [InlineData("/Рабочие", "Рабочие", null)]
    [InlineData("/Рабочие/Отчёты", "Отчёты", "Рабочие / Отчёты")]
    public void Properties_panel_shows_only_own_group(string folder, string text, string? tip)
    {
        var section = V8iDocument.Parse($"[База]\r\nConnect=File=\"C:\\A\";\r\nFolder={folder}\r\n").Sections.Single(s => s.Name.Length > 0);
        var infoBase = new InfoBaseViewModel(new InfoBase(section, new ListSource(ListSourceKind.Personal, string.Empty)), new LauncherUserData(new LauncherSettings()));
        Assert.Equal(text, infoBase.FolderText);
        Assert.Equal(tip, infoBase.FolderPathTip);
    }

    [Fact]
    public void Picker_builds_tree_with_parents_and_selects_current_group()
    {
        var picker = new GroupPickerViewModel(["/", "/Рабочие/Отчёты", "/Архив", "/рабочие"], "/Рабочие/Отчёты");

        Assert.Equal(["Архив", "Рабочие"], picker.Groups.Select(g => g.Name)); // без строки «Не выбрана»
        var work = picker.Groups[1];
        Assert.Equal("/Рабочие", work.Path);
        Assert.Equal("Отчёты", Assert.Single(work.Children).Name);
        Assert.True(work.IsExpanded); // выбранная группа видна
        Assert.Equal("/Рабочие/Отчёты", picker.SelectedPath);

        var atRoot = new GroupPickerViewModel(["/Архив"], "/");
        Assert.Null(atRoot.SelectedGroup); // база в корне — ничего не выделено, «Выбрать» недоступна
        Assert.False(atRoot.HasSelection);
        Assert.True(picker.HasSelection);
    }

    [Fact]
    public async Task New_group_is_created_inside_selected_and_selected()
    {
        var asked = new List<string>();
        var answer = "Новая";
        var picker = new GroupPickerViewModel(["/Рабочие"], "/Рабочие", parent =>
        {
            asked.Add(parent);
            return Task.FromResult<string?>(answer);
        });

        await picker.CreateGroupCommand.ExecuteAsync(null);
        Assert.Equal(["/Рабочие"], asked);
        Assert.Equal("/Рабочие/Новая", picker.SelectedPath);
        Assert.Equal("Новая", Assert.Single(picker.Groups[0].Children).Name);

        // В корне — когда ничего не выделено; «/» в имени — ошибка, выбор не меняется.
        picker.SelectedGroup = null;
        answer = "А";
        await picker.CreateGroupCommand.ExecuteAsync(null);
        Assert.Equal(["А", "Рабочие"], picker.Groups.Select(g => g.Name));
        answer = "a/b";
        await picker.CreateGroupCommand.ExecuteAsync(null);
        Assert.True(picker.HasError);
        Assert.Equal("/А", picker.SelectedPath);

        Assert.False(new GroupPickerViewModel([], "/").CreateGroupCommand.CanExecute(null));
    }

    [Fact]
    public async Task Group_chosen_in_picker_is_saved_with_base()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";
        GroupPickerViewModel? shownPicker = null;
        fixture.Dialogs.PromptAnswer = "Кадры";
        fixture.Dialogs.GroupChooser = async picker =>
        {
            shownPicker = picker;
            await picker.CreateGroupCommand.ExecuteAsync(null); // внутри текущей «Рабочие»
            return true;
        };
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.Equal("Рабочие", editor.FolderText);
            editor.ChooseGroupCommand.Execute(null);
            Assert.Equal("Кадры", editor.FolderText);
            return true;
        };

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(["Рабочие"], shownPicker!.Groups.Select(g => g.Name));
        Assert.Equal("/Рабочие/Кадры", fixture.SavedList().Sections.Single(s => s.Name == "Зарплата и управление персоналом").Get("Folder"));
    }

    [Fact]
    public async Task Cancelled_picker_keeps_group()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft { FolderPath = "/Рабочие" }, ["/Рабочие", "/Архив"], isNew: false, new FakeFiles())
        {
            GroupChooser = picker =>
            {
                picker.SelectedGroup = null;
                return Task.FromResult(false);
            },
        };

        await editor.ChooseGroupCommand.ExecuteAsync(null);
        Assert.Equal("/Рабочие", editor.Folder);
    }

    [AvaloniaFact]
    public void Editor_form_has_group_field_with_clear_button_and_picker_window_shows_tree()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft { Name = "База", FilePath = @"C:\Bases\B" }, ["/Рабочие/Отчёты", "/Архив"], isNew: false, new FakeFiles())
        {
            GroupChooser = _ => Task.FromResult(false),
        };
        var form = new InfoBaseEditorWindow(editor);
        form.Show();
        MainWindowTests.Render();
        var label = form.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Группа", label);
        Assert.DoesNotContain("Папка в списке", label);
        Assert.Equal("Не выбрана", form.FindControl<TextBox>("GroupBox")!.Text);
        Assert.True(form.FindControl<Button>("ChooseGroupButton")!.IsEffectivelyEnabled);
        Assert.False(form.FindControl<Button>("ClearGroupButton")!.IsVisible); // группы нет — очищать нечего
        editor.Folder = "/Рабочие/Отчёты";
        MainWindowTests.Render();
        Assert.Equal("Отчёты", form.FindControl<TextBox>("GroupBox")!.Text);
        Assert.True(form.FindControl<Button>("ClearGroupButton")!.IsVisible);
        MainWindowTests.Snapshot(form, "42-editor-group-field");
        form.Close();

        var picker = new GroupPickerViewModel(editor.Folders, "/Рабочие/Отчёты", _ => Task.FromResult<string?>(null));
        var window = new GroupPickerWindow(picker);
        window.Show();
        MainWindowTests.Render();
        var tree = window.FindControl<TreeView>("GroupsTree")!;
        var shown = tree.GetVisualDescendants().OfType<TreeViewItem>().Select(i => ((GroupNodeViewModel)i.DataContext!).Name).ToList();
        Assert.Equal(["Архив", "Рабочие", "Отчёты"], shown);
        Assert.False(window.FindControl<TextBlock>("NoGroupsText")!.IsVisible);
        Assert.Equal("Отчёты", ((GroupNodeViewModel)tree.SelectedItem!).Name);
        Assert.True(window.FindControl<Button>("CreateGroupButton")!.IsEffectivelyEnabled);
        Assert.True(window.FindControl<Button>("OkButton")!.IsEffectivelyEnabled);
        MainWindowTests.Snapshot(window, "42-group-picker");

        // Щелчок по пустому месту дерева снимает выделение — новая группа создастся в корне.
        var bottom = tree.TranslatePoint(new Avalonia.Point(tree.Bounds.Width / 2, tree.Bounds.Height - 10), window)!.Value;
        window.MouseDown(bottom, Avalonia.Input.MouseButton.Left);
        window.MouseUp(bottom, Avalonia.Input.MouseButton.Left);
        MainWindowTests.Render();
        Assert.Null(picker.SelectedGroup);
        Assert.False(window.FindControl<Button>("OkButton")!.IsEffectivelyEnabled);
        window.Close();

        var empty = new GroupPickerWindow(new GroupPickerViewModel([], "/", _ => Task.FromResult<string?>(null)));
        empty.Show();
        MainWindowTests.Render();
        Assert.True(empty.FindControl<TextBlock>("NoGroupsText")!.IsVisible);
        MainWindowTests.Snapshot(empty, "42-group-picker-empty");
        empty.Close();
    }
}
