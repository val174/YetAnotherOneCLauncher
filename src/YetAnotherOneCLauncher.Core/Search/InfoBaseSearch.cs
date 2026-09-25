using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Search;

/// <summary>Фрагмент текста для подсветки.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>Найденная база: оценка и что подсветить в имени.</summary>
public sealed record SearchMatch(InfoBase InfoBase, int Score, IReadOnlyList<TextRange> NameHighlights);

/// <summary>
/// Быстрый поиск баз. Каждое слово запроса должно найтись в имени, папке или строке подключения.
/// Понимает запрос в неправильной раскладке (",e[" → «бух») и начальные буквы слов («зуп» →
/// «Зарплата и управление персоналом»). Регистр и «ё/е» не различаются.
/// </summary>
public static class InfoBaseSearch
{
    // Оценки совпадений: чем точнее и чем ближе к имени, тем выше.
    private const int NameStartsWith = 100;
    private const int NameWordStartsWith = 80;
    private const int NameInitials = 70;
    private const int NameContains = 50;
    private const int OtherWordStartsWith = 30;
    private const int OtherContains = 20;
    private const int WrongLayoutPenalty = 5;

    /// <param name="infoBases">Где искать.</param>
    /// <param name="query">Запрос; пустой — пустой результат.</param>
    /// <param name="boost">Надбавка к оценке: избранное, частота запусков.</param>
    public static IReadOnlyList<SearchMatch> Search(
        IEnumerable<InfoBase> infoBases,
        string? query,
        Func<InfoBase, int>? boost = null)
    {
        ArgumentNullException.ThrowIfNull(infoBases);
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return [];
        }

        var results = new List<(SearchMatch Match, int Boost)>();
        foreach (var infoBase in infoBases)
        {
            if (TryMatch(infoBase, tokens, out var score, out var highlights))
            {
                var extra = boost?.Invoke(infoBase) ?? 0;
                results.Add((new SearchMatch(infoBase, score + extra, highlights), extra));
            }
        }

        return results
            .OrderByDescending(r => r.Match.Score)
            .ThenByDescending(r => r.Boost)
            .ThenBy(r => r.Match.InfoBase.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => r.Match)
            .ToList();
    }

    /// <summary>Приводит текст к виду для сравнения. Длина не меняется, поэтому позиции совпадают с исходным текстом.</summary>
    public static string Normalize(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = char.ToLowerInvariant(source[i]);
                span[i] = c == 'ё' ? 'е' : c;
            }
        });

    private static List<Token> Tokenize(string? query)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return tokens;
        }

        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var original = Normalize(word);
            var variants = new List<(string Text, int Penalty)> { (original, 0) };
            foreach (var converted in new[] { Normalize(KeyboardLayout.ToCyrillic(word)), Normalize(KeyboardLayout.ToLatin(word)) })
            {
                if (variants.TrueForAll(v => v.Text != converted))
                {
                    variants.Add((converted, WrongLayoutPenalty));
                }
            }

            tokens.Add(new Token(variants));
        }

        return tokens;
    }

    private static bool TryMatch(InfoBase infoBase, List<Token> tokens, out int score, out IReadOnlyList<TextRange> highlights)
    {
        score = 0;
        var ranges = new List<TextRange>();
        var name = Normalize(infoBase.Name);
        var words = FindWords(name);
        var others = OtherFields(infoBase).Select(Normalize).ToList();

        foreach (var token in tokens)
        {
            var best = 0;
            IReadOnlyList<TextRange> bestRanges = [];
            foreach (var (text, penalty) in token.Variants)
            {
                var (value, found) = MatchName(name, words, text);
                if (value == 0)
                {
                    value = MatchOthers(others, text);
                }

                if (value > 0 && value - penalty > best)
                {
                    best = value - penalty;
                    bestRanges = found;
                }
            }

            if (best == 0)
            {
                highlights = [];
                return false;
            }

            score += best;
            ranges.AddRange(bestRanges);
        }

        highlights = Merge(ranges);
        return true;
    }

    private static (int Score, IReadOnlyList<TextRange> Ranges) MatchName(string name, List<TextRange> words, string token)
    {
        if (name.StartsWith(token, StringComparison.Ordinal))
        {
            return (NameStartsWith, [new TextRange(0, token.Length)]);
        }

        foreach (var word in words)
        {
            if (string.CompareOrdinal(name, word.Start, token, 0, token.Length) == 0 && word.Length >= token.Length)
            {
                return (NameWordStartsWith, [new TextRange(word.Start, token.Length)]);
            }
        }

        if (MatchInitials(name, words, token) is { } initials)
        {
            return (NameInitials, initials);
        }

        var index = name.IndexOf(token, StringComparison.Ordinal);
        return index >= 0 ? (NameContains, [new TextRange(index, token.Length)]) : (0, []);
    }

    /// <summary>
    /// Запрос — начальные буквы слов по порядку, первое слово обязательно: «зуп» и «зиуп» подходят
    /// к «Зарплата и управление персоналом».
    /// </summary>
    private static List<TextRange>? MatchInitials(string name, List<TextRange> words, string token)
    {
        if (token.Length < 2 || words.Count < token.Length || name[words[0].Start] != token[0])
        {
            return null;
        }

        var ranges = new List<TextRange> { new(words[0].Start, 1) };
        var next = 1;
        for (var w = 1; w < words.Count && next < token.Length; w++)
        {
            if (name[words[w].Start] == token[next])
            {
                ranges.Add(new TextRange(words[w].Start, 1));
                next++;
            }
        }

        return next == token.Length ? ranges : null;
    }

    private static int MatchOthers(List<string> fields, string token)
    {
        var best = 0;
        foreach (var field in fields)
        {
            var index = field.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(field[index - 1]))
                {
                    return OtherWordStartsWith;
                }

                best = OtherContains;
                index = field.IndexOf(token, index + 1, StringComparison.Ordinal);
            }
        }

        return best;
    }

    private static IEnumerable<string> OtherFields(InfoBase infoBase)
    {
        yield return infoBase.FolderPath;
        var connection = infoBase.Connection;
        switch (connection.Kind)
        {
            case ConnectionKind.File:
                yield return connection.FilePath ?? string.Empty;
                break;
            case ConnectionKind.Server:
                yield return connection.Server ?? string.Empty;
                yield return connection.InfobaseName ?? string.Empty;
                break;
            case ConnectionKind.Web:
                yield return connection.WebUrl ?? string.Empty;
                break;
            default:
                yield return connection.ToString();
                break;
        }
    }

    private static List<TextRange> FindWords(string text)
    {
        var words = new List<TextRange>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                words.Add(new TextRange(start, i - start));
                start = -1;
            }
        }

        return words;
    }

    private static List<TextRange> Merge(List<TextRange> ranges)
    {
        if (ranges.Count < 2)
        {
            return ranges;
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<TextRange> { ranges[0] };
        foreach (var range in ranges.Skip(1))
        {
            var last = merged[^1];
            if (range.Start <= last.Start + last.Length)
            {
                var end = Math.Max(last.Start + last.Length, range.Start + range.Length);
                merged[^1] = new TextRange(last.Start, end - last.Start);
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    private sealed record Token(List<(string Text, int Penalty)> Variants);
}
