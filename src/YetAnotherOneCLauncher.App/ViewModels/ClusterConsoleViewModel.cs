using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Строка окна консоли кластера: переход в «ПУСК», версия платформы с консолью или заголовок группы
/// «Доступные к регистрации» (сворачивается).
/// </summary>
public sealed partial class ClusterConsoleOption : ObservableObject
{
    private ClusterConsoleOption(PlatformInstallation? platform, bool isRegistered, Uri? puskUrl, int groupCount = -1)
    {
        Platform = platform;
        IsRegistered = isRegistered;
        PuskUrl = puskUrl;
        GroupCount = groupCount;
    }

    /// <summary>Версия платформы; <c>null</c> — строка «ПУСК» или заголовок группы.</summary>
    public PlatformInstallation? Platform { get; }

    public Uri? PuskUrl { get; }

    public bool IsPusk => PuskUrl is not null;

    /// <summary>Заголовок группы версий, доступных к регистрации.</summary>
    public bool IsGroupHeader => GroupCount >= 0;

    /// <summary>Сколько версий в группе (у заголовка).</summary>
    public int GroupCount { get; }

    /// <summary>Строка версии внутри группы «Доступные к регистрации» — с отступом.</summary>
    public bool IsInGroup => Platform is not null && !IsRegistered;

    /// <summary>Компонент этой версии зарегистрирован: консоль откроется без запроса прав.</summary>
    public bool IsRegistered { get; }

    /// <summary>Группа развёрнута (у заголовка).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    public partial bool IsExpanded { get; set; }

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>Над строкой — разделитель: у заголовка группы, если выше есть другие строки.</summary>
    public bool HasSeparatorAbove { get; private init; }

    public string Title => IsGroupHeader
        ? "Доступные к регистрации"
        : Platform?.Version.ToString() ?? "Панель управления сервисами и компонентами";

    public string Detail => IsGroupHeader
        ? GroupCount.ToString(System.Globalization.CultureInfo.CurrentCulture)
        : Platform is null
            ? string.Empty // адрес «ПУСК» не показывается: он есть в настройках
            : Platform.Architecture switch
            {
                PlatformArchitecture.X86 => "32-разрядная",
                PlatformArchitecture.X64 => "64-разрядная",
                _ => PlatformInstallation.ArchitectureName(Platform.Architecture),
            };

    public static ClusterConsoleOption Pusk(Uri url) => new(null, false, url);

    public static ClusterConsoleOption Version(PlatformInstallation platform, bool isRegistered) => new(platform, isRegistered, null);

    public static ClusterConsoleOption Group(int count, bool separatorAbove) =>
        new(null, false, null, count) { HasSeparatorAbove = separatorAbove };
}

/// <summary>
/// Окно «Консоль кластера серверов». Список: «Панель управления сервисами и компонентами» (если адрес «ПУСК»
/// задан в настройках), зарегистрированные версии, разделитель и свёрнутая группа «Доступные к регистрации».
/// Для незарегистрированной версии её компонент администрирования сначала регистрируется, затем открывается консоль.
/// </summary>
public sealed partial class ClusterConsoleViewModel : ObservableObject
{
    private readonly IClusterConsole _console;
    private readonly IProcessLauncher _processes;
    private readonly List<ClusterConsoleOption> _unregistered;
    private readonly ClusterConsoleOption? _group;

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
        Registrations = registrations;
        // Зарегистрированной считается только действующая регистрация — та, что загрузит консоль.
        bool IsRegistered(PlatformInstallation p) =>
            registrations.Any(r => r.IsActive && r.Architecture == p.Architecture && r.Matches(console.AdminLibraryPath(p)));

        // Платформы для запуска баз — только с клиентом 1С; консоли хватает radmin.dll, поэтому добавляются и
        // установки только с сервером и администрированием.
        var known = installations.ToList();
        known.AddRange(console.FindAdminInstallations()
            .Where(a => !known.Any(p => p.Architecture == a.Architecture
                && string.Equals(
                    Path.TrimEndingDirectorySeparator(p.BinDirectory),
                    Path.TrimEndingDirectorySeparator(a.BinDirectory),
                    StringComparison.OrdinalIgnoreCase))));

        // Зарегистрированная консоль платформы, которой нет среди найденных (стоит в нестандартном каталоге), —
        // тоже в списке: версия и каталог — из пути к radmin.dll в реестре.
        known.AddRange(registrations
            .Where(r => r.IsActive && !known.Any(p => p.Architecture == r.Architecture && r.Matches(console.AdminLibraryPath(p))))
            .Select(FromRegistration)
            .OfType<PlatformInstallation>());

        // Внутри групп: новые версии сверху, при равных — 64-разрядная первой.
        var versions = known
            .Where(console.IsAvailable)
            .OrderByDescending(p => p.Version)
            .ThenByDescending(p => p.Architecture == PlatformArchitecture.X64)
            .ToList();

        if (puskUrl is not null)
        {
            Options.Add(ClusterConsoleOption.Pusk(puskUrl));
        }

        foreach (var platform in versions.Where(IsRegistered))
        {
            Options.Add(ClusterConsoleOption.Version(platform, isRegistered: true));
        }

        // Незарегистрированные — в группе «Доступные к регистрации», по умолчанию свёрнутой.
        _unregistered = [.. versions.Where(p => !IsRegistered(p)).Select(p => ClusterConsoleOption.Version(p, isRegistered: false))];
        Versions = [.. Options.Where(o => o.Platform is not null), .. _unregistered];
        if (_unregistered.Count > 0)
        {
            _group = ClusterConsoleOption.Group(_unregistered.Count, separatorAbove: Options.Count > 0);
            Options.Add(_group);
        }

        SelectedOption = Options.FirstOrDefault(o => o.IsRegistered) ?? Options.FirstOrDefault();
    }

    /// <summary>Видимые строки: версии свёрнутой группы в них не входят.</summary>
    public ObservableCollection<ClusterConsoleOption> Options { get; } = [];

    /// <summary>Все версии с консолью — зарегистрированные и из группы (даже свёрнутой).</summary>
    public IReadOnlyList<ClusterConsoleOption> Versions { get; }

    /// <summary>Что нашлось в реестре — для лога.</summary>
    public IReadOnlyList<ClusterConsoleRegistration> Registrations { get; }

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
    public bool HasVersions => Versions.Count > 0;

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
    public bool NeedsRegistration => SelectedOption is { Platform: not null, IsRegistered: false };

    public string ActionText => SelectedOption switch
    {
        { IsPusk: true } => "Открыть",
        { IsGroupHeader: true, IsExpanded: true } => "Свернуть",
        { IsGroupHeader: true } => "Развернуть",
        _ => "Запустить",
    };

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

    /// <summary>Развернуть или свернуть группу «Доступные к регистрации».</summary>
    [RelayCommand]
    private void ToggleGroup()
    {
        if (_group is null)
        {
            return;
        }

        _group.IsExpanded = !_group.IsExpanded;
        var index = Options.IndexOf(_group);
        if (_group.IsExpanded)
        {
            for (var i = 0; i < _unregistered.Count; i++)
            {
                Options.Insert(index + 1 + i, _unregistered[i]);
            }
        }
        else
        {
            if (SelectedOption is { IsInGroup: true })
            {
                SelectedOption = _group; // выделенная версия скрывается — выделение на заголовок
            }

            foreach (var option in _unregistered)
            {
                Options.Remove(option);
            }
        }

        OnPropertyChanged(nameof(ActionText));
    }

    /// <summary>Выполнить выбранную строку: открыть «ПУСК», консоль выбранной версии или развернуть группу.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (SelectedOption is not { } option)
        {
            return;
        }

        if (option.IsGroupHeader)
        {
            ToggleGroup();
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
