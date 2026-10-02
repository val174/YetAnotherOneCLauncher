using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Окно «Настройки»: черновик со «*», сохранение и отмена, горячие клавиши.</summary>
public class SettingsTests
{
    [Fact]
    public void Changes_mark_their_tab_and_title_until_reverted()
    {
        var form = new SettingsViewModel(new SettingsValues { ThemeIndex = 0, ShowDetails = true });
        Assert.False(form.IsDirty);
        Assert.Equal(("Настройки", "Общие", "Внешний вид", "Горячие клавиши"), (form.Title, form.GeneralHeader, form.AppearanceHeader, form.HotKeysHeader));

        form.MinimizeToTray = true;
        Assert.True(form.IsDirty);
        Assert.Equal(("Настройки*", "Общие*", "Внешний вид"), (form.Title, form.GeneralHeader, form.AppearanceHeader));

        form.ThemeIndex = 2;
        Assert.Equal("Внешний вид*", form.AppearanceHeader);

        form.MinimizeToTray = false; // вернули как было — «*» уходит
        form.ThemeIndex = 0;
        Assert.False(form.IsDirty);
        Assert.Equal("Настройки", form.Title);
    }

    [Fact]
    public void Hot_key_is_assigned_only_if_allowed_and_free()
    {
        var form = new SettingsViewModel(new SettingsValues());
        var enterprise = Row(form, HotKeyCommand.LaunchEnterprise);
        var search = Row(form, HotKeyCommand.FocusSearch);

        form.StartCaptureCommand.Execute(enterprise);
        Assert.Same(enterprise, form.CapturingRow);
        Assert.Equal("Нажмите сочетание…", enterprise.GestureText);

        Assert.False(form.TryAssign(enterprise, new KeyGesture(Key.E))); // буква без Ctrl мешает набору в поиске
        Assert.Contains("Без Ctrl или Alt", enterprise.ErrorText, StringComparison.Ordinal);
        Assert.False(form.TryAssign(enterprise, new KeyGesture(Key.F, KeyModifiers.Control))); // занято поиском
        Assert.Contains("«Перейти к поиску»", enterprise.ErrorText, StringComparison.Ordinal);
        Assert.False(form.TryAssign(enterprise, new KeyGesture(Key.Enter)));
        Assert.False(form.IsDirty);

        Assert.True(form.TryAssign(enterprise, new KeyGesture(Key.E, KeyModifiers.Control)));
        Assert.Equal("Ctrl+E", enterprise.GestureText);
        Assert.Null(form.CapturingRow);
        Assert.Equal("Горячие клавиши*", form.HotKeysHeader);

        // Del без Ctrl допустим только для команд списка.
        Assert.False(form.TryAssign(search, new KeyGesture(Key.Delete)));
        form.ClearHotKeyCommand.Execute(Row(form, HotKeyCommand.Delete));
        Assert.True(form.TryAssign(Row(form, HotKeyCommand.Edit), new KeyGesture(Key.Delete)));

        // «По умолчанию» забирает сочетание у того, кто его занял.
        form.ResetHotKeyCommand.Execute(Row(form, HotKeyCommand.Delete));
        Assert.Equal("Del", Row(form, HotKeyCommand.Delete).GestureText);
        Assert.Equal("нет", Row(form, HotKeyCommand.Edit).GestureText);

        form.ResetAllHotKeysCommand.Execute(null);
        Assert.False(form.IsHotKeysDirty);
    }

    [Fact]
    public void Hot_key_overrides_round_trip_through_settings()
    {
        var map = HotKeyMap.Default
            .With(HotKeyCommand.LaunchEnterprise, new KeyGesture(Key.E, KeyModifiers.Control))
            .With(HotKeyCommand.Reload, null);

        var overrides = map.ToOverrides();
        Assert.Equal(new Dictionary<string, string> { ["LaunchEnterprise"] = "Ctrl+E", ["Reload"] = string.Empty }, overrides);

        var restored = HotKeyMap.FromSettings(overrides);
        Assert.True(restored.SameAs(map));
        Assert.Null(restored[HotKeyCommand.Reload]);
        Assert.True(HotKeyMap.FromSettings(new Dictionary<string, string> { ["Нет такой"] = "F9", ["Edit"] = "???" }).SameAs(HotKeyMap.Default));
    }

    [Fact]
    public void Letters_match_by_physical_key_in_any_layout()
    {
        // Ctrl+Й в русской раскладке — та же физическая клавиша, что Ctrl+Q.
        Assert.Equal(
            HotKeyCommand.ClearSearch,
            HotKeyMap.Default.Match(Key.None, KeyModifiers.Control, PhysicalKey.Q, HotKeyScope.Window));
        Assert.Null(HotKeyMap.Default.Match(Key.F2, KeyModifiers.None, PhysicalKey.F2, HotKeyScope.Window)); // F2 — только в списке
        Assert.Equal(HotKeyCommand.Edit, HotKeyMap.Default.Match(Key.F2, KeyModifiers.None, PhysicalKey.F2, HotKeyScope.List));
    }

    [Fact]
    public async Task Save_applies_and_stores_cancel_keeps_everything()
    {
        using var fixture = new ViewModelFixture();
        var vm = fixture.ViewModel;

        fixture.Dialogs.SettingsEditor = form =>
        {
            form.ThemeIndex = (int)ThemeMode.Dark;
            form.TryAssign(Row(form, HotKeyCommand.LaunchEnterprise), new KeyGesture(Key.E, KeyModifiers.Control));
            return false; // «Отменить»
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);
        Assert.Equal((int)ThemeMode.System, vm.ThemeIndex);
        Assert.Empty(fixture.Settings.Settings.Ui.HotKeys);

        fixture.Dialogs.SettingsEditor = form =>
        {
            Assert.Equal("Настройки", form.Title); // черновик — с текущих значений
            form.ThemeIndex = (int)ThemeMode.Dark;
            form.MinimizeToTray = true;
            form.CheckAvailability = false;
            form.TryAssign(Row(form, HotKeyCommand.LaunchEnterprise), new KeyGesture(Key.E, KeyModifiers.Control));
            return true; // «Сохранить»
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        var ui = fixture.Settings.Settings.Ui;
        Assert.Equal((ThemeMode.Dark, true, false), (ui.Theme, ui.MinimizeToTray, fixture.Settings.Settings.Network.CheckAvailability));
        Assert.Equal(ThemeMode.Dark, fixture.Shell.AppliedTheme);
        Assert.Equal("Ctrl+E", ui.HotKeys["LaunchEnterprise"]);
        Assert.Equal(new KeyGesture(Key.E, KeyModifiers.Control), vm.HotKeys.LaunchEnterprise);
        Assert.Equal("Настройки сохранены.", vm.StatusText);
    }

    [AvaloniaFact]
    public void Window_captures_gesture_and_esc_cancels_input_without_closing()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var form = new SettingsViewModel(new SettingsValues { ShowDetails = true });
        var window = new SettingsWindow(form);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled); // изменений нет

        var reload = Row(form, HotKeyCommand.Reload);
        form.StartCaptureCommand.Execute(reload);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible); // Esc отменил ввод, а не закрыл окно
        Assert.Null(form.CapturingRow);

        form.StartCaptureCommand.Execute(reload);
        window.KeyPress(Key.R, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.R, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ctrl+Shift+R", reload.GestureText);
        Assert.Equal("Настройки*", window.Title);
        Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);
        MainWindowTests.Snapshot(window, "11-settings-hotkeys");

        window.FindControl<TabControl>("Tabs")!.SelectedIndex = 0;
        form.MinimizeToTray = true;
        Dispatcher.UIThread.RunJobs();
        MainWindowTests.Snapshot(window, "11-settings-general");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Rebound_key_launches_and_old_one_no_longer_does()
    {
        using var fixture = new ViewModelFixture();
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var window = new MainWindow(fixture.ViewModel) { Width = 1040, Height = 560 };
        window.Show();
        await fixture.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        fixture.ViewModel.SelectedTreeItem = fixture.ViewModel.TreeItems.OfType<BaseNodeViewModel>().Single(n => n.Name == "Копия бухгалтерии");
        fixture.Dialogs.ConfirmAnswer = true;

        fixture.ViewModel.ApplySettings(fixture.ViewModel.CurrentSettings with
        {
            HotKeys = HotKeyMap.Default.With(HotKeyCommand.LaunchEnterprise, new KeyGesture(Key.E, KeyModifiers.Control)),
        });

        window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Processes.Started); // F3 больше ничего не делает

        window.KeyPress(Key.E, RawInputModifiers.Control, PhysicalKey.E, null);
        for (var i = 0; i < 100 && fixture.Processes.Started.Count == 0; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("ENTERPRISE", Assert.Single(fixture.Processes.Started).Arguments[0]);
        window.Close();
    }

    private static HotKeyRowViewModel Row(SettingsViewModel form, HotKeyCommand command) =>
        form.HotKeyRows.Single(r => r.Definition.Command == command);
}
