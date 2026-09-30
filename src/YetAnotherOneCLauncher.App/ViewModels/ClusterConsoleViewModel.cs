using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка окна консоли кластера: переход в «ПУСК» или версия платформы с консолью.</summary>
public sealed class ClusterConsoleOption
{
    private ClusterConsoleOption(PlatformInstallation? platform, bool isRegistered, Uri? puskUrl)
    {
        Platform = platform;
        IsRegistered = isRegistered;
        PuskUrl = puskUrl;
    }

    /// <summary>Версия платформы; <c>null</c> — строка «Панель управления сервисами и компонентами».</summary>
    public PlatformInstallation? Platform { get; }

    public Uri? PuskUrl { get; }

    public bool IsPusk => PuskUrl is not null;

    /// <summary>Компонент этой версии зарегистрирован: консоль откроется без запроса прав.</summary>
    public bool IsRegistered { get; }

    /// <summary>Первая из версий, доступных к регистрации, после зарегистрированных: над ней — разделитель.</summary>
    public bool HasSeparatorAbove { get; private init; }

    public string Title => Platform?.Version.ToString() ?? "Панель управления сервисами и компонентами";

    public string Detail => Platform is null
        ? string.Empty // адрес не показывается: он есть в настройках
        : Platform.Architecture switch
        {
            PlatformArchitecture.X86 => "32-разрядная",
            PlatformArchitecture.X64 => "64-разрядная",
            _ => PlatformInstallation.ArchitectureName(Platform.Architecture),
        };

    public static ClusterConsoleOption Pusk(Uri url) => new(null, false, url);

    public static ClusterConsoleOption Version(PlatformInstallation platform, bool isRegistered, bool separatorAbove = false) =>
        new(platform, isRegistered, null) { HasSeparatorAbove = separatorAbove };
}

/// <summary>
/// Окно «Консоль кластера серверов». Список: «Панель управления сервисами и компонентами» (если адрес задан в настройках), зарегистрированная
/// версия, разделитель, версии, доступные к регистрации. Для незарегистрированной версии её компонент
/// администрирования сначала регистрируется, затем открывается консоль.
/// </summary>
public sealed partial class ClusterConsoleViewModel : ObservableObject
{
    private readonly IClusterConsole _console;
    private readonly IProcessLauncher _processes;

    public ClusterConsoleViewModel(
        IEnumerable<PlatformInstallation> installations,
        IClusterConsole console,
        IProcessLauncher processes,
        Uri? puskUrl)
    {
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
        _processes = processes;

        // Регистрации читаются один раз: 64- и 32-разрядная независимы, их может быть две — от разных платформ.
        var registrations = console.FindRegistered();
        bool IsRegistered(PlatformInstallation p) =>
            registrations.Any(r => r.Architecture == p.Architecture && r.Matches(console.AdminLibraryPath(p)));

        // Зарегистрированная консоль платформы, которой нет среди найденных (стоит в нестандартном каталоге), —
        // тоже в списке: версия и каталог — из пути к radmin.dll в реестре.
        var known = installations.ToList();
        known.AddRange(registrations
            .Where(r => !known.Any(p => p.Architecture == r.Architecture && r.Matches(console.AdminLibraryPath(p))))
            .Select(FromRegistration)
            .OfType<PlatformInstallation>());

        // Внутри групп: новые версии сверху, при равных — 64-разрядная первой.
        var versions = known
            .Where(console.IsAvailable)
            .OrderByDescending(p => p.Version)
            .ThenByDescending(p => p.Architecture == PlatformArchitecture.X64)
            .ToList();
        var registered = versions.Where(IsRegistered).ToList();
        var unregistered = versions.Where(p => !IsRegistered(p)).ToList();

        var options = new List<ClusterConsoleOption>();
        if (puskUrl is not null)
        {
            options.Add(ClusterConsoleOption.Pusk(puskUrl));
        }

        options.AddRange(registered.Select(p => ClusterConsoleOption.Version(p, isRegistered: true)));
        // Разделитель — между зарегистрированными и доступными к регистрации.
        options.AddRange(unregistered.Select((p, i) => ClusterConsoleOption.Version(p, isRegistered: false, separatorAbove: i == 0 && registered.Count > 0)));
        Options = options;
        SelectedOption = options.FirstOrDefault(o => o.IsRegistered) ?? options.FirstOrDefault(o => !o.IsPusk) ?? options.FirstOrDefault();
    }

    public IReadOnlyList<ClusterConsoleOption> Options { get; }

    /// <summary>Платформа по пути из реестра: <c>…\8.3.25.1633\bin\radmin.dll</c>; <c>null</c> — версию не понять.</summary>
    internal static PlatformInstallation? FromRegistration(ClusterConsoleRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        try
        {
            var library = Environment.ExpandEnvironmentVariables(registration.LibraryPath.Trim().Trim('"'));
            var bin = Path.GetDirectoryName(library);
            var versionName = bin is null ? null : Path.GetFileName(Path.GetDirectoryName(bin));
            return versionName is not null && PlatformVersion.TryParse(versionName, out var version)
                ? new PlatformInstallation(version, registration.Architecture, bin!, null, null)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool HasOptions => Options.Count > 0;

    /// <summary>Есть ли версии с консолью (кроме строки «ПУСК»).</summary>
    public bool HasVersions => Options.Any(o => !o.IsPusk);

    /// <summary>Почему версий нет.</summary>
    public string EmptyText => _console.IsSupported
        ? "Ни в одной установленной платформе нет компонента администрирования серверов (radmin.dll). "
          + "Установите платформу с компонентом «Администрирование сервера 1С:Предприятия»."
        : "Консоль кластера серверов есть только в Windows.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRegistration), nameof(ActionText))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial ClusterConsoleOption? SelectedOption { get; set; }

    /// <summary>Выбрана незарегистрированная версия: перед открытием Windows спросит права администратора.</summary>
    public bool NeedsRegistration => SelectedOption is { IsPusk: false, IsRegistered: false };

    public string ActionText => SelectedOption?.IsPusk == true ? "Открыть" : "Запустить";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>Итог для строки состояния главного окна; <c>null</c> — ничего не сделано.</summary>
    public string? ResultMessage { get; private set; }

    /// <summary>Окну пора закрыться.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Выполнить выбранную строку: открыть «ПУСК» в браузере или консоль выбранной версии.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (SelectedOption is not { } option)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            if (option.PuskUrl is { } url)
            {
                _processes.OpenUrl(url);
                ResultMessage = "«ПУСК» открыт в браузере: " + url;
            }
            else if (option.Platform is { } platform)
            {
                if (!option.IsRegistered)
                {
                    await _console.RegisterAsync(platform);
                }

                _console.Open(platform);
                ResultMessage = $"Консоль кластера {option.Title} ({option.Detail}) открыта.";
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (LaunchFailedException ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLaunch() => SelectedOption is not null && !IsBusy;
}
