using System;

namespace Merpeeps.App;

/// <summary>
/// Conversions between RGB and HSV.
/// </summary>
/// <remarks>
/// Separated from the wheel so it can be tested without a window. The round trip
/// is the property that matters: a colour picked on the wheel, shown as hex, and
/// re-read has to come back to the same place — otherwise the marker drifts every
/// time the dialogue re-syncs.
/// </remarks>
public static class ColourMath
{
    /// <summary>
    /// HSV to RGB. Hue in degrees (0-360), saturation and value 0-1.
    /// </summary>
    public static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        // Round rather than truncate: truncating loses a step on every channel
        // and makes a round trip drift downwards a little at a time.
        return ((byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
    }

    /// <summary>
    /// RGB to HSV. Hue in degrees (0-360), saturation and value 0-1.
    /// </summary>
    /// <remarks>
    /// Grey has no meaningful hue — every hue produces it at zero saturation —
    /// so 0 is returned. Callers that keep the previous hue when saturation
    /// drops to zero give a less surprising wheel, because the marker stays on
    /// the axis the user was working along.
    /// </remarks>
    public static (double Hue, double Saturation, double Value) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        double hue;
        if (delta < 1e-9) hue = 0;
        else if (max == rd) hue = 60 * (((gd - bd) / delta % 6 + 6) % 6);
        else if (max == gd) hue = 60 * ((bd - rd) / delta + 2);
        else hue = 60 * ((rd - gd) / delta + 4);

        var saturation = max < 1e-9 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
