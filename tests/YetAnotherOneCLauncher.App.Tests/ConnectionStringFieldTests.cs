using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Поле «Строка подключения» при добавлении существующей базы: заполняет остальные поля формы.</summary>
public class ConnectionStringFieldTests
{
    private static InfoBaseEditorViewModel NewEditor() => new(new InfoBaseDraft(), [], isNew: true, new FakeFiles());

    [Fact]
    public void File_connection_fills_kind_directory_and_name()
    {
        var editor = NewEditor();
        editor.ConnectionText = "File=\"C:\\Bases\\Buh\";";

        Assert.True(editor.IsFile);
        Assert.Equal(@"C:\Bases\Buh", editor.FilePath);
        Assert.Equal("Buh", editor.Name);
        Assert.Equal(@"Файловая база, каталог: C:\Bases\Buh", editor.ConnectionHint);
        Assert.False(editor.IsConnectionHintWarning);
    }

    [Fact]
    public void Server_connection_fills_cluster_and_infobase()
    {
        var editor = NewEditor();
        editor.ConnectionText = "Srvr=\"srv1c:1541\";Ref=\"buh_prod\";";

        Assert.True(editor.IsServer);
        Assert.Equal("srv1c:1541", editor.Server);
        Assert.Equal("buh_prod", editor.InfobaseName);
        Assert.Contains("сервер srv1c, порт 1541", editor.ConnectionHint, StringComparison.Ordinal);
        Assert.True(editor.TryAccept());
        Assert.Equal("Srvr=\"srv1c:1541\";Ref=\"buh_prod\";", editor.Result!.BuildConnection().ToString());
    }

    [Fact]
    public void Web_address_fills_publication_and_keeps_extra_keys_without_password()
    {
        var editor = NewEditor();
        editor.ConnectionText = "ws=\"https://web.example/retail/ru_RU/\";wsn=\"svc\";Pwd=\"secret\";";

        Assert.True(editor.IsWeb);
        Assert.Equal("https://web.example/retail", editor.WebUrl);
        Assert.Equal("retail", editor.Name);
        Assert.True(editor.TryAccept());
        var connection = editor.Result!.BuildConnection();
        Assert.Equal("https://web.example/retail", connection.WebUrl);
        Assert.Equal("svc", connection["wsn"]);
        Assert.Null(connection["Pwd"]);
    }

    [Fact]
    public void Name_typed_by_user_is_kept_but_suggested_one_follows_the_string()
    {
        var editor = NewEditor();
        editor.ConnectionText = @"C:\Bases\Buh";
        Assert.Equal("Buh", editor.Name);
        editor.ConnectionText = @"C:\Bases\Zup"; // подставленное название заменяется
        Assert.Equal("Zup", editor.Name);

        editor.Name = "Зарплата";
        editor.ConnectionText = @"srv\zup_prod";
        Assert.Equal("Зарплата", editor.Name); // введённое пользователем — не трогаем
        Assert.Equal("zup_prod", editor.InfobaseName);
    }

    [Fact]
    public void Unrecognized_or_incomplete_string_shows_warning_and_keeps_fields()
    {
        var editor = NewEditor();
        editor.FilePath = @"C:\Bases\Old";
        editor.ConnectionText = "что-то непонятное";

        Assert.True(editor.IsConnectionHintWarning);
        Assert.StartsWith("Не удалось определить параметры", editor.ConnectionHint, StringComparison.Ordinal);
        Assert.Equal(@"C:\Bases\Old", editor.FilePath);
        Assert.True(editor.IsFile);

        editor.ConnectionText = "Srvr=\"srv1c\";";
        Assert.True(editor.IsServer);
        Assert.True(editor.IsConnectionHintWarning); // нет имени базы в кластере

        editor.ConnectionText = string.Empty;
        Assert.False(editor.HasConnectionHint);
    }

    [Fact]
    public void Field_is_only_for_adding_existing_base()
    {
        Assert.True(NewEditor().ShowConnectionStringField);
        Assert.False(new InfoBaseEditorViewModel(new InfoBaseDraft { Name = "База" }, [], isNew: false, new FakeFiles()).ShowConnectionStringField);

        var creating = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
        };
        Assert.True(creating.ShowConnectionStringField); // «существующая» — выбрана по умолчанию
        creating.IsEmptyMode = true;
        Assert.False(creating.ShowConnectionStringField);
    }

    [Fact]
    public async Task Pasted_web_base_is_saved_to_list()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.ConnectionText = "https://web.example/trade/ru_RU/#e1cib/list/Документ.Заказ";
            return true;
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        var section = fixture.SavedList().Sections.Single(s => s.Name == "trade");
        Assert.Equal("ws=\"https://web.example/trade\";", section.Get("Connect"));
    }

    [AvaloniaFact]
    public void Window_shows_field_under_name_and_fills_form()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            CreationPlatforms = [ViewModelFixture.Installations[0]],
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        MainWindowTests.Render();

        var name = window.FindControl<TextBox>("NameBox")!;
        var box = window.FindControl<TextBox>("ConnectionStringBox")!;
        Assert.True(box.IsEffectivelyVisible);
        Assert.True(box.TranslatePoint(default, window)!.Value.Y > name.TranslatePoint(default, window)!.Value.Y); // под названием
        Assert.True(box.TranslatePoint(default, window)!.Value.Y < window.FindControl<ComboBox>("KindBox")!.TranslatePoint(default, window)!.Value.Y);

        box.Text = "Srvr=\"srv1c:1541\";Ref=\"buh_prod\";";
        MainWindowTests.Render();
        Assert.Equal(1, window.FindControl<ComboBox>("KindBox")!.SelectedIndex);
        Assert.Equal("buh_prod", name.Text);
        Assert.True(window.FindControl<TextBlock>("ConnectionHintText")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "44-connection-string-server");

        box.Text = "непонятно что";
        MainWindowTests.Render();
        Assert.Contains("warning", window.FindControl<TextBlock>("ConnectionHintText")!.Classes);
        MainWindowTests.Snapshot(window, "44-connection-string-unrecognized");
        window.Close();

        // При изменении базы поля нет.
        var edit = new InfoBaseEditorWindow(new InfoBaseEditorViewModel(
            new InfoBaseDraft { Name = "База", Kind = ConnectionKind.File, FilePath = @"C:\B" }, [], isNew: false, new FakeFiles()));
        edit.Show();
        MainWindowTests.Render();
        Assert.False(edit.FindControl<TextBox>("ConnectionStringBox")!.IsEffectivelyVisible);
        edit.Close();
    }
}
