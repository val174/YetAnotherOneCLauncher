using System.Security.Cryptography;
using YetAnotherOneCLauncher.Core.IO;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Editing;

/// <summary>
/// Запись в личный список ibases.v8i без потери чужих изменений.
/// </summary>
/// <remarks>
/// Каждая правка: перечитать файл → применить правку к свежему документу → убедиться, что файл за это время
/// не изменился (иначе повторить) → записать атомарно. Перед первой записью за время работы
/// прежняя версия сохраняется в <c>ibases.v8i.bak</c>.
/// </remarks>
public sealed class PersonalListStore : IDisposable
{
    private const int MaxAttempts = 3;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _backupMade;

    public PersonalListStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = path;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    /// <summary>Отпечаток файла после последней записи лаунчером — чтобы не принимать свою запись за чужую.</summary>
    public string? LastWrittenFingerprint { get; private set; }

    /// <summary>Применяет правку к личному списку и сохраняет его.</summary>
    /// <param name="edit">Правка свежепрочитанного документа; бросает <see cref="ListEditException"/>, если невозможна.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Документ в том виде, в каком он записан.</returns>
    /// <exception cref="ListEditException">Правка невозможна; файл не изменён.</exception>
    public async Task<V8iDocument> EditAsync(Action<V8iDocument> edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var original = await ReadBytesAsync(cancellationToken).ConfigureAwait(false);
                var document = original is null ? new V8iDocument() : V8iDocument.Parse(original);
                edit(document);
                var content = document.ToBytes();

                // Файл поменяли, пока мы готовили правку, — начинаем заново с новой версии.
                var current = await ReadBytesAsync(cancellationToken).ConfigureAwait(false);
                if (!SameContent(original, current))
                {
                    continue;
                }

                var backup = !_backupMade && original is not null ? BackupPath : null;
                await AtomicFileWriter.WriteAllBytesAsync(FilePath, content, backup, cancellationToken).ConfigureAwait(false);
                _backupMade |= backup is not null;
                LastWrittenFingerprint = Fingerprint(content);
                return document;
            }

            throw new ListEditException(
                ListEditErrorKind.Conflict,
                "Файл списка баз всё время меняется другой программой. Повторите действие позже.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Читает текущее содержимое; <c>null</c>, если файла нет.</summary>
    public async Task<V8iDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : V8iDocument.Parse(bytes);
    }

    /// <summary>Отпечаток текущего файла; <c>null</c>, если файла нет.</summary>
    public async Task<string?> CurrentFingerprintAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Fingerprint(bytes);
    }

    private async Task<byte[]?> ReadBytesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool SameContent(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static string Fingerprint(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
