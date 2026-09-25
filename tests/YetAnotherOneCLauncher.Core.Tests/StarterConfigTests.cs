using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Tests;

public class StarterConfigTests
{
    [Fact]
    public async Task Reads_real_utf16_sample()
    {
        var config = await StarterConfig.LoadAsync(Fixtures.PathOf("1cestart_sample.cfg"));

        Assert.Equal(
            new[] { "\\\\fileserver\\1c\\bases.v8i", "C:\\ProgramData\\1C\\common.v8i" },
            config.CommonInfoBases.ToArray());
        Assert.Equal("8.3.24", config.DefaultVersion);
        Assert.Equal(new[] { "C:\\Program Files\\1cv8" }, config.InstalledLocations.ToArray());
        Assert.Equal(new[] { "http://server/ibases/" }, config.InternetServices.ToArray());
        Assert.Equal("1", config.GetLast("usehwlicenses"));
    }

    [Fact]
    public void Merge_accumulates_multi_values_and_last_single_value_wins()
    {
        var machine = StarterConfig.Parse("CommonInfoBases=A\r\nDefaultVersion=8.3.20\r\n");
        var user = StarterConfig.Parse("CommonInfoBases=B\r\nDefaultVersion=8.3.24\r\n");

        var merged = StarterConfig.Merge([machine, user]);

        Assert.Equal(new[] { "A", "B" }, merged.CommonInfoBases.ToArray());
        Assert.Equal("8.3.24", merged.DefaultVersion);
    }

    [Fact]
    public void Ignores_empty_values_and_garbage_lines()
    {
        var config = StarterConfig.Parse("CommonInfoBases=\r\nмусор без знака равенства\r\n; comment\r\nCommonInfoBases=X");

        Assert.Equal(new[] { "X" }, config.CommonInfoBases.ToArray());
    }
}
