using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Дублирование базы: строка-копия «Имя_копия» с новым ID — за источником, в другой папке или перед другой базой.</summary>
public class DuplicateBaseTests
{
    private static readonly ListSource Personal = new(ListSourceKind.Personal, "ibases.v8i");

    private const string List = """
        [Бухгалтерия]
        Connect=Srvr="srv";Ref="buh";
        ID=00000000-0000-0000-0000-000000000001
        OrderInList=16384
        Folder=/Рабочие
        OrderInTree=16384
        App=ThinClientApp
        Version=8.3.24
        [Зарплата]
        Connect=Srvr="srv";Ref="zup";
        ID=00000000-0000-0000-0000-000000000002
        OrderInList=32768
        Folder=/Рабочие
        OrderInTree=32768
        [Склад]
        Connect=File="C:\Bases\Sklad";
        ID=00000000-0000-0000-0000-000000000003
        OrderInList=49152
        Folder=/
        OrderInTree=16384
        """;

    private static V8iDocument Document() => V8iDocument.Parse(List);

    private static InfoBase Base(V8iDocument document, string name) => new(document.Sections.Single(s => s.Name == name), Personal);

    [Fact]
    public void Duplicate_is_placed_after_source_with_same_settings_and_new_id()
    {
        var document = Document();

        var copy = PersonalListEditor.DuplicateBase(document, Base(document, "Бухгалтерия"), "/Рабочие", afterSource: true);

        Assert.Equal("Бухгалтерия_копия", copy.Name);
        Assert.Equal("Srvr=\"srv\";Ref=\"buh\";", copy.Get("Connect"));
        Assert.Equal("ThinClientApp", copy.Get("App"));
        Assert.Equal("8.3.24", copy.Get("Version"));
        Assert.Equal("/Рабочие", copy.Get("Folder"));
        Assert.NotEqual("00000000-0000-0000-0000-000000000001", copy.Get("ID"));
        Assert.True(Guid.TryParse(copy.Get("ID"), out _));
        Assert.Equal(["Бухгалтерия", "Бухгалтерия_копия", "Зарплата"], Order(document, "/Рабочие"));
        Assert.Equal("16384", document.Sections.Single(s => s.Name == "Бухгалтерия").Get("OrderInTree")); // источник не тронут
    }

    [Fact]
    public void Second_duplicate_gets_next_free_name()
    {
        var document = Document();
        var source = Base(document, "Склад");

        PersonalListEditor.DuplicateBase(document, source, "/", afterSource: true);
        var second = PersonalListEditor.DuplicateBase(document, source, "/", afterSource: true, takenNames: ["Склад_копия 2"]);

        Assert.Equal("Склад_копия 3", second.Name);
        Assert.Equal("Склад_копия", PersonalListEditor.UniqueDuplicateName("Склад", ["Склад"]));
    }

    [Fact]
    public void Repeated_duplicates_are_numbered_with_space()
    {
        var document = Document();
        var source = Base(document, "Бухгалтерия");

        var names = Enumerable.Range(0, 3)
            .Select(_ => PersonalListEditor.DuplicateBase(document, source, "/Рабочие", afterSource: true).Name)
            .ToList();

        Assert.Equal(["Бухгалтерия_копия", "Бухгалтерия_копия 2", "Бухгалтерия_копия 3"], names);
    }

    [Fact]
    public void Duplicate_dropped_on_folder_or_before_base()
    {
        var document = Document();

        PersonalListEditor.DuplicateBase(document, Base(document, "Склад"), "/Рабочие"); // на папку — в конец
        Assert.Equal(["Бухгалтерия", "Зарплата", "Склад_копия"], Order(document, "/Рабочие"));
        Assert.Equal(["Склад", "Рабочие"], Order(document, "/")); // источник остался на месте (у папки «Рабочие» нет записи — она после баз)

        PersonalListEditor.DuplicateBase(document, Base(document, "Зарплата"), "/Рабочие", before: EntryRef.Of(Base(document, "Бухгалтерия")));
        Assert.Equal(["Зарплата_копия", "Бухгалтерия", "Зарплата", "Склад_копия"], Order(document, "/Рабочие"));
    }

    [Fact]
    public void Base_from_common_list_is_duplicated_into_personal_list()
    {
        var document = Document();
        var common = new InfoBase(
            V8iDocument.Parse("[Общая]\r\nConnect=File=\"\\\\srv\\common\";\r\nID=00000000-0000-0000-0000-0000000000aa\r\nFolder=/\r\n").Sections.Single(s => s.Name.Length > 0),
            new ListSource(ListSourceKind.Common, @"\\srv\list.v8i"));

        var copy = PersonalListEditor.DuplicateBase(document, common, "/", afterSource: true);

        Assert.Equal("Общая_копия", copy.Name);
        Assert.Equal(["Склад", "Общая_копия", "Рабочие"], Order(document, "/")); // источника в личном нет — в конец
    }

    private static List<string> Order(V8iDocument document, string folder)
    {
        var catalog = InfoBaseCatalog.Build([new LoadedList(Personal, document, null)], StarterConfig.Empty, []);
        var node = catalog.BuildTree(CatalogSortMode.Custom);
        foreach (var segment in FolderPaths.Split(folder))
        {
            node = node.SubFolders.Single(f => f.Name == segment);
        }

        return node.Items.Select(i => i.Name).ToList();
    }
}
