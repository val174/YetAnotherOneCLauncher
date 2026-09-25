namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Какой файл изменился.</summary>
public enum ListFileKind
{
    /// <summary>Личный список ibases.v8i.</summary>
    PersonalList,

    /// <summary>Настройки стартера 1cestart.cfg (в том числе перечень общих списков).</summary>
    StarterConfig,
}

/// <summary>Сообщает об изменении файлов 1С, сделанном другой программой (стартером, другим лаунчером).</summary>
public interface IListChangeWatcher : IDisposable
{
    event EventHandler<ListFileKind>? Changed;

    void Start(string personalListPath, IEnumerable<string> starterConfigPaths);
}

/// <summary>
/// Следит за файлами через <see cref="FileSystemWatcher"/>. Серию событий одной записи (временный файл,
/// замена, смена атрибутов) склеивает: сообщение приходит один раз, спустя <see cref="Debounce"/> после последнего события.
/// </summary>
public sealed class ListChangeWatcher : IListChangeWatcher
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<ListFileKind, Timer> _timers = [];
    private readonly Lock _gate = new();

    public event EventHandler<ListFileKind>? Changed;

    public void Start(string personalListPath, IEnumerable<string> starterConfigPaths)
    {
        Watch(personalListPath, ListFileKind.PersonalList);
        foreach (var path in starterConfigPaths)
        {
            Watch(path, ListFileKind.StarterConfig);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }

            _watchers.Clear();
            _timers.Clear();
        }
    }

    private void Watch(string path, ListFileKind kind)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return; // каталога нет — файла ещё нет; появится после первой записи лаунчером
        }

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        watcher.Changed += (_, _) => Schedule(kind);
        watcher.Created += (_, _) => Schedule(kind);
        watcher.Deleted += (_, _) => Schedule(kind);
        watcher.Renamed += (_, _) => Schedule(kind);
        watcher.EnableRaisingEvents = true;

        lock (_gate)
        {
            _watchers.Add(watcher);
        }
    }

    private void Schedule(ListFileKind kind)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(kind, out var timer))
            {
                timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                return;
            }

            _timers[kind] = new Timer(_ => Changed?.Invoke(this, kind), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }
}
