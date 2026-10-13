using Avalonia.Controls;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Exchange;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Восстановление списка баз»; закрывается с <c>true</c>, если выбрана копия и нажато «Восстановить».</summary>
public partial class RestoreListWindow : Window
{
    // Нужен дизайнеру XAML.
    public RestoreListWindow()
        : this(new RestoreListViewModel(
        [
            new ListBackup("ibases-1.v8i", new ListBackupInfo
            {
                CreatedAt = DateTimeOffset.Now, Reason = ListBackupReason.Import, SourceFile = "work.json", Mode = ImportMode.Replace, EntryCount = 23,
            }),
        ]))
    {
    }

    public RestoreListWindow(RestoreListViewModel restore)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = restore;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        BackupsList.DoubleTapped += (_, _) =>
        {
            if (restore.HasSelection)
            {
                Close(true);
            }
        };
    }
}
