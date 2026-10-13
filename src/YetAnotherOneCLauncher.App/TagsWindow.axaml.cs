using Avalonia.Controls;
using Avalonia.Threading;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App;

/// <summary>Окно «Метки»; закрывается с <c>true</c>, если наименования в порядке и нажато «OK».</summary>
public partial class TagsWindow : Window
{
    // Нужен дизайнеру XAML.
    public TagsWindow()
        : this(new TagsEditorViewModel(
            [new TagDefinition { Name = "прод", Color = TagColor.Red }, new TagDefinition { Name = "тест", Color = TagColor.Green }],
            new Dictionary<string, int> { ["прод"] = 4 }))
    {
    }

    public TagsWindow(TagsEditorViewModel editor)
    {
        InitializeComponent();
        WindowTitleBar.Apply(this);
        DataContext = editor;

        OkButton.Click += (_, _) =>
        {
            if (editor.Validate())
            {
                Close(true);
            }
        };
        CancelButton.Click += (_, _) => Close(false);

        // Новая метка — сразу в поле наименования, текст выделен.
        CreateButton.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }
}
