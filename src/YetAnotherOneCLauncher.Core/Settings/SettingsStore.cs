using System.Text.Json;
using System.Text.Json.Serialization;
using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.IO;

namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>Результат чтения настроек.</summary>
/// <param name="Settings">Прочитанные настройки или значения по умолчанию.</param>
/// <param name="Warning">Что пошло не так, если файл не удалось прочитать.</param>
public sealed record SettingsLoadResult(LauncherSettings Settings, string? Warning);

/// <summary>
/// Чтение и запись <c>settings.json</c>. Файл пишется атомарно. Повреждённый файл не мешает запуску:
/// он переименовывается в <c>settings.json.bad</c>, а лаунчер стартует с настройками по умолчанию.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(new LauncherSettings(), null);
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var settings = await JsonSerializer
                .DeserializeAsync(stream, SettingsJsonContext.Default.LauncherSettings, cancellationToken)
                .ConfigureAwait(false);
            return new SettingsLoadResult(Normalize(settings ?? new LauncherSettings()), null);
        }
        catch (JsonException ex)
        {
            var badPath = FilePath + ".bad";
            TryMove(FilePath, badPath);
            return new SettingsLoadResult(
                new LauncherSettings(),
                $"Файл настроек повреждён и сохранён как {badPath}; используются настройки по умолчанию. {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(new LauncherSettings(), $"Не удалось прочитать настройки: {ex.Message}");
        }
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.LauncherSettings);
        return AtomicFileWriter.WriteAllBytesAsync(FilePath, bytes, backupPath: null, cancellationToken);
    }

    /// <summary>JSON может содержать <c>null</c> вместо списков — заменяем на пустые.</summary>
    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        settings.Favorites ??= [];
        settings.History ??= [];
        settings.PlatformOverrides ??= [];
        settings.InfoBaseProfiles ??= [];
        settings.FolderProfiles ??= [];
        settings.ParameterTemplates ??= [];
        settings.Ui ??= new UiSettings();
        settings.Ui.CollapsedFolders ??= [];
        settings.Ui.HotKeys ??= [];
        if (settings.Ui.IconStyle is not (IconStyle.Flat or IconStyle.Plate))
        {
            // Контурный и двухтоновый стили убраны — они, как и неизвестное значение, читаются как «Стиль 1».
            settings.Ui.IconStyle = IconStyle.Flat;
        }

        if (!Enum.IsDefined(settings.Ui.SortMode))
        {
            settings.Ui.SortMode = CatalogSortMode.Name;
        }

        if (!Enum.IsDefined(settings.Ui.RowStripes))
        {
            settings.Ui.RowStripes = RowStripes.Moderate;
        }

        settings.Launch ??= new LaunchSettings();
        settings.Cache ??= new CacheSettings();
        settings.Network ??= new NetworkSettings();
        settings.Updates ??= new UpdateSettings();
        if (!Enum.IsDefined(settings.Updates.Mode))
        {
            settings.Updates.Mode = UpdateMode.CheckOnly;
        }

        return settings;
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не удалось отложить повреждённый файл — он будет перезаписан при следующем сохранении.
        }
    }
}

// Генератор кода System.Text.Json: без отражения, совместимо с AOT и обрезкой.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(LauncherSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
