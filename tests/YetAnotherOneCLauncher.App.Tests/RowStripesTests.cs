using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Чередование цвета строк списка баз: чётные видимые строки — на подложке, заметность — настройка.</summary>
public class RowStripesTests
{
    [AvaloniaFact]
    public async Task Every_second_visible_row_is_striped_in_tree_and_list()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        Assert.Equal((int)RowStripes.Moderate, vm.RowStripesIndex); // по умолчанию — умеренно

        // Дерево: строки по порядку показа — «Рабочие», две базы в ней, затем базы в корне.
        List<TreeNodeViewModel> Visible()
        {
            var rows = new List<TreeNodeViewModel>();
            void Walk(IEnumerable<TreeNodeViewModel> nodes)
            {
                foreach (var node in nodes)
                {
                    rows.Add(node);
                    if (node is FolderNodeViewModel { IsExpanded: true } folder)
                    {
                        Walk(folder.Children);
                    }
                }
            }

            Walk(vm.TreeItems);
            return rows;
        }

        void AssertAlternates() => Assert.Equal(Visible().Select((_, i) => i % 2 == 1), Visible().Select(n => n.IsStripe));
        AssertAlternates();

        // Папку свернули — нечётность строк ниже пересчитана.
        var folder = vm.TreeItems.OfType<FolderNodeViewModel>().First(f => f.Children.Count > 0);
        folder.IsExpanded = false;
        AssertAlternates();
        folder.IsExpanded = true;
        AssertAlternates();

        // Плоский список.
        vm.ToggleViewModeCommand.Execute(null);
        Assert.Equal(vm.ListItems.Select((_, i) => i % 2 == 1), vm.ListItems.Select(i => i.IsStripe));
    }

    [AvaloniaFact]
    public async Task Stripe_color_follows_setting_and_covers_only_the_row()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;
        var tree = window.FindControl<TreeView>("CatalogTree")!;
        MainWindowTests.Render();

        var items = tree.GetVisualDescendants().OfType<TreeViewItem>().ToList();
        var striped = items.Where(i => i.Classes.Contains("stripe")).ToList();
        Assert.NotEmpty(striped);
        Assert.All(striped, i => Assert.True(Painted(i.Background)));
        Assert.All(items.Except(striped), i => Assert.False(Painted(i.Background)));
        MainWindowTests.Snapshot(window, "41-row-stripes-tree");

        // Подложка — только у строки: у развёрнутой папки дети не закрашены вместе с ней.
        var folderItem = items.First(i => i.DataContext is FolderNodeViewModel { IsExpanded: true });
        var header = folderItem.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
        Assert.True(header.Bounds.Height < folderItem.Bounds.Height);

        foreach (var (level, brush) in new[] { (RowStripes.Subtle, "RowStripeSubtleBrush"), (RowStripes.Strong, "RowStripeStrongBrush") })
        {
            vm.RowStripesIndex = (int)level;
            MainWindowTests.Render();
            Assert.True(Avalonia.Application.Current!.TryGetResource(brush, window.ActualThemeVariant, out var expected));
            Assert.Same(expected, striped[0].Background);
        }

        Assert.Equal(RowStripes.Strong, fixture.Settings.Settings.Ui.RowStripes);
        vm.RowStripesIndex = (int)RowStripes.None;
        MainWindowTests.Render();
        Assert.All(items, i => Assert.False(Painted(i.Background)));

        vm.RowStripesIndex = (int)RowStripes.Moderate;
        vm.ToggleViewModeCommand.Execute(null);
        MainWindowTests.Render();
        var listItems = window.FindControl<ListBox>("CatalogList")!.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(listItems.Select((_, i) => i % 2 == 1), listItems.Select(i => Painted(i.Background)));
        MainWindowTests.Snapshot(window, "41-row-stripes-list");
        window.Close();
    }

    private static bool Painted(IBrush? brush) => brush is ISolidColorBrush { Color.A: > 0 };

    [Fact]
    public void Setting_is_edited_on_appearance_tab()
    {
        var settings = new SettingsViewModel(new SettingsValues());
        Assert.Equal(["Не использовать", "Едва заметно", "Умеренно", "Заметно"], settings.RowStripesNames);
        Assert.Equal((int)RowStripes.Moderate, settings.RowStripesIndex);
        settings.RowStripesIndex = 0;
        Assert.True(settings.IsAppearanceDirty);
        Assert.Equal(0, settings.Result.RowStripesIndex);
    }
}

/// <summary>Вкладка «Внешний вид» окна настроек: все поля доступны, нижние — прокруткой.</summary>
public class AppearanceTabTests
{
    [AvaloniaFact]
    public void Appearance_tab_scrolls_to_last_field()
    {
        var window = new SettingsWindow(new SettingsViewModel(new SettingsValues()));
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        MainWindowTests.Render();

        var box = window.FindControl<ComboBox>("RowStripesBox")!;
        var scroll = box.FindAncestorOfType<ScrollViewer>()!;
        Assert.NotNull(scroll);
        // Помещается без прокрутки и с местом под заголовок окна (в тестах его нет — система его не рисует).
        var bottom = box.TranslatePoint(new Avalonia.Point(0, box.Bounds.Height), scroll)!.Value.Y;
        Assert.True(scroll.Viewport.Height - bottom >= Controls.WindowTitleBar.Height, $"низ поля {bottom}, область {scroll.Viewport.Height}");
        window.Close();
    }
}
