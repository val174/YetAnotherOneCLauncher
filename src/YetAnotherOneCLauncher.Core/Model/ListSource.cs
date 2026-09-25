namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>Откуда взят список баз.</summary>
public enum ListSourceKind
{
    /// <summary>Личный список пользователя (ibases.v8i). Единственный, в который можно писать.</summary>
    Personal,

    /// <summary>Общий список из <c>CommonInfoBases</c> в 1cestart.cfg.</summary>
    Common,

    /// <summary>Список с веб-сервиса из <c>InternetService</c> (пока не поддерживается).</summary>
    InternetService,
}

/// <summary>Источник записи: тип и путь (или URL).</summary>
public sealed record ListSource(ListSourceKind Kind, string Location)
{
    /// <summary>Записи из общих списков пользователь не может ни изменить, ни удалить.</summary>
    public bool IsReadOnly => Kind != ListSourceKind.Personal;

    public override string ToString() => $"{Kind}: {Location}";
}
