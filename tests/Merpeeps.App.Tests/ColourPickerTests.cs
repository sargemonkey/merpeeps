using Avalonia.Media;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The colour picker's parsing and formatting.
/// </summary>
/// <remarks>
/// The dialogue itself needs a desktop window, so it is verified by driving the
/// running application. These cover the part that can go wrong silently: what
/// gets written into the file.
/// </remarks>
public class ColourPickerTests
{
    [Theory]
    [InlineData("#4CAF50", 0x4C, 0xAF, 0x50)]
    [InlineData("4CAF50", 0x4C, 0xAF, 0x50)]     // a pasted hex often loses the hash
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]       // three-digit shorthand
    public void HexIsParsed(string input, byte r, byte g, byte b)
    {
        var c = ColourPickerDialog.ParseHex(input);
        Assert.NotNull(c);
        Assert.Equal((r, g, b), (c!.Value.R, c.Value.G, c.Value.B));
    }

    /// <summary>
    /// A half-typed colour is not an error.
    /// </summary>
    /// <remarks>
    /// This runs on every keystroke, so "#AB" is a colour on its way to being
    /// typed. Throwing, or worse applying a garbage colour, would make the field
    /// unusable.
    /// </remarks>
    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("#")] [InlineData("#AB")]
    [InlineData("#12345")] [InlineData("nonsense")]
    public void AHalfTypedColourIsRejectedQuietly(string input)
    {
        Assert.Null(ColourPickerDialog.ParseHex(input));
    }

    /// <summary>
    /// Alpha is not accepted, because the result is written into a mermaid file
    /// read by renderers we do not control and eight-digit hex is not
    /// universally understood.
    /// </summary>
    [Fact]
    public void EightDigitHexIsRejected()
    {
        Assert.Null(ColourPickerDialog.ParseHex("#804CAF50"));
    }

    /// <summary>
    /// A named colour is rejected too — it would round-trip into the file as a
    /// word, and only some renderers accept those.
    /// </summary>
    [Fact]
    public void NamedColoursAreRejected()
    {
        Assert.Null(ColourPickerDialog.ParseHex("red"));
    }

    [Fact]
    public void FormattingIsSixDigitUpperCase()
    {
        Assert.Equal("#4CAF50", ColourPickerDialog.Hex(Color.FromRgb(0x4C, 0xAF, 0x50)));
        Assert.Equal("#000000", ColourPickerDialog.Hex(Color.FromRgb(0, 0, 0)));
        Assert.Equal("#FFFFFF", ColourPickerDialog.Hex(Color.FromRgb(255, 255, 255)));
    }

    /// <summary>
    /// Alpha never reaches the file, even from a colour that carries it.
    /// </summary>
    [Fact]
    public void FormattingDropsAlpha()
    {
        var hex = ColourPickerDialog.Hex(Color.FromArgb(0x80, 0x4C, 0xAF, 0x50));
        Assert.Equal("#4CAF50", hex);
        Assert.Equal(7, hex.Length);
    }

    [Theory]
    [InlineData("#DAE8FC")] [InlineData("#1A1A1A")] [InlineData("#FFFFFF")]
    public void ParseAndFormatRoundTrip(string hex)
    {
        Assert.Equal(hex, ColourPickerDialog.Hex(ColourPickerDialog.ParseHex(hex)!.Value));
    }
}
