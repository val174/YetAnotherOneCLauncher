using Avalonia.Controls;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Exchange;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Загрузка списка баз»; закрывается с <c>true</c>, если пользователь нажал «Загрузить».</summary>
public partial class ImportListWindow : Window
{
    // Нужен дизайнеру XAML.
    public ImportListWindow()
        : this(new ImportListViewModel(
            "work-pc.json",
            new InfoBaseListFile { AppVersion = "0.3.0.1", ExportedAt = DateTimeOffset.Now },
            new ImportResult(9, 14, 1),
            new ImportResult(24, 0),
            23))
    {
    }

    public ImportListWindow(ImportListViewModel import)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = import;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
