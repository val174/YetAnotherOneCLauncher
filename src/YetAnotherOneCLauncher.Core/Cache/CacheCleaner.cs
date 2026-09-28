namespace YetAnotherOneCLauncher.Core.Cache;

public enum CacheCleanStatus
{
    Removed,

    /// <summary>Файлы кэша открыты — база, скорее всего, запущена. Каталог не тронут.</summary>
    InUse,

    Failed,
}

public sealed record CacheCleanItem(CacheDirectory Directory, CacheCleanStatus Status, string? Message = null);

public sealed record CacheCleanResult(IReadOnlyList<CacheCleanItem> Items)
{
    public long RemovedBytes => Items.Where(i => i.Status == CacheCleanStatus.Removed).Sum(i => i.Directory.SizeBytes);

    public int RemovedCount => Items.Count(i => i.Status == CacheCleanStatus.Removed);

    public IEnumerable<CacheCleanItem> Problems => Items.Where(i => i.Status != CacheCleanStatus.Removed);
}

/// <summary>
/// Удаляет каталоги кэша. Перед удалением каждый каталог проверяется: имя — GUID, и он не занят.
/// Занятый каталог пропускается целиком — иначе кэш открытой базы остался бы наполовину удалённым.
/// </summary>
public static class CacheCleaner
{
    /// <param name="directories">Что удалить.</param>
    /// <param name="remove">Удаление каталога (обычно <see cref="DeletePermanently"/>).</param>
    /// <param name="isInUse">Проверка, открыты ли файлы в каталоге.</param>
    /// <param name="cancellationToken">Отмена между каталогами.</param>
    public static Task<CacheCleanResult> CleanAsync(
        IEnumerable<CacheDirectory> directories,
        Action<string> remove,
        Func<string, bool> isInUse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(isInUse);
        var list = directories.ToList();
        return Task.Run(() => Clean(list, remove, isInUse, cancellationToken), cancellationToken);
    }

    public static CacheCleanResult Clean(
        IEnumerable<CacheDirectory> directories,
        Action<string> remove,
        Func<string, bool> isInUse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(isInUse);
        var items = new List<CacheCleanItem>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CleanOne(directory, remove, isInUse));
        }

        return new CacheCleanResult(items);
    }

    /// <summary>Окончательное удаление каталога со всем содержимым.</summary>
    public static void DeletePermanently(string path)
    {
        // Файлы только для чтения Directory.Delete не удаляет — снимаем атрибут.
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            var attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }

    private static CacheCleanItem CleanOne(CacheDirectory directory, Action<string> remove, Func<string, bool> isInUse)
    {
        // Защита от ошибки выше по стеку: удаляем только каталоги с именем-GUID.
        if (CacheScanner.TryParseId(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory.Path))) is null)
        {
            return new CacheCleanItem(directory, CacheCleanStatus.Failed, "Это не каталог кэша базы.");
        }

        if (!Directory.Exists(directory.Path))
        {
            return new CacheCleanItem(directory, CacheCleanStatus.Removed);
        }

        try
        {
            if (isInUse(directory.Path))
            {
                return new CacheCleanItem(directory, CacheCleanStatus.InUse, "Файлы кэша открыты — закройте базу и повторите.");
            }

            remove(directory.Path);
            return new CacheCleanItem(directory, CacheCleanStatus.Removed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new CacheCleanItem(directory, CacheCleanStatus.Failed, ex.Message);
        }
    }
}
