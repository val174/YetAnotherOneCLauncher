using YetAnotherOneCLauncher.Core.Launching;

namespace YetAnotherOneCLauncher.Core.Tests;

public class OneCCommandLineTests
{
    [Theory]
    [InlineData("ENTERPRISE", "ENTERPRISE")]
    [InlineData("/F", "/F")]
    [InlineData(@"C:\Bases\Buh", @"C:\Bases\Buh")]
    [InlineData(@"C:\My Bases\Buh", "\"C:\\My Bases\\Buh\"")]
    [InlineData(@"C:\Bases\Buh\", @"C:\Bases\Buh\")]
    [InlineData(@"C:\My Bases\", "\"C:\\My Bases\\\"")] // обратная косая черта перед кавычкой — обычный символ
    [InlineData("File=\"C:\\B\";", "\"File=\"\"C:\\B\"\";\"")]
    [InlineData("", "\"\"")]
    public void Quotes_by_1c_rules(string argument, string expected)
    {
        Assert.Equal(expected, OneCCommandLine.Quote(argument));
    }

    [Fact]
    public void Formats_arguments_and_appends_raw_tail_as_is()
    {
        var text = OneCCommandLine.Format(["ENTERPRISE", "/F", @"C:\My Bases\Buh"], "  /DisableStartupMessages /C\"x y\"  ");

        Assert.Equal("ENTERPRISE /F \"C:\\My Bases\\Buh\" /DisableStartupMessages /C\"x y\"", text);
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("/DisableStartupMessages /L ru", new[] { "/DisableStartupMessages", "/L", "ru" })]
    [InlineData("/C \"a b\"", new[] { "/C", "a b" })]
    [InlineData("/C\"a b\"", new[] { "/Ca b" })]
    [InlineData("/C \"say \"\"hi\"\"\"", new[] { "/C", "say \"hi\"" })]
    [InlineData("/C \"\"", new[] { "/C", "" })]
    public void Splits_raw_fragment(string? text, string[] expected)
    {
        Assert.Equal(expected, OneCCommandLine.Split(text));
    }

    [Fact]
    public void Split_and_format_round_trip()
    {
        string[] arguments = ["DESIGNER", "/IBConnectionString", "Srvr=\"srv\";Ref=\"buh\";", "/N", "Иванов И.И."];

        Assert.Equal(arguments, OneCCommandLine.Split(OneCCommandLine.Format(arguments)));
    }

    [Fact]
    public void Masks_password_argument()
    {
        var masked = OneCCommandLine.MaskPasswords(["/N", "user", "/P", "secret", "/p", "second"]);

        Assert.Equal(new[] { "/N", "user", "/P", "***", "/p", "***" }, masked);
    }

    [Theory]
    [InlineData("/N user /P secret /L ru", "/N user /P *** /L ru")]
    [InlineData("/P \"se cret\" /L ru", "/P *** /L ru")]
    [InlineData("/N user /P\"secret\"", "/N user /P***")]
    [InlineData("/Prmod /L ru", "/Prmod /L ru")]
    public void Masks_password_in_raw_text(string raw, string expected)
    {
        Assert.Equal(expected, OneCCommandLine.MaskPasswords(raw));
    }
}
