namespace YetAnotherOneCLauncher.Core.IO;

/// <summary>
/// Атомарная запись файла: сначала во временный файл рядом, затем замена оригинала.
/// При сбое во время записи исходный файл остаётся нетронутым.
/// </summary>
public static class AtomicFileWriter
{
    /// <param name="path">Целевой файл.</param>
    /// <param name="content">Содержимое.</param>
    /// <param name="backupPath">Если задан и файл существует, прежняя версия сохраняется сюда.</param>
    public static async Task WriteAllBytesAsync(
        string path,
        byte[] content,
        string? backupPath = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(fullPath))
            {
                if (backupPath is not null && File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Временный файл не критичен.
                }
            }
        }
    }
}
