using System.Text;

namespace YetAnotherOneCLauncher.Core.Text;

/// <summary>Кодировки, которые встречаются в файлах 1С.</summary>
public enum TextEncodingKind
{
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,
    Windows1251,
}

/// <summary>
/// Формат текстового файла: кодировка, наличие BOM и переводы строк.
/// Запоминается при чтении, чтобы при записи вернуть файл в том же виде.
/// </summary>
public sealed record TextFormat(TextEncodingKind EncodingKind, bool HasBom, string NewLine)
{
    /// <summary>Формат, в котором 1С сама пишет ibases.v8i: UTF-8 с BOM, CRLF.</summary>
    public static TextFormat V8iDefault { get; } = new(TextEncodingKind.Utf8, true, "\r\n");
}

/// <summary>Результат декодирования: текст и формат исходного файла.</summary>
public sealed record DecodedText(string Text, TextFormat Format);

/// <summary>
/// Чтение и запись текстовых файлов 1С с определением кодировки.
/// ibases.v8i обычно в UTF-8 с BOM, 1cestart.cfg часто в UTF-16 LE с BOM.
/// Без BOM: пробуем UTF-16 (по нулевым байтам), затем строгий UTF-8, иначе Windows-1251.
/// </summary>
public static class TextFileCodec
{
    static TextFileCodec()
    {
        // Нужен для Windows-1251 на .NET (Core).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static DecodedText Decode(ReadOnlySpan<byte> bytes)
    {
        TextEncodingKind kind;
        bool hasBom;
        int offset;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            (kind, hasBom, offset) = (TextEncodingKind.Utf8, true, 3);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            (kind, hasBom, offset) = (TextEncodingKind.Utf16LittleEndian, true, 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            (kind, hasBom, offset) = (TextEncodingKind.Utf16BigEndian, true, 2);
        }
        else
        {
            (kind, hasBom, offset) = (DetectWithoutBom(bytes), false, 0);
        }

        var text = GetEncoding(kind).GetString(bytes[offset..]);
        return new DecodedText(text, new TextFormat(kind, hasBom, DetectNewLine(text)));
    }

    public static byte[] Encode(string text, TextFormat format)
    {
        var encoding = GetEncoding(format.EncodingKind);
        var body = encoding.GetBytes(text);
        if (!format.HasBom)
        {
            return body;
        }

        var bom = GetBom(format.EncodingKind);
        var result = new byte[bom.Length + body.Length];
        bom.CopyTo(result, 0);
        body.CopyTo(result, bom.Length);
        return result;
    }

    public static async Task<DecodedText> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Decode(bytes);
    }

    internal static Encoding GetEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
        TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
        TextEncodingKind.Windows1251 => Encoding.GetEncoding(1251),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static byte[] GetBom(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => [0xEF, 0xBB, 0xBF],
        TextEncodingKind.Utf16LittleEndian => [0xFF, 0xFE],
        TextEncodingKind.Utf16BigEndian => [0xFE, 0xFF],
        _ => [],
    };

    private static TextEncodingKind DetectWithoutBom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return TextEncodingKind.Utf8;
        }

        // UTF-16 без BOM: у латиницы и цифр каждый второй байт нулевой.
        var sample = bytes[..Math.Min(bytes.Length, 512)];
        if (sample.Length >= 4)
        {
            int zerosEven = 0, zerosOdd = 0;
            for (var i = 0; i < sample.Length; i++)
            {
                if (sample[i] != 0)
                {
                    continue;
                }

                if (i % 2 == 0)
                {
                    zerosEven++;
                }
                else
                {
                    zerosOdd++;
                }
            }

            var half = sample.Length / 2;
            if (zerosOdd > half * 0.3 && zerosEven == 0)
            {
                return TextEncodingKind.Utf16LittleEndian;
            }

            if (zerosEven > half * 0.3 && zerosOdd == 0)
            {
                return TextEncodingKind.Utf16BigEndian;
            }
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return TextEncodingKind.Utf8;
        }
        catch (DecoderFallbackException)
        {
            return TextEncodingKind.Windows1251;
        }
    }

    private static string DetectNewLine(string text)
    {
        var index = text.IndexOf('\n');
        if (index > 0 && text[index - 1] == '\r')
        {
            return "\r\n";
        }

        if (index >= 0)
        {
            return "\n";
        }

        return text.Contains('\r') ? "\r" : "\r\n";
    }
}
