using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Картинки значков средств администрирования.</summary>
public interface IAdminToolIconSource
{
    /// <summary>
    /// Значок, который подбирается сам: у программы — её значок, у веб-сервиса — значок сайта (из разметки страницы,
    /// иначе <c>/favicon.ico</c>). <c>null</c> — не нашёлся: показывается значок по умолчанию.
    /// </summary>
    Task<Bitmap?> LoadAutoAsync(string target);

    /// <summary>Свой значок из каталога значков; <c>null</c> — файла нет или это не картинка.</summary>
    Bitmap? LoadFile(string fileName);

    /// <summary>Скопировать картинку в каталог значков; возвращает имя файла там, <c>null</c> — это не картинка.</summary>
    string? Import(string sourcePath);

    /// <summary>Удалить свои значки, которыми не пользуется ни один инструмент.</summary>
    void RemoveUnused(IEnumerable<AdminTool> tools);
}

/// <summary>
/// Значки средств администрирования: свои — файлами в каталоге значков лаунчера (копия, чтобы не зависеть от исходного
/// файла), подобранные сами — в памяти на время работы лаунчера (значок сайта скачивается один раз).
/// </summary>
public sealed partial class AdminToolIconStore : IAdminToolIconSource
{
    private const int MaxPageBytes = 512 * 1024;
    private const int MaxIconBytes = 1024 * 1024;

    private readonly HttpClient? _http;
    private readonly IFileIconReader _fileIcons;
    private readonly string? _directory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _auto = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="http">Для значков сайтов; <c>null</c> — у веб-сервисов значок по умолчанию.</param>
    /// <param name="directory">Каталог своих значков; <c>null</c> — свои значки не сохраняются.</param>
    public AdminToolIconStore(HttpClient? http, IFileIconReader fileIcons, string? directory, ILogger<AdminToolIconStore> logger)
    {
        ArgumentNullException.ThrowIfNull(fileIcons);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _fileIcons = fileIcons;
        _directory = directory;
        _logger = logger;
    }

    public Task<Bitmap?> LoadAutoAsync(string target)
    {
        var key = (target ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return Task.FromResult<Bitmap?>(null);
        }

        return _auto.GetOrAdd(key, k => AdminToolTarget.WebUrl(k) is { } url ? LoadFaviconAsync(url) : Task.Run(() => LoadProgramIcon(k)));
    }

    public Bitmap? LoadFile(string fileName)
    {
        if (_directory is null || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        var path = Path.Combine(_directory, fileName);
        return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
    }

    public string? Import(string sourcePath)
    {
        if (_directory is null || !File.Exists(sourcePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(sourcePath);
        if (Decode(bytes) is null)
        {
            return null;
        }

        Directory.CreateDirectory(_directory);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(_directory, name), bytes);
        return name;
    }

    public void RemoveUnused(IEnumerable<AdminTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (_directory is null || !Directory.Exists(_directory))
        {
            return;
        }

        var used = tools.Select(t => AdminToolIcon.FileName(t.Icon)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_directory).Where(f => !used.Contains(Path.GetFileName(f))))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException ex)
            {
                LogRemoveFailed(_logger, file, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogRemoveFailed(_logger, file, ex);
            }
        }
    }

    private Bitmap? LoadProgramIcon(string target)
    {
        try
        {
            return AdminToolTarget.ResolveProgram(target) is { } program && _fileIcons.Read(program.Path) is { } pixels ? ToBitmap(pixels) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogIconFailed(_logger, target, ex);
            return null;
        }
    }

    private async Task<Bitmap?> LoadFaviconAsync(Uri page)
    {
        if (_http is null)
        {
            return null;
        }

        try
        {
            string? html = null;
            var baseUri = page;
            using (var response = await _http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                baseUri = response.RequestMessage?.RequestUri ?? page; // после перенаправлений
                if (response.IsSuccessStatusCode
                    && response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) != false)
                {
                    html = System.Text.Encoding.UTF8.GetString(await ReadLimitedAsync(response, MaxPageBytes).ConfigureAwait(false));
                }
            }

            foreach (var candidate in AdminToolTarget.FaviconCandidates(html, baseUri))
            {
                if (await DownloadIconAsync(candidate).ConfigureAwait(false) is { } icon)
                {
                    return icon;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            LogDownloadFailed(_logger, page, ex);
        }

        return null;
    }

    private async Task<Bitmap?> DownloadIconAsync(Uri url)
    {
        if (url.Scheme == "data")
        {
            var text = url.OriginalString;
            var comma = text.IndexOf(',', StringComparison.Ordinal);
            return comma > 0 && text[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                   && TryBase64(text[(comma + 1)..]) is { } data
                ? Decode(data)
                : null;
        }

        try
        {
            using var response = await _http!.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? Decode(await ReadLimitedAsync(response, MaxIconBytes).ConfigureAwait(false)) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            LogDownloadFailed(_logger, url, ex);
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < limit && (read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static byte[]? TryBase64(string text)
    {
        try
        {
            return Convert.FromBase64String(Uri.UnescapeDataString(text));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Картинка (PNG, ICO, JPEG, BMP, GIF, WebP) или <c>null</c>, если это не картинка.</summary>
    internal static Bitmap? Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    internal static Bitmap ToBitmap(IconPixels pixels)
    {
        var handle = GCHandle.Alloc(pixels.Bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Не удалось получить значок для «{Target}»")]
    private static partial void LogIconFailed(ILogger logger, string target, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Не удалось скачать значок сайта: {Url}")]
    private static partial void LogDownloadFailed(ILogger logger, Uri url, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не удалось удалить неиспользуемый значок {Path}")]
    private static partial void LogRemoveFailed(ILogger logger, string path, Exception exception);
}
