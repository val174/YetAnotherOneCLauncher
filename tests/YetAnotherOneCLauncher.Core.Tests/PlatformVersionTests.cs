using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Tests;

public class PlatformVersionTests
{
    [Theory]
    [InlineData("8.3.24.1548", 8, 3, 24, 1548)]
    [InlineData(" 8.5.1.189 ", 8, 5, 1, 189)]
    public void Parses_full_version(string text, int major, int minor, int release, int build)
    {
        Assert.True(PlatformVersion.TryParse(text, out var version));
        Assert.Equal(new PlatformVersion(major, minor, release, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8.3.24")]
    [InlineData("8.3.24.1548.1")]
    [InlineData("8.3.x.1548")]
    [InlineData("8..24.1548")]
    [InlineData("-8.3.24.1548")]
    [InlineData("common")]
    public void Rejects_anything_but_four_numbers(string? text)
    {
        Assert.False(PlatformVersion.TryParse(text, out _));
    }

    [Fact]
    public void Compares_numerically_not_as_strings()
    {
        // Как строки "8.5.1.189" > "8.5.1.1150"; как версии — наоборот.
        Assert.True(PlatformVersion.Parse("8.5.1.189") < PlatformVersion.Parse("8.5.1.1150"));
        Assert.True(PlatformVersion.Parse("8.3.9.2170") < PlatformVersion.Parse("8.3.10.1877"));
        Assert.True(PlatformVersion.Parse("8.5.1.1") > PlatformVersion.Parse("8.3.27.2130"));
    }

    [Fact]
    public void Formats_back_to_text()
    {
        Assert.Equal("8.3.24.1548", PlatformVersion.Parse("8.3.24.1548").ToString());
    }

    [Theory]
    [InlineData("8", "8.3.24.1548", true)]
    [InlineData("8.3", "8.3.24.1548", true)]
    [InlineData("8.3", "8.5.1.1150", false)]
    [InlineData("8.3.24", "8.3.24.1548", true)]
    [InlineData("8.3.24", "8.3.25.1374", false)]
    [InlineData("8.3.24.1548", "8.3.24.1548", true)]
    [InlineData("8.3.24.1548", "8.3.24.1549", false)]
    public void Mask_matches_by_given_parts(string mask, string version, bool expected)
    {
        Assert.True(VersionMask.TryParse(mask, out var parsed));
        Assert.Equal(expected, parsed.Matches(PlatformVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("8.3.24.1548.1")]
    [InlineData("8.3.*")]
    [InlineData("последняя")]
    public void Rejects_invalid_masks(string mask)
    {
        Assert.False(VersionMask.TryParse(mask, out _));
    }
}
