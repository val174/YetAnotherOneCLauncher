namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Разрядность и процессорная архитектура сборки платформы.</summary>
public enum PlatformArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64,

    /// <summary>Эльбрус (e2k) — встречается в Linux-сборках платформы.</summary>
    E2k,
}

/// <summary>Исполняемый файл платформы, нужный для запуска.</summary>
public enum PlatformExecutable
{
    /// <summary><c>1cv8</c>: толстый клиент и Конфигуратор.</summary>
    ThickClient,

    /// <summary><c>1cv8c</c>: тонкий клиент, только режим Предприятия.</summary>
    ThinClient,
}

/// <summary>Установленная платформа: версия, разрядность и пути к исполняемым файлам.</summary>
/// <param name="Version">Версия из имени каталога.</param>
/// <param name="Architecture">Определяется по заголовку исполняемого файла.</param>
/// <param name="BinDirectory">Каталог с исполняемыми файлами.</param>
/// <param name="ThickClientPath"><c>1cv8</c>; <c>null</c>, если не установлен.</param>
/// <param name="ThinClientPath"><c>1cv8c</c>; <c>null</c>, если не установлен.</param>
public sealed record PlatformInstallation(
    PlatformVersion Version,
    PlatformArchitecture Architecture,
    string BinDirectory,
    string? ThickClientPath,
    string? ThinClientPath)
{
    public string? GetExecutablePath(PlatformExecutable executable) => executable switch
    {
        PlatformExecutable.ThickClient => ThickClientPath,
        PlatformExecutable.ThinClient => ThinClientPath,
        _ => null,
    };

    public bool Has(PlatformExecutable executable) => GetExecutablePath(executable) is not null;

    public override string ToString() => $"{Version} {ArchitectureName(Architecture)}";

    public static string ArchitectureName(PlatformArchitecture architecture) => architecture switch
    {
        PlatformArchitecture.X86 => "x86",
        PlatformArchitecture.X64 => "x64",
        PlatformArchitecture.Arm64 => "arm64",
        PlatformArchitecture.E2k => "e2k",
        _ => "?",
    };
}
