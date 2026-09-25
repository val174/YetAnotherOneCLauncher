using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Tests;

public class ConnectionStringTests
{
    [Fact]
    public void Parses_file_connection()
    {
        var cs = ConnectionString.Parse("File=\"C:\\Bases\\Buh\";");

        Assert.Equal(ConnectionKind.File, cs.Kind);
        Assert.Equal("C:\\Bases\\Buh", cs.FilePath);
        Assert.False(cs.HasErrors);
    }

    [Fact]
    public void Parses_server_connection()
    {
        var cs = ConnectionString.Parse("Srvr=\"srv1c:1541\";Ref=\"buh_prod\";");

        Assert.Equal(ConnectionKind.Server, cs.Kind);
        Assert.Equal("srv1c:1541", cs.Server);
        Assert.Equal("buh_prod", cs.InfobaseName);
        Assert.Equal("srv1c:1541\\buh_prod", cs.ToDisplayString());
    }

    [Fact]
    public void Parses_web_connection()
    {
        var cs = ConnectionString.Parse("ws=\"http://host/buh\";");

        Assert.Equal(ConnectionKind.Web, cs.Kind);
        Assert.Equal("http://host/buh", cs.WebUrl);
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var cs = ConnectionString.Parse("SRVR=\"srv\";REF=\"db\";");

        Assert.Equal(ConnectionKind.Server, cs.Kind);
        Assert.Equal("db", cs.InfobaseName);
    }

    [Fact]
    public void Unescapes_doubled_quotes()
    {
        var cs = ConnectionString.Parse("File=\"C:\\Say \"\"hello\"\"\";");

        Assert.Equal("C:\\Say \"hello\"", cs.FilePath);
        Assert.False(cs.HasErrors);
    }

    [Fact]
    public void ToString_escapes_quotes_and_round_trips()
    {
        var original = ConnectionString.ForFile("C:\\Say \"hello\"");
        var text = original.ToString();

        Assert.Equal("File=\"C:\\Say \"\"hello\"\"\";", text);
        Assert.Equal("C:\\Say \"hello\"", ConnectionString.Parse(text).FilePath);
    }

    [Fact]
    public void Preserves_unknown_keys_and_order()
    {
        const string text = "Srvr=\"srv\";Ref=\"db\";Usr=\"admin\";Custom=\"x;y\";";
        var cs = ConnectionString.Parse(text);

        Assert.Equal(new[] { "Srvr", "Ref", "Usr", "Custom" }, cs.Parts.Select(p => p.Key).ToArray());
        Assert.Equal("x;y", cs["custom"]);
        Assert.Equal(text, cs.ToString());
    }

    [Fact]
    public void Accepts_unquoted_values_and_missing_trailing_semicolon()
    {
        var cs = ConnectionString.Parse("File=C:\\Bases\\Buh");

        Assert.Equal("C:\\Bases\\Buh", cs.FilePath);
        Assert.False(cs.HasErrors);
    }

    [Fact]
    public void Reports_unterminated_quote_without_throwing()
    {
        var cs = ConnectionString.Parse("File=\"C:\\Bases");

        Assert.True(cs.HasErrors);
        Assert.Equal("C:\\Bases", cs.FilePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_string_has_kind_none(string? text)
    {
        Assert.Equal(ConnectionKind.None, ConnectionString.Parse(text).Kind);
    }

    [Fact]
    public void Unknown_kind_when_no_known_keys()
    {
        Assert.Equal(ConnectionKind.Unknown, ConnectionString.Parse("Foo=\"bar\";").Kind);
    }

    [Fact]
    public void Normalized_key_ignores_case_slashes_and_trailing_separator()
    {
        var a = ConnectionString.Parse("File=\"C:/Bases/Buh/\";");
        var b = ConnectionString.Parse("File=\"c:\\bases\\buh\";");

        Assert.Equal(a.ToNormalizedKey(), b.ToNormalizedKey());
    }

    [Fact]
    public void Indexer_sets_and_removes_values()
    {
        var cs = ConnectionString.ForServer("srv", "db");
        cs["Ref"] = "db2";
        cs["Usr"] = "user";
        cs["Srvr"] = null;

        Assert.Equal("Ref=\"db2\";Usr=\"user\";", cs.ToString());
    }
}
