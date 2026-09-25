using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Linux;

/// <summary>
/// Корзина по спецификации freedesktop.org: <c>$XDG_DATA_HOME/Trash</c> (обычно <c>~/.local/share/Trash</c>),
/// сам каталог — в <c>files/</c>, сведения для восстановления — в <c>info/&lt;имя&gt;.trashinfo</c>.
/// </summary>
public sealed class FreedesktopTrash : IRecycleBin
{
    private readonly string _trashDirectory;

    public FreedesktopTrash(string? trashDirectory = null)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        _trashDirectory = trashDirectory ?? Path.Combine(dataHome, "Trash");
    }

    public void MoveToRecycleBin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var files = Path.Combine(_trashDirectory, "files");
        var info = Path.Combine(_trashDirectory, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);

        var baseName = Path.GetFileName(fullPath);
        for (var attempt = 1; attempt < 1000; attempt++)
        {
            var name = attempt == 1 ? baseName : $"{baseName}.{attempt}";
            var infoPath = Path.Combine(info, name + ".trashinfo");
            try
            {
                // Файл сведений создаётся первым и только если его нет — так имя в корзине занимается атомарно.
                using var stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(TrashInfo(fullPath, DateTime.Now));
            }
            catch (IOException) when (File.Exists(infoPath))
            {
                continue;
            }

            var target = Path.Combine(files, name);
            if (Path.Exists(target))
            {
                File.Delete(infoPath);
                continue;
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Move(fullPath, target);
                }
                else
                {
                    Directory.Move(fullPath, target);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Delete(infoPath);
                throw new IOException($"Не удалось переместить в корзину {fullPath}: {ex.Message}", ex);
            }
        }

        throw new IOException($"Не удалось подобрать имя в корзине для {fullPath}.");
    }

    internal static string TrashInfo(string path, DateTime deletedAt) =>
        "[Trash Info]\n" +
        $"Path={EscapePath(path)}\n" +
        $"DeletionDate={deletedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}\n";

    /// <summary>Путь в виде URI без схемы: всё, кроме безопасных символов и '/', — в %XX (UTF-8).</summary>
    internal static string EscapePath(string path)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~'))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}

/// <summary>В Linux открытые файлы переименованию не мешают, поэтому смотрим дескрипторы процессов в <c>/proc</c>.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCacheUsageProbe : ICacheUsageProbe
{
    public IReadOnlyList<string> RunningPlatformProcesses() => PlatformProcesses.List();

    public bool IsDirectoryInUse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + "/";
        foreach (var process in SafeEnumerate("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(process), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            foreach (var descriptor in SafeEnumerate(Path.Combine(process, "fd")))
            {
                string? target;
                try
                {
                    target = new FileInfo(descriptor).LinkTarget;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (target is not null && target.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] SafeEnumerate(string directory)
    {
        try
        {
            return Directory.GetFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // чужие процессы и завершившиеся между шагами
        }
    }
}
