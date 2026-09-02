using System;
using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

/// <summary>
/// Draws a node outline for a given shape.
/// </summary>
/// <remarks>
/// Shared rather than inlined in the canvas, so the palette's preview thumbnails
/// and the canvas draw the same shape from one definition. A preview that only
/// approximated the canvas would be worse than no preview: its whole job is to
/// answer "what am I about to add?".
/// </remarks>
public static class ShapeRenderer
{
    /// <summary>Fill and outline <paramref name="shape"/> inside <paramref name="r"/>.</summary>
    public static void Draw(DrawingContext ctx, NodeShape shape, Rect r, IBrush? fill, IPen? pen)
    {
        switch (shape)
        {
            case NodeShape.Circle:
            case NodeShape.DoubleCircle:
                ctx.DrawEllipse(fill, pen, r.Center, r.Width / 2, r.Height / 2);
                if (shape == NodeShape.DoubleCircle)
                {
                    // Scaled, not a fixed 5px: at thumbnail size a fixed inset
                    // swallows the inner ring entirely.
                    var inset = Math.Min(5, Math.Min(r.Width, r.Height) * 0.12);
                    ctx.DrawEllipse(null, pen, r.Center,
                        r.Width / 2 - inset, r.Height / 2 - inset);
                }
                break;

            case NodeShape.Stadium:
                ctx.DrawRectangle(fill, pen, r, r.Height / 2, r.Height / 2);
                break;

            case NodeShape.RoundedRect:
                ctx.DrawRectangle(fill, pen, r, Math.Min(12, r.Height * 0.25), Math.Min(12, r.Height * 0.25));
                break;

            case NodeShape.Rhombus:
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.Center.X, r.Y), new Point(r.Right, r.Center.Y),
                    new Point(r.Center.X, r.Bottom), new Point(r.X, r.Center.Y)));
                break;

            case NodeShape.Hexagon:
                var i = r.Width * 0.18;
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X + i, r.Y), new Point(r.Right - i, r.Y),
                    new Point(r.Right, r.Center.Y), new Point(r.Right - i, r.Bottom),
                    new Point(r.X + i, r.Bottom), new Point(r.X, r.Center.Y)));
                break;

            case NodeShape.Parallelogram:
                var s = r.Width * 0.16;
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X + s, r.Y), new Point(r.Right, r.Y),
                    new Point(r.Right - s, r.Bottom), new Point(r.X, r.Bottom)));
                break;

            case NodeShape.ParallelogramAlt:
                var sa = r.Width * 0.16;
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X, r.Y), new Point(r.Right - sa, r.Y),
                    new Point(r.Right, r.Bottom), new Point(r.X + sa, r.Bottom)));
                break;

            case NodeShape.Trapezoid:
                var t = r.Width * 0.18;
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X + t, r.Y), new Point(r.Right - t, r.Y),
                    new Point(r.Right, r.Bottom), new Point(r.X, r.Bottom)));
                break;

            case NodeShape.TrapezoidAlt:
                var ta = r.Width * 0.18;
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X, r.Y), new Point(r.Right, r.Y),
                    new Point(r.Right - ta, r.Bottom), new Point(r.X + ta, r.Bottom)));
                break;

            case NodeShape.Asymmetric:
                ctx.DrawGeometry(fill, pen, Poly(
                    new Point(r.X, r.Y), new Point(r.Right - r.Width * 0.15, r.Y),
                    new Point(r.Right, r.Center.Y),
                    new Point(r.Right - r.Width * 0.15, r.Bottom), new Point(r.X, r.Bottom)));
                break;

            case NodeShape.Cylinder:
                var ry = Math.Min(12, r.Height * 0.18);
                ctx.DrawRectangle(fill, pen, new Rect(r.X, r.Y + ry, r.Width, r.Height - 2 * ry));
                ctx.DrawEllipse(fill, pen, new Point(r.Center.X, r.Y + ry), r.Width / 2, ry);
                ctx.DrawEllipse(fill, pen, new Point(r.Center.X, r.Bottom - ry), r.Width / 2, ry);
                break;

            case NodeShape.Subroutine:
                var bar = Math.Min(8, r.Width * 0.12);
                ctx.DrawRectangle(fill, pen, r);
                if (pen is not null)
                {
                    ctx.DrawLine(pen, new Point(r.X + bar, r.Y), new Point(r.X + bar, r.Bottom));
                    ctx.DrawLine(pen, new Point(r.Right - bar, r.Y), new Point(r.Right - bar, r.Bottom));
                }
                break;

            default:
                ctx.DrawRectangle(fill, pen, r, 3, 3);
                break;
        }
    }

    private static StreamGeometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], true);
        for (var i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(true);
        return g;
    }
}
