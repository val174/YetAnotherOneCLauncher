using System.Text;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Tests;

public class TextFileCodecTests
{
    private const string Sample = "[База]\r\nConnect=File=\"C:\\Базы\";\r\n";

    [Fact]
    public void Detects_utf8_with_bom()
    {
        var bytes = TextFileCodec.Encode(Sample, new TextFormat(TextEncodingKind.Utf8, true, "\r\n"));
        var decoded = TextFileCodec.Decode(bytes);

        Assert.Equal(TextEncodingKind.Utf8, decoded.Format.EncodingKind);
        Assert.True(decoded.Format.HasBom);
        Assert.Equal(Sample, decoded.Text);
    }

    [Fact]
    public void Detects_utf8_without_bom()
    {
        var decoded = TextFileCodec.Decode(Encoding.UTF8.GetBytes(Sample));

        Assert.Equal(TextEncodingKind.Utf8, decoded.Format.EncodingKind);
        Assert.False(decoded.Format.HasBom);
        Assert.Equal(Sample, decoded.Text);
    }

    [Fact]
    public void Detects_utf16_le_with_bom()
    {
        var bytes = TextFileCodec.Encode(Sample, new TextFormat(TextEncodingKind.Utf16LittleEndian, true, "\r\n"));

        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);

        var decoded = TextFileCodec.Decode(bytes);
        Assert.Equal(TextEncodingKind.Utf16LittleEndian, decoded.Format.EncodingKind);
        Assert.Equal(Sample, decoded.Text);
    }

    [Fact]
    public void Detects_utf16_le_without_bom()
    {
        const string ascii = "CommonInfoBases=\\\\server\\share\\bases.v8i\r\nDefaultVersion=8.3\r\n";
        var decoded = TextFileCodec.Decode(Encoding.Unicode.GetBytes(ascii));

        Assert.Equal(TextEncodingKind.Utf16LittleEndian, decoded.Format.EncodingKind);
        Assert.Equal(ascii, decoded.Text);
    }

    [Fact]
    public void Falls_back_to_windows_1251_for_invalid_utf8()
    {
        var bytes = TextFileCodec.Encode(Sample, new TextFormat(TextEncodingKind.Windows1251, false, "\r\n"));
        var decoded = TextFileCodec.Decode(bytes);

        Assert.Equal(TextEncodingKind.Windows1251, decoded.Format.EncodingKind);
        Assert.Equal(Sample, decoded.Text);
    }

    [Theory]
    [InlineData("a\r\nb", "\r\n")]
    [InlineData("a\nb", "\n")]
    [InlineData("single line", "\r\n")]
    public void Detects_new_line_style(string text, string expected)
    {
        Assert.Equal(expected, TextFileCodec.Decode(Encoding.UTF8.GetBytes(text)).Format.NewLine);
    }

    [Fact]
    public void Real_starter_config_sample_is_utf16()
    {
        var decoded = TextFileCodec.Decode(Fixtures.Read("1cestart_sample.cfg"));

        Assert.Equal(TextEncodingKind.Utf16LittleEndian, decoded.Format.EncodingKind);
        Assert.StartsWith("DefaultVersion=8.3.24", decoded.Text);
    }
}
