using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

/// <summary>
/// A thumbnail of a palette entry: its shape, its colours and its icon.
/// </summary>
/// <remarks>
/// The palette used to be a list of names, which asked people to know what
/// "Preparation" or "Asymmetric" looks like before choosing it. The names are
/// still there — a thumbnail is not a label — but the picture is what answers
/// "what am I about to add?".
///
/// <para>Drawn by <see cref="ShapeRenderer"/> and <see cref="IconSet"/>, the
/// same code the canvas uses, so the preview cannot promise something the
/// canvas then draws differently.</para>
/// </remarks>
public sealed class ShapePreview : Control
{
    public static readonly StyledProperty<PaletteItem?> ItemProperty =
        AvaloniaProperty.Register<ShapePreview, PaletteItem?>(nameof(Item));

    public PaletteItem? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    static ShapePreview()
    {
        AffectsRender<ShapePreview>(ItemProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (Item is not { } item) return;

        // Inset by the stroke width so a thick outline is not clipped.
        var r = new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2);
        if (r.Width <= 0 || r.Height <= 0) return;

        var fill = new SolidColorBrush(Parse(item.Fill, Color.Parse("#DAE8FC")));
        var pen = new Pen(new SolidColorBrush(Parse(item.Stroke, Color.Parse("#6C8EBF"))), 1.4);

        ShapeRenderer.Draw(ctx, item.Shape, r, fill, pen);

        if (item.Icon is not null && IconSet.IsKnown(item.Icon))
        {
            // Centred rather than left-aligned as on the canvas: there is no
            // label beside it here, and an off-centre glyph in a 40px box reads
            // as a mistake rather than a layout.
            var size = System.Math.Min(r.Height * 0.72, r.Width * 0.6);
            var box = new Rect(
                r.Center.X - size / 2, r.Center.Y - size / 2, size, size);

            IconSet.Draw(ctx, item.Icon, box, Ink(fill.Color));
        }
    }

    private static Color Parse(string? hex, Color fallback) =>
        hex is not null && Color.TryParse(hex, out var c) ? c : fallback;

    /// <summary>Black or white, whichever reads on the fill.</summary>
    private static IBrush Ink(Color fill)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(fill.R) + 0.7152 * Channel(fill.G) + 0.0722 * Channel(fill.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
    }
}
