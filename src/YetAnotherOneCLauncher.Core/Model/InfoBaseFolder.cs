using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Папка в дереве списка: секция .v8i без <c>Connect</c>.</summary>
public sealed class InfoBaseFolder : CatalogEntry
{
    public InfoBaseFolder(V8iSection section, ListSource source)
        : base(section, source)
    {
    }

    /// <summary>Полный путь самой папки: родитель + имя.</summary>
    public string FullPath => FolderPaths.Combine(FolderPath, Name);

    public override string ToString() => FullPath;
}
