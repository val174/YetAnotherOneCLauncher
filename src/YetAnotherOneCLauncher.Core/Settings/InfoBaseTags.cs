namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>Цвет метки: оттенок плашки в списке баз и в строке меток.</summary>
public enum TagColor
{
    Gray,
    Red,
    Amber,
    Green,
    Blue,
    Purple,
    Pink,
}

/// <summary>Метка — произвольное слово, которым помечаются базы для быстрого отбора.</summary>
public sealed record TagDefinition
{
    public string Name { get; init; } = string.Empty;

    public TagColor Color { get; init; }

    /// <summary>Показывать метку в строке списка баз (в свойствах и в фильтре она видна всегда).</summary>
    public bool ShowInRow { get; init; } = true;
}

/// <summary>Метки одной базы: хранятся в лаунчере, список баз не меняется.</summary>
public sealed record InfoBaseTagSet
{
    public InfoBaseRef InfoBase { get; init; } = new();

    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Имена меток: сравнение без учёта регистра, лишние пробелы убираются.</summary>
public static class TagNames
{
    /// <summary>Самая длинная метка.</summary>
    public const int MaxLength = 40;

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Пробелы по краям убираются, внутри — схлопываются; знак «#» в начале отбрасывается (так метки пишут в поиске).</summary>
    /// <returns><c>null</c> — имя пустое.</returns>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var text = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimStart('#').Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > MaxLength ? text[..MaxLength].TrimEnd() : text;
    }

    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Отбор по меткам. База подходит, если у неё есть все <paramref name="selected"/> (условие «И») и для каждого
    /// слова <paramref name="prefixes"/> из поиска («#прод») — метка, начинающаяся с него.
    /// </summary>
    public static bool Matches(IReadOnlyCollection<string> baseTags, IReadOnlyCollection<string> selected, IReadOnlyCollection<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(baseTags);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(prefixes);
        return selected.All(s => baseTags.Any(t => Same(t, s)))
               && prefixes.All(p => baseTags.Any(t => t.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                                                     || t.Replace(' ', '_').StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Слова поиска вида «#метка» — отбор по меткам, остальное — обычный поиск. Метку с пробелом пишут через «_»:
    /// «#клиент_А».
    /// </summary>
    public static (string Text, IReadOnlyList<string> TagPrefixes) ParseSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return (string.Empty, []);
        }

        var words = new List<string>();
        var prefixes = new List<string>();
        foreach (var word in search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && word[0] == '#')
            {
                prefixes.Add(word[1..]);
            }
            else if (word != "#")
            {
                words.Add(word);
            }
        }

        return (string.Join(' ', words), prefixes);
    }
}
