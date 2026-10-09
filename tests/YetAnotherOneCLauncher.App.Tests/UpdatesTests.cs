using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Обновление лаунчера в главном окне: режимы, полоса-оповещение, ручная проверка, настройки.</summary>
public class UpdatesTests
{
    private static async Task<ViewModelFixture> LoadedAsync(UpdateMode mode)
    {
        var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.Settings.Settings.Updates.Mode = mode;
        return fixture;
    }

    [Fact]
    public async Task Disabled_mode_does_not_check()
    {
        using var fixture = await LoadedAsync(UpdateMode.Disabled);
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");

        await fixture.ViewModel.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, fixture.Updates.CheckCount);
        Assert.False(fixture.ViewModel.ShowUpdateBanner);
    }

    [Fact]
    public async Task Check_only_shows_banner_once_a_day()
    {
        using var fixture = await LoadedAsync(UpdateMode.CheckOnly);
        var vm = fixture.ViewModel;
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");

        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.ShowUpdateBanner);
        Assert.True(vm.IsUpdateOffered);
        Assert.False(vm.IsUpdateInstalled);
        Assert.Equal("Доступна новая версия 0.2.0 (установлена 0.1.0).", vm.UpdateBannerText);
        Assert.Empty(fixture.Updates.Installed); // без согласия ничего не скачивается
        Assert.NotNull(fixture.Settings.Settings.Updates.LastCheck);
        Assert.StartsWith("Версия 0.1.0 · проверено ", vm.UpdateStatusText, StringComparison.Ordinal);

        // Сутки не прошли — снова не спрашиваем.
        vm.DismissUpdateBannerCommand.Execute(null);
        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.Updates.CheckCount);
        Assert.False(vm.ShowUpdateBanner);

        fixture.Settings.Settings.Updates.LastCheck = DateTimeOffset.Now.AddDays(-2);
        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, fixture.Updates.CheckCount);
        Assert.True(vm.ShowUpdateBanner);
    }

    [Fact]
    public async Task Skipped_version_is_not_offered_again_but_next_one_is()
    {
        using var fixture = await LoadedAsync(UpdateMode.CheckOnly);
        var vm = fixture.ViewModel;
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");
        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        vm.SkipUpdateVersionCommand.Execute(null);
        Assert.False(vm.ShowUpdateBanner);
        Assert.Equal("0.2.0", fixture.Settings.Settings.Updates.SkippedVersion);

        fixture.Settings.Settings.Updates.LastCheck = null;
        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.ShowUpdateBanner);

        fixture.Settings.Settings.Updates.LastCheck = null;
        fixture.Updates.CheckResult = FakeUpdates.Available("0.3.0");
        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.ShowUpdateBanner);
    }

    [Fact]
    public async Task Auto_update_installs_and_offers_restart()
    {
        using var fixture = await LoadedAsync(UpdateMode.AutoUpdate);
        var vm = fixture.ViewModel;
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");

        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        Assert.Single(fixture.Updates.Installed);
        Assert.True(vm.IsUpdateInstalled);
        Assert.False(vm.IsUpdateOffered);
        Assert.Equal("Лаунчер обновлён до версии 0.2.0. Новая версия откроется после перезапуска.", vm.UpdateBannerText);
        Assert.Empty(fixture.Dialogs.Questions); // фоновая установка ничего не спрашивает

        vm.RestartToUpdateCommand.Execute(null);
        Assert.Equal(1, fixture.Updates.RestartCount);
        Assert.Equal(1, fixture.Shell.CloseCount);
    }

    [Fact]
    public async Task Auto_update_without_write_access_only_offers()
    {
        using var fixture = await LoadedAsync(UpdateMode.AutoUpdate);
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");
        fixture.Updates.CannotInstallReason = "Нет прав на запись в каталог C:\\Program Files\\YAOCL.";

        await fixture.ViewModel.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Updates.Installed);
        Assert.True(fixture.ViewModel.IsUpdateOffered);

        // «Обновить» — объясняет и открывает страницу релиза.
        await fixture.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Contains("Нет прав на запись", Assert.Single(fixture.Dialogs.Messages), StringComparison.Ordinal);
        Assert.Equal("https://github.com/val174/YetAnotherOneCLauncher/releases/tag/0.2.0", Assert.Single(fixture.Processes.OpenedUrls).ToString());
    }

    [Fact]
    public async Task Manual_check_reports_up_to_date_or_installs_on_consent()
    {
        using var fixture = await LoadedAsync(UpdateMode.Disabled); // ручная проверка работает в любом режиме
        var vm = fixture.ViewModel;

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal("Установлена последняя версия 0.1.0.", Assert.Single(fixture.Dialogs.Messages));

        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");
        fixture.Settings.Settings.Updates.SkippedVersion = "0.2.0"; // пропущенную тоже показываем, если спросили сами
        fixture.Dialogs.ConfirmAnswer = true;
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Доступна новая версия 0.2.0 (установлена 0.1.0). Обновить сейчас?", "Обновление установлено. Перезапустить лаунчер сейчас?"],
            fixture.Dialogs.Questions);
        Assert.Single(fixture.Updates.Installed);
        Assert.Equal(1, fixture.Updates.RestartCount);
    }

    [Fact]
    public async Task Release_notes_open_release_page()
    {
        using var fixture = await LoadedAsync(UpdateMode.CheckOnly);
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");
        await fixture.ViewModel.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);

        fixture.ViewModel.ShowUpdateReleaseNotesCommand.Execute(null);

        Assert.Equal("https://github.com/val174/YetAnotherOneCLauncher/releases/tag/0.2.0", Assert.Single(fixture.Processes.OpenedUrls).ToString());
    }

    [Fact]
    public async Task Update_mode_is_edited_in_settings_and_saved()
    {
        using var fixture = await LoadedAsync(UpdateMode.CheckOnly);
        var vm = fixture.ViewModel;
        SettingsViewModel? shown = null;
        fixture.Dialogs.SettingsEditor = settings =>
        {
            shown = settings;
            Assert.Equal(1, settings.UpdateModeIndex); // «Только проверка» — по умолчанию
            Assert.NotNull(settings.CheckForUpdatesCommand);
            settings.UpdateModeIndex = 2;
            Assert.True(settings.IsGeneralDirty);
            Assert.StartsWith("Новая версия скачивается", settings.UpdateModeHint, StringComparison.Ordinal);
            return true;
        };

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(["Не использовать", "Только проверка", "Автообновление"], shown!.UpdateModeNames);
        Assert.Equal(UpdateMode.AutoUpdate, fixture.Settings.Settings.Updates.Mode);
        Assert.Equal(2, vm.UpdateModeIndex);
    }

    [AvaloniaFact]
    public async Task Banner_and_settings_section_render()
    {
        using var fixture = new ViewModelFixture();
        var window = await MainWindowTests.OpenAsync(fixture);
        var vm = fixture.ViewModel;
        fixture.Settings.Settings.Updates.Mode = UpdateMode.CheckOnly;
        fixture.Updates.CheckResult = FakeUpdates.Available("0.2.0");
        fixture.Settings.Settings.Updates.LastCheck = null; // окно при открытии уже проверило — «ещё не пора»

        await vm.CheckForUpdatesInBackgroundAsync(TestContext.Current.CancellationToken);
        MainWindowTests.Render();
        Assert.True(window.FindControl<Border>("UpdateBanner")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("InstallUpdateButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("RestartToUpdateButton")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "46-update-available");

        await vm.InstallUpdateCommand.ExecuteAsync(null); // согласие на перезапуск не дали — полоса «Перезапустить»
        MainWindowTests.Render();
        Assert.True(window.FindControl<Button>("RestartToUpdateButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("SkipUpdateButton")!.IsEffectivelyVisible);
        Assert.Contains("installed", window.FindControl<Border>("UpdateBanner")!.Classes);
        MainWindowTests.Snapshot(window, "46-update-installed");
        window.Close();

        var settings = new SettingsWindow(new SettingsViewModel(vm.CurrentSettings)
        {
            CheckForUpdatesCommand = vm.CheckForUpdatesCommand,
            UpdateStatusText = vm.UpdateStatusText,
        });
        settings.Show();
        MainWindowTests.Render();
        Assert.True(settings.FindControl<ComboBox>("UpdateModeBox")!.IsEffectivelyVisible);
        Assert.True(settings.FindControl<Button>("CheckForUpdatesButton")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(settings, "46-update-settings");
        settings.Close();

        var about = new AboutWindow(new AboutViewModel { CheckForUpdatesCommand = vm.CheckForUpdatesCommand });
        about.Show();
        MainWindowTests.Render();
        Assert.True(about.FindControl<Button>("CheckForUpdatesButton")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(about, "46-update-about");
        about.Close();
    }
}
