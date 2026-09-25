using YetAnotherOneCLauncher.Core.Model;

namespace YetAnotherOneCLauncher.Core.Parsing;

/// <summary>Общие правила для секций .v8i.</summary>
public static class V8iSections
{
    /// <summary>Секция без непустого <c>Connect</c> — папка.</summary>
    public static bool IsFolder(V8iSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return string.IsNullOrWhiteSpace(section.Get(V8iKeys.Connect));
    }

    /// <summary>Текст секции как в файле: заголовок и строки через перевод строки. Для сравнения версий секции.</summary>
    public static string ToText(V8iSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return string.Join('\n', section.Lines.Select(l => l.ToString()).Prepend(section.HeaderText));
    }
}
