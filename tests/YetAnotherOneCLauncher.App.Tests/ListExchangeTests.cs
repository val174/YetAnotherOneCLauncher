using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Сохранение личного списка в JSON и загрузка (задача #24): меню ≡, контекстное меню, окно загрузки, настройки.</summary>
public class ListExchangeTests
{
    private static InfoBaseListFile ReadFile(string path) => InfoBaseListExchange.Parse(File.ReadAllBytes(path));

    private static void WriteFile(string path, string json) => File.WriteAllText(path, json, new UTF8Encoding(false));

    [Fact]
    public async Task Whole_list_is_saved_with_settings_from_options()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Settings.UserData.SetFavorite(fixture.Base("Копия бухгалтерии").InfoBase, true);
        fixture.Settings.UserData.SetLaunchProfile(
            fixture.Base("Зарплата и управление персоналом").InfoBase,
            new InfoBaseLaunchProfile { Parameters = "/Debug", UserName = "Кадровик", PasswordKey = "pw-key" });
        fixture.Files.SaveAnswer = Path.Combine(fixture.Directory, "list.json");
        vm.ListFilter = BaseListFilter.Recent; // сохранять можно в любом режиме
        Assert.True(vm.ExportListJsonCommand.CanExecute(null));

        await vm.ExportListJsonCommand.ExecuteAsync(null);

        var file = ReadFile(fixture.Files.SaveAnswer);
        Assert.StartsWith("ibases-", fixture.Files.SuggestedName, StringComparison.Ordinal);
        Assert.EndsWith(".json", fixture.Files.SuggestedName, StringComparison.Ordinal);
        Assert.Equal(["/Рабочие"], file.Folders.Select(f => f.Path));
        Assert.Equal(4, file.InfoBases.Count);
        Assert.True(file.InfoBases.Single(b => b.Name == "Копия бухгалтерии").Launcher?.Favorite);
        var zup = file.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").Launcher!;
        Assert.Equal("/Debug", zup.Parameters);
        Assert.Null(zup.UserName); // имена пользователей по умолчанию не сохраняются
        Assert.DoesNotContain("pw-key", File.ReadAllText(fixture.Files.SaveAnswer), StringComparison.Ordinal);
        Assert.Contains("баз: 4, групп: 1", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selected_group_or_base_is_saved_from_context_menu()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Files.SaveAnswer = Path.Combine(fixture.Directory, "group.json");
        Assert.False(vm.ExportSelectionJsonCommand.CanExecute(null)); // ничего не выделено

        vm.SelectedTreeItem = vm.TreeItems.OfType<FolderNodeViewModel>().Single(f => f.Name == "Рабочие");
        Assert.True(vm.ExportSelectionJsonCommand.CanExecute(null));
        await vm.ExportSelectionJsonCommand.ExecuteAsync(null);

        Assert.Equal("Рабочие.json", fixture.Files.SuggestedName);
        var group = ReadFile(fixture.Files.SaveAnswer);
        Assert.Equal(["/Рабочие"], group.Folders.Select(f => f.Path));
        Assert.Equal(
            ["Бухгалтерия предприятия", "Зарплата и управление персоналом"],
            group.InfoBases.Select(b => b.Name).Order(StringComparer.Ordinal));

        vm.SelectedTreeItem = vm.TreeItems.OfType<BaseNodeViewModel>().Single(b => b.Name == "Розница (тест)");
        await vm.ExportSelectionJsonCommand.ExecuteAsync(null);
        var single = ReadFile(fixture.Files.SaveAnswer);
        Assert.Empty(single.Folders);
        Assert.Equal("ws=\"https://web.example/retail\";", Assert.Single(single.InfoBases).Connection);
    }

    [Fact]
    public async Task Bases_from_common_list_cannot_be_saved_to_json()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadWithCommonListAsync("""
            [Общая база]
            Connect=Srvr="srv";Ref="common";
            """);
        var vm = fixture.ViewModel;
        vm.SearchText = "Общая";
        vm.SelectedListItem = vm.ListItems.Single();

        Assert.False(vm.ExportSelectionJsonCommand.CanExecute(null));
    }

    [Fact]
    public async Task Import_in_add_mode_shows_summary_adds_new_bases_and_their_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var path = Path.Combine(fixture.Directory, "in.json");
        WriteFile(path, """
            {
              "format": "YetAnotherOneCLauncher.InfoBaseList",
              "formatVersion": 1,
              "appVersion": "0.3.0.1",
              "exportedAt": "2026-10-13T09:15:00+03:00",
              "folders": [ { "path": "/Разработка" } ],
              "infoBases": [
                { "id": "00000000-0000-0000-0000-000000000002", "name": "Зарплата и управление персоналом", "folder": "/Рабочие", "connection": "Srvr=\"srv-1c\";Ref=\"zup\";" },
                { "name": "ЗУП (тест)", "folder": "/Разработка", "connection": "Srvr=\"msc-1c-02\";Ref=\"zup_test\";",
                  "launcher": { "favorite": true, "parameters": "/Debug", "userName": "Тестер" } },
                { "name": "Копия бухгалтерии", "connection": "File=\"D:\\Другая\";" }
              ]
            }
            """);
        fixture.Files.OpenAnswer = path;
        fixture.Dialogs.ImportConfirmer = form =>
        {
            Assert.True(form.IsAddMode);
            Assert.True(form.HasSettings);
            Assert.True(form.ApplySettings);
            return true;
        };

        await vm.ImportListJsonCommand.ExecuteAsync(null);

        var form = Assert.Single(fixture.Dialogs.ImportForms);
        Assert.Equal("in.json", form.FileName);
        Assert.Equal("В файле баз: 3, групп: 1.", form.ContentText);
        Assert.Contains("версия 0.3.0.1", form.SavedText, StringComparison.Ordinal);
        Assert.Equal("Новых записей: 2; уже в списке: 1; название занято: 1.", form.OutcomeText);
        form.IsReplaceMode = true;
        Assert.Contains("Будет загружено записей: 4", form.OutcomeText, StringComparison.Ordinal);
        Assert.Contains("Сейчас в личном списке записей: 5", form.OutcomeText, StringComparison.Ordinal);

        var saved = fixture.SavedList().Sections.Where(s => s.Name.Length > 0).Select(s => s.Name).ToList();
        Assert.Equal(7, saved.Count);
        Assert.Contains("ЗУП (тест)", saved);
        Assert.Contains("Разработка", saved);
        var added = fixture.Base("ЗУП (тест)");
        Assert.True(added.IsFavorite);
        Assert.Equal("Тестер", fixture.Settings.UserData.LaunchProfile(added.InfoBase)?.UserName);
        Assert.Null(fixture.Settings.UserData.LaunchProfile(added.InfoBase)?.PasswordKey);
        Assert.Contains("добавлено записей: 2", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("настройки перенесены для баз: 1", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_in_replace_mode_replaces_list_and_keeps_backup()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var before = File.ReadAllBytes(fixture.ListPath);
        var path = Path.Combine(fixture.Directory, "in.json");
        WriteFile(path, """
            {
              "format": "YetAnotherOneCLauncher.InfoBaseList",
              "infoBases": [ { "name": "Единственная", "connection": "File=\"D:\\One\";", "launcher": { "favorite": true } } ]
            }
            """);
        fixture.Files.OpenAnswer = path;
        fixture.Dialogs.ImportConfirmer = form =>
        {
            form.IsReplaceMode = true;
            form.ApplySettings = false;
            return true;
        };

        await vm.ImportListJsonCommand.ExecuteAsync(null);

        Assert.Equal(["Единственная"], fixture.SavedList().Sections.Where(s => s.Name.Length > 0).Select(s => s.Name));
        Assert.Equal(before, File.ReadAllBytes(fixture.Store.BackupPath));
        Assert.False(fixture.Base("Единственная").IsFavorite); // настройки не загружать — так выбрано
        Assert.Contains("Список заменён", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("ibases.v8i.bak", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_or_wrong_file_changes_nothing()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var before = File.ReadAllBytes(fixture.ListPath);
        var path = Path.Combine(fixture.Directory, "in.json");
        WriteFile(path, """{ "format": "YetAnotherOneCLauncher.InfoBaseList", "infoBases": [ { "name": "Новая", "connection": "File=\"D:\\N\";" } ] }""");
        fixture.Files.OpenAnswer = path;

        await vm.ImportListJsonCommand.ExecuteAsync(null); // «Отмена» в окне загрузки
        Assert.Single(fixture.Dialogs.ImportForms);
        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));

        WriteFile(path, """{ "something": "else" }""");
        await vm.ImportListJsonCommand.ExecuteAsync(null);
        Assert.Single(fixture.Dialogs.ImportForms); // окно не открывалось
        Assert.Contains("не файл списка баз", Assert.Single(fixture.Dialogs.Messages), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));
    }

    [Fact]
    public async Task Settings_section_keeps_options_and_saves_with_unsaved_checkboxes()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        fixture.Settings.UserData.SetLaunchProfile(
            fixture.Base("Зарплата и управление персоналом").InfoBase, new InfoBaseLaunchProfile { UserName = "Кадровик" });
        fixture.Files.SaveAnswer = Path.Combine(fixture.Directory, "from-settings.json");
        Task? saving = null;
        fixture.Dialogs.SettingsEditor = settings =>
        {
            Assert.True(settings.CanExchangeList);
            Assert.True(settings.ExportLaunchParameters);
            Assert.True(settings.ExportBaseSettings);
            Assert.False(settings.ExportUserNames);

            // Флажок ещё не сохранён, но сохранение из окна настроек его учитывает.
            settings.ExportUserNames = true;
            Assert.True(settings.IsGeneralDirty);
            saving = settings.SaveListJsonCommand.ExecuteAsync(null);
            return true;
        };

        await vm.OpenSettingsCommand.ExecuteAsync(null);
        await saving!;

        Assert.Equal("Кадровик", ReadFile(fixture.Files.SaveAnswer).InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").Launcher?.UserName);
        Assert.True(fixture.Settings.Settings.ListExchange.IncludeUserNames); // «Сохранить» в окне настроек
    }

    [Fact]
    public void Hot_keys_have_defaults_for_save_and_load()
    {
        Assert.Equal(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.S, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift), HotKeyMap.Default.ExportListJson);
        Assert.Equal(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift), HotKeyMap.Default.ImportListJson);
    }

    [AvaloniaFact]
    public async Task Menu_button_is_in_header_and_context_menu_has_json_item()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var panel = window.FindControl<StackPanel>("ToolbarButtons")!;
        var menuButton = window.FindControl<Button>("MenuButton")!;
        // Задача #31: меню — в верхней строке справа (перед кнопками окна), а не на панели команд.
        Assert.Same(window.FindControl<DockPanel>("HeaderPanel"), menuButton.Parent);
        Assert.Equal(Dock.Right, DockPanel.GetDock(menuButton));
        Assert.DoesNotContain(menuButton, panel.Children);
        Assert.Equal("ViewModeButton", panel.Children[0].Name);

        var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
        Assert.Equal(
            ["Сохранить список в файл JSON…", "Загрузить список из файла JSON…", "Загрузить из файла .v8i…", "Восстановить список…"],
            flyout.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
        menuButton.Flyout!.ShowAt(menuButton);
        MainWindowTests.Render();
        MainWindowTests.Snapshot(window, "50-menu-json");
        menuButton.Flyout.Hide();

        var menu = window.GetVisualDescendants().OfType<Panel>().Select(p => p.ContextMenu).First(m => m is not null)!;
        var headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Equal(headers.IndexOf("Сохранить ссылку в файл…") + 1, headers.IndexOf("Сохранить в файл JSON…"));
        Assert.IsType<ToolIcon>(menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Сохранить в файл JSON…").Icon);
        window.Close();
    }

    [AvaloniaFact]
    public void Settings_general_tab_has_list_section_with_help_button()
    {
        var form = new SettingsViewModel(new SettingsValues()) { SaveList = _ => Task.CompletedTask, LoadList = () => Task.CompletedTask };
        var window = new SettingsWindow(form);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<Button>("SaveListJsonButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("LoadListJsonButton")!.IsEffectivelyVisible);
        var help = window.FindControl<Button>("UserNamesHelpButton")!;
        Assert.Equal("?", help.Content);
        Assert.Equal("Пароли в файл не сохраняются.", ToolTip.GetTip(help));
        Assert.Same(window.FindControl<CheckBox>("ExportUserNamesBox")!.Parent, help.Parent); // в одной строке с флажком
        window.FindControl<TabControl>("Tabs")!.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
        MainWindowTests.Snapshot(window, "50-settings-list-json");

        // Без личного списка кнопок нет.
        var without = new SettingsWindow(new SettingsViewModel(new SettingsValues()));
        without.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(without.FindControl<Button>("SaveListJsonButton")!.IsEffectivelyVisible);
        without.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Import_window_switches_mode_and_shows_outcome()
    {
        var file = new InfoBaseListFile
        {
            AppVersion = "0.3.0.1",
            InfoBases = [new ExportedInfoBase { Name = "А", Connection = "File=\"D:\\A\";", Launcher = new ExportedLaunchSettings { Favorite = true } }],
        };
        var import = new ImportListViewModel("work.json", file, new Core.Editing.ImportResult(1, 0), new Core.Editing.ImportResult(1, 0), 3);
        var window = new ImportListWindow(import);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        MainWindowTests.Snapshot(window, "50-import-window");

        Assert.True(window.FindControl<RadioButton>("AddModeRadio")!.IsChecked);
        window.FindControl<RadioButton>("ReplaceModeRadio")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ImportMode.Replace, import.Mode);
        Assert.Contains("будут удалены", window.FindControl<TextBlock>("OutcomeText")!.Text, StringComparison.Ordinal);
        Assert.True(window.FindControl<CheckBox>("ApplySettingsBox")!.IsVisible);
        window.Close();
    }
}
