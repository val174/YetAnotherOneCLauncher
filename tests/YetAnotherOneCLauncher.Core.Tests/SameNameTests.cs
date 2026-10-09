using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Две базы с одним названием не допускаются: копия, загрузка из файла и правка записи текстом.</summary>
public class SameNameTests
{
    private const string List = """
        [Бухгалтерия]
        Connect=File="C:\Bases\Buh";
        ID=00000000-0000-0000-0000-000000000001
        [Бухгалтерия (копия)]
        Connect=File="C:\Bases\BuhCopy";
        ID=00000000-0000-0000-0000-000000000002
        [Склад]
        Connect=Srvr="srv";Ref="sklad";
        ID=00000000-0000-0000-0000-000000000003
        """;

    private static V8iDocument Document() => V8iDocument.Parse(List);

    [Fact]
    public void Copy_gets_free_name_in_all_lists()
    {
        var document = Document();
        var common = new InfoBase(
            V8iDocument.Parse("[Бухгалтерия]\r\nConnect=File=\"\\\\srv\\buh\";\r\n").Sections.Single(s => s.Name.Length > 0),
            new ListSource(ListSourceKind.Common, @"\\srv\list.v8i"));

        // «Бухгалтерия (копия)» уже есть в личном, «… (копия 2)» — в другом списке.
        var copy = PersonalListEditor.CopyBase(document, common, ["Бухгалтерия (КОПИЯ 2)"]);

        Assert.Equal("Бухгалтерия (копия 3)", copy.Name);
        Assert.Equal("Склад (копия)", PersonalListEditor.UniqueCopyName("Склад", PersonalListEditor.BaseNames(document)));
    }

    [Fact]
    public void Import_skips_bases_with_taken_names_and_counts_them()
    {
        var document = Document();
        var source = V8iDocument.Parse("""
            [бухгалтерия]
            Connect=File="D:\Other\Buh";
            [Зарплата]
            Connect=Srvr="srv";Ref="zup";
            [Зарплата]
            Connect=Srvr="srv2";Ref="zup";
            [Торговля]
            Connect=Srvr="srv";Ref="ut";
            [Склад]
            Connect=Srvr="srv";Ref="sklad";
            """);

        var result = PersonalListEditor.Import(document, source, ["Торговля"]);

        // «бухгалтерия» — занято в личном (регистр не важен), второй «Зарплаты» — занято только что добавленной,
        // «Торговля» — в другом списке; «Склад» — та же база (по подключению).
        Assert.Equal(new ImportResult(Added: 1, Skipped: 1, SameName: 3), result);
        Assert.Single(document.Sections, s => s.Name == "Зарплата");
        Assert.Equal("Srvr=\"srv\";Ref=\"zup\";", document.Sections.Single(s => s.Name == "Зарплата").Get("Connect"));
    }

    [Fact]
    public void Text_edit_cannot_rename_base_to_taken_name()
    {
        var document = Document();
        var target = EntryRef.Of(new InfoBase(document.Sections.Single(s => s.Name == "Склад"), new ListSource(ListSourceKind.Personal, "")));

        var error = Assert.Throws<ListEditException>(() =>
            PersonalListEditor.ReplaceText(document, target, "[ Бухгалтерия ]\r\nConnect=Srvr=\"srv\";Ref=\"sklad\";", ["Бухгалтерия"]));
        Assert.Contains("уже есть база «Бухгалтерия»", error.Message, StringComparison.Ordinal);

        // Прежнее название (даже в другом регистре ключей) и свободное — можно.
        PersonalListEditor.ReplaceText(document, target, "[Склад]\r\nConnect=Srvr=\"srv\";Ref=\"sklad\";\r\nVersion=8.3", ["Бухгалтерия", "Склад"]);
        PersonalListEditor.ReplaceText(document, EntryRef.Of(new InfoBase(document.Sections.Single(s => s.Name == "Склад"), new ListSource(ListSourceKind.Personal, ""))),
            "[Склад 2]\r\nConnect=Srvr=\"srv\";Ref=\"sklad\";", ["Бухгалтерия"]);
        Assert.Contains(document.Sections, s => s.Name == "Склад 2");
    }
}
