using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;
using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Консоль кластера серверов: список версий, регистрация компонента, «Панель управления сервисами и компонентами».</summary>
public class ClusterConsoleTests
{
    private static readonly PlatformInstallation New64 = Platform("8.3.27.2130", PlatformArchitecture.X64);
    private static readonly PlatformInstallation New32 = Platform("8.3.27.2130", PlatformArchitecture.X86);
    private static readonly PlatformInstallation Old64 = Platform("8.3.24.1667", PlatformArchitecture.X64);
    private static readonly PlatformInstallation Old32 = Platform("8.3.22.2239", PlatformArchitecture.X86);
    private static readonly PlatformInstallation[] All = [Old32, New32, Old64, New64];

    [Fact]
    public void Registered_versions_first_then_collapsed_group_of_versions_to_register()
    {
        var console = Console(registered: Old32); // 32-разрядная тоже распознаётся как зарегистрированная

        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);

        // Группа «Доступные к регистрации» по умолчанию свёрнута: видно зарегистрированную и заголовок с числом версий.
        Assert.Equal(["8.3.22.2239 32-разрядная", "Доступные к регистрации 3"], form.Options.Select(o => $"{o.Title} {o.Detail}"));
        var group = form.Options[1];
        Assert.True(group.IsGroupHeader);
        Assert.False(group.IsExpanded);
        Assert.True(group.HasSeparatorAbove); // разделитель — перед группой
        Assert.Same(form.Options[0], form.SelectedOption); // по умолчанию — зарегистрированная
        Assert.Equal(1, console.RegistryLookups); // регистрация ищется один раз

        form.ToggleGroupCommand.Execute(null);
        Assert.Equal(
            ["8.3.22.2239", "Доступные к регистрации", "8.3.27.2130", "8.3.27.2130", "8.3.24.1667"],
            form.Options.Select(o => o.Title));
        Assert.All(form.Options.Skip(2), o => Assert.True(o.IsInGroup));
        Assert.Equal("▾", group.ExpandGlyph);

        form.SelectedOption = form.Options[3];
        form.ToggleGroupCommand.Execute(null); // свернули — выделенная версия скрылась, выделение на заголовке
        Assert.Equal(2, form.Options.Count);
        Assert.Same(group, form.SelectedOption);
        Assert.Equal(("Развернуть", false), (form.ActionText, form.NeedsRegistration));
    }

    [Fact]
    public async Task Enter_on_group_header_expands_it_instead_of_launching()
    {
        var console = Console(registered: null);
        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);
        var group = Assert.Single(form.Options); // ничего не зарегистрировано — только свёрнутая группа
        Assert.Same(group, form.SelectedOption);
        Assert.False(group.HasSeparatorAbove); // выше ничего нет — разделитель не нужен

        await form.LaunchCommand.ExecuteAsync(null);

        Assert.True(group.IsExpanded);
        Assert.Equal(5, form.Options.Count);
        Assert.Equal("Свернуть", form.ActionText);
        Assert.Empty(console.Opened);
    }

    [Fact]
    public void Both_registered_consoles_are_seen_when_x64_and_x86_come_from_different_platforms()
    {
        // Как на ПК с двумя консолями: 64-разрядная от 8.3.24, 32-разрядная от 8.3.27.
        var console = Console(registered: Old64);
        console.RegisterNow(New32);

        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);

        Assert.Equal(
            ["8.3.27.2130 32-разрядная", "8.3.24.1667 64-разрядная"],
            form.Options.Where(o => o.IsRegistered).Select(o => $"{o.Title} {o.Detail}"));
        Assert.Equal([true, true, false], form.Options.Select(o => o.IsRegistered)); // обе — сверху, дальше группа
        Assert.True(form.Options[2].IsGroupHeader);
        Assert.Equal(2, form.Options[2].GroupCount);
        Assert.Equal(1, console.RegistryLookups);
    }

    [Fact]
    public void Registered_console_of_platform_outside_standard_folders_is_listed()
    {
        var console = Console(registered: null);
        console.Available.Add("8.3.23.1865");
        console.Registrations[PlatformArchitecture.X86] =
            new ClusterConsoleRegistration(@"D:\1C\8.3.23.1865\bin\radmin.dll", PlatformArchitecture.X86);

        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);

        var registered = Assert.Single(form.Options, o => o.IsRegistered);
        Assert.Equal(("8.3.23.1865", "32-разрядная"), (registered.Title, registered.Detail));
        Assert.Equal(Path.Combine(@"D:\1C\8.3.23.1865", "bin"), registered.Platform!.BinDirectory);
        Assert.Null(ClusterConsoleViewModel.FromRegistration(new ClusterConsoleRegistration(@"C:\tools\radmin.dll", PlatformArchitecture.X64)));
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
        Assert.Equal(("Панель управления сервисами и компонентами", string.Empty), (pusk.Title, pusk.Detail)); // адрес не показывается
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
            Assert.Equal(["8.3.24.1667", "Доступные к регистрации"], console.Options.Select(o => o.Title)); // зарегистрированная — сверху
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
        form.ToggleGroupCommand.Execute(null); // 8.3.27 x86 — в группе «Доступные к регистрации»
        form.SelectedOption = form.Options.Single(o => o.Platform == New32);
        Assert.True(form.NeedsRegistration);

        console.DenyRegistration = true; // отказ в правах администратора
        await form.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("отменена", form.ErrorText, StringComparison.Ordinal);
        Assert.Empty(console.Opened);
        Assert.Equal(0, closed);

        console.DenyRegistration = false;
        await form.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(new ClusterConsoleRegistration(console.AdminLibraryPath(New32), PlatformArchitecture.X86), console.Registrations[PlatformArchitecture.X86]);
        Assert.Equal(new ClusterConsoleRegistration(console.AdminLibraryPath(Old64), PlatformArchitecture.X64), console.Registrations[PlatformArchitecture.X64]); // 64-разрядная не тронута
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

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = window.FindControl<ListBox>("VersionsList")!;
        Assert.Equal(3, list.ItemCount); // «ПУСК», зарегистрированная, свёрнутая группа
        MainWindowTests.Snapshot(window, "10-cluster-console-collapsed");

        // Щелчок по заголовку группы раскрывает её.
        var header = list.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Доступные к регистрации");
        var point = header.TranslatePoint(new Avalonia.Point(5, 5), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(6, list.ItemCount);

        form.SelectedOption = form.Options.Single(o => o.Platform == New64);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(list.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("optionSeparator") && b.IsVisible);
        Assert.True(window.FindControl<TextBlock>("RegistrationHint")!.IsVisible);
        Assert.Null(window.FindControl<Button>("PuskButton")); // отдельной кнопки нет — строка в списке
        Assert.Single(list.GetVisualDescendants().OfType<Image>(), i => i.Name == "PuskLogo" && i.IsEffectivelyVisible); // логотип «ПУСК» — только у своей строки
        MainWindowTests.Snapshot(window, "10-cluster-console");
        window.Close();
    }

    [Fact]
    public void Platform_with_only_server_components_is_offered_for_console()
    {
        // 32-разрядная 8.3.27.1936 без клиента 1С: поиск платформ её не находит, а radmin.dll у неё есть.
        var console = Console(registered: Old64);
        var serverOnly = Platform("8.3.27.1936", PlatformArchitecture.X86) with { ThickClientPath = null };
        console.Available.Add("8.3.27.1936");
        console.AdminInstallations.AddRange([serverOnly, Old64]); // Old64 уже есть среди платформ — не дублируется

        var form = new ClusterConsoleViewModel(All, console, new FakeProcessLauncher(), puskUrl: null);

        Assert.Single(form.Versions, o => o.Platform == Old64);
        var option = Assert.Single(form.Versions, o => o.Title == "8.3.27.1936");
        Assert.Equal(("32-разрядная", false), (option.Detail, option.IsRegistered));
    }

    [Fact]
    public void Stale_registration_of_older_platform_is_not_taken_for_registered()
    {
        // Как на ПК: 32-разрядная консоль зарегистрирована от 8.3.27.1936, в реестре осталась и прежняя — от 8.3.22.
        var console = Console(registered: null);
        console.Available.Add("8.3.27.1936");
        var stale = new ClusterConsoleRegistration(console.AdminLibraryPath(Old32), PlatformArchitecture.X86, "{11111111-1111-1111-1111-111111111111}", IsActive: false);
        console.Registrations[PlatformArchitecture.X86] = new ClusterConsoleRegistration(
            @"C:\Program Files (x86)\1cv8\8.3.27.1936\bin\radmin.dll", PlatformArchitecture.X86, "{22222222-2222-2222-2222-222222222222}");
        var form = new ClusterConsoleViewModel(All, new StaleAware(console, stale), new FakeProcessLauncher(), puskUrl: null);

        var registered = Assert.Single(form.Options, o => o.IsRegistered);
        Assert.Equal(("8.3.27.1936", "32-разрядная"), (registered.Title, registered.Detail));
        Assert.False(form.Versions.Single(o => o.Platform == Old32).IsRegistered); // 8.3.22 — устаревшая регистрация
    }

    [Fact]
    public void Active_registration_is_the_one_referenced_by_console_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string msc = """<Snapin CLSID="{22222222-2222-2222-2222-222222222222}"/><Snapin CLSID="{C96401CC-0E17-11D3-885B-00C04F72C717}"/>""";
        var classes = WindowsClusterConsole.SnapInClassesOf(msc);
        ClusterConsoleRegistration[] found =
        [
            new(@"C:\x86\8.3.22.2239\bin\radmin.dll", PlatformArchitecture.X86, "{11111111-1111-1111-1111-111111111111}"),
            new(@"C:\x86\8.3.27.1936\bin\radmin.dll", PlatformArchitecture.X86, "{22222222-2222-2222-2222-222222222222}"),
        ];

        Assert.Equal([false, true], WindowsClusterConsole.MarkActive(found, classes).Select(r => r.IsActive));
        Assert.Equal([true, true], WindowsClusterConsole.MarkActive(found, new HashSet<string>()).Select(r => r.IsActive)); // файла нет — все

        // Тот же класс зарегистрирован для компьютера (8.3.27.1936) и для пользователя (8.3.22.2239): перекрытая
        // регистрация остаётся недействующей, хотя её класс указан в файле консоли.
        ClusterConsoleRegistration[] twice =
        [
            new(@"C:\x86\8.3.27.1936\bin\radmin.dll", PlatformArchitecture.X86, "{22222222-2222-2222-2222-222222222222}"),
            new(@"C:\x86\8.3.22.2239\bin\radmin.dll", PlatformArchitecture.X86, "{22222222-2222-2222-2222-222222222222}", IsActive: false),
        ];
        Assert.Equal([true, false], WindowsClusterConsole.MarkActive(twice, classes).Select(r => r.IsActive));
    }

    [Fact]
    public void Narrow_snap_in_copy_gets_class_of_32_bit_snap_in()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string msc = """
            <Snapin CLSID="{A42674D4-2D97-4988-A81D-2C113CC42A95}" AllExtensionsEnabled="false">
            <Node ID="2" ImageIdx="0" CLSID="{a42674d4-2d97-4988-a81d-2c113cc42a95}" Preload="false">
            <Snapin CLSID="{C96401CC-0E17-11D3-885B-00C04F72C717}" AllExtensionsEnabled="true"/>
            """;

        var patched = WindowsClusterConsole.PatchSnapInClass(msc, WindowsClusterConsole.SnapInClassId, "{11111111-2222-3333-4444-555555555555}");

        Assert.Equal(2, patched.Split("11111111-2222-3333-4444-555555555555").Length - 1); // оба упоминания, в любом регистре
        Assert.DoesNotContain("A42674D4", patched, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C96401CC-0E17-11D3-885B-00C04F72C717", patched, StringComparison.Ordinal); // чужие классы не тронуты
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

    /// <summary>Подделка, которая вдобавок к регистрациям подделки возвращает устаревшую.</summary>
    private sealed class StaleAware(FakeClusterConsole inner, ClusterConsoleRegistration stale) : IClusterConsole
    {
        public bool IsSupported => inner.IsSupported;

        public bool IsAvailable(PlatformInstallation platform) => inner.IsAvailable(platform);

        public string AdminLibraryPath(PlatformInstallation platform) => inner.AdminLibraryPath(platform);

        public IReadOnlyList<PlatformInstallation> FindAdminInstallations() => inner.FindAdminInstallations();

        public IReadOnlyList<ClusterConsoleRegistration> FindRegistered() => [.. inner.FindRegistered(), stale];

        public Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default) =>
            inner.RegisterAsync(platform, cancellationToken);

        public void Open(PlatformInstallation platform) => inner.Open(platform);
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
