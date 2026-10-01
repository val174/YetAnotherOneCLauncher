using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Cache;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка окна «Кэш баз»: кэш одной базы или кэш без хозяина.</summary>
public sealed partial class CacheRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public CacheRowViewModel(CacheOwner owner, Action selectionChanged)
    {
        Owner = owner;
        _selectionChanged = selectionChanged;
    }

    public CacheOwner Owner { get; }

    public string Name => Owner.InfoBase?.Name ?? "Нет в списках баз";

    public string Details => Owner.InfoBase is { } infoBase
        ? infoBase.FolderPath == Core.Model.FolderPaths.Root
            ? infoBase.Connection.ToDisplayString()
            : $"{infoBase.FolderPath}  ·  {infoBase.Connection.ToDisplayString()}"
        : Owner.Id;

    public bool IsOrphan => Owner.IsOrphan;

    public string LocalText => Owner.LocalBytes > 0 ? ByteSize.Format(Owner.LocalBytes) : "—";

    public string RoamingText => Owner.RoamingBytes > 0 ? ByteSize.Format(Owner.RoamingBytes) : "—";

    public string LastWriteText => Owner.LastWriteTime.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

/// <summary>Порядок строк окна «Кэш баз».</summary>
public enum CacheSortColumn
{
    Size,
    Name,
}

/// <summary>Окно «Кэш баз»: размеры по базам, кэш без хозяина, массовая очистка, сортировка по имени и размеру.</summary>
public sealed partial class CacheManagerViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<CacheDirectory>, Task<CacheReport?>> _clean;
    private readonly Func<Task<CacheReport>> _rescan;

    /// <param name="report">Текущий отчёт.</param>
    /// <param name="includeRoaming">Удалять и Roaming.</param>
    /// <param name="hasRoaming">Есть ли на этой ОС отдельный Roaming (Windows).</param>
    /// <param name="clean">Очистка с подтверждением; возвращает новый отчёт или <c>null</c>, если отменили.</param>
    /// <param name="rescan">Пересчитать размеры.</param>
    public CacheManagerViewModel(
        CacheReport report,
        bool includeRoaming,
        bool hasRoaming,
        Func<IReadOnlyList<CacheDirectory>, Task<CacheReport?>> clean,
        Func<Task<CacheReport>> rescan)
    {
        _clean = clean;
        _rescan = rescan;
        HasRoaming = hasRoaming;
        IncludeRoaming = includeRoaming && hasRoaming;
        Show(report);
    }

    public ObservableCollection<CacheRowViewModel> Rows { get; } = [];

    public bool HasRoaming { get; }

    /// <summary>Порядок строк: по размеру (кэш и настройки вместе) или по имени базы.</summary>
    public CacheSortColumn SortColumn { get; private set; } = CacheSortColumn.Size;

    /// <summary>По убыванию: по умолчанию для размера (сначала самые большие), по возрастанию — для имени.</summary>
    public bool SortDescending { get; private set; } = true;

    public string NameSortGlyph => SortColumn == CacheSortColumn.Name ? (SortDescending ? "▼" : "▲") : string.Empty;

    public string SizeSortGlyph => SortColumn == CacheSortColumn.Size ? (SortDescending ? "▼" : "▲") : string.Empty;

    /// <summary>Сортировать по имени базы; повторно — в обратном порядке.</summary>
    [RelayCommand]
    private void SortByName() => SortBy(CacheSortColumn.Name, defaultDescending: false);

    /// <summary>Сортировать по размеру; повторно — в обратном порядке.</summary>
    [RelayCommand]
    private void SortBySize() => SortBy(CacheSortColumn.Size, defaultDescending: true);

    private void SortBy(CacheSortColumn column, bool defaultDescending)
    {
        SortDescending = SortColumn == column ? !SortDescending : defaultDescending;
        SortColumn = column;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        ApplySort();
    }

    /// <summary>Переставить строки в текущем порядке; отметки «выбрано» остаются у своих строк.</summary>
    private void ApplySort()
    {
        IEnumerable<CacheRowViewModel> ordered = SortColumn == CacheSortColumn.Name
            ? Rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Owner.Id, StringComparer.OrdinalIgnoreCase)
            : Rows.OrderBy(r => r.Owner.TotalBytes).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        var list = ordered.ToList();
        if (SortDescending)
        {
            list.Reverse();
        }

        Rows.Clear();
        foreach (var row in list)
        {
            Rows.Add(row);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    public partial bool IncludeRoaming { get; set; }

    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    public string SelectedText
    {
        get
        {
            var directories = SelectedDirectories();
            return directories.Count == 0
                ? "Ничего не выбрано"
                : $"Выбрано: {Rows.Count(r => r.IsSelected)}, к удалению {ByteSize.Format(directories.Sum(d => d.SizeBytes))}";
        }
    }

    /// <summary>Каталоги, которые будут удалены: локальный кэш выбранных баз и, если отмечено, Roaming.</summary>
    public IReadOnlyList<CacheDirectory> SelectedDirectories() =>
        Rows.Where(r => r.IsSelected).SelectMany(r => r.Owner.In(local: true, roaming: IncludeRoaming)).ToList();

    [RelayCommand]
    private void SelectOrphans()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = row.IsOrphan;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        IsBusy = true;
        try
        {
            if (await _clean(SelectedDirectories()) is { } report)
            {
                Show(report);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanClean() => !IsBusy && SelectedDirectories().Count > 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Show(await _rescan());
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => CleanCommand.NotifyCanExecuteChanged();

    private void Show(CacheReport report)
    {
        Rows.Clear();
        foreach (var owner in report.Owners)
        {
            Rows.Add(new CacheRowViewModel(owner, OnSelectionChanged));
        }

        ApplySort();

        var orphans = report.Owners.Count(o => o.IsOrphan);
        TotalText = $"Всего {ByteSize.Format(report.TotalBytes)} в {report.Owners.Count} каталогах баз; " +
                    $"удалённых баз: {orphans} ({ByteSize.Format(report.OrphanBytes)})";
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedText));
        CleanCommand.NotifyCanExecuteChanged();
    }
}
