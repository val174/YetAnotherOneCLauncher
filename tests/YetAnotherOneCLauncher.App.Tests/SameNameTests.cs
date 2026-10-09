using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Запрет двух баз с одним названием: форма добавления и изменения, загрузка из файла.</summary>
public class SameNameTests
{
    [Fact]
    public async Task Base_with_taken_name_is_not_added()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var before = File.ReadAllBytes(fixture.ListPath);
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            editor.Name = "  бухгалтерия ПРЕДПРИЯТИЯ "; // регистр и пробелы по краям не важны
            editor.FilePath = @"C:\Bases\Other";
            Assert.True(editor.IsNameTakenHintVisible); // подсказка — сразу, до «Сохранить»
            return true;
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));
        Assert.Null(shown!.Result);
        Assert.StartsWith("В списке уже есть база «бухгалтерия ПРЕДПРИЯТИЯ»", shown.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edited_base_keeps_own_name_but_cannot_take_another()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        // Своё название — можно, даже сменив регистр.
        vm.SearchText = "зуп";
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            Assert.False(editor.IsNameTakenHintVisible);
            editor.Name = "ЗАРПЛАТА и управление персоналом";
            return true;
        };
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Contains(fixture.SavedList().Sections, s => s.Name == "ЗАРПЛАТА и управление персоналом");

        // Чужое — нельзя.
        vm.SearchText = "зарплата";
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            editor.Name = "Копия бухгалтерии";
            return true;
        };
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Null(shown!.Result);
        Assert.Contains("уже есть база «Копия бухгалтерии»", shown.Errors, StringComparison.Ordinal);
        Assert.Single(fixture.SavedList().Sections, s => s.Name == "Копия бухгалтерии");
    }

    [Fact]
    public void Editor_hint_follows_name()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            ExistingNames = ["Бухгалтерия"],
        };

        editor.Name = "Бухгалтерия";
        Assert.Equal("База «Бухгалтерия» уже есть в списке — укажите другое название.", editor.NameTakenHint);
        editor.Name = "Бухгалтерия 2";
        Assert.False(editor.IsNameTakenHintVisible);

        // Название из строки подключения тоже проверяется.
        editor.Name = string.Empty;
        editor.ConnectionText = @"C:\Bases\Бухгалтерия";
        Assert.Equal("Бухгалтерия", editor.Name);
        Assert.True(editor.IsNameTakenHintVisible);
        Assert.False(editor.TryAccept());
    }

    [Fact]
    public async Task Import_reports_skipped_same_name_bases()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var importPath = Path.Combine(fixture.Directory, "import.v8i");
        File.WriteAllText(importPath, """
            [Бухгалтерия предприятия]
            Connect=File="E:\Other\Buh";
            [Новая]
            Connect=File="E:\New";
            """.ReplaceLineEndings("\r\n"));
        fixture.Files.OpenAnswer = importPath;

        await fixture.ViewModel.ImportCommand.ExecuteAsync(null);

        Assert.Contains("добавлено записей: 1", fixture.ViewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("база с таким же названием уже есть): 1", fixture.ViewModel.StatusText, StringComparison.Ordinal);
        Assert.Single(fixture.SavedList().Sections, s => s.Name == "Бухгалтерия предприятия");
    }

    [AvaloniaFact]
    public void Window_shows_hint_under_name()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            ExistingNames = ["Бухгалтерия"],
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();
        var hint = window.FindControl<TextBlock>("NameTakenText")!;
        Assert.False(hint.IsVisible);

        window.FindControl<TextBox>("NameBox")!.Text = "бухгалтерия";
        MainWindowTests.Render();
        Assert.True(hint.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "45-same-name-hint");
        window.Close();
    }
}
