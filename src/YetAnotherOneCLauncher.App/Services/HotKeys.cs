using Avalonia.Input;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Команды главного окна, у которых есть сочетание клавиш и его можно переопределить.</summary>
public enum HotKeyCommand
{
    LaunchEnterprise,
    LaunchDesigner,
    LaunchWithParameters,
    Reload,
    FocusSearch,
    ClearSearch,
    ToggleFavorite,
    AddBase,
    AddFolder,
    Edit,
    Delete,
    MoveUp,
    MoveDown,
    ShowEdtProjects,
}

/// <summary>Где действует сочетание: во всём окне или когда фокус в дереве или списке баз.</summary>
public enum HotKeyScope
{
    Window,
    List,
}

/// <param name="Command">Команда.</param>
/// <param name="Title">Название в окне настроек.</param>
/// <param name="Default">Сочетание по умолчанию.</param>
/// <param name="Scope">Где действует.</param>
public sealed record HotKeyDefinition(HotKeyCommand Command, string Title, KeyGesture Default, HotKeyScope Scope);

/// <summary>Сочетания клавиш: по умолчанию и с переопределениями пользователя. Неизменяемый — при правке создаётся новый.</summary>
public sealed class HotKeyMap
{
    /// <summary>
    /// Команды в порядке окна настроек. Помимо них всегда работают (и не переопределяются): Enter и двойной щелчок —
    /// запуск, Esc — очистить поиск, Ins в списке — новая база.
    /// </summary>
    public static IReadOnlyList<HotKeyDefinition> Definitions { get; } =
    [
        new(HotKeyCommand.LaunchEnterprise, "1С: Предприятие", new KeyGesture(Key.F3), HotKeyScope.Window),
        new(HotKeyCommand.LaunchDesigner, "Конфигуратор", new KeyGesture(Key.F4), HotKeyScope.Window),
        new(HotKeyCommand.LaunchWithParameters, "Запустить с параметрами", new KeyGesture(Key.F6), HotKeyScope.Window),
        new(HotKeyCommand.Reload, "Обновить списки и платформы", new KeyGesture(Key.F5), HotKeyScope.Window),
        new(HotKeyCommand.FocusSearch, "Перейти к поиску", new KeyGesture(Key.F, KeyModifiers.Control), HotKeyScope.Window),
        new(HotKeyCommand.ClearSearch, "Очистить поиск", new KeyGesture(Key.Q, KeyModifiers.Control), HotKeyScope.Window),
        new(HotKeyCommand.ToggleFavorite, "Избранное", new KeyGesture(Key.D, KeyModifiers.Control), HotKeyScope.Window),
        new(HotKeyCommand.AddBase, "Новая база", new KeyGesture(Key.N, KeyModifiers.Control), HotKeyScope.Window),
        new(HotKeyCommand.AddFolder, "Новая папка", new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), HotKeyScope.Window),
        new(HotKeyCommand.Edit, "Изменить базу, переименовать папку", new KeyGesture(Key.F2), HotKeyScope.List),
        new(HotKeyCommand.Delete, "Удалить из списка", new KeyGesture(Key.Delete), HotKeyScope.List),
        new(HotKeyCommand.MoveUp, "Выше в папке", new KeyGesture(Key.Up, KeyModifiers.Alt), HotKeyScope.List),
        new(HotKeyCommand.MoveDown, "Ниже в папке", new KeyGesture(Key.Down, KeyModifiers.Alt), HotKeyScope.List),
        new(HotKeyCommand.ShowEdtProjects, "Проекты 1C:EDT (режим списка; повторно — все базы)", new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift), HotKeyScope.Window),
    ];

    private readonly Dictionary<HotKeyCommand, KeyGesture?> _gestures;

    private HotKeyMap(Dictionary<HotKeyCommand, KeyGesture?> gestures)
    {
        _gestures = gestures;
    }

    public static HotKeyMap Default { get; } = new(Definitions.ToDictionary(d => d.Command, d => (KeyGesture?)d.Default));

    public KeyGesture? this[HotKeyCommand command] => _gestures.GetValueOrDefault(command);

    // Для привязок в разметке (подписи сочетаний в меню).
    public KeyGesture? LaunchEnterprise => this[HotKeyCommand.LaunchEnterprise];

    public KeyGesture? LaunchDesigner => this[HotKeyCommand.LaunchDesigner];

    public KeyGesture? LaunchWithParameters => this[HotKeyCommand.LaunchWithParameters];

    public KeyGesture? ToggleFavorite => this[HotKeyCommand.ToggleFavorite];

    public KeyGesture? AddBase => this[HotKeyCommand.AddBase];

    public KeyGesture? AddFolder => this[HotKeyCommand.AddFolder];

    public KeyGesture? Edit => this[HotKeyCommand.Edit];

    public KeyGesture? Delete => this[HotKeyCommand.Delete];

    public KeyGesture? MoveUp => this[HotKeyCommand.MoveUp];

    public KeyGesture? MoveDown => this[HotKeyCommand.MoveDown];

    // Для подсказок: « (F5)» или пусто, если сочетание снято.
    public string ReloadSuffix => Suffix(HotKeyCommand.Reload);

    public string FocusSearchSuffix => Suffix(HotKeyCommand.FocusSearch);

    public string LaunchWithParametersSuffix => Suffix(HotKeyCommand.LaunchWithParameters);

    public string ToggleFavoriteSuffix => Suffix(HotKeyCommand.ToggleFavorite);

    public string DeleteSuffix => Suffix(HotKeyCommand.Delete);

    public string EditSuffix => Suffix(HotKeyCommand.Edit);

    public string ShowEdtProjectsSuffix => Suffix(HotKeyCommand.ShowEdtProjects);

    /// <summary>Сочетания из настроек; нераспознанные записи пропускаются (работает сочетание по умолчанию).</summary>
    public static HotKeyMap FromSettings(IReadOnlyDictionary<string, string>? overrides)
    {
        var gestures = Definitions.ToDictionary(d => d.Command, d => (KeyGesture?)d.Default);
        foreach (var (name, text) in overrides ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse<HotKeyCommand>(name, out var command) || !gestures.ContainsKey(command))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                gestures[command] = null;
            }
            else if (TryParse(text, out var gesture))
            {
                gestures[command] = gesture;
            }
        }

        return new HotKeyMap(gestures);
    }

    /// <summary>Новая карта с изменённым сочетанием команды (<c>null</c> — снять).</summary>
    public HotKeyMap With(HotKeyCommand command, KeyGesture? gesture) =>
        new(new Dictionary<HotKeyCommand, KeyGesture?>(_gestures) { [command] = gesture });

    /// <summary>Для настроек: только отличия от сочетаний по умолчанию.</summary>
    public Dictionary<string, string> ToOverrides() =>
        Definitions
            .Where(d => !SameGesture(this[d.Command], d.Default))
            .ToDictionary(d => d.Command.ToString(), d => this[d.Command]?.ToString() ?? string.Empty);

    public bool SameAs(HotKeyMap other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Definitions.All(d => SameGesture(this[d.Command], other[d.Command]));
    }

    /// <summary>
    /// Какая команда назначена нажатию в этой области. Буквы сравниваются и по физической клавише —
    /// чтобы Ctrl+Q срабатывало и в русской раскладке (Ctrl+Й).
    /// </summary>
    public HotKeyCommand? Match(Key key, KeyModifiers modifiers, PhysicalKey physicalKey, HotKeyScope scope)
    {
        var qwerty = physicalKey == PhysicalKey.None ? Key.None : physicalKey.ToQwertyKey();
        foreach (var definition in Definitions)
        {
            if (definition.Scope == scope
                && this[definition.Command] is { } gesture
                && gesture.KeyModifiers == modifiers
                && (gesture.Key == key || (qwerty != Key.None && gesture.Key == qwerty)))
            {
                return definition.Command;
            }
        }

        return null;
    }

    /// <summary>Сочетание для окна настроек и подсказок: «Ctrl+Shift+N», «Alt+↑», «Del»; снятое — «нет».</summary>
    public static string Format(KeyGesture? gesture) =>
        gesture is null
            ? "нет"
            : gesture.ToString()
                .Replace("PageUp", "PgUp", StringComparison.Ordinal).Replace("PageDown", "PgDn", StringComparison.Ordinal)
                .Replace("Up", "↑", StringComparison.Ordinal).Replace("Down", "↓", StringComparison.Ordinal)
                .Replace("Delete", "Del", StringComparison.Ordinal).Replace("Insert", "Ins", StringComparison.Ordinal);

    /// <summary>
    /// Почему сочетание нельзя назначить команде; <c>null</c> — можно. Сочетание не должно мешать набору текста
    /// в поиске: без Ctrl или Alt допускаются только F1–F24, а Del — только для команд списка.
    /// </summary>
    public static string? Validate(KeyGesture gesture, HotKeyScope scope)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var key = gesture.Key;
        var modifiers = gesture.KeyModifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return "Нужна клавиша, а не только Ctrl, Alt или Shift.";
        }

        if (modifiers == KeyModifiers.Alt && key == Key.F4)
        {
            return "Alt+F4 закрывает окно.";
        }

        if (modifiers == KeyModifiers.None && key is Key.Enter or Key.Escape or Key.Insert or Key.Tab)
        {
            return "Эта клавиша занята: Enter — запуск, Esc — очистить поиск, Ins — новая база, Tab — переход между полями.";
        }

        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (isFunctionKey || modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt))
        {
            return null;
        }

        if (key == Key.Delete && scope == HotKeyScope.List)
        {
            return null;
        }

        return "Без Ctrl или Alt можно назначить только F1–F24 (и Del — командам списка): иначе клавиша мешает набору в поиске.";
    }

    public static bool TryParse(string text, out KeyGesture gesture)
    {
        try
        {
            gesture = KeyGesture.Parse(text);
            return gesture.Key != Key.None;
        }
        catch (ArgumentException)
        {
            gesture = null!;
            return false;
        }
    }

    private static bool SameGesture(KeyGesture? a, KeyGesture? b) =>
        a is null ? b is null : b is not null && a.Key == b.Key && a.KeyModifiers == b.KeyModifiers;

    private string Suffix(HotKeyCommand command) => this[command] is { } gesture ? $" ({Format(gesture)})" : string.Empty;
}
