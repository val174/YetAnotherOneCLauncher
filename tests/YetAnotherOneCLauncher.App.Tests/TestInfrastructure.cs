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

    public Task ShowMessageAsync(string title, string text)
    {
        Messages.Add(text);
        return Task.CompletedTask;
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
        Task.FromResult(InfoBaseEditor(editor) && editor.TryAccept());
}

internal sealed class FakeFiles : IFileDialogService
{
    public string? FolderAnswer { get; set; }

    public string? OpenAnswer { get; set; }

    public string? SaveAnswer { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderAnswer);

    public Task<string?> OpenListFileAsync(string title) => Task.FromResult(OpenAnswer);

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

    public IReadOnlyList<string> DefaultPlatformInstallRoots { get; } = [];

    public PlatformExecutableNames PlatformExecutableNames => PlatformExecutableNames.Windows;

    public IReadOnlyList<string> InfoBaseCacheRoots { get; } = [];

    public string AppDataDirectory => Path.GetDirectoryName(PersonalInfoBaseListPath)!;
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

    public void OpenUrl(Uri url) => OpenedUrls.Add(url);

    public void OpenFolder(string path) => OpenedFolders.Add(path);
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

    public ViewModelFixture(string list = SampleList)
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
            new FakePaths(ListPath),
            new FakeLocator(Installations),
            Store);
    }

    public string ListPath { get; }

    public string Directory => _directory;

    public FakeFiles Files { get; } = new();

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
