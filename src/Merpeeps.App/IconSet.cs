using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

/// <summary>
/// A small built-in icon set for cloud/architecture nodes.
///
/// <para>Rendering an icon instead of a plain box is the most-requested node
/// feature in mermaid (issue #1358, 143 reactions, still open — plus #4544 for
/// official AWS/GCP/Azure icons at 69). It cannot be expressed in mermaid
/// syntax, which is precisely why it belongs in the Merpeeps layer: the editor draws
/// the icon, and a stock renderer still draws a correct plain box.</para>
///
/// <para>Icons are simple vector primitives rather than vendor artwork, so
/// there is no licensing question and no binary assets to ship.</para>
/// </summary>
public static class IconSet
{
    /// <summary>The recognised icon names, from the format vocabulary.</summary>
    public static readonly string[] Names = IconVocabulary.Names;

    public static bool IsKnown(string? name) => IconVocabulary.IsKnown(name);

    /// <summary>Draw <paramref name="name"/> centred in <paramref name="box"/>.</summary>
    /// <remarks>
    /// The geometry comes from <see cref="IconGeometry"/> in the core, not from
    /// draw calls written here. It used to live in this file, which put it out
    /// of reach of anything that was not an Avalonia canvas — so the SVG
    /// exporter would have produced diagrams with every icon silently missing,
    /// or a second copy of thirteen icons free to drift from this one. One
    /// definition, two renderers.
    /// </remarks>
    public static void Draw(DrawingContext ctx, string name, Rect box, IBrush brush)
    {
        var pen = new Pen(brush, 1.6);
        var s = Math.Min(box.Width, box.Height);

        // The geometry is described in a square unit box, so centre that square
        // inside a target box that may not be square.
        var ox = box.X + (box.Width - s) / 2;
        var oy = box.Y + (box.Height - s) / 2;
        Point P(double x, double y) => new(ox + x * s, oy + y * s);

        foreach (var shape in IconGeometry.For(name))
        {
            switch (shape)
            {
                case IconShape.Box b:
                    ctx.DrawRectangle(null, pen,
                        new Rect(P(b.X, b.Y), new Size(b.W * s, b.H * s)),
                        b.Radius * s, b.Radius * s);
                    break;

                case IconShape.Ellipse e:
                    ctx.DrawEllipse(
                        e.Filled ? brush : null,
                        e.Filled ? null : pen,
                        P(e.CX, e.CY), e.RX * s, e.RY * s);
                    break;

                case IconShape.Line l:
                    ctx.DrawLine(pen, P(l.X1, l.Y1), P(l.X2, l.Y2));
                    break;

                case IconShape.Polyline p:
                    ctx.DrawGeometry(null, pen,
                        Path(false, p.Points.Select(pt => P(pt.X, pt.Y)).ToArray()));
                    break;

                case IconShape.Polygon g:
                    ctx.DrawGeometry(null, pen,
                        Path(true, g.Points.Select(pt => P(pt.X, pt.Y)).ToArray()));
                    break;
            }
        }
    }

    private static StreamGeometry Path(bool closed, params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], false);
        for (var i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(closed);
        return g;
    }
}
