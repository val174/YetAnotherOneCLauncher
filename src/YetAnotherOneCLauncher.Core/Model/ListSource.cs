namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Откуда взят список баз.</summary>
public enum ListSourceKind
{
    /// <summary>Личный список пользователя (ibases.v8i). Единственный, в который можно писать.</summary>
    Personal,

    /// <summary>Общий список из <c>CommonInfoBases</c> в 1cestart.cfg.</summary>
    Common,

    /// <summary>Список с веб-сервиса из <c>InternetService</c> (<c>WebCommonInfoBases</c>).</summary>
    InternetService,
}

/// <summary>Источник записи: тип и путь (или URL).</summary>
public sealed record ListSource(ListSourceKind Kind, string Location)
{
    /// <summary>
    /// Когда сохранена копия, из которой прочитан список; <c>null</c> — список прочитан из источника сейчас.
    /// Копия используется, когда общий список или веб-сервис недоступен.
    /// </summary>
    public DateTimeOffset? CachedAt { get; init; }

    public bool IsFromCache => CachedAt is not null;

    /// <summary>Записи из общих списков пользователь не может ни изменить, ни удалить.</summary>
    public bool IsReadOnly => Kind != ListSourceKind.Personal;

    public override string ToString() => IsFromCache
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Kind}: {Location} (копия на {CachedAt:yyyy-MM-dd HH:mm})")
        : $"{Kind}: {Location}";
}
