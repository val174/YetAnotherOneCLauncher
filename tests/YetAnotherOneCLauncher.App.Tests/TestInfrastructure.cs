using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.App;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.Tests;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Core.Text;
using YetAnotherOneCLauncher.Platform.Abstractions;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Приложение для headless-тестов: настоящая отрисовка через Skia, чтобы можно было снять кадр.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LauncherApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; }

    public List<string> Questions { get; } = [];

    public List<string> Messages { get; } = [];

    public Task<bool> ConfirmAsync(string title, string question, string acceptText)
    {
        Questions.Add(question);
        return Task.FromResult(ConfirmAnswer);
    }

    /// <summary>Флажок в вопросе: <c>null</c> — оставить, как предложено.</summary>
    public bool? OptionAnswer { get; set; }

    public List<(string Text, bool Default)> Options { get; } = [];

    public Task<(bool Accepted, bool Option)> ConfirmWithOptionAsync(string title, string question, string acceptText, string optionText, bool optionChecked)
    {
        Questions.Add(question);
        Options.Add((optionText, optionChecked));
        return Task.FromResult((ConfirmAnswer, OptionAnswer ?? optionChecked));
    }

    public Task ShowMessageAsync(string title, string text)
    {
        Messages.Add(text);
        return Task.CompletedTask;
    }

    /// <summary>Выбор из вариантов: индекс ответа (<c>null</c> — отмена) и что было предложено.</summary>
    public int? ChoiceAnswer { get; set; }

    public List<(string Question, IReadOnlyList<string> Options)> Choices { get; } = [];

    public Task<int?> ChooseAsync(string title, string question, IReadOnlyList<string> options)
    {
        Choices.Add((question, options));
        return Task.FromResult(ChoiceAnswer);
    }

    /// <summary>Ответ на запрос строки; <c>null</c> — отмена.</summary>
    public string? PromptAnswer { get; set; }

    /// <summary>Что «пользователь» введёт в редакторе текста: получает исходный текст.</summary>
    public Func<string, string?> TextEditor { get; set; } = _ => null;

    /// <summary>Что «пользователь» сделает в форме базы; <c>false</c> — отмена.</summary>
    public Func<InfoBaseEditorViewModel, bool> InfoBaseEditor { get; set; } = _ => false;

    public Task<string?> PromptAsync(string title, string label, string initialText) => Task.FromResult(PromptAnswer);

    public Task<string?> EditTextAsync(string title, string hint, string text) => Task.FromResult(TextEditor(text));

    public Task<bool> EditInfoBaseAsync(InfoBaseEditorViewModel editor) =>
        // Новая база создаётся в самой форме (TryCreateAsync): тогда результат уже есть, повторная проверка не нужна.
        Task.FromResult(InfoBaseEditor(editor) && (editor.IsCreateMode ? editor.Result is not null : editor.TryAccept()));

    /// <summary>Что «пользователь» сделает в форме параметров запуска: режим разового запуска или <c>null</c> для сохранения; <c>false</c> в ответе — отмена.</summary>
    public Func<LaunchParametersViewModel, (bool Accept, LaunchMode? Mode)> LaunchParameters { get; set; } = _ => (false, null);

    public List<LaunchParametersViewModel> LaunchParameterForms { get; } = [];

    /// <summary>Что «пользователь» сделает в окне «Кэш баз».</summary>
    public Func<CacheManagerViewModel, Task> CacheManager { get; set; } = _ => Task.CompletedTask;

    public Task ShowCacheManagerAsync(CacheManagerViewModel cache) => CacheManager(cache);

    /// <summary>Что сделать в окне консоли кластера.</summary>
    public Func<ClusterConsoleViewModel, Task> ClusterConsole { get; set; } = _ => Task.CompletedTask;

    public Task ShowClusterConsoleAsync(ClusterConsoleViewModel console) => ClusterConsole(console);

    /// <summary>Что сделать в окне настроек; возвращает «Сохранить».</summary>
    public Func<SettingsViewModel, bool> SettingsEditor { get; set; } = _ => false;

    public Task<bool> EditSettingsAsync(SettingsViewModel settings) => Task.FromResult(SettingsEditor(settings));

    public AboutViewModel? ShownAbout { get; private set; }

    public Task ShowAboutAsync(AboutViewModel about)
    {
        ShownAbout = about;
        return Task.CompletedTask;
    }

    public Task<bool> EditLaunchParametersAsync(LaunchParametersViewModel parameters)
    {
        LaunchParameterForms.Add(parameters);
        var (accept, mode) = LaunchParameters(parameters);
        return Task.FromResult(accept && parameters.TryAccept(mode));
    }
}

internal sealed class FakeFiles : IFileDialogService
{
    public string? FolderAnswer { get; set; }

    public string? OpenAnswer { get; set; }

    public string? SaveAnswer { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderAnswer);

    public Task<string?> OpenListFileAsync(string title) => Task.FromResult(OpenAnswer);

    public Task<string?> OpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult(OpenAnswer);

    public Task<string?> SaveListFileAsync(string title, string suggestedName) => Task.FromResult(SaveAnswer);
}

internal sealed class FakeShell : IClipboardService, IWindowService, IThemeService
{
    public string? ClipboardText { get; private set; }

    public int MinimizeCount { get; private set; }

    public int CloseCount { get; private set; }

    public ThemeMode? AppliedTheme { get; private set; }

    public Task SetTextAsync(string text)
    {
        ClipboardText = text;
        return Task.CompletedTask;
    }

    public void Minimize() => MinimizeCount++;

    public int BringToFrontCount { get; private set; }

    public void BringToFront() => BringToFrontCount++;

    public void Close() => CloseCount++;

    public void Apply(ThemeMode mode) => AppliedTheme = mode;
}

internal sealed class FakePaths : IPlatformPaths
{
    public FakePaths(string personalListPath)
    {
        PersonalInfoBaseListPath = personalListPath;
    }

    public string PersonalInfoBaseListPath { get; }

    public IReadOnlyList<string> StarterConfigPaths { get; } = [];

    public IReadOnlyList<string> DefaultPlatformInstallRoots { get; set; } = [];

    public PlatformExecutableNames PlatformExecutableNames => PlatformExecutableNames.Windows;

    public IReadOnlyList<Core.Cache.CacheRoot> InfoBaseCacheRoots { get; set; } = [];

    public string DefaultTemplatesDirectory { get; set; } = string.Empty;

    public string AppDataDirectory => Path.GetDirectoryName(PersonalInfoBaseListPath)!;
}

/// <summary>Хранилище паролей в памяти.</summary>
internal sealed class FakeCredentials : ICredentialStore
{
    public Dictionary<string, (string UserName, string Password)> Entries { get; } = [];

    public string? UnavailableReason { get; set; }

    public string? Read(string key) => Entries.TryGetValue(key, out var entry) ? entry.Password : null;

    public void Write(string key, string label, string userName, string password) => Entries[key] = (userName, password);

    public void Delete(string key) => Entries.Remove(key);
}

/// <summary>Занятые каталоги и запущенные процессы задаются тестом.</summary>
internal sealed class FakeCacheUsage : ICacheUsageProbe
{
    public HashSet<string> InUse { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Процессы 1С текущего пользователя с командными строками.</summary>
    public List<PlatformProcess> Processes { get; } = [];

    // Копия: подсветка запущенных баз читает процессы в фоне, пока тест может менять список.
    public IReadOnlyList<PlatformProcess> CurrentUserPlatformProcesses()
    {
        lock (Processes)
        {
            return [.. Processes];
        }
    }

    public bool IsDirectoryInUse(string path) => InUse.Contains(path);
}

/// <summary>Проверка доступности без сети: недоступное перечисляет тест.</summary>
internal sealed class FakeAvailabilityProbe : Core.Availability.IAvailabilityProbe
{
    public HashSet<string> MissingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DeadHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(!MissingDirectories.Contains(path));

    public Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken) =>
        Task.FromResult(!DeadHosts.Contains(host));
}

/// <summary>Консоль кластера без реестра и MMC: какие версии с консолью, что зарегистрировано, что открыто.</summary>
internal sealed class FakeClusterConsole : IClusterConsole
{
    public bool IsSupported { get; set; } = true;

    /// <summary>Версии с компонентом администрирования (любой разрядности).</summary>
    public HashSet<string> Available { get; } = [];

    /// <summary>Зарегистрированные компоненты — как их вернул бы реестр: 64- и 32-разрядный раздел независимы.</summary>
    public Dictionary<PlatformArchitecture, ClusterConsoleRegistration> Registrations { get; } = [];

    /// <summary>Отказ в правах администратора при регистрации.</summary>
    public bool DenyRegistration { get; set; }

    public int RegistryLookups { get; private set; }

    public List<string> Opened { get; } = [];

    public bool IsAvailable(PlatformInstallation platform) => Available.Contains(platform.Version.ToString());

    public string AdminLibraryPath(PlatformInstallation platform) => Path.Combine(platform.BinDirectory, "radmin.dll");

    /// <summary>Установки только с сервером и администрированием (без клиента 1С).</summary>
    public List<PlatformInstallation> AdminInstallations { get; } = [];

    public IReadOnlyList<PlatformInstallation> FindAdminInstallations() => AdminInstallations;

    public IReadOnlyList<ClusterConsoleRegistration> FindRegistered()
    {
        RegistryLookups++;
        return [.. Registrations.OrderBy(r => r.Key != PlatformArchitecture.X64).Select(r => r.Value)];
    }

    /// <summary>Считать зарегистрированной эту платформу (регистрация той же разрядности заменяется).</summary>
    public void RegisterNow(PlatformInstallation platform) =>
        Registrations[platform.Architecture] = new ClusterConsoleRegistration(AdminLibraryPath(platform), platform.Architecture);

    public Task RegisterAsync(PlatformInstallation platform, CancellationToken cancellationToken = default)
    {
        if (DenyRegistration)
        {
            throw new LaunchFailedException("Регистрация отменена.");
        }

        RegisterNow(platform);
        return Task.CompletedTask;
    }

    public void Open(PlatformInstallation platform) => Opened.Add($"{platform.Version} {PlatformInstallation.ArchitectureName(platform.Architecture)}");
}

/// <summary>Список переходов в памяти.</summary>
internal sealed class FakeJumpList : IJumpList
{
    public IReadOnlyList<JumpListEntry> Recent { get; private set; } = [];

    public int Updates { get; private set; }

    public void SetRecent(IReadOnlyList<JumpListEntry> entries)
    {
        Recent = entries;
        Updates++;
    }
}

internal sealed class FakeLocator(IReadOnlyList<PlatformInstallation> installations) : IPlatformLocator
{
    public Task<PlatformScanResult> LocateAsync(IEnumerable<string> additionalRoots, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlatformScanResult(installations, []));
}

internal sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<LaunchCommand> Started { get; } = [];

    public List<Uri> OpenedUrls { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public int Start(LaunchCommand command)
    {
        Started.Add(command);
        return 4242;
    }

    /// <summary>Запуски с ожиданием (CREATEINFOBASE); <see cref="Runner"/> — что «сделает платформа» и код завершения.</summary>
    public List<LaunchCommand> Ran { get; } = [];

    public Func<LaunchCommand, int> Runner { get; set; } = _ => 0;

    public Task<int> RunAsync(LaunchCommand command, CancellationToken cancellationToken = default)
    {
        Ran.Add(command);
        return Task.FromResult(Runner(command));
    }

    public void OpenUrl(Uri url) => OpenedUrls.Add(url);

    public void OpenFolder(string path) => OpenedFolders.Add(path);

    public List<string> StartedPrograms { get; } = [];

    public void StartProgram(string path) => StartedPrograms.Add(path);

    /// <summary>Программы средств администрирования: путь и параметры.</summary>
    public List<(string Path, string Arguments)> OpenedPrograms { get; } = [];

    public void OpenProgram(string path, string arguments) => OpenedPrograms.Add((path, arguments));
}

/// <summary>ViewModel главного окна с поддельными службами и каталогом из временных файлов.</summary>
internal sealed class ViewModelFixture : IDisposable
{
    public const string SampleList = """
        [Бухгалтерия предприятия]
        Connect=Srvr="srv-1c";Ref="buh_prod";
        ID=00000000-0000-0000-0000-000000000001
        Folder=/Рабочие
        Version=8.3
        [Рабочие]
        Connect=
        ID=00000000-0000-0000-0000-00000000000f
        Folder=/
        [Зарплата и управление персоналом]
        Connect=Srvr="srv-1c";Ref="zup";
        ID=00000000-0000-0000-0000-000000000002
        Folder=/Рабочие
        [Копия бухгалтерии]
        Connect=File="C:\Bases\BuhCopy";
        ID=00000000-0000-0000-0000-000000000003
        Folder=/
        Version=8.3.22
        [Розница (тест)]
        Connect=ws="https://web.example/retail";
        ID=00000000-0000-0000-0000-000000000004
        Folder=/
        """;

    private readonly string _directory;
    private PersonalListStore? _store;

    public ViewModelFixture(string list = SampleList, string? startupLaunchKey = null)
    {
        _directory = Path.Combine(Path.GetTempPath(), "yaocl-app-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_directory);
        ListPath = Path.Combine(_directory, "ibases.v8i");
        File.WriteAllBytes(ListPath, TextFileCodec.Encode(list.ReplaceLineEndings("\r\n") + "\r\n", TextFormat.V8iDefault));

        Settings = new UserSettingsService(null, NullLogger<UserSettingsService>.Instance);
        ViewModel = new MainWindowViewModel(
            new InfoBaseCatalogLoader(),
            new LaunchCoordinator(Processes, NullLogger<LaunchCoordinator>.Instance),
            Settings,
            Dialogs,
            Shell,
            Shell,
            Shell,
            Processes,
            Files,
            NullLogger<MainWindowViewModel>.Instance,
            new FakePaths(ListPath)
            {
                DefaultPlatformInstallRoots = [InstallRoot, InstallRootX86],
                DefaultTemplatesDirectory = TemplatesRoot,
                InfoBaseCacheRoots =
                [
                    new Core.Cache.CacheRoot(LocalCacheRoot, Core.Cache.CacheLocation.Local),
                    new Core.Cache.CacheRoot(RoamingCacheRoot, Core.Cache.CacheLocation.Roaming),
                ],
            },
            new FakeLocator(Installations),
            Store,
            watcher: null,
            credentials: Credentials,
            cacheUsage: CacheUsage,
            availabilityChecker: new Core.Availability.AvailabilityChecker(Availability),
            jumpList: JumpList,
            startup: new StartupOptions(startupLaunchKey),
            clusterConsole: ClusterConsole);
    }

    public string ListPath { get; }

    public string Directory => _directory;

    public FakeFiles Files { get; } = new();

    public FakeCredentials Credentials { get; } = new();

    public FakeCacheUsage CacheUsage { get; } = new();

    public FakeAvailabilityProbe Availability { get; } = new();

    public FakeJumpList JumpList { get; } = new();

    public FakeClusterConsole ClusterConsole { get; } = new();

    /// <summary>Стандартные каталоги установки 1С (как «Program Files» и «Program Files (x86)») — здесь ищется стартер 1cestart; пустые.</summary>
    public string InstallRoot => Path.Combine(_directory, "Program Files", "1cv8");

    public string InstallRootX86 => Path.Combine(_directory, "Program Files (x86)", "1cv8");

    /// <summary>Каталог шаблонов конфигураций 1С (как %APPDATA%\1C\1cv8\tmplts); пустой.</summary>
    public string TemplatesRoot => Path.Combine(_directory, "tmplts");

    /// <summary>Временный каталог теста — для новых баз и т. п.</summary>
    public string TempDirectory => _directory;

    public string LocalCacheRoot => Path.Combine(_directory, "local");

    public string RoamingCacheRoot => Path.Combine(_directory, "roaming");

    /// <summary>Создать каталог кэша с файлом заданного размера.</summary>
    public string AddCache(string id, int bytes, bool roaming = false)
    {
        var path = Path.Combine(roaming ? RoamingCacheRoot : LocalCacheRoot, id);
        System.IO.Directory.CreateDirectory(Path.Combine(path, "vrs"));
        File.WriteAllBytes(Path.Combine(path, "vrs", "data.bin"), new byte[bytes]);
        return path;
    }

    public PersonalListStore Store => _store ??= new PersonalListStore(ListPath);

    /// <summary>Файл списка как документ — чтобы проверить, что записано.</summary>
    public V8iDocument SavedList() => V8iDocument.Parse(File.ReadAllBytes(ListPath));

    public FakeDialogs Dialogs { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeProcessLauncher Processes { get; } = new();

    public UserSettingsService Settings { get; }

    public MainWindowViewModel ViewModel { get; }

    public static IReadOnlyList<PlatformInstallation> Installations { get; } =
    [
        Installation("8.5.1.1150"),
        Installation("8.3.27.2130"),
        Installation("8.3.24.1667"),
    ];

    /// <summary>Загрузить список и показать его, как это делает окно при открытии.</summary>
    public async Task LoadAsync()
    {
        var catalog = await new InfoBaseCatalogLoader().LoadAsync(new CatalogSources(ListPath, []));
        ViewModel.Apply(catalog, new PlatformScanResult(Installations, []));
    }

    /// <summary>Загрузить личный список и один общий (из 1cestart.cfg).</summary>
    public async Task LoadWithCommonListAsync(string commonList)
    {
        var commonPath = Path.Combine(_directory, "common.v8i");
        File.WriteAllBytes(commonPath, TextFileCodec.Encode(commonList.ReplaceLineEndings("\r\n") + "\r\n", TextFormat.V8iDefault));
        var cfgPath = Path.Combine(_directory, "1cestart.cfg");
        File.WriteAllText(cfgPath, $"CommonInfoBases={commonPath}\r\n");

        var catalog = await new InfoBaseCatalogLoader().LoadAsync(new CatalogSources(ListPath, [cfgPath]));
        ViewModel.Apply(catalog, new PlatformScanResult(Installations, []));
    }

    public InfoBaseViewModel Base(string name) => ViewModel.InfoBases.Single(b => b.Name == name);

    public void Dispose()
    {
        ViewModel.StopRunningWatch();
        Settings.Dispose();
        _store?.Dispose();
        try
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static PlatformInstallation Installation(string version)
    {
        var bin = Path.Combine("C:", "1cv8", version, "bin");
        return new PlatformInstallation(
            PlatformVersion.Parse(version), PlatformArchitecture.X64, bin, Path.Combine(bin, "1cv8.exe"), Path.Combine(bin, "1cv8c.exe"));
    }
}
