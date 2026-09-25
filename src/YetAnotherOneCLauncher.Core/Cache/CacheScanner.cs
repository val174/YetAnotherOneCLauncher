namespace YetAnotherOneCLauncher.Core.Cache;

/// <summary>Результат поиска кэша.</summary>
/// <param name="Directories">Каталоги кэша баз.</param>
/// <param name="Warnings">Что не удалось просмотреть.</param>
public sealed record CacheScanResult(IReadOnlyList<CacheDirectory> Directories, IReadOnlyList<string> Warnings);

/// <summary>
/// Находит каталоги кэша баз и считает их размер. Берутся только подкаталоги, имя которых — GUID:
/// рядом лежат служебные каталоги и файлы платформы (<c>tmplts</c>, <c>EmptyIB</c>, <c>ExtCompT</c>, <c>*.pfl</c>),
/// их лаунчер не трогает.
/// </summary>
public static class CacheScanner
{
    public static Task<CacheScanResult> ScanAsync(IEnumerable<CacheRoot> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var list = roots.ToList();
        return Task.Run(() => Scan(list, cancellationToken), cancellationToken);
    }

    public static CacheScanResult Scan(IEnumerable<CacheRoot> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var directories = new List<CacheDirectory>();
        var warnings = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.GetDirectories(root.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Не удалось просмотреть каталог кэша {root.Path}: {ex.Message}");
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryParseId(Path.GetFileName(child)) is not { } id)
                {
                    continue;
                }

                var info = new DirectoryInfo(child);
                if (info.LinkTarget is not null)
                {
                    continue; // ссылку не считаем и не удаляем: кэш за ней чужой
                }

                directories.Add(new CacheDirectory(id, child, root.Location, SizeOf(info, cancellationToken), info.LastWriteTime));
            }
        }

        return new CacheScanResult(directories, warnings);
    }

    /// <summary>GUID из имени каталога в нижнем регистре; <c>null</c> — это не каталог базы.</summary>
    public static string? TryParseId(string? name) =>
        Guid.TryParseExact(name, "D", out var id) ? id.ToString("D") : null;

    private static long SizeOf(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long total = 0;
        try
        {
            foreach (var file in directory.EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файлы меняются, пока 1С работает, — размер приблизительный.
        }

        return total;
    }
}
