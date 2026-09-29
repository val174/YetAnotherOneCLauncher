using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Tests;

public class PersonalListEditorTests
{
    private static readonly ListSource Personal = new(ListSourceKind.Personal, "ibases.v8i");

    [Fact]
    public void Added_base_has_starter_key_order_and_new_id()
    {
        var document = Sample();
        var draft = new InfoBaseDraft
        {
            Name = "Новая",
            Kind = ConnectionKind.Server,
            Server = "srv:1541",
            InfobaseName = "new_db",
            FolderPath = "/Бухгалтерия",
            Version = "8.3.27",
        };

        var section = PersonalListEditor.AddBase(document, draft);

        Assert.Equal(
            new[] { "Connect", "ID", "OrderInList", "Folder", "OrderInTree", "External", "ClientConnectionSpeed", "App", "WA", "Version" },
            section.Lines.Select(l => l.Key));
        Assert.Equal("Srvr=\"srv:1541\";Ref=\"new_db\";", section.Get("Connect"));
        Assert.True(Guid.TryParse(section.Get("ID"), out _));
        Assert.Equal("81920", section.Get("OrderInList")); // max 65536 + 16384
        Assert.Equal("32768", section.Get("OrderInTree")); // в /Бухгалтерия была 16384
        Assert.Equal("1", section.Get("WA"));
        Assert.Same(section, document.Sections[^1]);
    }

    [Fact]
    public void Next_order_is_integer_after_fractional_and_ignores_minus_one()
    {
        var document = V8iDocument.Parse(string.Join("\r\n",
            "[Старая]", "Connect=File=\"C:\\A\";", "OrderInList=-1", "Folder=/", "OrderInTree=18590.9903978051",
            "[Другая]", "Connect=File=\"C:\\B\";", "OrderInList=573440", "Folder=/", "OrderInTree=202.27"));

        var section = PersonalListEditor.AddBase(document, new InfoBaseDraft { Name = "Новая", FilePath = @"C:\C" });

        Assert.Equal("589824", section.Get("OrderInList"));
        Assert.Equal("34974", section.Get("OrderInTree")); // floor(18590.99) + 16384
    }

    [Fact]
    public void Adding_does_not_touch_existing_text()
    {
        var original = Fixtures.Read("ibases_sample.v8i");
        var document = V8iDocument.Parse(original);

        PersonalListEditor.AddBase(document, new InfoBaseDraft { Name = "X", FilePath = @"C:\X" });
        var text = document.ToText();

        Assert.StartsWith(V8iDocument.Parse(original).ToText(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_changes_only_affected_lines_and_keeps_unknown_keys()
    {
        var document = Sample();
        var target = Base(document, "Тест", "ws");
        var draft = InfoBaseDraft.From(target) with { Name = "Тест (веб)", Version = "8.3.27" };
        var before = document.ToText();

        PersonalListEditor.UpdateBase(document, EntryRef.Of(target), draft);
        var after = document.ToText();

        var changed = Diff(before, after);
        Assert.Equal(new[] { "[Тест] -> [Тест (веб)]", " -> Version=8.3.27" }, changed);
        Assert.Contains("UnknownKey=значение неизвестного ключа сохраняется", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_connection_kind_keeps_extra_connection_keys()
    {
        var document = V8iDocument.Parse("[A]\r\nConnect=ws=\"http://h/a\";wsn=\"user\";\r\nID=1\r\n");
        var target = new InfoBase(document.Sections[0], Personal);

        PersonalListEditor.UpdateBase(document, EntryRef.Of(target), InfoBaseDraft.From(target) with { WebUrl = "https://h/b" });

        Assert.Equal("ws=\"https://h/b\";wsn=\"user\";", document.Sections[0].Get("Connect"));
    }

    [Fact]
    public void Moving_to_another_folder_puts_base_last_there()
    {
        var document = Sample();
        var target = Base(document, "Тест", "File");

        PersonalListEditor.UpdateBase(document, EntryRef.Of(target), InfoBaseDraft.From(target) with { FolderPath = "/Бухгалтерия" });

        var section = FindByName(document, "Тест", "File");
        Assert.Equal("/Бухгалтерия", section.Get("Folder"));
        Assert.Equal("32768", section.Get("OrderInTree"));
    }

    [Fact]
    public void Invalid_draft_is_rejected()
    {
        var document = Sample();

        var ex = Assert.Throws<ListEditException>(() => PersonalListEditor.AddBase(document, new InfoBaseDraft { Name = " ", Kind = ConnectionKind.Web, WebUrl = "ftp://x" }));

        Assert.Equal(ListEditErrorKind.Invalid, ex.Kind);
        Assert.Contains("название", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_removes_only_that_section()
    {
        var document = Sample();
        var count = document.Sections.Count;

        PersonalListEditor.DeleteBase(document, EntryRef.Of(Base(document, "Тест", "ws")));

        Assert.Equal(count - 1, document.Sections.Count);
        Assert.DoesNotContain(document.Sections, s => s.Get("Connect")?.StartsWith("ws=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Base_changed_elsewhere_is_a_conflict_and_deleted_base_is_not_found()
    {
        var loaded = Sample();
        var target = EntryRef.Of(Base(loaded, "Тест", "File"));

        var changed = Sample();
        FindByName(changed, "Тест", "File").Set("Version", "8.3.99");
        var conflict = Assert.Throws<ListEditException>(() => PersonalListEditor.DeleteBase(changed, target));
        Assert.Equal(ListEditErrorKind.Conflict, conflict.Kind);
        Assert.True(conflict.RequiresReload);

        var deleted = Sample();
        deleted.Sections.Remove(FindByName(deleted, "Тест", "File"));
        Assert.Equal(ListEditErrorKind.NotFound, Assert.Throws<ListEditException>(() => PersonalListEditor.DeleteBase(deleted, target)).Kind);
    }

    [Fact]
    public void Edits_apply_to_fresh_document_when_other_entries_changed()
    {
        var loaded = Sample();
        var target = EntryRef.Of(Base(loaded, "Тест", "File"));

        var fresh = Sample();
        FindByName(fresh, "Тест", "ws").Set("Version", "8.3.99"); // другая запись — не мешает
        PersonalListEditor.DeleteBase(fresh, target);

        Assert.Contains(fresh.Sections, s => s.Get("Version") == "8.3.99");
    }

    [Fact]
    public void Folders_add_rename_with_descendants_and_delete()
    {
        var document = Sample();

        var added = PersonalListEditor.AddFolder(document, "/Бухгалтерия", "Новая");
        Assert.Equal(new[] { "ID", "OrderInList", "Folder", "OrderInTree", "External" }, added.Lines.Select(l => l.Key));
        Assert.Equal(ListEditErrorKind.Invalid, Assert.Throws<ListEditException>(() => PersonalListEditor.AddFolder(document, "/", "бухгалтерия")).Kind);

        var folder = new InfoBaseFolder(document.Sections.First(s => s.Name == "Бухгалтерия"), Personal);
        PersonalListEditor.RenameFolder(document, EntryRef.Of(folder), "Учёт");

        Assert.Equal("Учёт", document.Sections[0].Name);
        Assert.Equal("/Учёт", FindByName(document, "Бухгалтерия (рабочая)").Get("Folder"));
        Assert.Equal("/Учёт/Архив", FindByName(document, "Бухгалтерия (копия)").Get("Folder"));
        Assert.Equal("/Учёт", FindByName(document, "Новая").Get("Folder"));
        Assert.Equal(3, PersonalListEditor.CountDescendants(document, "/Учёт"));

        var renamed = new InfoBaseFolder(document.Sections[0], Personal);
        PersonalListEditor.DeleteFolder(document, EntryRef.Of(renamed));
        Assert.DoesNotContain(document.Sections, s => s.Name is "Учёт" or "Бухгалтерия (рабочая)" or "Бухгалтерия (копия)" or "Новая");
        Assert.Contains(document.Sections, s => s.Name == "Тест");
    }

    [Fact]
    public void Implied_folder_can_be_renamed_through_its_bases()
    {
        var document = Sample();

        PersonalListEditor.RenameFolder(document, EntryRef.OfImpliedFolder("/Бухгалтерия/Архив"), "2023");

        Assert.Equal("/Бухгалтерия/2023", FindByName(document, "Бухгалтерия (копия)").Get("Folder"));
    }

    [Fact]
    public void Move_places_before_target_and_renumbers_folder()
    {
        var document = Sample();
        var file = EntryRef.Of(Base(document, "Тест", "File"));
        var quotes = EntryRef.Of(Base(document, "База с \"кавычками\""));

        PersonalListEditor.Move(document, quotes, "/", before: file);

        var root = Order(document, "/");
        // Было: Тест (8192), Бухгалтерия (16384), Тест (65536), кавычки (без порядка).
        Assert.Equal(new[] { "База с \"кавычками\"", "Тест", "Бухгалтерия", "Тест" }, root);
        Assert.Equal("16384", FindByName(document, "База с \"кавычками\"").Get("OrderInTree"));
        Assert.Equal("65536", FindByName(document, "Тест", "ws").Get("OrderInTree"));
    }

    [Fact]
    public void Folder_cannot_be_moved_into_itself()
    {
        var document = Sample();
        var folder = EntryRef.Of(new InfoBaseFolder(document.Sections[0], Personal));

        Assert.Equal(ListEditErrorKind.Invalid, Assert.Throws<ListEditException>(() => PersonalListEditor.Move(document, folder, "/Бухгалтерия/Архив")).Kind);
    }

    [Fact]
    public void Move_folder_moves_its_contents()
    {
        var document = Sample();
        PersonalListEditor.AddFolder(document, "/", "Архивы");
        var folder = EntryRef.Of(new InfoBaseFolder(document.Sections[0], Personal));

        PersonalListEditor.Move(document, folder, "/Архивы");

        Assert.Equal("/Архивы/Бухгалтерия/Архив", FindByName(document, "Бухгалтерия (копия)").Get("Folder"));
        Assert.Equal("/Архивы", document.Sections[0].Get("Folder"));
    }

    private const string FruitList = """
        [Яблоко]
        Connect=File="C:\a";
        Folder=/
        OrderInTree=16384
        [Банан]
        Connect=File="C:\b";
        Folder=/
        OrderInTree=32768
        [Папка]
        Connect=
        Folder=/
        OrderInTree=49152
        [Вишня]
        Connect=File="C:\c";
        Folder=/
        OrderInTree=65536
        """;

    [Fact]
    public void Tree_by_name_puts_folders_first_then_alphabet()
    {
        var document = V8iDocument.Parse(FruitList);

        Assert.Equal(new[] { "Яблоко", "Банан", "Папка", "Вишня" }, Order(document, "/"));
        Assert.Equal(new[] { "Папка", "Банан", "Вишня", "Яблоко" }, Order(document, "/", CatalogSortMode.Name));
    }

    [Fact]
    public void SortByName_writes_name_order_everywhere()
    {
        var document = Sample();
        var byName = Order(document, "/", CatalogSortMode.Name);
        var nestedByName = Order(document, "/Бухгалтерия", CatalogSortMode.Name);
        Assert.Equal(new[] { "Архив", "Бухгалтерия (рабочая)" }, nestedByName);

        PersonalListEditor.SortByName(document);

        Assert.Equal(byName, Order(document, "/"));
        // «Архив» — папка без своей записи (есть только в пути базы): порядок ей записать некуда, в своём порядке она в конце.
        Assert.Equal(new[] { "Бухгалтерия (рабочая)", "Архив" }, Order(document, "/Бухгалтерия"));
    }

    [Fact]
    public void SortFolderByName_changes_only_that_folder()
    {
        var document = V8iDocument.Parse(FruitList + """

            [Слива]
            Connect=File="C:\d";
            Folder=/Папка
            OrderInTree=16384
            [Абрикос]
            Connect=File="C:\e";
            Folder=/Папка
            OrderInTree=32768
            """);

        Assert.Equal(2, PersonalListEditor.SortFolderByName(document, "/Папка"));

        Assert.Equal(new[] { "Абрикос", "Слива" }, Order(document, "/Папка"));
        Assert.Equal(new[] { "Яблоко", "Банан", "Папка", "Вишня" }, Order(document, "/")); // корень не тронут

        Assert.Equal(4, PersonalListEditor.SortFolderByName(document, "/"));
        Assert.Equal(new[] { "Папка", "Банан", "Вишня", "Яблоко" }, Order(document, "/"));
        Assert.Equal(0, PersonalListEditor.SortFolderByName(document, "/Нет такой"));
    }

    [Fact]
    public void Moves_made_in_name_order_write_that_order_first()
    {
        var document = V8iDocument.Parse(FruitList);
        var apple = EntryRef.Of(Base(document, "Яблоко"));

        // В своём порядке «Яблоко» первое — выше некуда; по наименованию оно последнее.
        Assert.False(PersonalListEditor.MoveBy(document, apple, -1));
        Assert.True(PersonalListEditor.MoveBy(document, apple, -1, sortByNameFirst: true));
        Assert.Equal(new[] { "Папка", "Банан", "Яблоко", "Вишня" }, Order(document, "/"));

        document = V8iDocument.Parse(FruitList);
        PersonalListEditor.Move(
            document, EntryRef.Of(Base(document, "Вишня")), "/", before: EntryRef.Of(Base(document, "Банан")), sortByNameFirst: true);
        Assert.Equal(new[] { "Папка", "Вишня", "Банан", "Яблоко" }, Order(document, "/"));
    }

    [Fact]
    public void MoveBy_reorders_within_folder()
    {
        var document = Sample();
        var before = Order(document, "/");
        var last = EntryRef.Of(Base(document, "Тест", "ws"));

        Assert.True(PersonalListEditor.MoveBy(document, last, -1));
        Assert.False(PersonalListEditor.MoveBy(document, EntryRef.Of(Base(document, "Тест", "File")), -1)); // уже первая

        var after = Order(document, "/");
        Assert.NotEqual(before, after);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public void Replace_text_swaps_the_section()
    {
        var document = Sample();
        var target = EntryRef.Of(Base(document, "Тест", "File"));

        PersonalListEditor.ReplaceText(document, target, "[Тест вручную]\r\nConnect=File=\"D:\\Other\";\r\nID=3f2a1c7e-8d4b-4e6a-9b1f-0c5d7e8a9b14\r\nMyKey=1");

        var section = FindByName(document, "Тест вручную");
        Assert.Equal("1", section.Get("MyKey"));
        Assert.Throws<ListEditException>(() => PersonalListEditor.ReplaceText(document, EntryRef.Of(Base(document, "Тест вручную")), "без заголовка"));
    }

    [Fact]
    public void Copy_from_common_list_gets_new_id_and_suffix()
    {
        var document = Sample();
        var common = new InfoBase(V8iDocument.Parse("[Общая]\r\nConnect=Srvr=\"s\";Ref=\"c\";\r\nID=aaaa\r\nFolder=/\r\nCustom=1\r\n").Sections[0], new ListSource(ListSourceKind.Common, "\\\\srv\\list.v8i"));

        var copy = PersonalListEditor.CopyBase(document, common);

        Assert.Equal("Общая (копия)", copy.Name);
        Assert.NotEqual("aaaa", copy.Get("ID"));
        Assert.Equal("1", copy.Get("Custom"));
    }

    [Fact]
    public void Import_skips_existing_bases_and_folders()
    {
        var document = Sample();
        var source = V8iDocument.Parse("""
            [Бухгалтерия]
            Folder=/
            [Тест под другим именем]
            Connect=File="D:\Test";
            [Дубль по ID]
            Connect=File="E:\Other";
            ID=3f2a1c7e-8d4b-4e6a-9b1f-0c5d7e8a9b12
            [Новая]
            Connect=File="E:\New";
            ID=bbbb
            [Новая ещё раз]
            Connect=File="E:\New";
            """.ReplaceLineEndings("\r\n"));

        var result = PersonalListEditor.Import(document, source);

        Assert.Equal(new ImportResult(1, 4), result);
        Assert.Equal("Новая", document.Sections[^1].Name);
    }

    [Fact]
    public void Export_copies_sections_into_new_document()
    {
        var document = Sample();
        var exported = PersonalListEditor.Export(document.Sections.Take(2));

        exported.Sections[0].Name = "Изменена";

        Assert.Equal("Бухгалтерия", document.Sections[0].Name);
        Assert.Equal(2, exported.Sections.Count);
        Assert.Equal(TextFormat.V8iDefault, exported.Format);
    }

    [Fact]
    public void Catalog_can_be_rebuilt_with_new_personal_document()
    {
        var document = Sample();
        var lists = new[] { new LoadedList(Personal, document, null) };
        var catalog = InfoBaseCatalog.Build(lists, StarterConfig.Empty, [new CatalogWarning(CatalogWarningLevel.Warning, "общий недоступен", @"\\srv\x.v8i")]);

        var edited = Sample();
        PersonalListEditor.AddBase(edited, new InfoBaseDraft { Name = "Новая", FilePath = @"C:\N" });
        var rebuilt = catalog.WithPersonalDocument(edited);

        Assert.Equal(catalog.InfoBases.Count + 1, rebuilt.InfoBases.Count);
        Assert.Contains(rebuilt.Warnings, w => w.Message == "общий недоступен");
    }

    private static V8iDocument Sample() => V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));

    private static InfoBase Base(V8iDocument document, string name, string? connectPrefix = null) =>
        new(FindByName(document, name, connectPrefix), Personal);

    private static V8iSection FindByName(V8iDocument document, string name, string? connectPrefix = null) =>
        document.Sections.Single(s => s.Name == name && (connectPrefix is null || s.Get("Connect")?.StartsWith(connectPrefix, StringComparison.Ordinal) == true));

    private static List<string> Order(V8iDocument document, string folder, CatalogSortMode sortMode = CatalogSortMode.Custom)
    {
        var catalog = InfoBaseCatalog.Build([new LoadedList(Personal, document, null)], StarterConfig.Empty, []);
        var node = catalog.BuildTree(sortMode);
        foreach (var segment in FolderPaths.Split(folder))
        {
            node = node.SubFolders.Single(f => f.Name == segment);
        }

        return node.Items.Select(i => i.Name).ToList();
    }

    /// <summary>Построчная разница двух текстов одинаковой длины в строках: «было -> стало».</summary>
    private static List<string> Diff(string before, string after)
    {
        var a = before.Split("\r\n");
        var b = after.Split("\r\n").ToList();
        var result = new List<string>();
        var j = 0;
        foreach (var line in a)
        {
            if (j < b.Count && b[j] == line)
            {
                j++;
                continue;
            }

            // Строка изменена или перед ней вставлена новая.
            if (j + 1 < b.Count && b[j + 1] == line)
            {
                result.Add(" -> " + b[j]);
                j += 2;
                continue;
            }

            result.Add($"{line} -> {b[j]}");
            j++;
        }

        result.AddRange(b.Skip(j).Select(l => " -> " + l));
        return result.Where(r => r != " -> ").ToList();
    }
}
