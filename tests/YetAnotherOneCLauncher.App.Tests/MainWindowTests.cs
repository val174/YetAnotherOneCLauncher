using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>
/// Окно в headless-режиме: разметка, привязки и клавиатура.
/// Если задана переменная YAOCL_SCREENSHOTS, кадры окна сохраняются туда в PNG — для просмотра вёрстки.
/// </summary>
public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Opens_with_tree_and_focused_search()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        var tree = window.FindControl<TreeView>("CatalogTree")!;
        Assert.True(tree.IsEffectivelyVisible);
        Assert.Equal(3, tree.ItemCount);
        Assert.NotNull(window.Icon); // значок приложения из стиля окон
        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);

        // Кнопки запуска видны и без выделенной базы — просто недоступны.
        Assert.True(window.FindControl<StackPanel>("LaunchButtons")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("LaunchEnterpriseButton")!.IsEffectivelyEnabled);
        Snapshot(window, "01-tree");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Rows_hide_connection_path_but_search_still_finds_by_it()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        string RowTexts(Control root) => string.Join("|", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

        var tree = window.FindControl<TreeView>("CatalogTree")!;
        Assert.DoesNotContain(@"srv-1c\buh_prod", RowTexts(tree), StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Bases\BuhCopy", RowTexts(tree), StringComparison.Ordinal);

        fixture.ViewModel.SearchText = "buh_prod"; // только в пути, в имени такого нет
        Render();
        var list = window.FindControl<ListBox>("CatalogList")!;
        var item = Assert.Single(fixture.ViewModel.ListItems);
        Assert.Equal("Бухгалтерия предприятия", item.Base.Name);
        Assert.DoesNotContain("srv-1c", RowTexts(list), StringComparison.Ordinal);
        Assert.Contains("/Рабочие", RowTexts(list), StringComparison.Ordinal); // папка в списке осталась
        Assert.Contains(@"srv-1c\buh_prod", item.Base.NameToolTip, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Typing_searches_and_enter_launches_best_match()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        window.KeyTextInput(",e["); // «бух» в английской раскладке
        Render();

        var list = window.FindControl<ListBox>("CatalogList")!;
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(2, list.ItemCount);
        Snapshot(window, "02-search");

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitAsync(() => fixture.Processes.Started.Count > 0);

        Assert.Equal("ENTERPRISE", Assert.Single(fixture.Processes.Started).Arguments[0]);
        Assert.Equal("Бухгалтерия предприятия", fixture.Settings.Settings.History.Single().InfoBase.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task F4_launches_designer_and_ctrl_d_toggles_favorite()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        window.KeyTextInput("зуп");
        Render();

        window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d");
        window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
        await WaitAsync(() => fixture.Processes.Started.Count > 0);

        Assert.Equal("DESIGNER", Assert.Single(fixture.Processes.Started).Arguments[0]);
        Assert.True(fixture.ViewModel.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").IsFavorite);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Render();
        Assert.True(fixture.ViewModel.ShowTree);
        Snapshot(window, "03-favorites-and-recent");

        fixture.ViewModel.ShowRecentCommand.Execute(null);
        Render();
        Assert.True(window.FindControl<ListBox>("CatalogList")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("AddButton")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<Button>("DeleteButton")!.IsEffectivelyEnabled);
        Snapshot(window, "17-recent");
        fixture.ViewModel.ShowAllBasesCommand.Execute(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Status_bar_button_toggles_details_panel()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var toggle = window.FindControl<ToggleButton>("DetailsToggle")!;
        Assert.True(toggle.IsChecked);
        Snapshot(window, "08-details-on");

        toggle.IsChecked = false;
        Render();

        Assert.False(fixture.ViewModel.ShowDetails);
        Assert.False(fixture.Settings.Settings.Ui.ShowDetails); // запоминается
        Assert.True(window.FindControl<StackPanel>("LaunchButtons")!.IsEffectivelyVisible); // кнопки запуска — всегда
        Snapshot(window, "09-details-off");

        toggle.IsChecked = true;
        Render();
        Assert.True(fixture.ViewModel.ShowDetails);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Connection_row_has_copy_and_open_folder_buttons()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        fixture.ViewModel.IsTreeMode = false;
        fixture.ViewModel.SelectedListItem = fixture.ViewModel.ListItems.Single(i => i.Base.Name == "Копия бухгалтерии");
        Render();

        var copy = window.FindControl<Button>("CopyConnectionButton")!;
        var open = window.FindControl<Button>("OpenBaseFolderButton")!;
        Assert.True(copy.IsEffectivelyVisible);
        Assert.True(open.IsEffectivelyVisible);
        // Значки кнопок запуска — цвета надписи: на синей «1С: Предприятие» — белые, не серые по умолчанию.
        foreach (var name in new[] { "LaunchEnterpriseButton", "LaunchDesignerButton" })
        {
            var button = window.FindControl<Button>(name)!;
            var icon = button.GetVisualDescendants().OfType<ToolIcon>().Single();
            var caption = button.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.NotNull(icon.Foreground);
            Assert.Equal((icon.Foreground as Avalonia.Media.ISolidColorBrush)?.Color, (caption.Foreground as Avalonia.Media.ISolidColorBrush)?.Color);
        }

        // Строка состояния: адрес выделенной базы и копирование строки подключения.
        Assert.Equal(@"C:\Bases\BuhCopy", window.FindControl<TextBlock>("StatusAddressText")!.Text);
        var statusCopy = window.FindControl<Button>("StatusCopyConnectionButton")!;
        Assert.True(statusCopy.IsEffectivelyVisible);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)statusCopy.Command!).ExecuteAsync(null);
        Assert.Equal(@"File=""C:\Bases\BuhCopy"";", fixture.Shell.ClipboardText);

        Snapshot(window, "10-details-file-base");

        copy.Command!.Execute(copy.CommandParameter);
        await WaitAsync(() => fixture.Shell.ClipboardText is not null);
        Assert.Equal("""File="C:\Bases\BuhCopy";""", fixture.Shell.ClipboardText);

        // У серверной базы каталога нет — кнопка скрыта.
        fixture.ViewModel.SelectedListItem = fixture.ViewModel.ListItems.Single(i => i.Base.Name == "Бухгалтерия предприятия");
        Render();
        Assert.False(open.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Splitter_resizes_details_panel_and_width_is_remembered()
    {
        using var fixture = new ViewModelFixture();
        fixture.Settings.Settings.Ui.DetailsWidth = 360;
        var window = await OpenAsync(fixture);
        var splitter = window.FindControl<GridSplitter>("DetailsSplitter")!;
        var search = window.FindControl<TextBox>("SearchBox")!;
        var tree = window.FindControl<TreeView>("CatalogTree")!;
        double RightEdge(Control c) => c.TranslatePoint(new Point(c.Bounds.Width, 0), window)!.Value.X;

        Assert.Equal(360, window.FindControl<Grid>("BodyGrid")!.ColumnDefinitions[2].ActualWidth);
        // Кнопки слева; поле поиска — до правого края окна, над панелью подробностей и её ширины.
        var toolbar = window.FindControl<Panel>("ToolbarPanel")!;
        double LeftEdge(Control c) => c.TranslatePoint(default, window)!.Value.X;
        Assert.Equal(8, LeftEdge(toolbar), tolerance: 1.5);
        Assert.True(LeftEdge(search) > RightEdge(toolbar));
        Assert.Equal(window.Bounds.Width - 8, RightEdge(search), tolerance: 1.5);
        Assert.Equal(LeftEdge(splitter), LeftEdge(search), tolerance: 1.5);

        // Тянем разделитель на 100 пикселей влево — панель шире.
        var start = splitter.TranslatePoint(new Point(2, 200), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(-50, 0));
        window.MouseMove(start + new Point(-100, 0));
        window.MouseUp(start + new Point(-100, 0), MouseButton.Left);
        Render();

        Assert.Equal(460, fixture.ViewModel.DetailsWidth, tolerance: 1);
        Assert.Equal(460, fixture.Settings.Settings.Ui.DetailsWidth, tolerance: 1);
        Assert.Equal(window.Bounds.Width - 8, RightEdge(search), tolerance: 1.5);
        Assert.Equal(LeftEdge(splitter), LeftEdge(search), tolerance: 1.5); // поле растёт вместе с панелью
        Snapshot(window, "11-details-wide");

        // Свойства скрыты — кнопки запуска остаются на месте, ширина колонки и поиска прежние.
        var searchWidth = search.Bounds.Width;
        var treeRight = RightEdge(tree);
        fixture.ViewModel.ShowDetails = false;
        Render();
        Assert.False(window.FindControl<Panel>("PropertiesPanel")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<StackPanel>("LaunchButtons")!.IsEffectivelyVisible);
        Assert.Equal(treeRight, RightEdge(tree), tolerance: 1.5);
        Assert.Equal(window.Bounds.Width - 8, RightEdge(search), tolerance: 1.5);
        Assert.Equal(searchWidth, search.Bounds.Width, tolerance: 0.5);
        Snapshot(window, "09b-properties-hidden");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Editor_offers_installed_platform_versions()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.ViewModel.SelectedTreeItem = fixture.ViewModel.TreeItems.OfType<ViewModels.BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");
        ViewModels.InfoBaseEditorViewModel? editor = null;
        fixture.Dialogs.InfoBaseEditor = e =>
        {
            editor = e;
            return false;
        };
        await fixture.ViewModel.EditCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "8.5", "8.3", "8.5.1.1150", "8.3.27.2130", "8.3.24.1667" }, editor!.PlatformVersions);

        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        Render();
        var box = window.FindControl<ComboBox>("VersionBox")!;
        Assert.Equal("8.3.22", box.Text); // версия из списка баз, которой нет среди установленных, — как есть

        box.SelectedItem = "8.3.24.1667";
        Render();
        Assert.Equal("8.3.24.1667", editor.Version);

        box.IsDropDownOpen = true;
        Render();
        Snapshot(window, "12-editor-versions");
        box.IsDropDownOpen = false;

        editor.Version = "8.3.25";
        Render();
        Assert.Equal("8.3.25", box.Text);
        Assert.True(editor.TryAccept());
        Assert.Equal("8.3.25", editor.Result!.Version);
        window.Close();
    }

    [AvaloniaFact]
    public async Task One_off_window_shows_platform_choice()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        ViewModels.LaunchParametersViewModel? form = null;
        fixture.Dialogs.LaunchParameters = f =>
        {
            form = f;
            return (false, null);
        };
        await fixture.ViewModel.LaunchWithParametersCommand.ExecuteAsync(fixture.Base("Бухгалтерия предприятия"));

        var window = new LaunchParametersWindow(form!);
        window.Show();
        Render();
        Assert.True(window.FindControl<ComboBox>("PlatformBox")!.IsEffectivelyVisible);
        Assert.Equal(4, window.FindControl<ComboBox>("PlatformBox")!.ItemCount);
        Snapshot(window, "13-one-off-platform");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Collapse_and_expand_all_buttons_work_only_in_tree()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var vm = fixture.ViewModel;
        vm.ToggleFavoriteCommand.Execute(fixture.Base("Бухгалтерия предприятия")); // папка «Избранное»
        Render();
        var collapse = window.FindControl<Button>("CollapseAllButton")!;
        var expand = window.FindControl<Button>("ExpandAllButton")!;
        IEnumerable<ViewModels.FolderNodeViewModel> Folders() => vm.TreeItems.OfType<ViewModels.FolderNodeViewModel>();
        Assert.True(collapse.IsEffectivelyVisible);
        Assert.True(expand.IsEffectivelyVisible);

        collapse.Command!.Execute(null);
        Render();
        Assert.All(Folders(), f => Assert.False(f.IsExpanded));
        Assert.Contains("/Рабочие", fixture.Settings.Settings.Ui.CollapsedFolders);
        Snapshot(window, "14-collapsed");

        expand.Command!.Execute(null);
        Render();
        Assert.All(Folders(), f => Assert.True(f.IsExpanded));
        Assert.Empty(fixture.Settings.Settings.Ui.CollapsedFolders);

        // Кнопки прячутся, но место под них остаётся: поле поиска не прыгает, пока вводишь текст.
        var search = window.FindControl<TextBox>("SearchBox")!;
        double SearchLeft() => search.TranslatePoint(default, window)!.Value.X;
        var searchLeft = SearchLeft();
        var add = window.FindControl<Button>("AddButton")!;
        double AddLeft() => add.TranslatePoint(default, window)!.Value.X;
        var addLeft = AddLeft();

        vm.SearchText = "бух"; // результаты поиска — списком
        Render();
        Assert.False(collapse.IsEffectivelyEnabled); // скрыта, но место держит
        Assert.Equal(0, collapse.Opacity);
        Assert.Equal(searchLeft, SearchLeft(), tolerance: 0.5);
        vm.SearchText = string.Empty;
        vm.IsTreeMode = false;
        Render();
        Assert.False(expand.IsEffectivelyEnabled);
        Assert.Equal(0, expand.Opacity);
        Assert.Equal(searchLeft, SearchLeft(), tolerance: 0.5);
        Assert.Equal(addLeft, AddLeft(), tolerance: 0.5); // кнопки после «Развернуть/Свернуть» не сдвигаются

        // Пункты контекстного меню — со значками, как у кнопок главного окна.
        var menu = window.GetVisualDescendants().OfType<Panel>().Select(p => p.ContextMenu).First(m => m is not null)!;
        var items = menu.Items.OfType<MenuItem>().ToDictionary(i => (string)i.Header!);
        foreach (var header in new[] { "1С: Предприятие", "Конфигуратор", "Запустить с параметрами…", "Удалить", "Новая база…", "Избранное", "Копировать строку подключения" })
        {
            Assert.IsType<ToolIcon>(items[header].Icon);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Toolbar_order_and_theme_button_cycles_themes()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var vm = fixture.ViewModel;
        var toolbar = window.FindControl<Panel>("ToolbarPanel")!.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToList();
        // Сначала — вид списка/дерева, затем правка списка и режимы.
        Assert.Equal(
            new[] { "ViewModeButton", "ExpandAllButton", "CollapseAllButton", "AddButton", "DeleteButton", "AllBasesButton", "RecentButton" },
            toolbar.Take(7).Select(b => b.Name));
        Assert.Equal("ThemeButton", toolbar[^2].Name);

        var theme = window.FindControl<Button>("ThemeButton")!;
        var expected = new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System };
        Assert.True(vm.IsSystemTheme);
        Snapshot(window, "15-theme-system");
        foreach (var mode in expected)
        {
            theme.Command!.Execute(null);
            Avalonia.Application.Current!.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
                ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Light,
            };
            Render();
            Assert.Equal(mode, fixture.Shell.AppliedTheme);
            Assert.Equal(mode, fixture.Settings.Settings.Ui.Theme);
            Assert.Equal((int)mode, vm.ThemeIndex); // тот же выбор, что в настройках
            Snapshot(window, "15-theme-" + mode.ToString().ToLowerInvariant());
        }

        Assert.Contains("светлая", vm.ThemeToolTip, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Icon_style_switches_all_icons_and_is_saved()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var vm = fixture.ViewModel;
        window.KeyTextInput("зуп");
        Render();
        window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d"); // «Избранное» в дереве
        window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
        await WaitAsync(() => fixture.Processes.Started.Count > 0);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Render();

        Assert.Equal(IconStyle.Outline, vm.IconStyle);
        Assert.Equal(new[] { "Стиль 1", "Стиль 2" }, vm.IconStyleNames);
        var addIcon = window.FindControl<Button>("AddButton")!.GetVisualDescendants().OfType<ToolIcon>().Single();
        foreach (var (index, style) in new[] { (1, IconStyle.Plate), (0, IconStyle.Outline) })
        {
            vm.IconStyleIndex = index;
            foreach (var variant in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = variant;
                Render();
                Assert.Equal(style, ToolIcon.GetIconStyle(addIcon)); // стиль наследуется от окна
                Assert.Equal(style, fixture.Settings.Settings.Ui.IconStyle);
                Snapshot(window, $"16-icons-{style.ToString().ToLowerInvariant()}-{variant.Key.ToString()!.ToLowerInvariant()}");
            }
        }

        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        window.Close();
    }

    [AvaloniaFact]
    public async Task Delete_button_uses_same_command_and_dialog_has_checked_flag()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var delete = window.FindControl<Button>("DeleteButton")!;
        Assert.Same(fixture.ViewModel.DeleteCommand, delete.Command);

        var dialog = MessageDialog.AskWithOptionAsync(
            window,
            "Список баз",
            "Удалить «Копия бухгалтерии» из списка баз?" + Environment.NewLine + "Сама база (её данные) не удаляется.",
            "Удалить",
            "Удалить временные файлы информационной базы (4 КБ)",
            optionChecked: true);
        Render();
        var dialogWindow = window.OwnedWindows.OfType<MessageDialog>().Single();
        var option = dialogWindow.GetLogicalDescendants().OfType<CheckBox>().Single();
        Assert.True(option.IsChecked);
        Snapshot(dialogWindow, "16-delete-dialog");

        // Снять флажок и нажать «Удалить» (кнопка закрывает окно с true).
        option.IsChecked = false;
        dialogWindow.Close(true);
        var (accepted, deleteCache) = await dialog;
        Assert.True(accepted);
        Assert.False(deleteCache);
        window.Close();
    }

    [AvaloniaFact]
    public async Task F3_launches_enterprise_and_f6_opens_launch_with_parameters()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        window.KeyTextInput("зуп");
        Render();

        window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        await WaitAsync(() => fixture.Processes.Started.Count == 1);
        Assert.Equal("ENTERPRISE", fixture.Processes.Started[0].Arguments[0]);

        fixture.Dialogs.LaunchParameters = _ => (true, LaunchMode.Designer);
        window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        await WaitAsync(() => fixture.Processes.Started.Count == 2);
        Assert.Single(fixture.Dialogs.LaunchParameterForms);
        Assert.Equal("DESIGNER", fixture.Processes.Started[1].Arguments[0]);

        // Прежние сочетания больше не запускают Конфигуратор: Ctrl+Enter — ничего.
        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        Render();
        Assert.Equal(2, fixture.Processes.Started.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_q_clears_search_from_list()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        window.KeyTextInput("бух");
        Render();
        window.FindControl<ListBox>("CatalogList")!.ContainerFromIndex(0)!.Focus();
        Render();

        window.KeyPress(Key.Q, RawInputModifiers.Control, PhysicalKey.Q, "q");
        Render();

        Assert.Equal(string.Empty, fixture.ViewModel.SearchText);
        Assert.True(fixture.ViewModel.ShowTree);
        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);

        // Русская раскладка: та же физическая клавиша, символ «й».
        window.KeyTextInput("зуп");
        Render();
        window.KeyPress(Key.None, RawInputModifiers.Control, PhysicalKey.Q, "й");
        Render();
        Assert.Equal(string.Empty, fixture.ViewModel.SearchText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Dark_theme_list_mode_snapshot()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        fixture.ViewModel.ThemeIndex = (int)ThemeMode.Dark;
        Assert.Equal(ThemeMode.Dark, fixture.Shell.AppliedTheme);
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; // вместо поддельной службы темы
        fixture.ViewModel.IsTreeMode = false;
        fixture.ViewModel.SelectedListItem = fixture.ViewModel.ListItems[1];
        Render();

        Assert.True(window.FindControl<ListBox>("CatalogList")!.IsEffectivelyVisible);
        Snapshot(window, "04-dark-list");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Editing_keys_in_tree_keep_focus_after_rebuild()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        var tree = window.FindControl<TreeView>("CatalogTree")!;
        var retail = fixture.ViewModel.TreeItems.Single(n => n.Name == "Розница (тест)");
        fixture.ViewModel.SelectedTreeItem = retail;
        Render();
        tree.ContainerFromItem(retail)!.Focus(); // как после щелчка по элементу
        Render();

        window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
        await WaitAsync(() => fixture.ViewModel.TreeItems[1].Name == "Розница (тест)");
        Render();

        // Дерево перестроено — фокус вернулся на ту же базу, Del работает сразу.
        Assert.True(tree.IsKeyboardFocusWithin);
        fixture.Dialogs.ConfirmAnswer = false;
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await WaitAsync(() => fixture.Dialogs.Questions.Count == 1);
        Assert.Contains("Розница (тест)", fixture.Dialogs.Questions[0], StringComparison.Ordinal);

        var opened = false;
        fixture.Dialogs.InfoBaseEditor = _ =>
        {
            opened = true;
            return false; // отмена: правка списка не начинается и не переживает тест
        };
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        await WaitAsync(() => opened);
        window.Close();
    }

    [AvaloniaFact]
    public void Editor_window_snapshot()
    {
        var editor = new ViewModels.InfoBaseEditorViewModel(
            new Core.Editing.InfoBaseDraft { Name = "Бухгалтерия", Kind = Core.Parsing.ConnectionKind.Server, Server = "srv-1c", InfobaseName = "buh", FolderPath = "/Рабочие", Version = "8.3" },
            ["/Рабочие", "/Архив"],
            isNew: false,
            new FakeFiles())
        {
            LaunchParametersEditor = _ => Task.CompletedTask,
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        editor.InfobaseName = string.Empty;
        editor.TryAccept(); // показать ошибку проверки
        Render();

        Assert.True(editor.HasErrors);
        Snapshot(window, "05-editor");
        window.Close();
    }

    [AvaloniaFact]
    public void Launch_parameters_window_snapshot()
    {
        var form = new ViewModels.LaunchParametersViewModel(
            ViewModels.LaunchParametersKind.InfoBase,
            "Бухгалтерия",
            Core.Launching.ParameterLibrary.BuiltIn,
            ["/DisableStartupMessages", "/L ru"],
            new FakeFiles())
        {
            Parameters = "/UC 42",
            UserName = "Бухгалтер",
            HasSavedPassword = true,
            SavePassword = true,
        };
        var window = new LaunchParametersWindow(form);
        window.Show();
        Render();

        Assert.True(window.FindControl<TextBox>("ParametersBox")!.IsFocused);
        Assert.False(window.FindControl<Button>("EnterpriseButton")!.IsVisible);
        Snapshot(window, "06-launch-parameters");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Cache_manager_window_snapshot()
    {
        using var fixture = new ViewModelFixture();
        fixture.AddCache("00000000-0000-0000-0000-000000000001", 3_500_000);
        fixture.AddCache("00000000-0000-0000-0000-000000000001", 120_000, roaming: true);
        fixture.AddCache("00000000-0000-0000-0000-000000000003", 800_000);
        fixture.AddCache("99999999-0000-0000-0000-000000000009", 1_200_000_000 / 1000);
        await fixture.LoadAsync();
        await fixture.ViewModel.CacheScanTask;

        ViewModels.CacheManagerViewModel? manager = null;
        fixture.Dialogs.CacheManager = m =>
        {
            manager = m;
            return Task.CompletedTask;
        };
        await fixture.ViewModel.OpenCacheManagerCommand.ExecuteAsync(null);
        manager!.SelectOrphansCommand.Execute(null);

        var window = new CacheManagerWindow(manager) { Width = 820, Height = 420 };
        window.Show();
        Render();

        Assert.Equal(3, window.FindControl<ListBox>("RowsList")!.ItemCount);
        Assert.True(window.FindControl<Button>("CleanButton")!.IsEffectivelyEnabled);
        Snapshot(window, "07-cache-manager");
        window.Close();
    }

    private static async Task<MainWindow> OpenAsync(ViewModelFixture fixture)
    {
        // Настоящее окно применяет тему через Application; в тестах — через подделку, поэтому ставим вручную.
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var window = new MainWindow(fixture.ViewModel) { Width = 1040, Height = 560 };
        window.Show();
        await WaitAsync(() => fixture.ViewModel.InfoBases.Count > 0);
        Render();
        return window;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "Условие не выполнилось за отведённое время.");
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Snapshot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YAOCL_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Render();
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
