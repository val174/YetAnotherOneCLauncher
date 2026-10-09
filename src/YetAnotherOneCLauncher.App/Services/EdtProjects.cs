using Avalonia.Media.Imaging;
using YetAnotherOneCLauncher.Core.Edt;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Проекты 1C:EDT для лаунчера: данные EDT Start, Java, открытые рабочие области, значок EDT.</summary>
public interface IEdtProjects
{
    /// <summary>Проекты и версии EDT; EDT Start не установлен — пусто.</summary>
    EdtCatalog Load();

    /// <summary>Java для версии EDT; <c>null</c> — EDT найдёт сам.</summary>
    string? JavaFor(EdtInstallation installation);

    /// <summary>Рабочая область открыта в EDT.</summary>
    bool IsOpen(EdtProject project);

    /// <summary>ID информационных баз, с которыми EDT связал проекты рабочей области.</summary>
    IReadOnlySet<string> InfobaseIds(EdtProject project);

    /// <summary>Программа 1C:EDT Start; <c>null</c> — не установлена.</summary>
    string? StarterPath { get; }

    /// <summary>Значок 1C:EDT — с компьютера (EDT Start или 1cedt.exe), в лаунчер не встроен; <c>null</c> — не нашли.</summary>
    Bitmap? Icon { get; }
}

/// <summary>
/// Данные EDT Start с диска. Значок 1C:EDT — из EDT Start (<c>icons\1cedt-ruby.png</c>, 128×128) или из <c>1cedt.exe</c>:
/// это знак фирмы «1С», лаунчер его не распространяет, а берёт у установленного EDT.
/// </summary>
public sealed class EdtProjects : IEdtProjects
{
    private const string StartIconFile = "1cedt-ruby.png";

    private readonly string _dataDirectory;
    private readonly IReadOnlyList<string> _componentRoots;
    private readonly IFileIconReader _fileIcons;
    private readonly Dictionary<string, string?> _java = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _icon;
    private bool _iconLoaded;
    private string? _starter;
    private bool _starterFound;
    private EdtCatalog _lastCatalog = EdtCatalog.Empty;

    public EdtProjects(IFileIconReader fileIcons)
        : this(EdtStartReader.DefaultDataDirectory, EdtJava.DefaultComponentRoots, fileIcons)
    {
    }

    public EdtProjects(string dataDirectory, IReadOnlyList<string> componentRoots, IFileIconReader fileIcons)
    {
        _dataDirectory = dataDirectory;
        _componentRoots = componentRoots;
        _fileIcons = fileIcons;
    }

    public EdtCatalog Load() => _lastCatalog = EdtStartReader.Load(_dataDirectory);

    public string? JavaFor(EdtInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!_java.TryGetValue(installation.ExecutablePath, out var java))
        {
            java = EdtJava.Find(_componentRoots, EdtJava.RequiredVersion(installation.ExecutablePath));
            _java[installation.ExecutablePath] = java;
        }

        return java;
    }

    public bool IsOpen(EdtProject project) => EdtLaunch.IsWorkspaceOpen(project.Workspace);

    public IReadOnlySet<string> InfobaseIds(EdtProject project) => EdtWorkspaceBindings.InfobaseIds(project.Workspace);

    public string? StarterPath
    {
        get
        {
            if (!_starterFound)
            {
                _starter = EdtStartReader.FindStarter(_componentRoots);
                _starterFound = true;
            }

            return _starter;
        }
    }

    public Bitmap? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _icon = LoadIcon();
                _iconLoaded = true;
            }

            return _icon;
        }
    }

    private Bitmap? LoadIcon()
    {
        try
        {
            var startIcon = Path.Combine(_dataDirectory, "icons", StartIconFile);
            if (File.Exists(startIcon))
            {
                return new Bitmap(startIcon);
            }

            var catalog = _lastCatalog.Installations.Count > 0 ? _lastCatalog : Load();
            return catalog.Newest is { } edt && _fileIcons.Read(edt.ExecutablePath) is { } pixels
                ? AdminToolIconStore.ToBitmap(pixels)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
