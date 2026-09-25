using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherOneCLauncher.Core.Launching;

/// <summary>
/// Правила командной строки 1С: значение с пробелами или кавычками берётся в кавычки,
/// кавычка внутри удваивается. Обратная косая черта обычного символа не экранирует,
/// поэтому стандартное экранирование Windows (<c>\"</c>) здесь не подходит.
/// </summary>
public static partial class OneCCommandLine
{
    private const string PasswordMask = "***";

    /// <summary>Берёт аргумент в кавычки, если нужно.</summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>Собирает строку аргументов; <paramref name="rawTail"/> дописывается как есть.</summary>
    public static string Format(IEnumerable<string> arguments, string? rawTail = null)
    {
        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Quote(argument));
        }

        if (!string.IsNullOrWhiteSpace(rawTail))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(rawTail.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Разбивает фрагмент командной строки (например, <c>AdditionalParameters</c>) на аргументы:
    /// пробелы разделяют, кавычки группируют, <c>""</c> внутри кавычек — литеральная кавычка.
    /// </summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>Скрывает пароль в списке аргументов: значение после <c>/P</c>.</summary>
    public static IReadOnlyList<string> MaskPasswords(IReadOnlyList<string> arguments)
    {
        var result = new List<string>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            result.Add(i > 0 && IsPasswordSwitch(arguments[i - 1]) ? PasswordMask : arguments[i]);
        }

        return result;
    }

    /// <summary>Скрывает пароль в сыром фрагменте: <c>/P secret</c> и <c>/P "secret"</c>.</summary>
    public static string? MaskPasswords(string? rawText) =>
        rawText is null ? null : RawPasswordRegex().Replace(rawText, "$1" + PasswordMask);

    private static bool IsPasswordSwitch(string argument) =>
        string.Equals(argument, "/P", StringComparison.OrdinalIgnoreCase);

    // "/P" с пробелом или кавычкой после: так не задеваются другие ключи, начинающиеся на /P.
    [GeneratedRegex("""((?:^|\s)/P(?:\s+|(?=")))(?:"(?:[^"]|"")*"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RawPasswordRegex();
}
