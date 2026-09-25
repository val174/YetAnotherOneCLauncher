using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Search;

namespace YetAnotherOneCLauncher.Core.Tests;

public class KeyboardLayoutTests
{
    [Theory]
    [InlineData(",e[", "бух")]
    [InlineData("pfhgkfnf", "зарплата")]
    [InlineData("Pfhgkfnf", "Зарплата")]
    [InlineData("`", "ё")]
    [InlineData("1c:", "1с:")] // c → с, цифры и двоеточие не меняются
    public void Latin_to_cyrillic(string typed, string expected)
    {
        Assert.Equal(expected, KeyboardLayout.ToCyrillic(typed));
    }

    [Theory]
    [InlineData("куезщ", "retpo")]
    [InlineData("ЫКМ", "SRV")]
    [InlineData("ю.", "./")]
    public void Cyrillic_to_latin(string typed, string expected)
    {
        Assert.Equal(expected, KeyboardLayout.ToLatin(typed));
    }
}

public class InfoBaseSearchTests
{
    private static readonly InfoBase Buh = Base("Бухгалтерия предприятия", "Connect=Srvr=\"srv-1c\";Ref=\"buh_prod\";", "Folder=/Рабочие");
    private static readonly InfoBase Zup = Base("Зарплата и управление персоналом", "Connect=Srvr=\"srv-1c\";Ref=\"zup\";");
    private static readonly InfoBase BuhCopy = Base("Копия бухгалтерии", "Connect=File=\"D:\\Bases\\BuhCopy\";");
    private static readonly InfoBase Retail = Base("Розница (тест)", "Connect=ws=\"https://web.example/retail\";");
    private static readonly InfoBase Yolka = Base("Ёлка", "Connect=File=\"D:\\Bases\\Tree\";");
    private static readonly InfoBase[] All = [Buh, Zup, BuhCopy, Retail, Yolka];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_finds_nothing(string? query)
    {
        Assert.Empty(InfoBaseSearch.Search(All, query));
    }

    [Fact]
    public void Name_prefix_ranks_above_word_prefix_and_is_highlighted()
    {
        var results = InfoBaseSearch.Search(All, "бух");

        Assert.Equal(new[] { Buh, BuhCopy }, results.Select(r => r.InfoBase));
        Assert.Equal(new[] { new TextRange(0, 3) }, results[0].NameHighlights);
        Assert.Equal(new[] { new TextRange(6, 3) }, results[1].NameHighlights);
    }

    [Fact]
    public void Query_in_wrong_layout_is_understood()
    {
        var results = InfoBaseSearch.Search(All, ",e[");

        Assert.Equal(Buh, results[0].InfoBase);
        Assert.True(results[0].Score < InfoBaseSearch.Search(All, "бух")[0].Score);
    }

    [Theory]
    [InlineData("зуп")]
    [InlineData("ЗУП")]
    [InlineData("зиуп")]
    [InlineData("peg")] // «зуп» в английской раскладке
    public void Initials_of_words_match(string query)
    {
        var result = Assert.Single(InfoBaseSearch.Search(All, query));

        Assert.Equal(Zup, result.InfoBase);
        Assert.Contains(new TextRange(0, 1), result.NameHighlights);
    }

    [Fact]
    public void Finds_by_server_database_path_url_and_folder()
    {
        Assert.Equal(Zup, Assert.Single(InfoBaseSearch.Search(All, "zup")).InfoBase);
        Assert.Equal(BuhCopy, Assert.Single(InfoBaseSearch.Search(All, "buhcopy")).InfoBase);
        Assert.Equal(Retail, Assert.Single(InfoBaseSearch.Search(All, "web.example")).InfoBase);
        Assert.Equal(Buh, Assert.Single(InfoBaseSearch.Search(All, "рабочие")).InfoBase);
        Assert.Equal(2, InfoBaseSearch.Search(All, "srv-1c").Count);
    }

    [Fact]
    public void All_words_must_match()
    {
        Assert.Equal(Buh, Assert.Single(InfoBaseSearch.Search(All, "бух пред")).InfoBase);
        Assert.Empty(InfoBaseSearch.Search(All, "бух розница"));
    }

    [Fact]
    public void Yo_and_case_are_ignored()
    {
        Assert.Equal(Yolka, Assert.Single(InfoBaseSearch.Search(All, "ЕЛКА")).InfoBase);
        Assert.Equal(Yolka, Assert.Single(InfoBaseSearch.Search(All, "ёлк")).InfoBase);
    }

    [Fact]
    public void Contains_in_the_middle_of_a_word_matches_with_lower_score()
    {
        var results = InfoBaseSearch.Search(All, "галт");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Score < 80));
    }

    [Fact]
    public void Boost_breaks_ties_between_equal_matches()
    {
        var first = Base("Тест А", "Connect=File=\"C:\\A\";");
        var second = Base("Тест Б", "Connect=File=\"C:\\B\";");

        var results = InfoBaseSearch.Search([first, second], "тест", b => b == second ? 10 : 0);

        Assert.Equal(second, results[0].InfoBase);
    }

    [Fact]
    public void Highlights_of_several_words_are_merged_and_sorted()
    {
        var result = Assert.Single(InfoBaseSearch.Search(All, "предприятия бухгалтерия"));

        Assert.Equal(new[] { new TextRange(0, 11), new TextRange(12, 11) }, result.NameHighlights);
    }

    private static InfoBase Base(string name, params string[] lines)
    {
        var section = V8iDocument.Parse($"[{name}]\r\n{string.Join("\r\n", lines)}\r\n").Sections.Single();
        return new InfoBase(section, new ListSource(ListSourceKind.Personal, "ibases.v8i"));
    }
}
