namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Картинка значка: пиксели BGRA (не умноженные на прозрачность), строки сверху вниз.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>Значок файла, который показывает оболочка ОС (у программы — её собственный).</summary>
public interface IFileIconReader
{
    /// <summary>Значок файла; <c>null</c> — файла нет или значок не получить.</summary>
    IconPixels? Read(string path);
}

/// <summary>Значков файлов нет (Linux и прочее): у программ — значок по умолчанию.</summary>
public sealed class NoFileIconReader : IFileIconReader
{
    public IconPixels? Read(string path) => null;
}
