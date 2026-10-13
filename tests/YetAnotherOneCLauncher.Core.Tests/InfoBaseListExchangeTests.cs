using System.Text;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Сохранение личного списка в JSON и загрузка обратно (задача #24).</summary>
public class InfoBaseListExchangeTests
{
    private const string List = """
        [Разработка]
        ID=00000000-0000-0000-0000-0000000000f1
        OrderInList=16384
        Folder=/
        OrderInTree=16384
        External=0
        [Тестовые]
        ID=00000000-0000-0000-0000-0000000000f2
        Folder=/Разработка
        OrderInTree=8192.5
        [ЗУП (тест)]
        Connect=Srvr="msc-1c-02";Ref="zup_test";
        ID=00000000-0000-0000-0000-000000000001
        OrderInList=32768
        Folder=/Разработка/Тестовые
        OrderInTree=16384
        External=0
        ClientConnectionSpeed=Normal
        App=ThinClient
        WA=0
        Version=8.3.25
        DefaultApp=ThinClient
        AdditionalParameters=/UseHwLicenses-
        AppArch=x86_64
        MyKey=значение
        [Бухгалтерия]
        Connect=File="C:\Bases\Buh";
        OrderInList=первая
        Folder=/
        WA=2
        """;

    private static readonly ListSource Personal = new(ListSourceKind.Personal, string.Empty);

    private static V8iDocument Document() => V8iDocument.Parse(List);

    private static InfoBase Base(V8iDocument document, string name) => new(document.Sections.Single(s => s.Name == name), Personal);

    private static Dictionary<string, string?> Keys(V8iSection section) =>
        section.Lines.Where(l => l.IsKeyValue).ToDictionary(l => l.Key!, l => l.Value, StringComparer.OrdinalIgnoreCase);

    private static InfoBaseListFile RoundTrip(InfoBaseListFile file) =>
        InfoBaseListExchange.Parse(InfoBaseListExchange.Serialize(file));

    [Fact]
    public void Whole_list_round_trips_without_losing_keys()
    {
        var document = Document();
        var file = RoundTrip(InfoBaseListExchange.Export(document.Sections, null, new ExportOptions(), wholeList: true));

        var restored = InfoBaseListExchange.ToDocument(file);

        Assert.Equal(
            document.Sections.Select(s => s.Name).Order(StringComparer.Ordinal),
            restored.Sections.Select(s => s.Name).Order(StringComparer.Ordinal));
        foreach (var section in document.Sections.Where(s => s.Name.Length > 0))
        {
            var copy = restored.Sections.Single(s => s.Name == section.Name);
            Assert.Equal(Keys(section).OrderBy(k => k.Key), Keys(copy).OrderBy(k => k.Key));
        }

        // Ключи — в порядке штатного стартера, неизвестные — в конце.
        var zup = restored.Sections.Single(s => s.Name == "ЗУП (тест)");
        Assert.Equal(
            ["Connect", "ID", "OrderInList", "Folder", "OrderInTree", "External", "ClientConnectionSpeed", "App", "WA", "Version",
             "DefaultApp", "AdditionalParameters", "AppArch", "MyKey"],
            zup.Lines.Select(l => l.Key));
    }

    [Fact]
    public void File_is_readable_json_with_format_marker_and_named_fields()
    {
        var file = InfoBaseListExchange.Export(
            Document().Sections, null, new ExportOptions(), wholeList: true, "0.3.0.1", new DateTimeOffset(2026, 10, 13, 9, 15, 0, TimeSpan.FromHours(3)));

        var json = Encoding.UTF8.GetString(InfoBaseListExchange.Serialize(file));

        Assert.Contains("\"format\": \"YetAnotherOneCLauncher.InfoBaseList\"", json, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"appVersion\": \"0.3.0.1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"ЗУП (тест)\"", json, StringComparison.Ordinal);
        Assert.Contains("\"connection\": \"Srvr=\\\"msc-1c-02\\\";Ref=\\\"zup_test\\\";\"", json, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"/Разработка/Тестовые\"", json, StringComparison.Ordinal);
        Assert.Contains("\"windowsAuthentication\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"orderInTree\": 8192.5", json, StringComparison.Ordinal);
        Assert.Contains("\"MyKey\": \"значение\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher", json, StringComparison.Ordinal); // настроек не просили
    }

    [Fact]
    public void Launcher_settings_follow_options_and_never_include_passwords()
    {
        var document = Document();
        var data = new LauncherUserData(new LauncherSettings());
        var zup = Base(document, "ЗУП (тест)");
        data.SetFavorite(zup, true);
        data.SetPlatformVersionOverride(zup, "8.3.25.1374");
        data.SetLaunchProfile(zup, new InfoBaseLaunchProfile
        {
            Parameters = "/Debug", UserName = "Администратор", PasswordKey = "secret-key", EdtProjectId = "ers", EdtWorkspace = @"D:\EDT\ers",
        });
        data.SetFolderParameters("/", "/DisableStartupMessages");
        data.SetFolderParameters("/Разработка", "/TechnicalSpecialistMode");

        var defaults = InfoBaseListExchange.Export(document.Sections, data, new ExportOptions(), wholeList: true);
        var launcher = defaults.InfoBases.Single(b => b.Name == "ЗУП (тест)").Launcher;
        Assert.Equal(
            new ExportedLaunchSettings
            {
                Favorite = true, Parameters = "/Debug", PlatformVersion = "8.3.25.1374", EdtProjectId = "ers", EdtWorkspace = @"D:\EDT\ers",
            },
            launcher);
        Assert.Equal(["/", "/Разработка"], defaults.FolderParameters.Select(p => p.Folder));
        Assert.DoesNotContain("secret-key", Encoding.UTF8.GetString(InfoBaseListExchange.Serialize(defaults)), StringComparison.Ordinal);

        var withNames = InfoBaseListExchange.Export(
            document.Sections, data, new ExportOptions(LaunchParameters: false, BaseSettings: false, UserNames: true), wholeList: true);
        Assert.Equal(new ExportedLaunchSettings { UserName = "Администратор" }, withNames.InfoBases.Single(b => b.Name == "ЗУП (тест)").Launcher);
        Assert.Empty(withNames.FolderParameters);

        // Выбранная группа: параметры корня списка не сохраняются.
        var folder = InfoBaseListExchange.Export(
            document.Sections.Where(s => s.Name is "Разработка" or "Тестовые" or "ЗУП (тест)"), data, new ExportOptions(), wholeList: false);
        Assert.Equal(["/Разработка"], folder.FolderParameters.Select(p => p.Folder));
    }

    [Theory]
    [InlineData("не json", "повреждён")]
    [InlineData("""{ "format": "other", "formatVersion": 1 }""", "не файл списка баз")]
    [InlineData("""{ "format": "YetAnotherOneCLauncher.InfoBaseList", "formatVersion": 2, "appVersion": "9.0" }""", "более новой версией лаунчера (9.0)")]
    [InlineData("""{ "format": "YetAnotherOneCLauncher.InfoBaseList", "infoBases": [ { "name": "Без подключения" } ] }""", "базе №1")]
    [InlineData("""{ "format": "YetAnotherOneCLauncher.InfoBaseList", "folders": [ { "path": "/" } ] }""", "групп не указан путь")]
    public void Wrong_files_are_rejected_with_a_clear_message(string json, string message)
    {
        var error = Assert.Throws<InfoBaseListFileException>(() => InfoBaseListExchange.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Minimal_file_is_accepted()
    {
        var file = InfoBaseListExchange.Parse("""
            {
              // комментарии и лишние запятые допустимы
              "format": "YetAnotherOneCLauncher.InfoBaseList",
              "infoBases": [ { "name": "Новая", "connection": "File=\"D:\\Bases\\New\";", }, ],
            }
            """u8);

        var section = Assert.Single(InfoBaseListExchange.ToDocument(file).Sections);
        Assert.Equal("Новая", section.Name);
        Assert.Equal("File=\"D:\\Bases\\New\";", section.Get("Connect"));
        Assert.Equal("/", section.Get("Folder"));
    }

    [Fact]
    public void Add_mode_skips_existing_and_taken_names_and_moves_settings_only_to_added_bases()
    {
        var current = V8iDocument.Parse("""
            [ЗУП (тест)]
            Connect=Srvr="msc-1c-02";Ref="zup_test";
            ID=00000000-0000-0000-0000-000000000001
            Folder=/Разработка/Тестовые
            """);
        var source = Document();
        var data = new LauncherUserData(new LauncherSettings());
        data.SetFavorite(Base(source, "ЗУП (тест)"), true);
        data.SetLaunchProfile(Base(source, "Бухгалтерия"), new InfoBaseLaunchProfile { Parameters = "/Debug", UserName = "Бух", PasswordKey = "k" });
        data.SetFolderParameters("/Разработка", "/FromFile");
        var file = RoundTrip(InfoBaseListExchange.Export(source.Sections, data, new ExportOptions(UserNames: true), wholeList: true));

        var target = new LauncherUserData(new LauncherSettings());
        target.SetFolderParameters("/Разработка", "/Mine");
        var preview = InfoBaseListExchange.Preview(current, file, ImportMode.Add);
        var added = new List<V8iSection>();
        var result = InfoBaseListExchange.Import(current, file, ImportMode.Add, ["Чужая"], added);
        var withSettings = InfoBaseListExchange.ApplySettings(file, added, target, replaceFolderParameters: false);

        // Группы «Разработка», «Тестовые» и база «Бухгалтерия» — новые; «ЗУП (тест)» уже есть.
        Assert.Equal(new ImportResult(3, 1), result);
        Assert.Equal(result, preview);
        Assert.Equal(["ЗУП (тест)", "Разработка", "Тестовые", "Бухгалтерия"], current.Sections.Select(s => s.Name));
        Assert.Equal(1, withSettings);
        Assert.False(target.IsFavorite(Base(current, "ЗУП (тест)"))); // база была — её настройки не трогаем
        Assert.Equal(
            new InfoBaseLaunchProfile { InfoBase = InfoBaseRef.From(Base(current, "Бухгалтерия")), Parameters = "/Debug", UserName = "Бух" },
            target.LaunchProfile(Base(current, "Бухгалтерия")));
        Assert.Equal("/Mine", target.FolderParameters("/Разработка")); // свои параметры группы не перезаписываются
    }

    [Fact]
    public void Replace_mode_replaces_list_but_keeps_names_of_other_lists_unique()
    {
        var current = V8iDocument.Parse("""
            [Старая]
            Connect=File="C:\Old";
            [Бухгалтерия]
            Connect=File="C:\Other";
            """);
        var file = InfoBaseListExchange.Export(Document().Sections, null, new ExportOptions(), wholeList: true);

        var preview = InfoBaseListExchange.Preview(current, file, ImportMode.Replace, ["Бухгалтерия"]);
        Assert.Equal(2, current.Sections.Count); // предпросмотр список не меняет

        var result = InfoBaseListExchange.Import(current, file, ImportMode.Replace, ["Бухгалтерия"], added: null);

        Assert.Equal(new ImportResult(3, 0, SameName: 1), result);
        Assert.Equal(result, preview);
        Assert.Equal(["Разработка", "Тестовые", "ЗУП (тест)"], current.Sections.Select(s => s.Name));
    }

    [Fact]
    public void Replace_mode_overwrites_folder_parameters()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetFolderParameters("/Разработка", "/FromFile");
        var file = InfoBaseListExchange.Export(Document().Sections, data, new ExportOptions(), wholeList: true);
        var target = new LauncherUserData(new LauncherSettings());
        target.SetFolderParameters("/Разработка", "/Mine");

        InfoBaseListExchange.ApplySettings(file, [], target, replaceFolderParameters: true);

        Assert.Equal("/FromFile", target.FolderParameters("/Разработка"));
    }

    [Fact]
    public async Task Replace_keeps_previous_list_in_backup_every_time()
    {
        var directory = Directory.CreateTempSubdirectory("yaocl-exchange-");
        try
        {
            var path = Path.Combine(directory.FullName, "ibases.v8i");
            await File.WriteAllTextAsync(path, "[Первая]\r\nConnect=File=\"C:\\1\";\r\n");
            using var store = new PersonalListStore(path);
            await store.EditAsync(d => d.Sections[^1].Name = "Вторая");

            await store.EditAsync(d => d.Sections.Clear(), backup: true);

            // Обычная правка делает .bak только первой за сеанс, замена — всегда.
            Assert.Contains("[Вторая]", await File.ReadAllTextAsync(store.BackupPath), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
