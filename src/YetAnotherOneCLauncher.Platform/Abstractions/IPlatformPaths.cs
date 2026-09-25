using YetAnotherOneCLauncher.Core.Catalog;

namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>Расположение файлов 1С и самого приложения на конкретной ОС.</summary>
public interface IPlatformPaths
{
    /// <summary>Личный список баз пользователя (ibases.v8i).</summary>
    string PersonalInfoBaseListPath { get; }

    /// <summary>Файлы 1cestart.cfg в порядке применения: сначала общий для компьютера, затем пользовательский.</summary>
    IReadOnlyList<string> StarterConfigPaths { get; }

    /// <summary>Каталоги, в которых по умолчанию ищутся установленные платформы 1С.</summary>
    IReadOnlyList<string> DefaultPlatformInstallRoots { get; }

    /// <summary>Корневые каталоги кэша баз (внутри — подкаталоги с именами по ID базы).</summary>
    IReadOnlyList<string> InfoBaseCacheRoots { get; }

    /// <summary>Каталог настроек самого лаунчера (избранное, история, параметры запуска).</summary>
    string AppDataDirectory { get; }
}

public static class PlatformPathsExtensions
{
    public static CatalogSources ToCatalogSources(this IPlatformPaths paths) =>
        new(paths.PersonalInfoBaseListPath, paths.StarterConfigPaths);
}
