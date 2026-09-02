using System;
using System.Collections.Generic;

namespace Merpeeps.Core.Model;

/// <summary>
/// The built-in icons, described as geometry rather than drawn.
/// </summary>
/// <remarks>
/// <para>These were originally expressed directly as Avalonia draw calls inside
/// the editor, which made them unreachable from anywhere else — so exporting a
/// diagram to SVG could reproduce every colour, shape and label but silently
/// dropped the artwork. Redrawing them a second time in SVG would have been two
/// definitions of the same icon, free to drift apart with nothing to catch
/// it.</para>
///
/// <para>So the geometry lives here, in a form neither renderer owns, and both
/// consume it: the canvas turns shapes into Avalonia primitives, the exporter
/// turns them into SVG elements. Coordinates are in a unit box — 0 to 1 on both
/// axes — so the caller scales.</para>
/// </remarks>
public abstract record IconShape
{
    /// <summary>A rounded rectangle, outlined.</summary>
    public sealed record Box(double X, double Y, double W, double H, double Radius) : IconShape;

    /// <summary>An ellipse. Filled ones are dots and markers; the rest are outlines.</summary>
    public sealed record Ellipse(double CX, double CY, double RX, double RY, bool Filled) : IconShape;

    /// <summary>A straight line.</summary>
    public sealed record Line(double X1, double Y1, double X2, double Y2) : IconShape;

    /// <summary>An open polyline through the given points.</summary>
    public sealed record Polyline(IReadOnlyList<(double X, double Y)> Points) : IconShape;

    /// <summary>A closed polygon through the given points.</summary>
    public sealed record Polygon(IReadOnlyList<(double X, double Y)> Points) : IconShape;
}

/// <summary>Geometry for the icons Merpeeps ships, keyed by name.</summary>
public static class IconGeometry
{
    /// <summary>
    /// The shapes making up <paramref name="name"/>, in a unit box, or an empty
    /// list when the icon is not one of the built-ins.
    /// </summary>
    /// <remarks>
    /// An unknown name yields nothing rather than a placeholder. Icons come from
    /// stencil packs too, and those carry their own SVG path data — a renderer
    /// that drew a "missing" glyph here would stamp it over every pack icon.
    /// </remarks>
    public static IReadOnlyList<IconShape> For(string? name)
    {
        if (name is null) return [];

        // A dot small enough to read as a dot at icon size, in unit terms.
        const double Dot = 0.065;

        return name.ToLowerInvariant() switch
        {
            "server" =>
            [
                .. Rows(3, 0.18, 0.26),
            ],

            "database" or "storage" =>
            [
                new IconShape.Ellipse(0.5, 0.20, 0.36, 0.13, false),
                new IconShape.Line(0.14, 0.20, 0.14, 0.72),
                new IconShape.Line(0.86, 0.20, 0.86, 0.72),
                new IconShape.Ellipse(0.5, 0.72, 0.36, 0.13, false),
            ],

            "queue" =>
            [
                new IconShape.Box(0.10, 0.34, 0.22, 0.32, 0.08),
                new IconShape.Box(0.38, 0.34, 0.22, 0.32, 0.08),
                new IconShape.Box(0.66, 0.34, 0.22, 0.32, 0.08),
            ],

            "cache" =>
            [
                new IconShape.Box(0.18, 0.24, 0.64, 0.52, 0.12),
                new IconShape.Line(0.18, 0.50, 0.82, 0.50),
                new IconShape.Line(0.50, 0.24, 0.50, 0.76),
            ],

            "function" =>
            [
                new IconShape.Polyline(
                [
                    (0.44, 0.14), (0.20, 0.54), (0.48, 0.54),
                    (0.56, 0.78), (0.80, 0.46), (0.52, 0.46),
                ]),
            ],

            "gateway" =>
            [
                new IconShape.Polygon([(0.50, 0.18), (0.84, 0.50), (0.50, 0.82), (0.16, 0.50)]),
                new IconShape.Line(0.34, 0.50, 0.66, 0.50),
            ],

            "user" =>
            [
                new IconShape.Ellipse(0.50, 0.34, 0.15, 0.15, false),
                new IconShape.Polyline([(0.24, 0.80), (0.30, 0.56), (0.70, 0.56), (0.76, 0.80)]),
            ],

            "browser" =>
            [
                new IconShape.Box(0.16, 0.24, 0.68, 0.52, 0.12),
                new IconShape.Line(0.16, 0.40, 0.84, 0.40),
                new IconShape.Ellipse(0.24, 0.32, Dot, Dot, true),
            ],

            "mobile" =>
            [
                new IconShape.Box(0.32, 0.20, 0.36, 0.60, 0.14),
                new IconShape.Line(0.44, 0.72, 0.56, 0.72),
            ],

            "shield" =>
            [
                new IconShape.Polygon(
                [
                    (0.50, 0.18), (0.76, 0.32), (0.70, 0.70),
                    (0.50, 0.82), (0.30, 0.70), (0.24, 0.32),
                ]),
            ],

            "cloud" =>
            [
                new IconShape.Ellipse(0.34, 0.54, 0.18, 0.14, false),
                new IconShape.Ellipse(0.64, 0.54, 0.16, 0.12, false),
                new IconShape.Ellipse(0.50, 0.42, 0.20, 0.16, false),
            ],

            "cog" => [.. Cog()],

            _ => [],
        };
    }

    /// <summary>Stacked rack units, each with an indicator light.</summary>
    private static IEnumerable<IconShape> Rows(int count, double top, double step)
    {
        for (var i = 0; i < count; i++)
        {
            var y = top + i * step;
            yield return new IconShape.Box(0.12, y, 0.76, 0.18, 0.08);
            yield return new IconShape.Ellipse(0.24, y + 0.09, 0.065, 0.065, true);
        }
    }

    private static IEnumerable<IconShape> Cog()
    {
        yield return new IconShape.Ellipse(0.50, 0.50, 0.20, 0.20, false);
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            yield return new IconShape.Line(
                0.50 + Math.Cos(a) * 0.22, 0.50 + Math.Sin(a) * 0.22,
                0.50 + Math.Cos(a) * 0.32, 0.50 + Math.Sin(a) * 0.32);
        }
    }
}
