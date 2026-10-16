using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Exchange;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Копия списка перед загрузкой из файла и возврат прежнего списка (задача #24).</summary>
public class ListRestoreTests
{
    private const string NewBaseJson = """
        {
          "format": "YetAnotherOneCLauncher.InfoBaseList",
          "infoBases": [ { "name": "Единственная", "connection": "File=\"D:\\One\";", "launcher": { "favorite": true } } ]
        }
        """;

    private static async Task ImportJsonAsync(ViewModelFixture fixture, string json, bool replace)
    {
        var path = Path.Combine(fixture.Directory, "in.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        fixture.Files.OpenAnswer = path;
        fixture.Dialogs.ImportConfirmer = form =>
        {
            form.IsReplaceMode = replace;
            return true;
        };
        await fixture.ViewModel.ImportListJsonCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Json_import_makes_backup_and_banner_returns_previous_list_with_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var before = File.ReadAllBytes(fixture.ListPath);
        fixture.Settings.UserData.SetFavorite(fixture.Base("Копия бухгалтерии").InfoBase, true);

        await ImportJsonAsync(fixture, NewBaseJson, replace: true);

        Assert.True(vm.ShowRestoreBanner);
        Assert.Contains("in.json", vm.RestoreBannerText, StringComparison.Ordinal);
        Assert.Contains("верните прежний список", vm.RestoreBannerText, StringComparison.Ordinal);
        var backup = Assert.Single(vm.ListBackups!.List());
        Assert.Equal(Path.Combine(fixture.Directory, "list-backups"), vm.ListBackups.Directory);
        Assert.Equal(before, File.ReadAllBytes(backup.ListPath));
        Assert.Equal(ImportMode.Replace, backup.Info.Mode);
        Assert.True(fixture.Base("Единственная").IsFavorite);

        fixture.Dialogs.ConfirmAnswer = true;
        await vm.UndoLastImportCommand.ExecuteAsync(null);

        Assert.Contains("до загрузки из in.json", Assert.Single(fixture.Dialogs.Questions), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.ListPath));
        Assert.False(vm.ShowRestoreBanner);
        Assert.True(fixture.Base("Копия бухгалтерии").IsFavorite);
        Assert.DoesNotContain(vm.InfoBases, b => b.Name == "Единственная");
        Assert.DoesNotContain(fixture.Settings.Settings.Favorites, f => f.Name == "Единственная");
        Assert.Contains("Список восстановлен", vm.StatusText, StringComparison.Ordinal);

        // Перед восстановлением — ещё одна копия (загруженного списка): восстановление тоже можно отменить.
        var backups = vm.ListBackups.List();
        Assert.Equal(2, backups.Count);
        Assert.Equal(ListBackupReason.Restore, backups[0].Info.Reason);
        Assert.Contains("[Единственная]", File.ReadAllText(backups[0].ListPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declined_undo_keeps_list_and_dismiss_hides_banner()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        await ImportJsonAsync(fixture, NewBaseJson, replace: false);
        var after = File.ReadAllBytes(fixture.ListPath);

        fixture.Dialogs.ConfirmAnswer = false;
        await vm.UndoLastImportCommand.ExecuteAsync(null);
        Assert.Equal(after, File.ReadAllBytes(fixture.ListPath));
        Assert.True(vm.ShowRestoreBanner);

        vm.DismissRestoreBannerCommand.Execute(null);
        Assert.False(vm.ShowRestoreBanner);
    }

    [Fact]
    public async Task Next_list_edit_hides_banner()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        await ImportJsonAsync(fixture, NewBaseJson, replace: false);
        Assert.True(vm.ShowRestoreBanner);

        fixture.Dialogs.PromptAnswer = "Новая группа";
        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(vm.ShowRestoreBanner);
    }

    [Fact]
    public async Task V8i_import_also_makes_backup()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var path = Path.Combine(fixture.Directory, "in.v8i");
        File.WriteAllText(path, "[Из v8i]\r\nConnect=File=\"D:\\V8i\";\r\n");
        fixture.Files.OpenAnswer = path;

        await vm.ImportCommand.ExecuteAsync(null);

        var backup = Assert.Single(vm.ListBackups!.List());
        Assert.Equal("in.v8i", backup.Info.SourceFile);
        Assert.Equal(ImportMode.Add, backup.Info.Mode);
        Assert.True(vm.ShowRestoreBanner);
    }

    [Fact]
    public async Task Restore_window_lists_backups_and_restores_selected_without_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        var original = File.ReadAllBytes(fixture.ListPath);
        await ImportJsonAsync(fixture, NewBaseJson, replace: true);
        await ImportJsonAsync(fixture, NewBaseJson.Replace("Единственная", "Вторая", StringComparison.Ordinal).Replace("D:\\\\One", "D:\\\\Two", StringComparison.Ordinal), replace: false);
        var favoritesBefore = fixture.Settings.Settings.Favorites.Count;

        fixture.Dialogs.BackupChooser = restore =>
        {
            Assert.Equal(2, restore.Rows.Count);
            Assert.Equal("загрузкой in.json (добавить)", restore.Rows[0].ReasonText);
            Assert.Equal("загрузкой in.json (заменить)", restore.Rows[1].ReasonText);
            Assert.Equal("5", restore.Rows[1].CountText);
            Assert.NotNull(restore.OpenFolderCommand);
            restore.SelectedRow = restore.Rows[1]; // самая первая — до замены
            restore.RestoreSettings = false;
            return true;
        };
        await vm.RestoreListCommand.ExecuteAsync(null);

        Assert.Equal(original, File.ReadAllBytes(fixture.ListPath));
        Assert.Equal(favoritesBefore, fixture.Settings.Settings.Favorites.Count); // настройки не возвращали
        Assert.Contains("Список восстановлен из копии", vm.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("вместе с настройками", vm.StatusText, StringComparison.Ordinal);

        vm.OpenListBackupsFolderCommand.Execute(null);
        Assert.Contains(Path.Combine(fixture.Directory, "list-backups"), fixture.Processes.OpenedFolders);
    }

    [Fact]
    public async Task Settings_restore_button_opens_restore_window()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        Task? restoring = null;
        fixture.Dialogs.SettingsEditor = settings =>
        {
            restoring = settings.RestoreListJsonCommand.ExecuteAsync(null);
            return false;
        };

        await fixture.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        await restoring!;

        var restore = Assert.Single(fixture.Dialogs.RestoreForms);
        Assert.False(restore.HasRows);
    }

    [AvaloniaFact]
    public async Task Banner_menu_item_and_restore_window_are_shown()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var menu = Assert.IsType<MenuFlyout>(window.FindControl<Button>("MenuButton")!.Flyout);
        Assert.Contains(menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Список баз").Items.OfType<MenuItem>(), i => (string?)i.Header == "Восстановить…");
        Assert.False(window.FindControl<Border>("RestoreBanner")!.IsVisible);

        await ImportJsonAsync(fixture, NewBaseJson, replace: false);
        MainWindowTests.Render();
        Assert.True(window.FindControl<Border>("RestoreBanner")!.IsVisible);
        Assert.Equal("Вернуть прежний список", window.FindControl<Button>("UndoImportButton")!.Content);
        MainWindowTests.Snapshot(window, "51-restore-banner");
        window.Close();

        var restore = new RestoreListViewModel(fixture.ViewModel.ListBackups!.List(), fixture.ViewModel.OpenListBackupsFolderCommand);
        var dialog = new RestoreListWindow(restore);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, dialog.FindControl<ListBox>("BackupsList")!.ItemCount);
        Assert.True(dialog.FindControl<Button>("OkButton")!.IsEnabled);
        Assert.True(dialog.FindControl<CheckBox>("RestoreSettingsBox")!.IsChecked);
        MainWindowTests.Snapshot(dialog, "51-restore-window");
        dialog.Close();
    }
}
