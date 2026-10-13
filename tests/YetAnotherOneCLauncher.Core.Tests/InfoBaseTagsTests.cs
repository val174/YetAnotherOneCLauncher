using System.Text;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Метки баз: каталог, метки базы, отбор, сохранение (задача #25).</summary>
public class InfoBaseTagsTests
{
    private const string List = """
        [ЗУП]
        Connect=Srvr="srv";Ref="zup";
        ID=00000000-0000-0000-0000-000000000001
        [Бухгалтерия]
        Connect=File="C:\Bases\Buh";
        [Склад]
        Connect=Srvr="srv";Ref="sklad";
        ID=00000000-0000-0000-0000-000000000003
        """;

    private static readonly ListSource Personal = new(ListSourceKind.Personal, string.Empty);

    private static V8iDocument Document() => V8iDocument.Parse(List);

    private static InfoBase Base(V8iDocument document, string name) => new(document.Sections.Single(s => s.Name == name), Personal);

    [Theory]
    [InlineData("  прод  ", "прод")]
    [InlineData("#клиент   А", "клиент А")]
    [InlineData("   ", null)]
    [InlineData("#", null)]
    public void Names_are_trimmed_and_hash_is_dropped(string input, string? expected) =>
        Assert.Equal(expected, TagNames.Normalize(input));

    [Fact]
    public void Long_name_is_cut()
    {
        Assert.Equal(TagNames.MaxLength, TagNames.Normalize(new string('я', 60))!.Length);
    }

    [Fact]
    public void Tags_are_stored_case_insensitively_with_catalog_names_and_rotating_colors()
    {
        var document = Document();
        var data = new LauncherUserData(new LauncherSettings());
        var zup = Base(document, "ЗУП");

        data.SetTag(zup, "Прод", on: true);
        data.SetTag(zup, "прод", on: true); // та же метка
        data.SetTag(zup, "ЗУП", on: true);
        data.SetTag(Base(document, "Бухгалтерия"), "тест", on: true);

        Assert.Equal(["ЗУП", "Прод"], data.TagsOf(zup)); // по наименованию, имена — из каталога
        Assert.Equal(["Прод", "ЗУП", "тест"], data.Settings.Tags.Select(t => t.Name)); // каталог — в порядке создания
        Assert.Equal([TagColor.Red, TagColor.Amber, TagColor.Green], data.Settings.Tags.Select(t => t.Color));
        Assert.All(data.Settings.Tags, t => Assert.True(t.ShowInRow));

        data.SetTag(zup, "ПРОД", on: false);
        Assert.Equal(["ЗУП"], data.TagsOf(zup));
        Assert.NotNull(data.Tag("прод")); // метка без баз остаётся в каталоге

        data.SetTags(zup, []);
        Assert.Empty(data.TagsOf(zup));
        Assert.DoesNotContain(data.Settings.InfoBaseTags, t => t.InfoBase.Matches(zup));
    }

    [Fact]
    public void Base_without_id_is_found_by_connection()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetTag(Base(Document(), "Бухгалтерия"), "архив", on: true);

        Assert.Equal(["архив"], data.TagsOf(Base(Document(), "Бухгалтерия")));
        Assert.Empty(data.TagsOf(Base(Document(), "Склад")));
    }

    [Fact]
    public void Rename_moves_tag_on_all_bases_and_merges_into_existing()
    {
        var document = Document();
        var data = new LauncherUserData(new LauncherSettings());
        var zup = Base(document, "ЗУП");
        var sklad = Base(document, "Склад");
        data.SetTags(zup, ["прод", "тест"]);
        data.SetTags(sklad, ["тест"]);

        Assert.True(data.RenameTag("тест", "Тестовые"));
        Assert.Equal(["прод", "Тестовые"], data.TagsOf(zup));
        Assert.Equal(["Тестовые"], data.TagsOf(sklad));

        // В уже существующую — слияние, без повторов у базы; цвет — у той, что была.
        var color = data.Tag("прод")!.Color;
        Assert.True(data.RenameTag("Тестовые", "ПРОД"));
        Assert.Equal(["прод"], data.TagsOf(zup));
        Assert.Equal(["прод"], data.TagsOf(sklad));
        Assert.Equal(color, Assert.Single(data.Settings.Tags).Color);

        Assert.False(data.RenameTag("нет такой", "x"));
        Assert.False(data.RenameTag("прод", "  "));
    }

    [Fact]
    public void Delete_removes_tag_everywhere()
    {
        var document = Document();
        var data = new LauncherUserData(new LauncherSettings());
        data.SetTags(Base(document, "ЗУП"), ["прод", "ЗУП"]);
        data.SetTags(Base(document, "Склад"), ["прод"]);

        data.DeleteTag("ПРОД");

        Assert.Null(data.Tag("прод"));
        Assert.Equal(["ЗУП"], data.TagsOf(Base(document, "ЗУП")));
        Assert.Single(data.Settings.InfoBaseTags); // у «Склада» меток не осталось — запись удалена
    }

    [Fact]
    public void Appearance_changes_color_and_row_visibility()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.EnsureTag("архив");

        data.SetTagAppearance("Архив", TagColor.Gray, showInRow: false);

        Assert.Equal(new TagDefinition { Name = "архив", Color = TagColor.Gray, ShowInRow = false }, data.Tag("архив"));
    }

    [Fact]
    public void Filter_requires_all_selected_tags_and_search_prefixes()
    {
        string[] tags = ["прод", "клиент А"];

        Assert.True(TagNames.Matches(tags, [], []));
        Assert.True(TagNames.Matches(tags, ["ПРОД"], []));
        Assert.True(TagNames.Matches(tags, ["прод", "клиент а"], []));
        Assert.False(TagNames.Matches(tags, ["прод", "тест"], [])); // условие «И»
        Assert.True(TagNames.Matches(tags, [], ["пр"]));
        Assert.True(TagNames.Matches(tags, [], ["клиент_а"]));
        Assert.False(TagNames.Matches(tags, [], ["пр", "тест"]));
        Assert.False(TagNames.Matches([], ["прод"], []));
    }

    [Fact]
    public void Search_text_is_split_into_words_and_tag_prefixes()
    {
        var (words, prefixes) = TagNames.ParseSearch("бух #прод склад #клиент_А");
        Assert.Equal("бух склад", words);
        Assert.Equal(["прод", "клиент_А"], prefixes);

        (words, prefixes) = TagNames.ParseSearch("  # ");
        Assert.Empty(words);
        Assert.Empty(prefixes);
        Assert.Empty(TagNames.ParseSearch(null).TagPrefixes);
    }

    [Fact]
    public async Task Tags_survive_settings_save_and_bad_entries_are_dropped()
    {
        var directory = Directory.CreateTempSubdirectory("yaocl-tags-");
        try
        {
            var settings = new LauncherSettings();
            var data = new LauncherUserData(settings);
            data.SetTags(Base(Document(), "ЗУП"), ["прод"]);
            data.SetTagAppearance("прод", TagColor.Blue, showInRow: false);
            var store = new SettingsStore(directory.FullName);
            var path = store.FilePath;
            await store.SaveAsync(settings);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"color\": \"Blue\"", json, StringComparison.Ordinal);
            var loaded = (await store.LoadAsync()).Settings;
            Assert.Equal(settings.Tags, loaded.Tags);
            Assert.Equal(["прод"], new LauncherUserData(loaded).TagsOf(Base(Document(), "ЗУП")));

            await File.WriteAllTextAsync(path, """{ "tags": [ { "name": "  " }, null ], "infoBaseTags": null }""");
            loaded = (await store.LoadAsync()).Settings;
            Assert.Empty(loaded.Tags);
            Assert.Empty(loaded.InfoBaseTags);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Json_export_carries_tags_and_import_adds_missing_ones_keeping_own_colors()
    {
        var source = Document();
        var data = new LauncherUserData(new LauncherSettings());
        data.SetTags(Base(source, "ЗУП"), ["прод", "ЗУП"]);
        data.SetTagAppearance("ЗУП", TagColor.Purple, showInRow: false);
        data.EnsureTag("без баз");

        var file = InfoBaseListExchange.Parse(InfoBaseListExchange.Serialize(
            InfoBaseListExchange.Export(source.Sections, data, new ExportOptions(), wholeList: true)));

        Assert.Equal(["прод", "ЗУП"], file.InfoBases.Single(b => b.Name == "ЗУП").Launcher!.Tags!.Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(["ЗУП", "прод"], file.Tags!.Select(t => t.Name)); // только метки сохранённых баз
        Assert.Equal(new ExportedTag { Name = "ЗУП", Color = "purple", ShowInRow = false }, file.Tags![0]);
        Assert.Null(file.InfoBases.Single(b => b.Name == "Склад").Launcher);

        // Без «Избранное, метки…» метки не сохраняются.
        var bare = InfoBaseListExchange.Export(source.Sections, data, new ExportOptions(BaseSettings: false), wholeList: true);
        Assert.Null(bare.Tags);

        var current = new V8iDocument();
        var target = new LauncherUserData(new LauncherSettings());
        target.EnsureTag("прод", TagColor.Gray);
        var added = new List<V8iSection>();
        InfoBaseListExchange.Import(current, file, ImportMode.Add, [], added);
        InfoBaseListExchange.ApplySettings(file, added, target, replaceFolderParameters: false);

        Assert.Equal(["ЗУП", "прод"], target.TagsOf(Base(current, "ЗУП")));
        Assert.Equal(TagColor.Gray, target.Tag("прод")!.Color); // своя метка — свой цвет
        Assert.Equal(new TagDefinition { Name = "ЗУП", Color = TagColor.Purple, ShowInRow = false }, target.Tag("ЗУП"));
    }

    [Fact]
    public void Old_json_without_tags_loads()
    {
        var file = InfoBaseListExchange.Parse(Encoding.UTF8.GetBytes("""
            { "format": "YetAnotherOneCLauncher.InfoBaseList", "infoBases": [ { "name": "А", "connection": "File=\"C:\\A\";" } ] }
            """));

        Assert.Null(file.Tags);
    }

    [Fact]
    public void Backup_settings_restore_tags_and_old_backup_keeps_current_ones()
    {
        var data = new LauncherUserData(new LauncherSettings());
        data.SetTags(Base(Document(), "ЗУП"), ["прод"]);
        var snapshot = ListBackupSettings.From(data.Settings);

        data.DeleteTag("прод");
        data.SetTags(Base(Document(), "Склад"), ["тест"]);
        snapshot.ApplyTo(data.Settings);

        Assert.Equal(["прод"], data.TagsOf(Base(Document(), "ЗУП")));
        Assert.Empty(data.TagsOf(Base(Document(), "Склад")));

        // Копия из версии без меток: метки не трогаются.
        new ListBackupSettings().ApplyTo(data.Settings);
        Assert.Equal(["прод"], data.TagsOf(Base(Document(), "ЗУП")));
    }
}
