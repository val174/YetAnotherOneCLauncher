using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Platform;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Временное главное окно: показывает дерево баз из всех списков и предупреждения загрузки.
/// Код будет перенесён в MVVM, когда появятся поиск, запуск и редактирование.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly (string Title, ThemeVariant Variant)[] Themes =
    [
        ("Тема: как в системе", ThemeVariant.Default),
        ("Тема: светлая", ThemeVariant.Light),
        ("Тема: тёмная", ThemeVariant.Dark),
    ];

    public MainWindow()
    {
        InitializeComponent();

        ThemeBox.ItemsSource = Themes.Select(t => t.Title).ToList();
        ThemeBox.SelectedIndex = 0;
        ThemeBox.SelectionChanged += OnThemeChanged;

        ReloadButton.Click += async (_, _) => await ReloadAsync();
        Opened += async (_, _) => await ReloadAsync();
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = ThemeBox.SelectedIndex;
        if (Application.Current is { } app && index >= 0 && index < Themes.Length)
        {
            app.RequestedThemeVariant = Themes[index].Variant;
        }
    }

    private async Task ReloadAsync()
    {
        ReloadButton.IsEnabled = false;
        StatusText.Text = "Загрузка…";
        try
        {
            if (!PlatformServices.IsSupported)
            {
                StatusText.Text = "Эта ОС пока не поддерживается.";
                return;
            }

            var paths = PlatformServices.CreatePaths();
            var catalog = await new InfoBaseCatalogLoader().LoadAsync(paths.ToCatalogSources());

            CatalogTree.ItemsSource = null;
            CatalogTree.Items.Clear();
            foreach (var item in CreateItems(catalog.BuildTree()))
            {
                CatalogTree.Items.Add(item);
            }

            var available = catalog.Lists.Count(l => l.IsAvailable);
            StatusText.Text =
                $"Баз: {catalog.InfoBases.Count}, папок: {catalog.Folders.Count}, " +
                $"списков прочитано: {available} из {catalog.Lists.Count}";

            WarningsExpander.IsVisible = catalog.Warnings.Count > 0;
            WarningsExpander.Header = $"Предупреждения ({catalog.Warnings.Count})";
            WarningsText.Text = string.Join(Environment.NewLine, catalog.Warnings);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка загрузки: " + ex.Message;
        }
        finally
        {
            ReloadButton.IsEnabled = true;
        }
    }

    private static IEnumerable<TreeViewItem> CreateItems(CatalogFolderNode folder)
    {
        foreach (var item in folder.Items)
        {
            switch (item)
            {
                case CatalogFolderNode subFolder:
                {
                    var node = new TreeViewItem { Header = "📁 " + subFolder.Name, IsExpanded = true };
                    foreach (var child in CreateItems(subFolder))
                    {
                        node.Items.Add(child);
                    }

                    yield return node;
                    break;
                }

                case CatalogInfoBaseItem baseItem:
                {
                    var infoBase = baseItem.InfoBase;
                    var readOnlyMark = infoBase.IsReadOnly ? "  [общий список]" : string.Empty;
                    yield return new TreeViewItem
                    {
                        Header = $"{infoBase.Name}   —   {infoBase.Connection.ToDisplayString()}{readOnlyMark}",
                    };
                    break;
                }
            }
        }
    }
}
