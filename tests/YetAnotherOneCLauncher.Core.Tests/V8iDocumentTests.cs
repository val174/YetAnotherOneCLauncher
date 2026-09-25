using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Tests;

public class V8iDocumentTests
{
    [Fact]
    public void Reads_all_sections_including_duplicate_names()
    {
        var document = V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));

        Assert.Equal(6, document.Sections.Count);
        Assert.Equal(2, document.Sections.Count(s => s.Name == "Тест"));
        Assert.Equal("База с \"кавычками\"", document.Sections[^1].Name);
        Assert.Equal(TextEncodingKind.Utf8, document.Format.EncodingKind);
        Assert.True(document.Format.HasBom);
        Assert.Equal("\r\n", document.Format.NewLine);
    }

    [Fact]
    public void Section_without_connect_is_folder()
    {
        var document = V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));
        var folderSection = document.Sections[0];

        Assert.Equal("Бухгалтерия", folderSection.Name);
        Assert.Null(folderSection.Get(V8iKeys.Connect));

        var folder = new InfoBaseFolder(folderSection, new ListSource(ListSourceKind.Personal, "x"));
        Assert.Equal("/Бухгалтерия", folder.FullPath);
    }

    [Fact]
    public void Maps_section_to_info_base()
    {
        var document = V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));
        var infoBase = new InfoBase(document.Sections[1], new ListSource(ListSourceKind.Personal, "x"));

        Assert.Equal("Бухгалтерия (рабочая)", infoBase.Name);
        Assert.Equal("3f2a1c7e-8d4b-4e6a-9b1f-0c5d7e8a9b12", infoBase.Id);
        Assert.Equal(ConnectionKind.Server, infoBase.ConnectionKind);
        Assert.Equal("/Бухгалтерия", infoBase.FolderPath);
        Assert.Equal(16384L, infoBase.OrderInTree);
        Assert.Equal(ClientApp.ThinClient, infoBase.App);
        Assert.True(infoBase.WindowsAuthentication);
        Assert.Equal("8.3", infoBase.Version);
        Assert.Equal("/UseHwLicenses-", infoBase.AdditionalParameters);
        Assert.False(infoBase.IsReadOnly);
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var document = V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));

        Assert.Equal(document.Sections[1].Get("Connect"), document.Sections[1].Get("CONNECT"));
    }

    [Fact]
    public void Round_trip_of_real_sample_is_byte_exact()
    {
        var original = Fixtures.Read("ibases_sample.v8i");

        var saved = V8iDocument.Parse(original).ToBytes();

        Assert.Equal(original, saved);
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf8, true, "\r\n", true)]
    [InlineData(TextEncodingKind.Utf8, false, "\n", true)]
    [InlineData(TextEncodingKind.Utf16LittleEndian, true, "\r\n", false)]
    [InlineData(TextEncodingKind.Windows1251, false, "\r\n", true)]
    public void Round_trip_preserves_encoding_new_lines_and_final_new_line(
        TextEncodingKind encoding,
        bool bom,
        string newLine,
        bool finalNewLine)
    {
        var lines = new[]
        {
            "[Бухгалтерия]",
            "Connect=File=\"C:\\Базы\\Бух\";",
            "ID=11111111-1111-1111-1111-111111111111",
            "; комментарий",
            "",
            "[Папка]",
            "Folder=/",
        };
        var text = string.Join(newLine, lines) + (finalNewLine ? newLine : string.Empty);
        var bytes = TextFileCodec.Encode(text, new TextFormat(encoding, bom, newLine));

        var saved = V8iDocument.Parse(bytes).ToBytes();

        Assert.Equal(bytes, saved);
    }

    [Fact]
    public void Changing_a_value_touches_only_that_line()
    {
        var original = Fixtures.Read("ibases_sample.v8i");
        var document = V8iDocument.Parse(original);

        document.Sections[1].Set("Version", "8.3.25");

        var originalText = TextFileCodec.Decode(original).Text;
        var expected = originalText.Replace("WA=1\r\nVersion=8.3\r\n", "WA=1\r\nVersion=8.3.25\r\n");
        Assert.NotEqual(originalText, expected);
        Assert.Equal(expected, document.ToText());
    }

    [Fact]
    public void New_key_is_inserted_after_last_key_value_line()
    {
        var document = V8iDocument.Parse("[A]\r\nConnect=File=\"C:\\A\";\r\n\r\n[B]\r\nFolder=/\r\n");

        document.Sections[0].Set("Version", "8.3");

        Assert.Equal(
            "[A]\r\nConnect=File=\"C:\\A\";\r\nVersion=8.3\r\n\r\n[B]\r\nFolder=/\r\n",
            document.ToText());
    }

    [Fact]
    public void Unknown_keys_are_preserved()
    {
        var document = V8iDocument.Parse(Fixtures.Read("ibases_sample.v8i"));
        var section = document.Sections.Single(s => s.Get("UnknownKey") is not null);

        section.Set(V8iKeys.OrderInTree, "1");

        Assert.Contains("UnknownKey=значение неизвестного ключа сохраняется", document.ToText());
    }

    [Fact]
    public void Renaming_section_rewrites_header()
    {
        var document = V8iDocument.Parse("[Old]\r\nFolder=/\r\n");

        document.Sections[0].Name = "New";

        Assert.Equal("[New]\r\nFolder=/\r\n", document.ToText());
    }

    [Fact]
    public void Name_with_brackets_is_read_whole()
    {
        var document = V8iDocument.Parse("[База [тест]]\r\nFolder=/\r\n");

        Assert.Equal("База [тест]", document.Sections[0].Name);
    }

    [Fact]
    public void Lines_before_first_section_are_kept()
    {
        const string text = "; заголовок\r\n[A]\r\nFolder=/\r\n";
        var document = V8iDocument.Parse(text);

        Assert.Single(document.Preamble);
        Assert.Equal(text, document.ToText());
    }

    [Fact]
    public void Empty_file_gives_empty_document()
    {
        var document = V8iDocument.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Empty(document.Sections);
        Assert.Equal(string.Empty, document.ToText());
    }

    [Fact]
    public async Task Save_writes_atomically_and_keeps_backup()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ibases.v8i");
        var backup = temp.Combine("ibases.v8i.bak");
        var original = Fixtures.Read("ibases_sample.v8i");
        await File.WriteAllBytesAsync(path, original);

        var document = await V8iDocument.LoadAsync(path);
        document.Sections[1].Set("Version", "8.3.25");
        await document.SaveAsync(path, backup);

        Assert.Equal(original, await File.ReadAllBytesAsync(backup));
        var reloaded = await V8iDocument.LoadAsync(path);
        Assert.Equal("8.3.25", reloaded.Sections[1].Get("Version"));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length); // временных файлов не осталось
    }

    [Fact]
    public async Task Save_creates_missing_directory_and_file()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("1C", "1CEStart", "ibases.v8i");
        var document = new V8iDocument();
        var section = new V8iSection("Новая база");
        section.Set(V8iKeys.Connect, ConnectionString.ForFile("C:\\Bases\\New").ToString());
        document.Sections.Add(section);

        await document.SaveAsync(path);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(
            "[Новая база]\r\nConnect=File=\"C:\\Bases\\New\";\r\n",
            TextFileCodec.Decode(bytes).Text);
    }
}
