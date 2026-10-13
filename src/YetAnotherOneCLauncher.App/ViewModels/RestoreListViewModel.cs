using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Exchange;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка окна «Восстановление списка баз».</summary>
public sealed class ListBackupRowViewModel(ListBackup backup)
{
    public ListBackup Backup { get; } = backup;

    public string WhenText => Backup.Info.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>«загрузкой work.json (заменить)», «восстановлением».</summary>
    public string ReasonText => Backup.Info.Reason switch
    {
        ListBackupReason.Restore => "восстановлением",
        _ => "загрузкой "
             + (Backup.Info.SourceFile ?? "из файла")
             + Backup.Info.Mode switch
             {
                 ImportMode.Replace => " (заменить)",
                 ImportMode.Add => " (добавить)",
                 _ => string.Empty,
             },
    };

    public string CountText => Backup.Info.EntryCount.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Окно «Восстановление списка баз»: копии личного списка, сделанные перед загрузками и восстановлениями.</summary>
public sealed partial class RestoreListViewModel : ObservableObject
{
    public RestoreListViewModel(IReadOnlyList<ListBackup> backups, ICommand? openFolder = null)
    {
        ArgumentNullException.ThrowIfNull(backups);
        Rows = [.. backups.Select(b => new ListBackupRowViewModel(b))];
        SelectedRow = Rows.Count > 0 ? Rows[0] : null;
        OpenFolderCommand = openFolder;
    }

    public IReadOnlyList<ListBackupRowViewModel> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ListBackupRowViewModel? SelectedRow { get; set; }

    public bool HasSelection => SelectedRow is not null;

    /// <summary>Вернуть и настройки баз (избранное, параметры, платформа, EDT).</summary>
    [ObservableProperty]
    public partial bool RestoreSettings { get; set; } = true;

    /// <summary>«Открыть каталог копий».</summary>
    public ICommand? OpenFolderCommand { get; }
}
