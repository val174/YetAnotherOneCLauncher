using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>«Дублировать» в контекстном меню и перетаскивание базы с Ctrl: вопрос и строка «Имя_копия».</summary>
public class DuplicateBaseTests
{
    private const string Question = "Добавить новую строку в список?";

    [Fact]
    public async Task Duplicate_asks_and_adds_copy_right_after_source()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        vm.SearchText = "зуп";
        Assert.True(vm.DuplicateCommand.CanExecute(null));
        fixture.Dialogs.ConfirmAnswer = true;

        await vm.DuplicateCommand.ExecuteAsync(null);

        Assert.Equal(Question, Assert.Single(fixture.Dialogs.Questions));
        var saved = fixture.SavedList().Sections;
        var copy = saved.Single(s => s.Name == "Зарплата и управление персоналом_копия");
        var source = saved.Single(s => s.Name == "Зарплата и управление персоналом");
        Assert.Equal(source.Get("Connect"), copy.Get("Connect"));
        Assert.Equal("/Рабочие", copy.Get("Folder"));
        Assert.NotEqual(source.Get("ID"), copy.Get("ID"));
        Assert.Equal("Зарплата и управление персоналом_копия", vm.SelectedInfoBase?.Name); // копия выделена
        Assert.Contains("«Зарплата и управление персоналом_копия»", vm.StatusText, StringComparison.Ordinal);

        // Сразу за источником в его папке.
        vm.SearchText = string.Empty;
        var work = vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == "Рабочие");
        var names = work.Children.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("Зарплата и управление персоналом") + 1, names.IndexOf("Зарплата и управление персоналом_копия"));
    }

    [Fact]
    public async Task Declined_question_adds_nothing()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var before = File.ReadAllBytes(fixture.ListPath);
        fixture.ViewModel.SearchText = "зуп";
        fixture.Dialogs.ConfirmAnswer = false;

        await fixture.ViewModel.DuplicateCommand.ExecuteAsync(null);

        Assert.Single(fixture.Dialogs.Questions);
        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));
    }

    [Fact]
    public async Task Ctrl_drag_duplicates_into_folder_or_before_base()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Dialogs.ConfirmAnswer = true;
        FolderNodeViewModel Work() => vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == "Рабочие");
        BaseNodeViewModel Root(string name) => vm.TreeItems.OfType<BaseNodeViewModel>().Single(b => b.Name == name);

        // На папку — в конец папки; источник остаётся на месте.
        await vm.DuplicateNodeAsync(Root("Копия бухгалтерии"), Work());
        Assert.Equal("Копия бухгалтерии_копия", Work().Children.Last().Name);
        Assert.Contains(vm.TreeItems.OfType<BaseNodeViewModel>(), b => b.Name == "Копия бухгалтерии");

        // На базу — перед ней.
        var first = Work().Children.OfType<BaseNodeViewModel>().First();
        await vm.DuplicateNodeAsync(Root("Розница (тест)"), first);
        var names = Work().Children.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf(first.Name) - 1, names.IndexOf("Розница (тест)_копия"));
        Assert.All(fixture.Dialogs.Questions, q => Assert.Equal(Question, q));
        Assert.Equal(2, fixture.Dialogs.Questions.Count);

        // Повторный дубликат — следующее свободное название.
        await vm.DuplicateNodeAsync(Root("Копия бухгалтерии"), Work());
        Assert.Contains(Work().Children, c => c.Name == "Копия бухгалтерии_копия 2");
    }

    [Fact]
    public async Task Duplicate_is_unavailable_outside_all_bases_or_without_selection()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        Assert.False(vm.DuplicateCommand.CanExecute(null)); // ничего не выделено
        vm.SearchText = "зуп";
        Assert.True(vm.DuplicateCommand.CanExecute(null));
        vm.ListFilter = BaseListFilter.Favorites;
        Assert.False(vm.DuplicateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Context_menu_has_duplicate_after_new_base()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var menu = window.GetVisualDescendants().OfType<Panel>().First(p => p.ContextMenu is not null).ContextMenu!;
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();

        Assert.Equal(headers.IndexOf("Новая база…") + 1, headers.IndexOf("Дублировать"));
        window.Close();
    }
}
