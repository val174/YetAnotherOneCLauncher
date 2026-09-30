using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Консоль кластера серверов: список версий, регистрация компонента, «Открыть ПУСК».</summary>
public class ClusterConsoleTests
{
    private static readonly PlatformInstallation New64 = Platform("8.3.27.2130", PlatformArchitecture.X64);
    private static readonly PlatformInstallation New32 = Platform("8.3.27.2130", PlatformArchitecture.X86);
    private static readonly PlatformInstallation Old64 = Platform("8.3.24.1667", PlatformArchitecture.X64);
    private static readonly PlatformInstallation Old32 = Platform("8.3.22.2239", PlatformArchitecture.X86);
    private static readonly PlatformInstallation[] All = [Old32, New32, Old64, New64];

    [Fact]
    public void Registered_versions_first_then_separator_then_versions_to_register()
    {
        var console = Console(registered: Old32); // 32-разрядная тоже распознаётся как зарегистрированная

        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);

        Assert.Equal(
            ["8.3.22.2239 32-разрядная", "8.3.27.2130 64-разрядная", "8.3.27.2130 32-разрядная", "8.3.24.1667 64-разрядная"],
            form.Options.Select(o => $"{o.Title} {o.Detail}"));
        Assert.Equal([true, false, false, false], form.Options.Select(o => o.IsRegistered));
        Assert.Equal([false, true, false, false], form.Options.Select(o => o.HasSeparatorAbove)); // разделитель — перед доступными к регистрации
        Assert.Same(form.Options[0], form.SelectedOption); // по умолчанию — зарегистрированная
        Assert.Equal(1, console.RegistryLookups); // регистрация ищется один раз
    }

    [Fact]
    public void Without_registration_there_is_no_separator()
    {
        var form = new ClusterConsoleViewModel(All, Console(registered: null), new FakeProcessLauncher(), puskUrl: null);

        Assert.All(form.Options, o => Assert.False(o.IsRegistered));
        Assert.All(form.Options, o => Assert.False(o.HasSeparatorAbove));
        Assert.Equal("8.3.27.2130 64-разрядная", $"{form.SelectedOption!.Title} {form.SelectedOption.Detail}");
        Assert.True(form.NeedsRegistration);
    }

    [Fact]
    public void Same_library_of_other_bitness_is_not_taken_for_registered()
    {
        // В реестре — 64-разрядная 8.3.27; 32-разрядная той же версии зарегистрированной не считается.
        var form = new ClusterConsoleViewModel(All, Console(registered: New64), new FakeProcessLauncher(), puskUrl: null);

        var registered = Assert.Single(form.Options, o => o.IsRegistered);
        Assert.Equal(PlatformArchitecture.X64, registered.Platform!.Architecture);
    }

    [Fact]
    public async Task Pusk_is_the_first_row_and_opens_in_browser()
    {
        var processes = new FakeProcessLauncher();
        var form = new ClusterConsoleViewModel(All, Console(registered: Old64), processes, new Uri("https://pusk.example/app"));

        var pusk = form.Options[0];
        Assert.True(pusk.IsPusk);
        Assert.Equal(("Открыть ПУСК", "https://pusk.example/app"), (pusk.Title, pusk.Detail));
        Assert.True(form.Options[1].IsRegistered); // затем — зарегистрированная
        Assert.Same(form.Options[1], form.SelectedOption); // выбрана по умолчанию версия, а не «ПУСК»

        form.SelectedOption = pusk;
        Assert.Equal("Открыть", form.ActionText);
        Assert.False(form.NeedsRegistration);
        await form.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://pusk.example/app"), Assert.Single(processes.OpenedUrls));
        Assert.Contains("«ПУСК» открыт", form.ResultMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_version_opens_without_registration()
    {
        using var fixture = new ViewModelFixture();
        fixture.ClusterConsole.Available.UnionWith(["8.3.27.2130", "8.3.24.1667"]); // у 8.5 компонента нет
        fixture.ClusterConsole.RegisterNow(ViewModelFixture.Installations.Single(p => p.Version.ToString() == "8.3.24.1667"));
        await fixture.LoadAsync();

        fixture.Dialogs.ClusterConsole = console =>
        {
            Assert.Equal(["8.3.24.1667", "8.3.27.2130"], console.Options.Select(o => o.Title)); // зарегистрированная — сверху
            Assert.Equal("Запустить", console.ActionText);
            return console.LaunchCommand.ExecuteAsync(null);
        };
        await fixture.ViewModel.OpenClusterConsoleCommand.ExecuteAsync(null);

        Assert.Equal(["8.3.24.1667 x64"], fixture.ClusterConsole.Opened);
        Assert.Contains("Консоль кластера 8.3.24.1667", fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_version_is_registered_first_and_refusal_keeps_window_open()
    {
        var console = Console(registered: Old64);
        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), null);
        var closed = 0;
        form.CloseRequested += (_, _) => closed++;
        form.SelectedOption = form.Options.Single(o => o.Platform == New32);
        Assert.True(form.NeedsRegistration);

        console.DenyRegistration = true; // отказ в правах администратора
        await form.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("отменена", form.ErrorText, StringComparison.Ordinal);
        Assert.Empty(console.Opened);
        Assert.Equal(0, closed);

        console.DenyRegistration = false;
        await form.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(new ClusterConsoleRegistration(console.AdminLibraryPath(New32), PlatformArchitecture.X86), console.Registration);
        Assert.Equal(["8.3.27.2130 x86"], console.Opened);
        Assert.Equal(1, closed);
        Assert.False(form.HasError);
    }

    [Fact]
    public void Nothing_to_choose_explains_why_but_pusk_stays()
    {
        var console = Console(registered: null);
        console.Available.Clear();
        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), new Uri("https://pusk.example/"));
        Assert.False(form.HasVersions);
        Assert.True(Assert.Single(form.Options).IsPusk);
        Assert.Contains("radmin.dll", form.EmptyText, StringComparison.Ordinal);

        console.IsSupported = false;
        Assert.Contains("только в Windows", form.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pusk_address_is_saved_and_validated()
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
            Assert.True(console.Options[0].IsPusk);
            return Task.CompletedTask;
        };
        await vm.OpenClusterConsoleCommand.ExecuteAsync(null);

        vm.PuskUrl = string.Empty;
        Assert.Null(fixture.Settings.Settings.Network.PuskUrl);
    }

    [Fact]
    public void Registration_matches_same_file_regardless_of_case_and_quotes()
    {
        var registration = new ClusterConsoleRegistration(@"""C:\PROGRAM FILES\1cv8\8.3.25.1633\bin\RADMIN.DLL""", PlatformArchitecture.X64);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(registration.Matches(@"C:\Program Files\1cv8\8.3.25.1633\bin\radmin.dll"));
        }

        Assert.False(registration.Matches(@"C:\Program Files\1cv8\8.3.27.2130\bin\radmin.dll"));
    }

    [AvaloniaFact]
    public void Window_shows_pusk_row_separator_and_registration_hint()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var form = new ClusterConsoleViewModel(All, Console(registered: Old64), new FakeProcessLauncher(), new Uri("https://pusk.example/"));
        var window = new ClusterConsoleWindow(form);
        window.Show();

        form.SelectedOption = form.Options.Single(o => o.Platform == New64);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = window.FindControl<ListBox>("VersionsList")!;
        Assert.Equal(5, list.ItemCount);
        Assert.Single(list.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("optionSeparator") && b.IsVisible);
        Assert.True(window.FindControl<TextBlock>("RegistrationHint")!.IsVisible);
        Assert.Null(window.FindControl<Button>("PuskButton")); // отдельной кнопки нет — строка в списке
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
            PlatformInstallation Installed(PlatformArchitecture architecture) =>
                new(PlatformVersion.Parse("8.3.25.1633"), architecture, Path.Combine(root, "8.3.25.1633", "bin"), null, null);

            Assert.EndsWith("1CV8 Servers (x86-64).msc", WindowsClusterConsole.FindSnapIn(Installed(PlatformArchitecture.X64)), StringComparison.Ordinal);
            Assert.EndsWith("1CV8 Servers.msc", WindowsClusterConsole.FindSnapIn(Installed(PlatformArchitecture.X86)), StringComparison.Ordinal);
            Assert.False(new WindowsClusterConsole().IsAvailable(Installed(PlatformArchitecture.X64))); // radmin.dll нет

            File.WriteAllText(Path.Combine(root, "8.3.25.1633", "bin", "radmin.dll"), "");
            Assert.True(new WindowsClusterConsole().IsAvailable(Installed(PlatformArchitecture.X64)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FakeClusterConsole Console(PlatformInstallation? registered)
    {
        var console = new FakeClusterConsole();
        console.Available.UnionWith(All.Select(p => p.Version.ToString()));
        if (registered is not null)
        {
            console.RegisterNow(registered);
        }

        return console;
    }

    private static PlatformInstallation Platform(string version, PlatformArchitecture architecture)
    {
        var programFiles = architecture == PlatformArchitecture.X86 ? "Program Files (x86)" : "Program Files";
        var bin = Path.Combine("C:" + Path.DirectorySeparatorChar, programFiles, "1cv8", version, "bin");
        return new PlatformInstallation(PlatformVersion.Parse(version), architecture, bin, Path.Combine(bin, "1cv8.exe"), null);
    }
}
