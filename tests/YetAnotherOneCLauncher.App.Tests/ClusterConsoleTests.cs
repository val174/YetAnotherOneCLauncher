using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Консоль кластера серверов: выбор версии, регистрация компонента, переход в «ПУСК».</summary>
public class ClusterConsoleTests
{
    [Fact]
    public async Task Offers_versions_with_console_and_opens_registered_one_without_registration()
    {
        using var fixture = new ViewModelFixture();
        fixture.ClusterConsole.Available.UnionWith(["8.3.27.2130", "8.3.24.1667"]); // у 8.5 компонента нет
        fixture.ClusterConsole.Registered = "8.3.24.1667";
        await fixture.LoadAsync();

        ClusterConsoleViewModel? shown = null;
        fixture.Dialogs.ClusterConsole = async console =>
        {
            shown = console;
            Assert.Equal(["8.3.27.2130", "8.3.24.1667"], console.Options.Select(o => o.Version)); // новые сверху
            Assert.Equal("8.3.24.1667", console.SelectedOption!.Version); // по умолчанию — зарегистрированная
            Assert.False(console.NeedsRegistration);
            Assert.False(console.HasPusk); // адрес «ПУСК» не задан
            await console.LaunchCommand.ExecuteAsync(null);
        };
        await fixture.ViewModel.OpenClusterConsoleCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal(["8.3.24.1667"], fixture.ClusterConsole.Opened);
        Assert.Equal("8.3.24.1667", fixture.ClusterConsole.Registered); // перерегистрации не было
        Assert.Contains("Консоль кластера 8.3.24.1667", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_version_is_registered_first_and_refusal_keeps_window_open()
    {
        using var fixture = new ViewModelFixture();
        fixture.ClusterConsole.Available.UnionWith(["8.3.27.2130", "8.3.24.1667"]);
        fixture.ClusterConsole.Registered = "8.3.24.1667";
        await fixture.LoadAsync();
        var console = new ClusterConsoleViewModel(ViewModelFixture.Installations, fixture.ClusterConsole, fixture.Processes, null);
        var closed = 0;
        console.CloseRequested += (_, _) => closed++;

        console.SelectedOption = console.Options[0]; // 8.3.27 — не зарегистрирована
        Assert.True(console.NeedsRegistration);

        fixture.ClusterConsole.DenyRegistration = true; // отказ в правах администратора
        await console.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("отменена", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(fixture.ClusterConsole.Opened);
        Assert.Equal(0, closed);

        fixture.ClusterConsole.DenyRegistration = false;
        await console.LaunchCommand.ExecuteAsync(null);
        Assert.Equal("8.3.27.2130", fixture.ClusterConsole.Registered);
        Assert.Equal(["8.3.27.2130"], fixture.ClusterConsole.Opened);
        Assert.Equal(1, closed);
        Assert.False(console.HasError);
    }

    [Fact]
    public void Nothing_to_choose_explains_why()
    {
        using var fixture = new ViewModelFixture();
        var console = new ClusterConsoleViewModel(ViewModelFixture.Installations, fixture.ClusterConsole, fixture.Processes, null);
        Assert.False(console.HasOptions);
        Assert.False(console.LaunchCommand.CanExecute(null));
        Assert.Contains("radmin.dll", console.EmptyText, StringComparison.Ordinal);

        fixture.ClusterConsole.IsSupported = false;
        Assert.Contains("только в Windows", console.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pusk_address_is_saved_validated_and_opened_in_browser()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;

        vm.PuskUrl = "pusk.example"; // без схемы — не адрес
        Assert.True(vm.IsPuskUrlInvalid);
        vm.PuskUrl = "  https://pusk.example/app  ";
        Assert.False(vm.IsPuskUrlInvalid);
        Assert.Equal("https://pusk.example/app", fixture.Settings.Settings.Network.PuskUrl);

        fixture.Dialogs.ClusterConsole = console =>
        {
            Assert.True(console.HasPusk); // кнопка есть и без версий с консолью
            Assert.False(console.HasOptions);
            console.OpenPuskCommand.Execute(null);
            return Task.CompletedTask;
        };
        await vm.OpenClusterConsoleCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://pusk.example/app"), Assert.Single(fixture.Processes.OpenedUrls));
        Assert.Contains("«ПУСК» открыт", vm.StatusText, StringComparison.Ordinal);

        vm.PuskUrl = string.Empty;
        Assert.Null(fixture.Settings.Settings.Network.PuskUrl);
    }

    [AvaloniaFact]
    public void Window_shows_versions_registration_hint_and_pusk_button()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var consoleService = new FakeClusterConsole { Registered = "8.3.24.1667" };
        consoleService.Available.UnionWith(["8.5.1.1150", "8.3.27.2130", "8.3.24.1667"]);
        var console = new ClusterConsoleViewModel(ViewModelFixture.Installations, consoleService, new FakeProcessLauncher(), new Uri("https://pusk.example/"));
        var window = new ClusterConsoleWindow(console);
        window.Show();

        console.SelectedOption = console.Options[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, window.FindControl<ListBox>("VersionsList")!.ItemCount);
        Assert.True(window.FindControl<TextBlock>("RegistrationHint")!.IsVisible);
        Assert.True(window.FindControl<Button>("PuskButton")!.IsVisible);
        MainWindowTests.Snapshot(window, "10-cluster-console");
        window.Close();
    }

    [Fact]
    public void Snap_in_is_found_in_common_next_to_version_directories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "yaocl-console-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "8.3.25.1633", "bin"));
            Directory.CreateDirectory(Path.Combine(root, "common"));
            File.WriteAllText(Path.Combine(root, "common", "1CV8 Servers (x86-64).msc"), "");
            File.WriteAllText(Path.Combine(root, "common", "1CV8 Servers.msc"), "");
            PlatformInstallation Platform(PlatformArchitecture architecture) =>
                new(PlatformVersion.Parse("8.3.25.1633"), architecture, Path.Combine(root, "8.3.25.1633", "bin"), null, null);

            Assert.EndsWith("1CV8 Servers (x86-64).msc", WindowsClusterConsole.FindSnapIn(Platform(PlatformArchitecture.X64)), StringComparison.Ordinal);
            Assert.EndsWith("1CV8 Servers.msc", WindowsClusterConsole.FindSnapIn(Platform(PlatformArchitecture.X86)), StringComparison.Ordinal);
            Assert.False(new WindowsClusterConsole().IsAvailable(Platform(PlatformArchitecture.X64))); // radmin.dll нет

            File.WriteAllText(Path.Combine(root, "8.3.25.1633", "bin", "radmin.dll"), "");
            Assert.True(new WindowsClusterConsole().IsAvailable(Platform(PlatformArchitecture.X64)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
