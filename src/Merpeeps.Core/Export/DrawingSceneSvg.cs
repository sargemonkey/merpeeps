using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Merpeeps.Core.Layout;

namespace Merpeeps.Core.Export;

public static class DrawingSceneSvg
{
    public static string Write(DrawingScene scene)
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        var svg = new XElement(ns + "svg", new XAttribute("width", N(scene.Width)), new XAttribute("height", N(scene.Height)),
            new XAttribute("viewBox", $"0 0 {N(scene.Width)} {N(scene.Height)}"),
            new XAttribute("data-text-metrics", scene.UsesApproximateTextMetrics ? "approximate" : "measured"));
        var defs = new XElement(ns + "defs");
        svg.Add(defs);
        var clipIndex = 0;
        var gradientIndex = 0;
        foreach (var figure in scene.Figures.OrderBy(f => f.Layer))
        {
            XElement element;
            var r = figure.Bounds;
            switch (figure.Kind)
            {
                case DrawingFigureKind.Rectangle:
                    element = new(ns + "rect", new XAttribute("x", N(r.X)), new XAttribute("y", N(r.Y)), new XAttribute("width", N(r.Width)), new XAttribute("height", N(r.Height)));
                    break;
                case DrawingFigureKind.Ellipse:
                    element = new(ns + "ellipse", new XAttribute("cx", N(r.X + r.Width / 2)), new XAttribute("cy", N(r.Y + r.Height / 2)), new XAttribute("rx", N(r.Width / 2)), new XAttribute("ry", N(r.Height / 2)));
                    break;
                case DrawingFigureKind.Path:
                    var path = new StringBuilder();
                    foreach (var part in figure.Path)
                        path.Append(part.Kind switch
                        {
                            DrawingPathKind.Move => $"M{P(part.End)} ",
                            DrawingPathKind.Line => $"L{P(part.End)} ",
                            DrawingPathKind.Cubic => $"C{P(part.Control1)} {P(part.Control2)} {P(part.End)} ",
                            _ => "Z ",
                        });
                    element = new(ns + "path", new XAttribute("d", path.ToString()), new XAttribute("fill-rule", "evenodd"));
                    break;
                default:
                    element = new(ns + (figure.Kind == DrawingFigureKind.Polygon ? "polygon" : "polyline"),
                        new XAttribute("points", string.Join(" ", figure.Points.Select(P))));
                    break;
            }
            element.Add(new XAttribute("data-owner", figure.Owner.Id), new XAttribute("data-role", figure.Owner.Role),
                new XAttribute("fill", figure.Fill?.Css ?? "none"), new XAttribute("stroke", figure.Stroke?.Css ?? "none"),
                new XAttribute("stroke-width", N(figure.StrokeWidth)));
            if (figure.Id == "background" && figure.Kind == DrawingFigureKind.Rectangle)
                element.Add(new XAttribute("data-canvas-background", "true"));
            if (figure.Owner.DetailId is { } detail) element.Add(new XAttribute("data-detail", detail));
            if (figure.DashPattern is { Count: > 0 } pattern) element.Add(new XAttribute("stroke-dasharray", string.Join(",", pattern.Select(N))));
            else if (figure.Dashed) element.Add(new XAttribute("stroke-dasharray", "4,4"));
            if (figure.Gradient is { } gradient)
            {
                var id = "gradient-" + gradientIndex++;
                defs.Add(new XElement(ns + "linearGradient", new XAttribute("id", id),
                    new XAttribute("gradientTransform", $"rotate({N(gradient.Angle)} .5 .5)"),
                    new XElement(ns + "stop", new XAttribute("offset", "0"), new XAttribute("stop-color", gradient.Start.Css)),
                    new XElement(ns + "stop", new XAttribute("offset", "1"), new XAttribute("stop-color", gradient.End.Css))));
                element.SetAttributeValue("fill", $"url(#{id})");
            }
            if (figure.Clip is { } clip)
            {
                var id = "clip-" + clipIndex++;
                defs.Add(new XElement(ns + "clipPath", new XAttribute("id", id), new XElement(ns + "rect",
                    new XAttribute("x", N(clip.X)), new XAttribute("y", N(clip.Y)), new XAttribute("width", N(clip.Width)), new XAttribute("height", N(clip.Height)))));
                element.Add(new XAttribute("clip-path", $"url(#{id})"));
            }
            svg.Add(element);
        }
        foreach (var label in scene.Labels)
        {
            var r = label.Bounds;
            var x = label.Alignment switch { "center" => r.X + r.Width / 2, "right" => r.X + r.Width, _ => r.X };
            var element = new XElement(ns + "text", new XAttribute("data-owner", label.Owner.Id), new XAttribute("data-role", label.Owner.Role),
                new XAttribute("x", N(x)), new XAttribute("y", N(r.Y + label.Font.Size)), new XAttribute("fill", label.Color.Css),
                new XAttribute("font-family", label.Font.Family), new XAttribute("font-size", N(label.Font.Size)),
                new XAttribute("font-weight", label.Font.Weight), new XAttribute("font-style", label.Font.Italic ? "italic" : "normal"),
                new XAttribute("text-anchor", label.Alignment switch { "center" => "middle", "right" => "end", _ => "start" }), label.Text);
            if (label.Outline is { } outline && label.OutlineWidth > 0)
                element.Add(new XAttribute("stroke", outline.Css), new XAttribute("stroke-width", N(label.OutlineWidth)),
                    new XAttribute("stroke-linejoin", "round"), new XAttribute("paint-order", "stroke fill"));
            if (label.Rotation != 0) element.Add(new XAttribute("transform", $"rotate({N(label.Rotation)} {N(r.X + r.Width / 2)} {N(r.Y + r.Height / 2)})"));
            svg.Add(element);
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + svg + "\n";
    }
    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string P(Core.Model.DiagramPoint point) => N(point.X) + "," + N(point.Y);
}
