using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Exchange;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Окно «Загрузка списка баз»: что в файле, что изменится и как загружать.</summary>
public sealed partial class ImportListViewModel : ObservableObject
{
    private readonly ImportResult _addPreview;
    private readonly ImportResult _replacePreview;

    /// <param name="fileName">Имя файла.</param>
    /// <param name="file">Прочитанный файл.</param>
    /// <param name="addPreview">Итог режима «Добавить» (<see cref="InfoBaseListExchange.Preview"/>).</param>
    /// <param name="replacePreview">Итог режима «Заменить».</param>
    /// <param name="currentCount">Записей в личном списке сейчас.</param>
    public ImportListViewModel(string fileName, InfoBaseListFile file, ImportResult addPreview, ImportResult replacePreview, int currentCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        FileName = fileName;
        File = file;
        _addPreview = addPreview;
        _replacePreview = replacePreview;
        CurrentCount = currentCount;
        HasSettings = file.FolderParameters.Count > 0 || file.InfoBases.Any(b => b.Launcher is not null);
        ApplySettings = HasSettings;
    }

    public string FileName { get; }

    public InfoBaseListFile File { get; }

    public int CurrentCount { get; }

    /// <summary>«сохранён 13.10.2026 09:15, версия 0.3.0.1».</summary>
    public string SavedText
    {
        get
        {
            var parts = new List<string>();
            if (File.ExportedAt is { } at)
            {
                parts.Add("сохранён " + at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(File.AppVersion))
            {
                parts.Add("версия " + File.AppVersion);
            }

            return parts.Count == 0 ? string.Empty : "(" + string.Join(", ", parts) + ")";
        }
    }

    public string ContentText => $"В файле баз: {File.InfoBases.Count}, групп: {File.Folders.Count}.";

    /// <summary>Что произойдёт в выбранном режиме.</summary>
    public string OutcomeText => IsReplaceMode
        ? $"Будет загружено записей: {_replacePreview.Added}"
          + (_replacePreview.SameName > 0 ? $"; пропущено (название занято в общих списках): {_replacePreview.SameName}" : string.Empty)
          + $". Сейчас в личном списке записей: {CurrentCount} — они будут удалены."
        : $"Новых записей: {_addPreview.Added}; уже в списке: {_addPreview.Skipped}"
          + (_addPreview.SameName > 0 ? $"; название занято: {_addPreview.SameName}" : string.Empty) + ".";

    /// <summary>В файле есть настройки лаунчера (параметры, избранное и т. п.).</summary>
    public bool HasSettings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplaceMode), nameof(OutcomeText), nameof(Mode))]
    public partial bool IsAddMode { get; set; } = true;

    public bool IsReplaceMode
    {
        get => !IsAddMode;
        set => IsAddMode = !value;
    }

    public ImportMode Mode => IsAddMode ? ImportMode.Add : ImportMode.Replace;

    [ObservableProperty]
    public partial bool ApplySettings { get; set; }
}
