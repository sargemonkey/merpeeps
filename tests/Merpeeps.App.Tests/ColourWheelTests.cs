using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The colour wheel's image.
/// </summary>
/// <remarks>
/// The wheel is a custom-drawn control with no accessibility node, so driving
/// the running application can prove it holds the right hue and saturation but
/// never that it painted them. These read the bitmap back and check the pixel at
/// a position is the colour that position means — the property that makes the
/// marker land on the colour it is pointing at.
/// </remarks>
public class ColourWheelTests
{
    private const int Side = 101;   // odd, so there is an exact centre pixel

    /// <summary>Read a pixel as (r, g, b, a), un-premultiplied.</summary>
    private static (int R, int G, int B, int A) Pixel(int[] pixels, int x, int y)
    {
        var px = pixels[y * Side + x];
        var a = (px >> 24) & 0xFF;
        var b = px & 0xFF;
        var g = (px >> 8) & 0xFF;
        var r = (px >> 16) & 0xFF;

        // The bitmap is premultiplied, so undo it before comparing to a colour.
        if (a is 0 or 255) return (r, g, b, a);
        return (r * 255 / a, g * 255 / a, b * 255 / a, a);
    }

    private static int[] Read()
    {
        var bmp = ColourWheel.BuildWheelForTest(Side);
        var pixels = new int[Side * Side];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(
                new PixelRect(0, 0, Side, Side),
                handle.AddrOfPinnedObject(), pixels.Length * 4, Side * 4);
        }
        finally { handle.Free(); }
        return pixels;
    }

    /// <summary>
    /// Angle is hue and distance is saturation, together.
    /// </summary>
    /// <remarks>
    /// <para>Y grows downward in screen coordinates, so "down" from the centre
    /// is +90 degrees, not -90. Getting that backwards flips the wheel
    /// vertically and is invisible until someone notices that green and magenta
    /// have swapped sides.</para>
    ///
    /// <para>The expected colour is computed rather than written out, because
    /// the first version of this test hard-coded fully saturated primaries and
    /// failed on correct code: at 90% of the radius the saturation is 0.9, so
    /// red is rgb(255,28,28), not rgb(255,0,0). The claim under test is the
    /// geometry — which angle means which hue — so the hue is what is stated
    /// here and the conversion is left to the function that owns it.</para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1, 0, 0)]      // right — red
    [InlineData(0, 1, 90)]     // down  — chartreuse
    [InlineData(-1, 0, 180)]   // left  — cyan
    [InlineData(0, -1, 270)]   // up    — violet
    public void AngleIsHueAndDistanceIsSaturation(int dx, int dy, double hue)
    {
        var pixels = Read();
        var centre = Side / 2;

        const double Fraction = 0.9;   // clear of the feathered edge
        var offset = (int)(centre * Fraction);
        var (pr, pg, pb, pa) = Pixel(pixels, centre + dx * offset, centre + dy * offset);

        var (er, eg, eb) = ColourMath.HsvToRgb(hue, (double)offset / centre, 1);

        Assert.Equal(255, pa);
        Assert.True(Math.Abs(pr - er) <= 6 && Math.Abs(pg - eg) <= 6 && Math.Abs(pb - eb) <= 6,
            $"at hue {hue} expected about rgb({er},{eg},{eb}), got rgb({pr},{pg},{pb})");
    }

    /// <summary>Distance from the centre is saturation: the middle is white.</summary>
    [AvaloniaFact]
    public void TheCentreIsWhite()
    {
        var pixels = Read();
        var (r, g, b, a) = Pixel(pixels, Side / 2, Side / 2);

        Assert.Equal(255, a);
        Assert.True(r > 245 && g > 245 && b > 245, $"centre is rgb({r},{g},{b}), not white");
    }

    /// <summary>Saturation rises with distance, so the wheel is not flat.</summary>
    /// <remarks>
    /// The control that matters: every check above would still pass on an image
    /// where the hue was right but saturation was pinned at 1 everywhere, which
    /// would be a wheel with no white middle and no way to pick a pale colour.
    /// </remarks>
    [AvaloniaFact]
    public void SaturationRisesWithDistance()
    {
        var pixels = Read();
        var centre = Side / 2;

        // Along the red axis, so saturation shows up as green and blue falling.
        var near = Pixel(pixels, centre + (int)(centre * 0.3), centre);
        var far = Pixel(pixels, centre + (int)(centre * 0.9), centre);

        Assert.True(near.G > far.G + 60,
            $"expected the inner pixel to be paler: near g={near.G}, far g={far.G}");
    }

    /// <summary>Outside the circle is transparent, so the wheel is round.</summary>
    [AvaloniaFact]
    public void TheCornersAreTransparent()
    {
        var pixels = Read();

        Assert.Equal(0, Pixel(pixels, 0, 0).A);
        Assert.Equal(0, Pixel(pixels, Side - 1, 0).A);
        Assert.Equal(0, Pixel(pixels, 0, Side - 1).A);
        Assert.Equal(0, Pixel(pixels, Side - 1, Side - 1).A);
    }
}
