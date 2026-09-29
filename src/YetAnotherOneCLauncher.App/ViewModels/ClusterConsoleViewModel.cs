using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Platforms;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Версия платформы, для которой можно открыть консоль кластера.</summary>
public sealed class ClusterConsoleOption
{
    public ClusterConsoleOption(PlatformInstallation platform, bool isRegistered)
    {
        Platform = platform;
        IsRegistered = isRegistered;
    }

    public PlatformInstallation Platform { get; }

    /// <summary>Компонент этой версии уже зарегистрирован: консоль откроется без запроса прав.</summary>
    public bool IsRegistered { get; }

    public string Version => Platform.Version.ToString();

    public string ArchitectureText => Platform.Architecture switch
    {
        PlatformArchitecture.X86 => "32-разрядная",
        PlatformArchitecture.X64 => "64-разрядная",
        _ => PlatformInstallation.ArchitectureName(Platform.Architecture),
    };
}

/// <summary>
/// Окно «Консоль кластера серверов»: выбор версии платформы, регистрация её компонента администрирования
/// (если зарегистрирована другая версия) и открытие консоли; при заданном адресе — переход в «ПУСК».
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
        _console = console;
        _processes = processes;
        PuskUrl = puskUrl;

        // Новые версии сверху, при равных — 64-разрядная первой.
        Options = installations
            .Where(console.IsAvailable)
            .OrderByDescending(p => p.Version)
            .ThenByDescending(p => p.Architecture == PlatformArchitecture.X64)
            .Select(p => new ClusterConsoleOption(p, console.IsRegistered(p)))
            .ToList();
        SelectedOption = Options.FirstOrDefault(o => o.IsRegistered) ?? (Options.Count > 0 ? Options[0] : null);
    }

    public IReadOnlyList<ClusterConsoleOption> Options { get; }

    public bool HasOptions => Options.Count > 0;

    /// <summary>Почему выбирать не из чего.</summary>
    public string EmptyText => _console.IsSupported
        ? "Ни в одной установленной платформе нет компонента администрирования серверов (radmin.dll). "
          + "Установите платформу с компонентом «Администрирование сервера 1С:Предприятия»."
        : "Консоль кластера серверов есть только в Windows.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRegistration))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial ClusterConsoleOption? SelectedOption { get; set; }

    /// <summary>Выбранная версия не зарегистрирована: перед открытием Windows спросит права администратора.</summary>
    public bool NeedsRegistration => SelectedOption is { IsRegistered: false };

    /// <summary>Адрес «ПУСК» из настроек; <c>null</c> — кнопки перехода нет.</summary>
    public Uri? PuskUrl { get; }

    public bool HasPusk => PuskUrl is not null;

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
            if (!option.IsRegistered)
            {
                await _console.RegisterAsync(option.Platform);
            }

            _console.Open(option.Platform);
            ResultMessage = $"Консоль кластера {option.Version} ({option.ArchitectureText}) открыта.";
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

    [RelayCommand]
    private void OpenPusk()
    {
        if (PuskUrl is null)
        {
            return;
        }

        try
        {
            _processes.OpenUrl(PuskUrl);
            ResultMessage = "«ПУСК» открыт в браузере: " + PuskUrl;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (LaunchFailedException ex)
        {
            ErrorText = ex.Message;
        }
    }
}
