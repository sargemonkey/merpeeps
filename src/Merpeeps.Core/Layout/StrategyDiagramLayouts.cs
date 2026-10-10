using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record WardleyCoordinates(DiagramRect Plot)
{
    public DiagramPoint ToPoint(double visibility, double evolution) => new(Plot.X + evolution * Plot.Width, Plot.Y + (1 - visibility) * Plot.Height);
    /// <summary>X is evolution and Y is visibility; source component coordinates use the reverse order.</summary>
    public DiagramPoint FromPoint(DiagramPoint point) => new(Math.Clamp((point.X - Plot.X) / Plot.Width, 0, 1), Math.Clamp(1 - (point.Y - Plot.Y) / Plot.Height, 0, 1));
}
public sealed record WardleyLayoutResult(DrawingScene Scene, WardleyCoordinates Coordinates, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public sealed record CauseDomainLayoutResult(DrawingScene Scene, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class StrategyDiagramLayouts
{
    public static WardleyLayoutResult Wardley(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.Wardley ?? throw new ArgumentException("Wardley content is missing."); WardleyValidation.Check(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var coords = new WardleyCoordinates(new(90, 60, d.Width - 140, d.Height - 140));
        var plot = coords.Plot; var diagnostics = new List<DataDiagramDiagnostic>();
        b.Box("canvas", "Diagram", "", new(0, 0, d.Width, d.Height), b.Background, null);
        b.Line("evolution-axis", "WardleyAxis", "", [new(plot.X, plot.Y + plot.Height), new(plot.X + plot.Width, plot.Y + plot.Height)], b.Ink, 1.5);
        b.Line("visibility-axis", "WardleyAxis", "", [new(plot.X, plot.Y), new(plot.X, plot.Y + plot.Height)], b.Ink, 1.5);
        b.Text("title", "WardleyDiagram", "", d.Title, 20, plot.X, 10, plot.Width, true);
        b.Text("x-title", "WardleyAxis", "", "Evolution", 14, plot.X + plot.Width / 2 - 28, plot.Y + plot.Height + 52, 120);
        b.RotatedText("y-title", "WardleyAxis", "", "Visibility", 14, 0, plot.Y + plot.Height / 2, -90);
        var stages = d.Stages.Count > 0 ? (IReadOnlyList<WardleyStage>)d.Stages : new[]
        { new WardleyStage("Genesis", null, .25), new("Custom Built", null, .5), new("Product", null, .75), new("Commodity", null, 1) };
        var start = 0d;
        foreach (var (stage, i) in stages.Select((stage, i) => (stage, i)))
        {
            var end = plot.X + stage.End * plot.Width; var x = plot.X + (start + stage.End) / 2 * plot.Width;
            if (stage.End < 1) b.Line("stage-" + i, "WardleyStage", i.ToString(CultureInfo.InvariantCulture), [new(end, plot.Y), new(end, plot.Y + plot.Height)], b.Ink with { Alpha = 60 }, 1);
            var label = stage.Label + (stage.SecondaryLabel is null ? "" : "\n" + stage.SecondaryLabel);
            var m = b.MeasureWrapped(label, 12, (stage.End - start) * plot.Width - 8);
            b.Text("stage-label-" + i, "WardleyStage", i.ToString(CultureInfo.InvariantCulture), label, 12, x - m.Width / 2, plot.Y + plot.Height + 10, (stage.End - start) * plot.Width - 8, alignment: "center");
            start = stage.End;
        }
        var points = d.Components.ToDictionary(c => c.Id, c => coords.ToPoint(d.VisibilityOf(c.Id), c.Evolution));
        foreach (var parent in d.Components.Where(p => d.Components.Any(c => c.PipelineParentId == p.Id)))
        {
            var children = d.Components.Where(c => c.PipelineParentId == parent.Id).ToArray();
            var minimum = Math.Min(parent.Evolution, children.Min(c => c.Evolution)); var maximum = Math.Max(parent.Evolution, children.Max(c => c.Evolution));
            var left = coords.ToPoint(d.VisibilityOf(parent.Id), minimum); var right = coords.ToPoint(d.VisibilityOf(parent.Id), maximum);
            b.Box(parent.Id + "-pipeline", "WardleyComponent", parent.Id, new(left.X - 12, left.Y - 16, right.X - left.X + 24, 32), b.Tint(b.Ink) with { Alpha = 80 }, b.Ink, 1);
        }
        foreach (var link in d.Links)
        {
            var a = points[link.From]; var z = points[link.To];
            b.Line(link.Id, "WardleyLink", link.Id, [a, z], b.Ink, 1.5);
            if (link.Kind is WardleyLinkKind.Flow or WardleyLinkKind.BidirectionalFlow) Arrow(b, link.Id + "-to", "WardleyLink", link.Id, z, a, b.Ink);
            if (link.Kind is WardleyLinkKind.ReverseFlow or WardleyLinkKind.BidirectionalFlow) Arrow(b, link.Id + "-from", "WardleyLink", link.Id, a, z, b.Ink);
            if (link.Label.Length > 0) b.Text(link.Id + "-label", "WardleyLink", link.Id, link.Label, 12, (a.X + z.X) / 2, (a.Y + z.Y) / 2 - 16, 220);
        }
        var movement = new ClassColor(201, 44, 54);
        foreach (var c in d.Components)
        {
            var p = points[c.Id];
            if (c.EvolveTo is { } evolved)
            {
                var end = coords.ToPoint(d.VisibilityOf(c.Id), evolved);
                b.Line(c.Id + "-evolve", "WardleyComponent", c.Id, [p, end], movement, 1.5);
                Arrow(b, c.Id + "-evolve-head", "WardleyComponent", c.Id, end, p, movement);
                b.Circle(c.Id + "-future", "WardleyComponent", c.Id, end, 6, b.Background, movement);
            }
            if (c.Trend is { } trend)
            {
                var end = coords.ToPoint(trend.Y, trend.X);
                b.Line(c.Id + "-trend", "WardleyComponent", c.Id, [p, end], movement, 1);
                b.Circle(c.Id + "-trend-target", "WardleyComponent", c.Id, end, 5, b.Background, movement);
            }
            if (c.Anchor)
            {
                b.Circle(c.Id + "-head", "WardleyComponent", c.Id, new(p.X, p.Y - 4), 4, b.Ink, b.Ink);
                b.Line(c.Id, "WardleyComponent", c.Id, [new(p.X - 6, p.Y + 6), new(p.X, p.Y), new(p.X + 6, p.Y + 6)], b.Ink, 2);
            }
            else switch (c.Strategy)
            {
                case WardleyStrategy.Build: b.Polygon(c.Id, "WardleyComponent", c.Id, [new(p.X, p.Y - 8), new(p.X + 8, p.Y + 7), new(p.X - 8, p.Y + 7)], b.Background, b.Ink); break;
                case WardleyStrategy.Buy: b.Polygon(c.Id, "WardleyComponent", c.Id, [new(p.X, p.Y - 8), new(p.X + 8, p.Y), new(p.X, p.Y + 8), new(p.X - 8, p.Y)], b.Background, b.Ink); break;
                case WardleyStrategy.Outsource: b.Box(c.Id, "WardleyComponent", c.Id, new(p.X - 7, p.Y - 7, 14, 14), b.Background, b.Ink); break;
                default: b.Circle(c.Id, "WardleyComponent", c.Id, p, c.Strategy == WardleyStrategy.Market ? 8 : 5, b.Background, b.Ink); break;
            }
            if (c.Inertia) b.Line(c.Id + "-inertia", "WardleyComponent", c.Id, [new(p.X - 10, p.Y - 10), new(p.X - 10, p.Y + 10)], movement, 4);
            b.Text(c.Id + "-label", "WardleyComponent", c.Id, c.Name, 14, p.X + 12 + c.LabelOffset.X, p.Y - 18 + c.LabelOffset.Y, 240, c.Anchor);
            b.Hit("WardleyComponent", c.Id, new(p.X - 10, p.Y - 10, 20, 20), c.PipelineParentId);
        }
        var boxIndex = 0;
        foreach (var a in d.Annotations)
        {
            var p = coords.ToPoint(a.Visibility, a.Evolution);
            if (a.Kind == WardleyAnnotationKind.Note)
            {
                var m = b.MeasureWrapped(a.Text, 12, 220);
                b.Text(a.Id, "WardleyAnnotation", a.Id, a.Text, 12, p.X, p.Y, 220);
                b.Hit("WardleyAnnotation", a.Id, new(p.X, p.Y, m.Width, m.Height));
            }
            else if (a.Kind == WardleyAnnotationKind.Numbered)
            {
                b.Circle(a.Id, "WardleyAnnotation", a.Id, p, 11, b.Background, movement);
                b.Text(a.Id + "-number", "WardleyAnnotation", a.Id, a.Number!.Value.ToString(CultureInfo.InvariantCulture), 12, p.X - 4, p.Y - 8, 35);
                b.Hit("WardleyAnnotation", a.Id, new(p.X - 12, p.Y - 12, 24, 24));
                if (d.AnnotationBox is { } box)
                {
                    var position = coords.ToPoint(box.Y, box.X); var text = a.Number + ". " + a.Text;
                    var m = b.MeasureWrapped(text, 12, 260);
                    b.Text(a.Id + "-description", "WardleyAnnotation", a.Id, text, 12, position.X, position.Y + boxIndex, 260);
                    boxIndex += (int)Math.Ceiling(m.Height + 6);
                }
            }
            else
            {
                var right = a.Kind == WardleyAnnotationKind.Accelerator; var sign = right ? 1 : -1;
                b.Line(a.Id, "WardleyAnnotation", a.Id, [new(p.X - sign * 10, p.Y - 8), new(p.X + sign * 3, p.Y), new(p.X - sign * 10, p.Y + 8)], movement, 3);
                b.Text(a.Id + "-label", "WardleyAnnotation", a.Id, a.Text, 12, p.X + 12, p.Y - 18, 200);
                b.Hit("WardleyAnnotation", a.Id, new(p.X - 12, p.Y - 12, 24, 24));
            }
        }
        var scene = b.Finish(); var delta = b.OriginOffset;
        var dashed = d.Links.Where(l => l.Kind == WardleyLinkKind.Dashed).Select(l => l.Id).ToHashSet();
        var figures = scene.Figures.Select(f => dashed.Contains(f.Id) || f.Id.EndsWith("-evolve", StringComparison.Ordinal) || f.Id.EndsWith("-trend", StringComparison.Ordinal) ? f with { Dashed = true } : f).ToArray();
        var edges = figures.Where(f => d.Links.Any(l => l.Id == f.Id)).Select(f => new DrawingRegion(f.Owner, DrawingHitKind.Polyline, f.Bounds, f.Points));
        return new(scene with { Figures = figures, Regions = edges.Concat(scene.Regions).ToArray() },
            coords with { Plot = plot with { X = plot.X + delta.X, Y = plot.Y + delta.Y } }, diagnostics);
    }

    public static CauseDomainLayoutResult Ishikawa(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.Ishikawa ?? throw new ArgumentException("Ishikawa content is missing."); IshikawaDiagramAdapter.Validate(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var font = d.GlobalConfiguration["fontSize"] is { } size ? ChartConfiguration.ParseNumber(size.ToString()) : 14;
        if (font <= 0) throw new ArgumentException("Fishbone font size must be positive.");
        var line = Color(d.ThemeVariables, "lineColor", b.Ink); var fill = Color(d.ThemeVariables, "mainBkg", b.Background);
        var textColor = Color(d.ThemeVariables, "textColor", b.Ink);
        var lengths = new Dictionary<string, double>();
        double Measure(IshikawaCause c)
        {
            var text = b.MeasureWrapped(c.Label, font, 210);
            var length = Math.Max(100, text.Width + 24);
            foreach (var child in c.Children) length += Measure(child) * .45 + 22;
            lengths[c.Id] = length; return length;
        }
        foreach (var cause in d.Causes) Measure(cause);
        var pairs = (d.Causes.Count + 1) / 2;
        var widths = Enumerable.Range(0, pairs).Select(i => Math.Max(lengths.GetValueOrDefault(d.Causes[i * 2].Id), i * 2 + 1 < d.Causes.Count ? lengths[d.Causes[i * 2 + 1].Id] : 0) + 100).ToArray();
        var spineLength = Math.Max(260, widths.Sum()); var centerY = 320d;
        var head = b.MeasureWrapped(d.Problem, font + 2, 250, true);
        b.Line("spine", "IshikawaProblem", "", [new(20, centerY), new(20 + spineLength, centerY)], line, 2);
        b.Box("problem", "IshikawaProblem", "", new(20 + spineLength, centerY - head.Height / 2 - 12, Math.Max(100, head.Width + 24), head.Height + 24), fill, line);
        b.Text("problem-label", "IshikawaProblem", "", d.Problem, font + 2, 32 + spineLength, centerY - head.Height / 2, 250, true, textColor);
        b.Hit("IshikawaProblem", "", new(20 + spineLength, centerY - head.Height / 2 - 12, Math.Max(100, head.Width + 24), head.Height + 24));
        var cursor = 40d;
        for (var i = 0; i < d.Causes.Count; i++)
        {
            if (i > 0 && i % 2 == 0) cursor += widths[i / 2 - 1];
            var c = d.Causes[i]; var upper = i % 2 == 0; var height = Math.Max(170, lengths[c.Id] * .45);
            Branch(c, new(cursor + widths[i / 2] * .75, centerY), new(cursor, centerY + (upper ? -height : height)), upper, 0);
        }
        return new(b.Finish(), []);
        void Branch(IshikawaCause c, DiagramPoint attach, DiagramPoint end, bool upper, int depth)
        {
            b.Line(c.Id + "-bone", "IshikawaCause", c.Id, [end, attach], line, depth == 0 ? 2 : 1);
            var m = b.MeasureWrapped(c.Label, font, 210, depth == 0);
            var y = upper ? end.Y - m.Height - 5 : end.Y + 5;
            b.Text(c.Id + "-label", "IshikawaCause", c.Id, c.Label, font, end.X, y, 210, depth == 0, textColor);
            b.Hit("IshikawaCause", c.Id, new(end.X - 4, y - 3, m.Width + 8, m.Height + 6));
            for (var index = 0; index < c.Children.Count; index++)
            {
                var t = (index + 1d) / (c.Children.Count + 1);
                var point = new DiagramPoint(attach.X + (end.X - attach.X) * t, attach.Y + (end.Y - attach.Y) * t);
                var child = c.Children[index]; var length = Math.Min(280, lengths[child.Id]);
                var terminal = depth % 2 == 0 ? new DiagramPoint(point.X - length, point.Y) : new DiagramPoint(point.X - length * .55, point.Y + (upper ? -1 : 1) * length * .7);
                Branch(child, point, terminal, upper, depth + 1);
            }
        }
    }

    public static CauseDomainLayoutResult Cynefin(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null, string renderId = "cynefin")
    {
        var d = model.Cynefin ?? throw new ArgumentException("Cynefin content is missing."); CynefinDiagramAdapter.Validate(d);
        var theme = d.ThemeVariables["cynefin"] as JsonObject ?? new();
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var width = Number(d.Configuration, "width", 800); var height = Number(d.Configuration, "height", 600); var padding = Number(d.Configuration, "padding", 40);
        var amplitude = Number(d.Configuration, "boundaryAmplitude", 8); var seed = Number(d.Configuration, "seed", 0);
        if (width < 200 || height < 200 || padding < 0 || amplitude < 0) throw new ArgumentException("Cynefin dimensions are too small or invalid.");
        var effectiveSeed = seed == 0 ? Hash(renderId) : unchecked((int)seed);
        var subtitles = d.Configuration["showDomainDescriptions"]?.GetValue<bool>() ?? true;
        var font = Number(theme, "domainFontSize", 22); var itemFont = Number(theme, "itemFontSize", 12);
        var stroke = Color(theme, "boundaryColor", b.Ink); var titleColor = Color(theme, "labelColor", b.Ink); var textColor = Color(theme, "textColor", b.Ink);
        var diagnostics = new List<DataDiagramDiagnostic>();
        var regions = new Dictionary<CynefinDomain, DiagramRect>
        {
            [CynefinDomain.Complex] = new(padding, padding, width / 2, height / 2),
            [CynefinDomain.Complicated] = new(padding + width / 2, padding, width / 2, height / 2),
            [CynefinDomain.Chaotic] = new(padding, padding + height / 2, width / 2, height / 2),
            [CynefinDomain.Clear] = new(padding + width / 2, padding + height / 2, width / 2, height / 2),
            [CynefinDomain.Confusion] = new(padding + width * .35, padding + height * .35, width * .3, height * .3),
        };
        var colors = new Dictionary<CynefinDomain, ClassColor>();
        foreach (var (domain, i) in Enum.GetValues<CynefinDomain>().Select((domain, i) => (domain, i)))
            colors[domain] = Color(theme, domain.ToString().ToLowerInvariant() + "Bg", b.Tint(b.Scale(i)));
        b.Box("canvas", "Diagram", "", new(0, 0, width + padding * 2, height + padding * 2), b.Background, null);
        foreach (var domain in new[] { CynefinDomain.Complex, CynefinDomain.Complicated, CynefinDomain.Chaotic, CynefinDomain.Clear })
        {
            b.Box(domain.ToString(), "CynefinDomain", domain.ToString(), regions[domain], colors[domain], null);
            b.Hit("CynefinDomain", domain.ToString(), regions[domain]);
        }
        var cx = padding + width / 2; var cy = padding + height / 2;
        b.Path("fold", "CynefinBoundary", "", Wave(true, effectiveSeed), null, stroke);
        b.Path("cross", "CynefinBoundary", "", Wave(false, effectiveSeed + 100), null, stroke);
        var cliff = Color(theme, "cliffColor", new(171, 48, 42));
        b.Path("cliff", "CynefinBoundary", "", [
            new(DrawingPathKind.Move, new(cx, cy)),
            new(DrawingPathKind.Cubic, new(cx + width * .015, padding + height * .875), new(cx + width * .03, cy + height * .1), new(cx - width * .045, cy + height * .275)),
            new(DrawingPathKind.Cubic, new(cx, padding + height), new(cx - width * .03, padding + height * .925), new(cx + width * .009, padding + height * .975))], null, cliff);
        var confusion = regions[CynefinDomain.Confusion]; var ellipse = Enumerable.Range(0, 73).Select(i => new DiagramPoint(cx + confusion.Width / 2 * Math.Cos(i * Math.PI / 36), cy + confusion.Height / 2 * Math.Sin(i * Math.PI / 36))).ToArray();
        b.Polygon("confusion", "CynefinDomain", CynefinDomain.Confusion.ToString(), ellipse, colors[CynefinDomain.Confusion] with { Alpha = 128 }, stroke);
        b.Hit("CynefinDomain", CynefinDomain.Confusion.ToString(), confusion);
        if (d.Title.Length > 0) b.Text("title", "CynefinDiagram", "", d.Title, 20, padding, 3, width, true);
        foreach (var domain in Enum.GetValues<CynefinDomain>())
        {
            var r = regions[domain]; var center = new DiagramPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
            var labelY = domain == CynefinDomain.Confusion ? cy - 24 : center.Y - 60;
            var label = b.MeasureWrapped(domain.ToString(), font, r.Width - 10, true);
            b.Text("domain-label-" + domain, "CynefinDomain", domain.ToString(), domain.ToString(), font, center.X - label.Width / 2, labelY, r.Width - 10, true, titleColor);
            if (subtitles)
            {
                var text = domain switch
                {
                    CynefinDomain.Complex => "Probe - Sense - Respond\nEmergent practices",
                    CynefinDomain.Complicated => "Sense - Analyse - Respond\nGood practices",
                    CynefinDomain.Clear => "Sense - Categorise - Respond\nBest practices",
                    CynefinDomain.Chaotic => "Act - Sense - Respond\nNovel practices", _ => "Disorder",
                };
                var size = b.MeasureWrapped(text, itemFont, r.Width - 12);
                b.Text("subtitle-" + domain, "CynefinDomain", domain.ToString(), text, itemFont, center.X - size.Width / 2, labelY + label.Height + 4, r.Width - 12, color: textColor, alignment: "center");
            }
            var items = d.Items.Where(i => i.Domain == domain).ToArray(); var shown = domain == CynefinDomain.Confusion ? items.Take(3) : items.AsEnumerable();
            var y = domain == CynefinDomain.Confusion ? cy + (subtitles ? 22 : 14) : center.Y + (subtitles ? 25 : 15);
            foreach (var item in shown)
            {
                var size = b.MeasureWrapped(item.Label, itemFont, double.MaxValue); var badge = new DiagramRect(center.X - size.Width / 2 - 10, y, size.Width + 20, Math.Max(26, size.Height + 8));
                b.Box(item.Id, "CynefinItem", item.Id, badge, colors[domain] with { Alpha = 242 }, null);
                b.Text(item.Id + "-label", "CynefinItem", item.Id, item.Label, itemFont, badge.X + 10, badge.Y + 4, double.MaxValue, color: textColor);
                b.Hit("CynefinItem", item.Id, badge, domain.ToString());
                if (badge.X < r.X || badge.X + badge.Width > r.X + r.Width || badge.Y + badge.Height > r.Y + r.Height)
                    diagnostics.Add(new("cynefin-item-overflow", item.Id, "The item extends outside its domain; move it or enlarge the framework."));
                y += badge.Height + 4;
            }
            if (domain == CynefinDomain.Confusion && items.Length > 3)
            {
                b.Text("confusion-overflow", "CynefinDomain", domain.ToString(), $"+{items.Length - 3} more", itemFont, cx - 24, y, 120, color: textColor);
                diagnostics.Add(new("cynefin-confusion-overflow", "", $"{items.Length - 3} confusion items are retained in the document; use the item navigator to inspect them."));
            }
        }
        var arrows = Color(theme, "arrowColor", new(80, 92, 117));
        foreach (var transition in d.Transitions)
        {
            var a = Center(regions[transition.From]); var z = Center(regions[transition.To]);
            b.Line(transition.Id, "CynefinTransition", transition.Id, [a, z], arrows, Number(theme, "arrowWidth", 2));
            Arrow(b, transition.Id + "-head", "CynefinTransition", transition.Id, z, a, arrows);
            if (transition.Label.Length > 0) b.Text(transition.Id + "-label", "CynefinTransition", transition.Id, transition.Label, itemFont, (a.X + z.X) / 2 + 6, (a.Y + z.Y) / 2 - 16, 200, color: textColor);
        }
        var scene = b.Finish();
        var figures = scene.Figures.Select(f => f.Id is "fold" or "cross" ? f with { StrokeWidth = Number(theme, "boundaryWidth", 2) }
            : f.Id == "cliff" ? f with { StrokeWidth = Number(theme, "cliffWidth", 4) } : f).ToArray();
        var transitions = figures.Where(f => d.Transitions.Any(t => t.Id == f.Id)).Select(f => new DrawingRegion(f.Owner, DrawingHitKind.Polyline, f.Bounds, f.Points));
        var outline = figures.Single(f => f.Id == "confusion").Points;
        var hitRegions = scene.Regions.Select(r => r.Owner.Role == "CynefinDomain" && r.Owner.Id == "Confusion"
            ? r with { Kind = DrawingHitKind.Polygon, Points = outline } : r);
        return new(scene with { Figures = figures, Regions = hitRegions.Concat(transitions).ToArray() }, diagnostics);
        IReadOnlyList<DrawingPathPart> Wave(bool vertical, int randomSeed)
        {
            var path = new List<DrawingPathPart>();
            var points = Enumerable.Range(0, 8).Select(i =>
            {
                var jitter = (Random(randomSeed + i * (vertical ? 17 : 23)) * 2 - 1) * amplitude;
                return vertical ? new DiagramPoint(cx + jitter, padding + i * height / 7) : new DiagramPoint(padding + i * width / 7, cy + jitter);
            }).ToArray();
            path.Add(new(DrawingPathKind.Move, points[0]));
            for (var i = 0; i < 7; i++)
            {
                var offset = amplitude * 1.5 * (i % 2 == 0 ? 1 : -1) * Random(randomSeed + i * (vertical ? 31 : 37) + (vertical ? 7 : 11));
                var a = points[i]; var z = points[i + 1];
                var p1 = vertical ? new DiagramPoint(a.X + offset, (a.Y + z.Y) / 2) : new((a.X + z.X) / 2, a.Y + offset);
                var p2 = vertical ? new DiagramPoint(z.X - offset, (a.Y + z.Y) / 2) : new((a.X + z.X) / 2, z.Y - offset);
                path.Add(new(DrawingPathKind.Cubic, z, p1, p2));
            }
            return path;
        }
    }
    private static int Hash(string text) { var hash = 0; foreach (var c in text) hash = unchecked(hash * 31 + c); return hash; }
    private static double Random(int seed)
    {
        var t = unchecked((uint)seed + 0x6d2b79f5U);
        t = unchecked((t ^ t >> 15) * (t | 1));
        t ^= unchecked(t + ((t ^ t >> 7) * (t | 61)));
        return (t ^ t >> 14) / 4294967296d;
    }
    private static DiagramPoint Center(DiagramRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static double Number(JsonObject values, string key, double fallback) => values[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
    private static ClassColor Color(JsonObject values, string key, ClassColor fallback) => values[key] is { } value
        ? SequenceColors.TryParse(value.ToString(), out var color) ? color : throw new ArgumentException("Invalid diagram color: " + key) : fallback;
    private static void Arrow(DataDiagramLayouts.Builder b, string id, string role, string owner, DiagramPoint tip, DiagramPoint toward, ClassColor color)
    {
        var dx = toward.X - tip.X; var dy = toward.Y - tip.Y; var length = Math.Sqrt(dx * dx + dy * dy); if (length == 0) return;
        b.Polygon(id, role, owner, [tip, new(tip.X + dx / length * 10 - dy / length * 4, tip.Y + dy / length * 10 + dx / length * 4),
            new(tip.X + dx / length * 10 + dy / length * 4, tip.Y + dy / length * 10 - dx / length * 4)], color, color);
    }
}
