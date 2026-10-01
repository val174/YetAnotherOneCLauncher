using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>
/// Добавление базы: выбор варианта, шаг выбора шаблона (поиск, группы по конфигурации, .cf или демо-база .dt),
/// проверка названия, создание без конфигурации и на сервере 1С:Предприятия.
/// </summary>
public class CreateInfoBaseTests
{
    [Fact]
    public async Task Demo_base_is_created_from_template_chosen_on_separate_step()
    {
        using var fixture = new ViewModelFixture();
        var (cf, dt) = AddTemplate(fixture, "3_0_150_20", "3.0.150.20", withDump: true);
        AddTemplate(fixture, "3_0_149_10", "3.0.149.10", withDump: false);
        await fixture.LoadAsync();
        var target = Path.Combine(fixture.TempDirectory, "New Retail");
        fixture.Dialogs.InfoBaseEditor = editor =>
        {
            // Шаг 1 — вариант, по умолчанию «существующая».
            Assert.True(editor.ShowModePage);
            Assert.True(editor.IsExistingMode);
            Assert.Equal("Добавление информационной базы", editor.Title);
            editor.IsTemplateMode = true;
            editor.NextCommand.Execute(null);

            // Шаг 2 — шаблон: без выбора дальше не пускает.
            Assert.True(editor.ShowTemplatePage);
            Assert.Equal("Выбор шаблона информационной базы", editor.Title);
            Assert.Equal("Далее >", editor.AcceptText);
            editor.NextCommand.Execute(null);
            Assert.True(editor.ShowTemplatePage);
            Assert.StartsWith("Выберите шаблон", editor.Errors, StringComparison.Ordinal);

            // Версии сгруппированы по конфигурации, новые — первыми; группа свёрнута, при поиске — раскрыта.
            var group = Assert.Single(editor.TemplateGroups);
            Assert.Equal("Торговля/Новая розница", group.Name);
            Assert.Equal(["3.0.150.20", "3.0.149.10"], group.Items.Select(i => i.Text));
            Assert.Equal("версий: 2", group.Details);
            Assert.False(group.IsExpanded);
            editor.TemplateSearch = "розница 149";
            Assert.Equal("3.0.149.10", Assert.Single(Assert.Single(editor.TemplateGroups).Items).Text);
            Assert.True(editor.TemplateGroups[0].IsExpanded);
            editor.TemplateSearch = "бухгалтерия";
            Assert.True(editor.ShowNoTemplateMatches);
            editor.TemplateSearch = string.Empty;

            // В новой версии есть .cf и .dt — выбор, что создавать; по умолчанию — конфигурация.
            editor.SelectedTemplateNode = editor.TemplateGroups[0].Items[0];
            Assert.True(editor.ShowTemplateKindChoice);
            Assert.Equal(cf, editor.SelectedTemplateFile);
            editor.UseDemoData = true;
            Assert.Equal(dt, editor.SelectedTemplateFile);
            Assert.Equal("Новая розница", editor.Name); // название — из дерева шаблонов
            editor.NextCommand.Execute(null);

            // Шаг 3 — форма; «Назад» возвращает к шаблону, выбор сохраняется.
            Assert.True(editor.ShowForm);
            Assert.Equal("Создать", editor.AcceptText);
            Assert.Contains("демонстрационная база (.dt)", editor.SelectedTemplateText, StringComparison.Ordinal);
            editor.BackCommand.Execute(null);
            Assert.True(editor.ShowTemplatePage);
            Assert.Same(editor.TemplateGroups[0].Items[0], editor.SelectedTemplateNode);
            editor.NextCommand.Execute(null);

            Assert.Equal("8.5.1.1150 x64", editor.SelectedCreationPlatform?.ToString()); // самая новая с 1cv8
            editor.FilePath = target;
            return editor.TryCreateAsync().GetAwaiter().GetResult();
        };

        await fixture.ViewModel.AddBaseCommand.ExecuteAsync(null);

        var run = Assert.Single(fixture.Processes.Ran);
        Assert.Equal(ViewModelFixture.Installations[0].ThickClientPath, run.ExecutablePath);
        Assert.Equal(["CREATEINFOBASE", $"File=\"{target}\";Locale=\"ru_RU\";", "/UseTemplate", dt], run.Arguments.Take(4));
        Assert.Empty(fixture.Processes.Started); // создание — не запуск базы
        Assert.Equal(target, fixture.Base("Новая розница").InfoBase.Connection.FilePath);
        Assert.Equal("База «Новая розница» создана и добавлена в список.", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task Base_with_existing_name_is_not_created_and_platform_error_is_shown()
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
            Assert.True(editor.ShowForm); // без шаблона — сразу к форме
            Assert.Equal("Создание информационной базы без конфигурации", editor.Title);
            editor.KindIndex = 1;
            Assert.True(editor.ShowCreateServerFields);
            Assert.False(editor.ShowExistingServerFields);
            editor.Server = "srv-1c";
            editor.DatabaseName = "dev_db";
            Assert.Equal("dev_db", editor.InfobaseName); // имя в кластере следует за именем базы данных
            editor.DatabaseServer = "pg01";
            editor.DbmsIndex = 1;
            Assert.False(editor.IsDateOffsetEnabled); // смещение дат — только для MS SQL Server

            // Название уже есть в списке (регистр не важен) — платформа не запускается.
            editor.Name = "  бухгалтерия ПРЕДПРИЯТИЯ ";
            Assert.False(editor.TryCreateAsync().GetAwaiter().GetResult());
            Assert.StartsWith("В списке уже есть база «бухгалтерия ПРЕДПРИЯТИЯ».", editor.Errors, StringComparison.Ordinal);
            Assert.Empty(fixture.Processes.Ran);

            editor.Name = "Разработка";
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
            Assert.True(editor.ShowForm);
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
    public void Window_steps_variant_template_and_form()
    {
        var editor = new InfoBaseEditorViewModel(new InfoBaseDraft(), [], isNew: true, new FakeFiles())
        {
            Creator = (_, _) => Task.FromResult<string?>(null),
            CreationPlatforms = [ViewModelFixture.Installations[0]],
            FoundTemplates =
            [
                new ConfigurationTemplate("Бухгалтерия/Бухгалтерия предприятия", "3.0.150.20", @"C:\tmplts\1c\Acc\3_0_150_20\1cv8.cf", @"C:\tmplts\1c\Acc\3_0_150_20\1cv8.dt"),
                new ConfigurationTemplate("Бухгалтерия/Бухгалтерия предприятия", "3.0.149.10", @"C:\tmplts\1c\Acc\3_0_149_10\1cv8.cf", null),
                new ConfigurationTemplate("Демо/Управляемое приложение", "1.0.41.3", null, @"C:\tmplts\1c\Demo\1cv8.dt"),
            ],
        };
        var window = new InfoBaseEditorWindow(editor);
        window.Show();
        MainWindowTests.Render();
        void Click(string name)
        {
            window.FindControl<Button>(name)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            MainWindowTests.Render();
        }

        Assert.True(window.FindControl<StackPanel>("ModePage")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("BackButton")!.IsVisible);
        MainWindowTests.Snapshot(window, "21-add-base-variants");

        window.FindControl<RadioButton>("TemplateModeButton")!.IsChecked = true;
        Click("SaveButton");
        Assert.True(window.FindControl<DockPanel>("TemplatePage")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<ScrollViewer>("FormScroll")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<TextBox>("TemplateSearchBox")!.IsFocused);
        Assert.Equal(2, window.FindControl<TreeView>("TemplatesTree")!.ItemCount); // две конфигурации
        Assert.False(window.FindControl<StackPanel>("TemplateKindChoice")!.IsVisible);

        editor.SelectTemplate(editor.TemplateGroups[0].Items[0].Template);
        MainWindowTests.Render();
        Assert.True(window.FindControl<StackPanel>("TemplateKindChoice")!.IsVisible); // .cf и .dt — выбор
        Assert.True(window.FindControl<RadioButton>("ConfigurationOnlyButton")!.IsChecked);
        MainWindowTests.Snapshot(window, "22-choose-template");

        Click("SaveButton");
        Assert.True(window.FindControl<ScrollViewer>("FormScroll")!.IsEffectivelyVisible);
        Assert.Contains("конфигурация (.cf)", window.FindControl<TextBlock>("SelectedTemplateText")!.Text, StringComparison.Ordinal);
        Assert.False(window.FindControl<ComboBox>("KindBox")!.IsVisible); // веб-сервер для новой базы не предлагается
        editor.KindIndex = 1;
        MainWindowTests.Render();
        Assert.True(window.FindControl<Grid>("CreateServerGrid")!.IsEffectivelyVisible);
        MainWindowTests.Snapshot(window, "23-create-on-server");

        Click("BackButton");
        Assert.True(window.FindControl<DockPanel>("TemplatePage")!.IsEffectivelyVisible);
        Click("BackButton");
        Assert.True(window.FindControl<StackPanel>("ModePage")!.IsEffectivelyVisible);
        window.Close();
    }

    /// <summary>Версия шаблона «Торговля/Новая розница» в каталоге шаблонов: .cf и, если нужно, демо-база .dt.</summary>
    private static (string Cf, string? Dt) AddTemplate(ViewModelFixture fixture, string folder, string version, bool withDump)
    {
        var directory = Directory.CreateDirectory(Path.Combine(fixture.TemplatesRoot, "1c", "Retail", folder)).FullName;
        var manifest = $"Vendor=Фирма \"1С\"\r\nVersion={version}\r\n[Config1]\r\nCatalog=Торговля/Новая розница\r\nSource=1cv8.cf\r\n";
        if (withDump)
        {
            manifest += "[Config2]\r\nCatalog=Торговля/Новая розница (демо)\r\nSource=1cv8.dt\r\n";
        }

        File.WriteAllText(Path.Combine(directory, "1cv8.mft"), manifest);
        var cf = Path.Combine(directory, "1cv8.cf");
        File.WriteAllBytes(cf, [1]);
        string? dt = null;
        if (withDump)
        {
            dt = Path.Combine(directory, "1cv8.dt");
            File.WriteAllBytes(dt, [1]);
        }

        return (cf, dt);
    }
}
