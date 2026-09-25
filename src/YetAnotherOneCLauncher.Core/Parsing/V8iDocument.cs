using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Файл списка баз (.v8i) в исходной структуре: порядок секций, неизвестные ключи,
/// комментарии и формат файла сохраняются, чтобы запись не портила файл.
/// </summary>
public sealed class V8iDocument
{
    /// <summary>Строки до первой секции (обычно пусто).</summary>
    public List<IniLine> Preamble { get; } = [];

    public List<V8iSection> Sections { get; } = [];

    public TextFormat Format { get; set; } = TextFormat.V8iDefault;

    /// <summary>Заканчивался ли исходный файл переводом строки.</summary>
    public bool EndsWithNewLine { get; set; } = true;

    public static V8iDocument Parse(string text, TextFormat? format = null) =>
        V8iParser.Parse(text, format ?? TextFormat.V8iDefault);

    public static V8iDocument Parse(ReadOnlySpan<byte> bytes)
    {
        var decoded = TextFileCodec.Decode(bytes);
        return V8iParser.Parse(decoded.Text, decoded.Format);
    }

    public static async Task<V8iDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var decoded = await TextFileCodec.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        return V8iParser.Parse(decoded.Text, decoded.Format);
    }

    public string ToText() => V8iWriter.Serialize(this);

    public byte[] ToBytes() => TextFileCodec.Encode(ToText(), Format);

    /// <summary>Атомарно сохраняет файл; прежняя версия, если есть, кладётся в <paramref name="backupPath"/>.</summary>
    public Task SaveAsync(string path, string? backupPath = null, CancellationToken cancellationToken = default) =>
        V8iWriter.SaveAsync(this, path, backupPath, cancellationToken);
}
