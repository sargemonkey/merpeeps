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

    [Fact]
    public void EightDigitHexUsesCssAlphaLastNotAvaloniaAlphaFirst()
    {
        var color = ColourPickerDialog.ParseHex("#804CAF50")!.Value;
        Assert.Equal((0x80, 0x4C, 0xAF, 0x50), ((int)color.R, (int)color.G, (int)color.B, (int)color.A));
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

    [Fact]
    public void FormattingKeepsAlphaLast()
    {
        var hex = ColourPickerDialog.Hex(Color.FromArgb(0x80, 0x4C, 0xAF, 0x50));
        Assert.Equal("#4CAF5080", hex);
        Assert.Equal(9, hex.Length);
    }

    [Theory]
    [InlineData("#DAE8FC")] [InlineData("#1A1A1A")] [InlineData("#FFFFFF")]
    public void ParseAndFormatRoundTrip(string hex)
    {
        Assert.Equal(hex, ColourPickerDialog.Hex(ColourPickerDialog.ParseHex(hex)!.Value));
    }
}
