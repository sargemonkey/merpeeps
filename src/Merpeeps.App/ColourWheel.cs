using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Merpeeps.App;

/// <summary>
/// A hue/saturation colour wheel: angle is hue, distance from the centre is
/// saturation.
/// </summary>
/// <remarks>
/// <para>The wheel is drawn pixel by pixel into a bitmap rather than assembled
/// from a conic gradient and a radial overlay. Avalonia has both brushes and the
/// composite looks right, but its angle convention would then have to be matched
/// by hand against the arithmetic that reads a click — and any mismatch shows up
/// as a marker sitting on a colour that is not the one selected. Computing the
/// image with the same function that interprets the pointer makes that
/// disagreement impossible rather than merely unlikely.</para>
///
/// <para>The bitmap is built once per size. It is around 40,000 pixels, which
/// costs a few milliseconds and only on resize.</para>
/// </remarks>
public sealed class ColourWheel : Control
{
    private WriteableBitmap? _bitmap;
    private int _bitmapSize;

    /// <summary>Hue in degrees, 0-360.</summary>
    public double Hue { get; private set; }

    /// <summary>Saturation, 0-1.</summary>
    public double Saturation { get; private set; }

    /// <summary>Raised when a drag or click moves the selection.</summary>
    public event EventHandler? Picked;

    /// <summary>Move the marker without raising <see cref="Picked"/>.</summary>
    /// <remarks>
    /// Silent on purpose: this is how the dialogue reflects a colour chosen
    /// somewhere else — a preset, or the hex box. Raising the event here would
    /// feed straight back into whatever made the change.
    /// </remarks>
    public void SetHueSaturation(double hue, double saturation)
    {
        Hue = hue;
        Saturation = Math.Clamp(saturation, 0, 1);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Track(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Equals(e.Pointer.Captured, this)) Track(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Track(Point p)
    {
        var (cx, cy, radius) = Geometry();
        if (radius <= 0) return;

        var dx = p.X - cx;
        var dy = p.Y - cy;

        // Saturation is clamped rather than ignored beyond the edge, so a drag
        // that leaves the wheel keeps tracking the hue instead of freezing.
        Saturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1);

        var degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
        Hue = (degrees + 360) % 360;

        InvalidateVisual();
        Picked?.Invoke(this, EventArgs.Empty);
    }

    private (double CX, double CY, double Radius) Geometry()
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        return (Bounds.Width / 2, Bounds.Height / 2, size / 2 - 1);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var (cx, cy, radius) = Geometry();
        if (radius <= 0) return;

        var side = (int)Math.Ceiling(radius * 2);
        if (_bitmap is null || _bitmapSize != side)
        {
            _bitmap?.Dispose();
            _bitmap = BuildWheel(side);
        }
        _bitmapSize = side;

        var box = new Rect(cx - radius, cy - radius, radius * 2, radius * 2);
        ctx.DrawImage(_bitmap!, box);

        // The marker. Drawn as a white ring inside a dark one so it stays
        // visible over both a pale yellow and a deep blue.
        var angle = Hue * Math.PI / 180;
        var mx = cx + Math.Cos(angle) * Saturation * radius;
        var my = cy + Math.Sin(angle) * Saturation * radius;

        ctx.DrawEllipse(null, new Pen(Brushes.Black, 3), new Point(mx, my), 6, 6);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 1.6), new Point(mx, my), 6, 6);
    }

    /// <summary>Paint the wheel: hue by angle, saturation by radius.</summary>
    /// <remarks>
    /// Built as a managed array and copied in, rather than written through a
    /// raw pointer. The pointer version needs <c>AllowUnsafeBlocks</c> for the
    /// whole project, which is a large permission to grant for one loop over
    /// 40,000 pixels that runs on resize.
    /// </remarks>
    private static WriteableBitmap BuildWheel(int side)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(side, side), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        var radius = side / 2.0;
        var row = new int[side];

        using var buffer = bmp.Lock();

        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var dx = x - radius + 0.5;
                var dy = y - radius + 0.5;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist > radius)
                {
                    row[x] = 0;   // transparent outside the circle
                    continue;
                }

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var (r, g, b) = ColourMath.HsvToRgb(hue, dist / radius, 1);

                // Feather the outermost pixel, or the circle reads as a
                // staircase at this size.
                var alpha = (byte)(255 * Math.Clamp(radius - dist, 0, 1));

                row[x] = (alpha << 24)
                       | (Premultiply(r, alpha) << 16)
                       | (Premultiply(g, alpha) << 8)
                       | Premultiply(b, alpha);
            }

            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, side);
        }

        return bmp;
    }

    /// <summary>Build the wheel image, for tests that read its pixels.</summary>
    /// <remarks>
    /// The wheel has no accessibility node, so a running application can only
    /// prove it holds the right values, not that it drew them. This is the way
    /// in to check the painting itself.
    /// </remarks>
    internal static WriteableBitmap BuildWheelForTest(int side) => BuildWheel(side);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Scale a channel by alpha, because the bitmap is premultiplied.
    /// </summary>
    /// <remarks>
    /// Skipping this leaves a bright halo around the edge, where a
    /// partly-transparent pixel keeps its full-intensity colour.
    /// </remarks>
    private static int Premultiply(byte channel, byte alpha) =>
        channel * alpha / 255;
}
