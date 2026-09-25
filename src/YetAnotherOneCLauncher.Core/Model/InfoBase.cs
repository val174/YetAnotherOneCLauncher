using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Клиент для запуска (ключ <c>App</c>).</summary>
public enum ClientApp
{
    Auto,
    ThinClient,
    ThickClient,
    WebClient,
}

/// <summary>Информационная база из списка. Свойства читаются из исходной секции.</summary>
public sealed class InfoBase : CatalogEntry
{
    public InfoBase(V8iSection section, ListSource source)
        : base(section, source)
    {
        Connection = ConnectionString.Parse(section.Get(V8iKeys.Connect));
    }

    /// <summary>Строка подключения, разобранная при создании объекта (снимок на момент загрузки списка).</summary>
    public ConnectionString Connection { get; }

    public ConnectionKind ConnectionKind => Connection.Kind;

    /// <summary>Клиент из ключа <c>App</c>; неизвестное или пустое значение — <see cref="ClientApp.Auto"/>.</summary>
    public ClientApp App => ParseClientApp(Section.Get(V8iKeys.App));

    /// <summary>Исходное значение <c>App</c> как есть.</summary>
    public string? AppRaw => NullIfEmpty(Section.Get(V8iKeys.App));

    /// <summary>Маска версии платформы: "8.3", "8.3.24", "8.3.24.1548" или <c>null</c>.</summary>
    public string? Version => NullIfEmpty(Section.Get(V8iKeys.Version));

    /// <summary>Аутентификация Windows (ключ <c>WA</c>); <c>null</c>, если не указано.</summary>
    public bool? WindowsAuthentication => Section.Get(V8iKeys.WindowsAuthentication)?.Trim() switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };

    public string? DefaultApp => NullIfEmpty(Section.Get(V8iKeys.DefaultApp));

    public string? ClientConnectionSpeed => NullIfEmpty(Section.Get(V8iKeys.ClientConnectionSpeed));

    public string? AdditionalParameters => NullIfEmpty(Section.Get(V8iKeys.AdditionalParameters));

    /// <summary>
    /// Ключ для поиска дубликатов и привязки избранного/истории:
    /// ID, а если его нет — нормализованная строка подключения.
    /// </summary>
    public string IdentityKey => Id is { } id
        ? "id:" + id.Trim().ToLowerInvariant()
        : ConnectionKey;

    /// <summary>Ключ по нормализованной строке подключения — запасной, когда <c>ID</c> нет или он изменился.</summary>
    public string ConnectionKey => "conn:" + Connection.ToNormalizedKey();

    public override string ToString() => $"{Name} ({Connection.ToDisplayString()})";

    private static ClientApp ParseClientApp(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "thinclient" => ClientApp.ThinClient,
        "thickclient" => ClientApp.ThickClient,
        "webclient" => ClientApp.WebClient,
        _ => ClientApp.Auto,
    };
}
