using System;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// HSV conversions behind the colour wheel.
/// </summary>
/// <remarks>
/// The round trip is the property that matters. The dialogue converts constantly
/// — wheel to hex, hex back to wheel — so a conversion that loses a step each
/// way makes the marker creep away from the colour under it every time the two
/// re-sync.
/// </remarks>
public class ColourMathTests
{
    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]       // red
    [InlineData(120, 1, 1, 0, 255, 0)]     // green
    [InlineData(240, 1, 1, 0, 0, 255)]     // blue
    [InlineData(60, 1, 1, 255, 255, 0)]    // yellow
    [InlineData(180, 1, 1, 0, 255, 255)]   // cyan
    [InlineData(300, 1, 1, 255, 0, 255)]   // magenta
    [InlineData(0, 0, 1, 255, 255, 255)]   // white — no saturation
    [InlineData(0, 0, 0, 0, 0, 0)]         // black — no value
    [InlineData(0, 0, 0.5, 128, 128, 128)] // mid grey
    public void HsvToRgbHitsTheKnownCorners(
        double h, double s, double v, byte r, byte g, byte b)
    {
        Assert.Equal((r, g, b), ColourMath.HsvToRgb(h, s, v));
    }

    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 255, 0, 60)]
    public void RgbToHsvRecoversTheHue(byte r, byte g, byte b, double hue)
    {
        var (h, s, v) = ColourMath.RgbToHsv(r, g, b);
        Assert.Equal(hue, h, 3);
        Assert.Equal(1, s, 3);
        Assert.Equal(1, v, 3);
    }

    /// <summary>
    /// Every colour survives a trip through HSV and back.
    /// </summary>
    /// <remarks>
    /// Exhaustive over a stride rather than a handful of samples, because the
    /// failure this catches is a rounding bug that only bites in the gaps
    /// between the obvious cases.
    /// </remarks>
    [Fact]
    public void EveryColourRoundTrips()
    {
        var worst = 0;
        (byte R, byte G, byte B) offender = default;

        for (var r = 0; r < 256; r += 7)
        for (var g = 0; g < 256; g += 11)
        for (var b = 0; b < 256; b += 13)
        {
            var (h, s, v) = ColourMath.RgbToHsv((byte)r, (byte)g, (byte)b);
            var (r2, g2, b2) = ColourMath.HsvToRgb(h, s, v);

            var drift = Math.Max(Math.Abs(r - r2), Math.Max(Math.Abs(g - g2), Math.Abs(b - b2)));
            if (drift > worst) { worst = drift; offender = ((byte)r, (byte)g, (byte)b); }
        }

        Assert.True(worst == 0,
            $"round trip drifted by {worst} on rgb({offender.R},{offender.G},{offender.B})");
    }

    /// <summary>
    /// Grey reports no hue, because at zero saturation every hue produces it.
    /// </summary>
    /// <remarks>
    /// The dialogue relies on this: it keeps the wheel's existing angle when
    /// saturation is zero, so typing a grey does not fling the marker round to
    /// red.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(128, 128, 128)] [InlineData(255, 255, 255)]
    public void GreyHasNoHue(byte r, byte g, byte b)
    {
        var (h, s, _) = ColourMath.RgbToHsv(r, g, b);
        Assert.Equal(0, s, 6);
        Assert.Equal(0, h, 6);
    }

    /// <summary>Out-of-range input is clamped or wrapped, never thrown on.</summary>
    /// <remarks>
    /// The wheel produces angles from <c>Atan2</c> and saturations from a drag
    /// that may leave the circle, so both arrive unnormalised.
    /// </remarks>
    [Theory]
    [InlineData(-90)] [InlineData(450)] [InlineData(720)]
    public void HueWrapsRatherThanThrowing(double hue)
    {
        var direct = ColourMath.HsvToRgb(hue, 1, 1);
        var wrapped = ColourMath.HsvToRgb(((hue % 360) + 360) % 360, 1, 1);
        Assert.Equal(wrapped, direct);
    }

    [Theory]
    [InlineData(-0.5)] [InlineData(1.5)]
    public void SaturationAndValueAreClamped(double outOfRange)
    {
        var expected = Math.Clamp(outOfRange, 0, 1);
        Assert.Equal(ColourMath.HsvToRgb(200, expected, 1), ColourMath.HsvToRgb(200, outOfRange, 1));
        Assert.Equal(ColourMath.HsvToRgb(200, 1, expected), ColourMath.HsvToRgb(200, 1, outOfRange));
    }
}
