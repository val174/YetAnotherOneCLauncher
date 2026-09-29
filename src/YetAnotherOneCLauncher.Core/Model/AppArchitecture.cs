using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Model;

/// <summary>
/// Разрядность клиента для базы — ключ <c>AppArch</c> в <c>ibases.v8i</c>, как в свойствах базы штатного стартера.
/// </summary>
public enum AppArchitecture
{
    /// <summary>Ключа нет: разрядность по настройкам лаунчера.</summary>
    Auto,

    /// <summary><c>x86</c> — только 32-разрядная платформа.</summary>
    X86,

    /// <summary><c>x86_64</c> — только 64-разрядная платформа.</summary>
    X64,

    /// <summary><c>x86_prt</c> — предпочтительно 32-разрядная, иначе любая.</summary>
    PreferX86,

    /// <summary><c>x86_64_prt</c> — предпочтительно 64-разрядная, иначе любая.</summary>
    PreferX64,
}

public static class AppArchitectures
{
    /// <summary>Значение ключа <c>AppArch</c>; пусто или неизвестное значение — <see cref="AppArchitecture.Auto"/>.</summary>
    public static AppArchitecture Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "x86" => AppArchitecture.X86,
        "x86_64" => AppArchitecture.X64,
        "x86_prt" => AppArchitecture.PreferX86,
        "x86_64_prt" => AppArchitecture.PreferX64,
        _ => AppArchitecture.Auto,
    };

    /// <summary>Значение для ключа <c>AppArch</c>; <c>null</c> — ключ не писать.</summary>
    public static string? ToV8iValue(AppArchitecture architecture) => architecture switch
    {
        AppArchitecture.X86 => "x86",
        AppArchitecture.X64 => "x86_64",
        AppArchitecture.PreferX86 => "x86_prt",
        AppArchitecture.PreferX64 => "x86_64_prt",
        _ => null,
    };

    /// <summary>
    /// Какую разрядность искать и обязательна ли она. Для <see cref="AppArchitecture.Auto"/> — <paramref name="launcherDefault"/>
    /// из настроек лаунчера, необязательно.
    /// </summary>
    public static (PlatformArchitecture Architecture, bool Required) Resolve(AppArchitecture architecture, PlatformArchitecture launcherDefault) =>
        architecture switch
        {
            AppArchitecture.X86 => (PlatformArchitecture.X86, true),
            AppArchitecture.X64 => (PlatformArchitecture.X64, true),
            AppArchitecture.PreferX86 => (PlatformArchitecture.X86, false),
            AppArchitecture.PreferX64 => (PlatformArchitecture.X64, false),
            _ => (launcherDefault, false),
        };

    /// <summary>Для сообщений: «32-разрядная», «64-разрядная».</summary>
    public static string Describe(PlatformArchitecture architecture) => architecture switch
    {
        PlatformArchitecture.X86 => "32-разрядная",
        PlatformArchitecture.X64 => "64-разрядная",
        _ => architecture.ToString(),
    };
}
