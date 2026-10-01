using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Добавление базы: выбор варианта, создание из шаблона, без конфигурации, на сервере 1С:Предприятия.</summary>
public class CreateInfoBaseTests
{
    [Fact]
    public async Task Base_is_created_from_template_and_added_to_personal_list()
    {
        using var fixture = new ViewModelFixture();
        var template = AddTemplate(fixture);
        await fixture.LoadAsync();
        var target = Path.Combine(fixture.TempDirectory, "New Buh");
        InfoBaseEditorViewModel? shown = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            shown = editor;
            // Сначала — выбор варианта, по умолчанию «существующая».
            Assert.True(editor.ShowModePage);
            Assert.True(editor.IsExistingMode);
            Assert.Equal("Добавление информационной базы", editor.Title);
            editor.IsTemplateMode = true;
            editor.NextCommand.Execute(null);

            Assert.Equal("Создание информационной базы из шаблона", editor.Title);
            Assert.Equal("Создать", editor.AcceptText);
            Assert.True(editor.CanGoBack);
            Assert.Equal(template, Assert.Single(editor.Templates).Path);
            Assert.Equal("8.5.1.1150 x64", editor.SelectedCreationPlatform?.ToString()); // самая новая с 1cv8
            editor.SelectedTemplate = editor.Templates[0];
            Assert.Equal("Новая розница", editor.Name); // название — из дерева шаблонов
            editor.FilePath = target;
            return editor.TryCreateAsync().GetAwaiter().GetResult();
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        var run = Assert.Single(fixture.Processes.Ran);
        Assert.Equal(ViewModelFixture.Installations[0].ThickClientPath, run.ExecutablePath);
        Assert.Equal(["CREATEINFOBASE", $"File=\"{target}\";Locale=\"ru_RU\";", "/UseTemplate", template], run.Arguments.Take(4));
        Assert.Empty(fixture.Processes.Started); // создание — не запуск базы
        var created = fixture.Base("Новая розница");
        Assert.Equal(target, created.InfoBase.Connection.FilePath);
        Assert.Equal("База «Новая розница» создана и добавлена в список.", fixture.ViewModel.StatusText);
        Assert.NotNull(shown);
    }

    [Fact]
    public async Task Platform_error_is_shown_in_form_and_nothing_is_added()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var count = fixture.ViewModel.InfoBases.Count;
        // «Платформа» пишет ошибку в файл /Out и завершается с кодом 1.
        fixture.Processes.Runner = command =>
        {
            File.WriteAllText(command.Arguments[^1], "\uFEFFОшибка соединения с сервером 1С:Предприятия", System.Text.Encoding.UTF8);
            return 1;
        };
        string? errors = null;
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.IsEmptyMode = true;
            editor.NextCommand.Execute(null);
            Assert.Equal("Создание информационной базы без конфигурации", editor.Title);
            editor.Name = "Разработка";
            editor.KindIndex = 1;
            Assert.True(editor.ShowCreateServerFields);
            Assert.False(editor.ShowExistingServerFields);

            // Сначала не заполнено ничего — подсказки по всем полям сервера.
            Assert.False(editor.TryCreateAsync().GetAwaiter().GetResult());
            Assert.Contains("Укажите кластер серверов 1С.", editor.Errors, StringComparison.Ordinal);

            editor.Server = "srv-1c";
            editor.DatabaseName = "dev_db";
            Assert.Equal("dev_db", editor.InfobaseName); // имя в кластере следует за именем базы данных
            editor.DatabaseServer = "pg01";
            editor.DbmsIndex = 1;
            Assert.False(editor.IsDateOffsetEnabled); // смещение дат — только для MS SQL Server
            editor.DatabasePassword = "secret";
            var result = editor.TryCreateAsync().GetAwaiter().GetResult();
            errors = editor.Errors;
            return result;
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        Assert.NotNull(errors);
        Assert.Contains("Ошибка соединения с сервером 1С:Предприятия", errors, StringComparison.Ordinal);
        Assert.Equal(count, fixture.ViewModel.InfoBases.Count);
        var run = Assert.Single(fixture.Processes.Ran);
        Assert.Contains("DBMS=\"PostgreSQL\"", run.Arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("SQLYOffs", run.Arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("/UseTemplate", run.Arguments);
        Assert.False(File.Exists(run.Arguments[^1])); // временный файл результата удалён
    }

    [Fact]
    public async Task Existing_base_is_added_without_creation()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            editor.NextCommand.Execute(null); // «существующая» — выбрана по умолчанию
            Assert.Equal("Сохранить", editor.AcceptText);
            Assert.False(editor.IsCreateMode);
            editor.Name = "Старая";
            editor.FilePath = @"C:\Bases\Old";
            return true;
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Processes.Ran);
        Assert.Equal(@"C:\Bases\Old", fixture.Base("Старая").InfoBase.Connection.FilePath);
    }

    [AvaloniaFact]
    public void Window_shows_variants_first_then_server_parameters()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            CreationPlatforms = [ViewModelFixture.Installations[0]],
            FoundTemplates = [new ConfigurationTemplate(@"C:\tmplts\1c\Buh\1cv8.cf", "Бухгалтерия/Бухгалтерия предприятия", "3.0.150.20")],
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();

        Assert.True(window.FindControl<StackPanel>("ModePage")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<ScrollViewer>("FormScroll")!.IsEffectivelyVisible);
        Assert.False(editor.CanGoBack);
        Assert.False(window.FindControl<Button>("BackButton")!.IsVisible);
        MainWindowTests.Snapshot(window, "21-add-base-variants");

        window.FindControl<RadioButton>("TemplateModeButton")!.IsChecked = true;
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        MainWindowTests.Render();
        Assert.True(window.FindControl<ScrollViewer>("FormScroll")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<ListBox>("TemplatesList")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("BackButton")!.IsVisible);
        Assert.False(window.FindControl<ComboBox>("KindBox")!.IsVisible); // веб-сервер для новой базы не предлагается
        Assert.Equal(2, window.FindControl<ComboBox>("CreationKindBox")!.ItemCount);
        MainWindowTests.Snapshot(window, "22-create-from-template");

        editor.KindIndex = 1;
        MainWindowTests.Render();
        Assert.True(window.FindControl<Grid>("CreateServerGrid")!.IsEffectivelyVisible);
        Assert.Equal("русский (Россия)", window.FindControl<ComboBox>("LocaleBox")!.SelectionBoxItem?.ToString());
        MainWindowTests.Snapshot(window, "23-create-on-server");
        window.Close();
    }

    private static string AddTemplate(ViewModelFixture fixture)
    {
        var directory = Directory.CreateDirectory(Path.Combine(fixture.TemplatesRoot, "1c", "Accounting", "3_0_150_20")).FullName;
        File.WriteAllText(
            Path.Combine(directory, "1cv8.mft"),
            "Vendor=Фирма \"1С\"\r\nVersion=3.0.150.20\r\n[Config1]\r\nCatalog=Торговля/Новая розница\r\nSource=1cv8.cf\r\n");
        var file = Path.Combine(directory, "1cv8.cf");
        File.WriteAllBytes(file, [1]);
        return file;
    }
}
