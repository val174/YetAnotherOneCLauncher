using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;

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
        Assert.Equal("Рабочие / Отчёты", editor.FolderText);
        Assert.False(editor.ChooseGroupCommand.CanExecute(null)); // окно выбора не задано
    }

    [Fact]
    public void Picker_builds_tree_with_parents_and_selects_current_group()
    {
        var picker = new GroupPickerViewModel(["/", "/Рабочие/Отчёты", "/Архив", "/рабочие"], "/Рабочие/Отчёты");

        Assert.Equal(["Не выбрана", "Архив", "Рабочие"], picker.Groups.Select(g => g.Name));
        var work = picker.Groups[2];
        Assert.Equal("/Рабочие", work.Path);
        Assert.Equal("Отчёты", Assert.Single(work.Children).Name);
        Assert.True(work.IsExpanded); // выбранная группа видна
        Assert.Equal("/Рабочие/Отчёты", picker.SelectedPath);

        var atRoot = new GroupPickerViewModel(["/Архив"], "/");
        Assert.Same(atRoot.NoGroup, atRoot.SelectedGroup);
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
        Assert.Equal("Новая", Assert.Single(picker.Groups[1].Children).Name);

        // В корне — при выделенном «Не выбрана»; «/» в имени — ошибка, выбор не меняется.
        picker.SelectedGroup = picker.NoGroup;
        answer = "А";
        await picker.CreateGroupCommand.ExecuteAsync(null);
        Assert.Equal(["Не выбрана", "А", "Рабочие"], picker.Groups.Select(g => g.Name));
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
            Assert.Equal("Рабочие / Кадры", editor.FolderText);
            return true;
        };

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(["Не выбрана", "Рабочие"], shownPicker!.Groups.Select(g => g.Name));
        Assert.Equal("/Рабочие/Кадры", fixture.SavedList().Sections.Single(s => s.Name == "Зарплата и управление персоналом").Get("Folder"));
    }

    [Fact]
    public async Task Cancelled_picker_keeps_group()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft { FolderPath = "/Рабочие" }, ["/Рабочие", "/Архив"], isNew: false, new FakeFiles())
        {
            GroupChooser = picker =>
            {
                picker.SelectedGroup = picker.NoGroup;
                return Task.FromResult(false);
            },
        };

        await editor.ChooseGroupCommand.ExecuteAsync(null);
        Assert.Equal("/Рабочие", editor.Folder);
    }

    [AvaloniaFact]
    public void Editor_form_has_group_field_and_picker_window_shows_tree()
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
        MainWindowTests.Snapshot(form, "42-editor-group-field");
        form.Close();

        var picker = new GroupPickerViewModel(editor.Folders, "/Рабочие/Отчёты", _ => Task.FromResult<string?>(null));
        var window = new GroupPickerWindow(picker);
        window.Show();
        MainWindowTests.Render();
        var tree = window.FindControl<TreeView>("GroupsTree")!;
        var shown = tree.GetVisualDescendants().OfType<TreeViewItem>().Select(i => ((GroupNodeViewModel)i.DataContext!).Name).ToList();
        Assert.Equal(["Не выбрана", "Архив", "Рабочие", "Отчёты"], shown);
        Assert.Equal("Отчёты", ((GroupNodeViewModel)tree.SelectedItem!).Name);
        Assert.True(window.FindControl<Button>("CreateGroupButton")!.IsEffectivelyEnabled);
        MainWindowTests.Snapshot(window, "42-group-picker");
        window.Close();
    }
}
