using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Launching;

/// <summary>Режим запуска.</summary>
public enum LaunchMode
{
    /// <summary>Пользовательский режим (<c>ENTERPRISE</c>).</summary>
    Enterprise,

    /// <summary>Конфигуратор (<c>DESIGNER</c>); всегда через <c>1cv8</c>.</summary>
    Designer,
}

/// <summary>Что и как запустить.</summary>
public sealed record LaunchRequest(InfoBase InfoBase, LaunchMode Mode)
{
    /// <summary>Клиент вместо указанного в базе (ключ <c>App</c>).</summary>
    public ClientApp? ClientOverride { get; init; }

    /// <summary>Версия платформы, выбранная пользователем в лаунчере; важнее ключа <c>Version</c> базы.</summary>
    public string? PlatformVersionOverride { get; init; }

    /// <summary>Пользователь 1С (<c>/N</c>).</summary>
    public string? UserName { get; init; }

    /// <summary>Пароль (<c>/P</c>). В логи и на экран попадает только замаскированным.</summary>
    public string? Password { get; init; }

    /// <summary>Дополнительные аргументы, добавляются после <c>AdditionalParameters</c> базы.</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    // Сгенерированный ToString записи вывел бы пароль.
    public override string ToString() => $"{InfoBase.Name} ({Mode})";
}

/// <summary>Настройки выбора клиента и платформы.</summary>
public sealed record LaunchOptions
{
    /// <summary>Разрядность, которой отдаётся предпочтение при одинаковых версиях.</summary>
    public PlatformArchitecture PreferredArchitecture { get; init; } = PlatformArchitecture.X64;

    /// <summary>
    /// Клиент для файловых баз при <c>App=Auto</c>: по умолчанию тонкий, как и для остальных.
    /// Толстый может понадобиться для старых конфигураций на обычных формах.
    /// </summary>
    public bool UseThickClientForFileBasesByDefault { get; init; }
}

/// <summary>Готовая команда запуска.</summary>
/// <param name="ExecutablePath">Исполняемый файл платформы.</param>
/// <param name="Arguments">Аргументы, сформированные лаунчером.</param>
/// <param name="RawArguments">
/// <c>AdditionalParameters</c> базы. В Windows дописываются в командную строку как есть (как у штатного стартера),
/// в Linux разбиваются на аргументы по правилам 1С.
/// </param>
/// <param name="Platform">Платформа, на которой выполняется запуск.</param>
public sealed record LaunchCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? RawArguments,
    PlatformInstallation Platform)
{
    /// <summary>Строка аргументов для Windows по правилам 1С.</summary>
    public string ToWindowsArguments() => OneCCommandLine.Format(Arguments, RawArguments);

    /// <summary>Аргументы для Linux: без оболочки, по одному.</summary>
    public IReadOnlyList<string> ToArgumentVector() => [.. Arguments, .. OneCCommandLine.Split(RawArguments)];

    /// <summary>Команда для лога и интерфейса: пароль скрыт.</summary>
    public string ToDisplayString() =>
        OneCCommandLine.Quote(ExecutablePath) + " "
        + OneCCommandLine.Format(OneCCommandLine.MaskPasswords(Arguments), OneCCommandLine.MaskPasswords(RawArguments));

    public override string ToString() => ToDisplayString();
}
