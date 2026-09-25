using System.Globalization;

namespace YetAnotherOneCLauncher.Core.Cache;

/// <summary>Вид каталога кэша.</summary>
public enum CacheLocation
{
    /// <summary>
    /// Кэш конфигурации и метаданных (Windows: <c>%LOCALAPPDATA%\1C\1cv8\&lt;ID&gt;</c>, Linux: <c>~/.1cv8/1C/1cv8/&lt;ID&gt;</c>).
    /// Удалять безопасно: 1С заново заполнит его при следующем запуске.
    /// </summary>
    Local,

    /// <summary>
    /// Локальные настройки пользователя для базы (Windows: <c>%APPDATA%\1C\1cv8\&lt;ID&gt;</c>).
    /// Удаление сбрасывает то, что хранится на компьютере, а не в базе, — поэтому только по отдельному выбору.
    /// </summary>
    Roaming,
}

/// <summary>Корневой каталог, внутри которого 1С создаёт подкаталоги с именами по <c>ID</c> баз.</summary>
public sealed record CacheRoot(string Path, CacheLocation Location);

/// <summary>Каталог кэша одной базы.</summary>
/// <param name="Id">GUID базы в нижнем регистре — имя каталога.</param>
/// <param name="Path">Полный путь.</param>
/// <param name="Location">Вид кэша.</param>
/// <param name="SizeBytes">Размер всех файлов внутри.</param>
/// <param name="LastWriteTime">Последнее изменение каталога.</param>
public sealed record CacheDirectory(string Id, string Path, CacheLocation Location, long SizeBytes, DateTimeOffset LastWriteTime);

/// <summary>Размеры для показа: «1,2 ГБ».</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    /// <summary>В текущей культуре.</summary>
    public static string Format(long bytes) => Format(bytes, CultureInfo.CurrentCulture);

    internal static string Format(long bytes, IFormatProvider provider)
    {
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, provider) + " " + Units[unit];
    }
}
