namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Работа с путями папок из ключа <c>Folder</c>: "/" — корень, "/A/B" — вложенная папка.</summary>
public static class FolderPaths
{
    public const string Root = "/";

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Root;
        }

        var segments = Split(path);
        return segments.Length == 0 ? Root : "/" + string.Join('/', segments);
    }

    public static string Combine(string parent, string name)
    {
        var normalizedParent = Normalize(parent);
        var trimmedName = name.Trim().Trim('/');
        if (trimmedName.Length == 0)
        {
            return normalizedParent;
        }

        return normalizedParent == Root ? "/" + trimmedName : normalizedParent + "/" + trimmedName;
    }

    public static string[] Split(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
