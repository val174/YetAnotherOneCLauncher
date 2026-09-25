using System.Collections.Frozen;

namespace YetAnotherOneCLauncher.Core.Search;

/// <summary>
/// Перевод текста, набранного не в той раскладке: ЙЦУКЕН ↔ QWERTY.
/// ",e[" — это «бух», набранное в английской раскладке.
/// </summary>
public static class KeyboardLayout
{
    // Одинаковые клавиши в двух раскладках (без Shift).
    private const string Latin = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string Cyrillic = "ёйцукенгшщзхъфывапролджэячсмитьбю.";

    private static readonly FrozenDictionary<char, char> LatinToCyrillic = Build(Latin, Cyrillic);
    private static readonly FrozenDictionary<char, char> CyrillicToLatin = Build(Cyrillic, Latin);

    /// <summary>Как выглядел бы текст, набранный в русской раскладке вместо английской.</summary>
    public static string ToCyrillic(string text) => Convert(text, LatinToCyrillic);

    /// <summary>Как выглядел бы текст, набранный в английской раскладке вместо русской.</summary>
    public static string ToLatin(string text) => Convert(text, CyrillicToLatin);

    private static string Convert(string text, FrozenDictionary<char, char> map) =>
        string.Create(text.Length, (text, map), static (span, state) =>
        {
            for (var i = 0; i < state.text.Length; i++)
            {
                var c = state.text[i];
                var lower = char.ToLowerInvariant(c);
                if (state.map.TryGetValue(lower, out var mapped))
                {
                    span[i] = char.IsUpper(c) ? char.ToUpperInvariant(mapped) : mapped;
                }
                else
                {
                    span[i] = c;
                }
            }
        });

    private static FrozenDictionary<char, char> Build(string from, string to)
    {
        // Точка есть в обеих раскладках на разных клавишах: латинская '.' — это «ю», русская '.' — это '/'.
        var map = new Dictionary<char, char>();
        for (var i = 0; i < from.Length; i++)
        {
            map[from[i]] = to[i];
        }

        return map.ToFrozenDictionary();
    }
}
