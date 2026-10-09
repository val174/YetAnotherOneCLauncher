namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>Обновление лаунчера из релизов GitHub.</summary>
public enum UpdateMode
{
    /// <summary>Не проверять (проверить можно кнопкой «Проверить обновления»).</summary>
    Disabled,

    /// <summary>Проверять и сообщать о новой версии.</summary>
    CheckOnly,

    /// <summary>Скачивать новую версию и заменять файл программы; применится после перезапуска.</summary>
    AutoUpdate,
}

/// <summary>Настройки обновления лаунчера.</summary>
public sealed class UpdateSettings
{
    /// <summary>Как часто проверять обновления, пока лаунчер открыт (и при запуске, если с прошлой проверки прошло больше).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    public UpdateMode Mode { get; set; } = UpdateMode.CheckOnly;

    /// <summary>Когда последний раз удалось проверить обновления.</summary>
    public DateTimeOffset? LastCheck { get; set; }

    /// <summary>Версия, о которой пользователь попросил больше не напоминать («Пропустить эту версию»).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Пора проверять: режим не «Не использовать» и с прошлой проверки прошли сутки.</summary>
    public bool IsCheckDue(DateTimeOffset now) =>
        Mode != UpdateMode.Disabled && (LastCheck is not { } last || now - last >= CheckInterval || last > now);
}
