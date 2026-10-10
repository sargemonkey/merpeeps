using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class DiagramContrast
{
    public static ClassColor Parse(string css) => SequenceColors.TryParse(css, out var color)
        ? color : throw new ArgumentException("Unsupported diagram color: " + css);

    public static ClassColor Composite(ClassColor foreground, ClassColor background)
    {
        var alpha = foreground.Alpha / 255d;
        byte Channel(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return new(Channel(foreground.Red, background.Red), Channel(foreground.Green, background.Green), Channel(foreground.Blue, background.Blue));
    }

    public static double Ratio(ClassColor a, ClassColor b)
    {
        static double Linear(byte channel) { var c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(ClassColor c) => .2126 * Linear(c.Red) + .7152 * Linear(c.Green) + .0722 * Linear(c.Blue);
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    /// <summary>Only inherited ink is adapted. Callers keep intentional authored colors unchanged.</summary>
    public static ClassColor ReadableInk(ClassColor preferred, ClassColor surface, double minimum = 4.5)
    {
        if (Ratio(Composite(preferred, surface), surface) >= minimum) return preferred;
        var dark = new ClassColor(0, 0, 0); var light = new ClassColor(255, 255, 255);
        return Ratio(dark, surface) >= Ratio(light, surface) ? dark : light;
    }

    public static ClassColor SurfaceAt(DiagramModel model, DiagramPoint point, ClassColor canvas)
    {
        var surface = canvas;
        if (model.RenderedGeometry is null) return surface;
        foreach (var group in model.Subgraphs.Select(g => (Group: g, Bounds: GraphGroupGeometry.Bounds(model, g)))
            .Where(g => point.X >= g.Bounds.X && point.Y >= g.Bounds.Y && point.X <= g.Bounds.X + g.Bounds.Width && point.Y <= g.Bounds.Y + g.Bounds.Height)
            .OrderByDescending(g => g.Bounds.Width * g.Bounds.Height))
            if (model.RenderedGeometry.Groups.TryGetValue(group.Group.Id, out var frame) && frame.Fill is { } fill)
                surface = Composite(Parse(fill), surface);
        return surface;
    }

    public static ClassColor LabelSurface(RenderedText label, ClassColor beneath) =>
        label.Background is { } background ? Composite(Parse(background), beneath) : beneath;

    public static (ClassColor Ink, ClassColor? Halo) ConnectorInk(DiagramModel model, IReadOnlyList<DiagramPoint> route,
        ClassColor preferred, ClassColor canvas)
    {
        var ink = ReadableInk(preferred, SurfaceAt(model, GraphEdgeGeometry.LabelPoint(route), canvas), 3);
        var needsHalo = route.Any(p => Ratio(ink, SurfaceAt(model, p, canvas)) < 3);
        return (ink, needsHalo ? ReadableInk(ink, ink, 3) : null);
    }
}
