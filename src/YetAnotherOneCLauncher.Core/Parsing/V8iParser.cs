using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>
/// Разбор .v8i. Стандартные INI-библиотеки не подходят: имена секций повторяются,
/// а исходную структуру нужно сохранить для обратной записи.
/// </summary>
internal static class V8iParser
{
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    public static V8iDocument Parse(string text, TextFormat format)
    {
        var document = new V8iDocument { Format = format };
        var lines = SplitLines(text, out var endsWithNewLine);
        document.EndsWithNewLine = endsWithNewLine;

        V8iSection? current = null;
        foreach (var line in lines)
        {
            if (TryParseHeader(line, out var name))
            {
                current = new V8iSection(name, rawHeader: line);
                document.Sections.Add(current);
                continue;
            }

            var target = current?.Lines ?? document.Preamble;
            target.Add(IniLine.Parse(line));
        }

        return document;
    }

    internal static string[] SplitLines(string text, out bool endsWithNewLine)
    {
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..]; // BOM, оставшийся в тексте (например, двойной)
        }

        if (text.Length == 0)
        {
            endsWithNewLine = false;
            return [];
        }

        var lines = text.Split(LineSeparators, StringSplitOptions.None);
        endsWithNewLine = lines[^1].Length == 0;
        return endsWithNewLine ? lines[..^1] : lines;
    }

    internal static bool TryParseHeader(string line, out string name)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            // Берём всё между первой '[' и последней ']', чтобы "[База [тест]]" читалась целиком.
            name = trimmed[1..^1];
            return true;
        }

        name = string.Empty;
        return false;
    }
}
