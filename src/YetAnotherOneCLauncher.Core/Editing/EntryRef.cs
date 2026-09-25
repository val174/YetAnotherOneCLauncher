using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Editing;

/// <summary>
/// Ссылка на запись личного списка, по которой она находится в заново прочитанном файле.
/// <see cref="ExpectedText"/> — текст секции на момент загрузки: если в файле он другой,
/// запись изменили в другой программе, и правка не применяется (конфликт).
/// </summary>
public abstract record EntryRef(string Name, string? ExpectedText)
{
    public static BaseEntryRef Of(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        return new BaseEntryRef(infoBase.Id?.Trim(), infoBase.ConnectionKey, infoBase.Name, V8iSections.ToText(infoBase.Section));
    }

    public static FolderEntryRef Of(InfoBaseFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new FolderEntryRef(folder.FullPath, V8iSections.ToText(folder.Section));
    }

    /// <summary>Папка, которой нет отдельной записью — она есть только в путях <c>Folder</c> у баз.</summary>
    public static FolderEntryRef OfImpliedFolder(string fullPath) => new(FolderPaths.Normalize(fullPath), null);
}

public sealed record BaseEntryRef(string? Id, string ConnectionKey, string Name, string? ExpectedText)
    : EntryRef(Name, ExpectedText);

public sealed record FolderEntryRef(string FullPath, string? ExpectedText)
    : EntryRef(FolderPaths.Split(FullPath).LastOrDefault() ?? string.Empty, ExpectedText);

public enum ListEditErrorKind
{
    /// <summary>Данные некорректны или действие невозможно (папка с таким именем уже есть и т. п.).</summary>
    Invalid,

    /// <summary>Запись не найдена: её удалили в другой программе.</summary>
    NotFound,

    /// <summary>Запись изменили в другой программе после загрузки списка.</summary>
    Conflict,
}

/// <summary>Правка личного списка не выполнена; файл не изменён.</summary>
public sealed class ListEditException : Exception
{
    public ListEditException()
    {
    }

    public ListEditException(string message)
        : base(message)
    {
    }

    public ListEditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ListEditException(ListEditErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public ListEditErrorKind Kind { get; }

    /// <summary>После такой ошибки список нужно перечитать.</summary>
    public bool RequiresReload => Kind is ListEditErrorKind.NotFound or ListEditErrorKind.Conflict;
}
