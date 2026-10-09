using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>База на веб-сервере в форме базы: поле «Адрес базы на веб-сервере» есть и при добавлении, и при изменении.</summary>
public class WebBaseEditorTests
{
    [AvaloniaFact]
    public void Adding_existing_web_base_shows_address_field()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            CreationPlatforms = [ViewModelFixture.Installations[0]],
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();

        // «Добавление существующей» — выбрано по умолчанию; «Далее» — к форме.
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        MainWindowTests.Render();
        window.FindControl<ComboBox>("KindBox")!.SelectedIndex = 2; // «На веб-сервере»
        MainWindowTests.Render();

        Assert.Equal(2, editor.KindIndex);
        Assert.True(editor.IsWeb);
        var address = window.FindControl<TextBox>("WebUrlBox")!;
        Assert.True(address.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "43-add-existing-web-base");

        editor.Name = "Розница";
        address.Text = "https://web.example/retail";
        Assert.True(editor.TryAccept());
        Assert.Equal(ConnectionKind.Web, editor.Result!.Kind);
        Assert.Equal("https://web.example/retail", editor.Result.WebUrl);
        window.Close();
    }

    [AvaloniaFact]
    public void Editing_web_base_keeps_kind_and_address_field()
    {
        var draft = new InfoBaseDraft { Name = "Розница", Kind = ConnectionKind.Web, WebUrl = "https://web.example/retail" };
        var editor = new InfoBaseEditorViewModel(draft, [], isNew: false, new FakeFiles());
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();

        Assert.Equal(2, editor.KindIndex);
        Assert.Equal(2, window.FindControl<ComboBox>("KindBox")!.SelectedIndex);
        Assert.Equal("https://web.example/retail", window.FindControl<TextBox>("WebUrlBox")!.Text);
        Assert.True(window.FindControl<TextBox>("WebUrlBox")!.IsEffectivelyVisible);
        Assert.True(editor.TryAccept());
        Assert.Equal(ConnectionKind.Web, editor.Result!.Kind);
        window.Close();
    }
}
