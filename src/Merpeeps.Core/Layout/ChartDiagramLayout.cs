using System.Globalization;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record ChartCoordinateMap(DiagramRect Plot, double XMinimum, double XMaximum, double YMinimum, double YMaximum,
    bool Horizontal = false, bool CategoryX = false)
{
    public DiagramPoint ToPoint(double x, double y)
    {
        var fx = Fraction(x, XMinimum, XMaximum); var fy = Fraction(y, YMinimum, YMaximum);
        return Horizontal ? new(Plot.X + fy * Plot.Width, Plot.Y + (CategoryX ? fx : 1 - fx) * Plot.Height)
            : new(Plot.X + fx * Plot.Width, Plot.Y + (1 - fy) * Plot.Height);
    }
    public DiagramPoint FromPoint(DiagramPoint point, bool clamp = true)
    {
        var fx = Horizontal ? (point.Y - Plot.Y) / Plot.Height : (point.X - Plot.X) / Plot.Width;
        var fy = Horizontal ? (point.X - Plot.X) / Plot.Width : 1 - (point.Y - Plot.Y) / Plot.Height;
        if (Horizontal && !CategoryX) fx = 1 - fx;
        if (clamp) { fx = Math.Clamp(fx, 0, 1); fy = Math.Clamp(fy, 0, 1); }
        return new(XMinimum * (1 - fx) + XMaximum * fx, YMinimum * (1 - fy) + YMaximum * fy);
    }
    internal static double Fraction(double value, double min, double max)
    {
        if (min == max) return .5;
        var scale = Math.Max(Math.Abs(min), Math.Abs(max));
        return scale == 0 ? 0 : (value / scale - min / scale) / (max / scale - min / scale);
    }
}
public sealed record RadarCoordinateMap(DiagramPoint Center, double Radius, double Minimum, double Maximum, IReadOnlyList<string> AxisIds)
{
    public double ValueAt(string axisId, DiagramPoint point)
    {
        var index = AxisIds.ToList().IndexOf(axisId);
        if (index < 0) throw new ArgumentException("Radar axis no longer exists.");
        var angle = index * 2 * Math.PI / AxisIds.Count - Math.PI / 2;
        var fraction = Math.Clamp(((point.X - Center.X) * Math.Cos(angle) + (point.Y - Center.Y) * Math.Sin(angle)) / Radius, 0, 1);
        return Minimum * (1 - fraction) + Maximum * fraction;
    }
}
public sealed record ChartLayout(DrawingScene Scene, ChartCoordinateMap? Coordinates, RadarCoordinateMap? RadarCoordinates, IReadOnlyList<ChartDiagnostic> Diagnostics);
public sealed record QuadrantPointAppearance(ClassColor Fill, double Radius, ClassColor? Stroke, double StrokeWidth);

public static class ChartDiagramLayout
{
    public static ChartLayout Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null, ChartTarget? hovered = null)
    {
        var chart = model.Chart ?? throw new ArgumentException("No chart data.");
        var diagnostics = ChartValidation.Validate(chart).ToList();
        if (diagnostics.Any(d => d.Kind != ChartDiagnosticKind.Warning)) throw new ChartNotEditableException(new(chart, diagnostics));
        var builder = new Builder(chart, measure, themeOverride ?? model.Theme);
        ChartCoordinateMap? coordinates = null;
        RadarCoordinateMap? radar = null;
        switch (chart)
        {
            case PieChart pie: Pie(builder, pie, hovered); break;
            case QuadrantChart quadrant: coordinates = Quadrant(builder, quadrant); break;
            case XyChart xy: coordinates = Xy(builder, xy); break;
            case RadarChart radial: radar = Radar(builder, radial); break;
        }
        return new(builder.Scene(), coordinates, radar, diagnostics);
    }

    public static ChartTarget? Hit(ChartLayout layout, DiagramPoint point, double tolerance = 5)
    {
        var hit = DrawingHitTest.Hit(layout.Scene, point, tolerance);
        return hit is { } owner && Enum.TryParse<ChartElementRole>(owner.Role, out var role) ? new(role, owner.Id, owner.DetailId) : null;
    }

    public static QuadrantPointAppearance PointAppearance(QuadrantChart chart, QuadrantPoint point, string? theme = null)
    {
        var defaults = new Builder(chart, null, theme);
        var style = new System.Text.Json.Nodes.JsonObject();
        if (point.StyleClass is { } key && chart.StyleClasses.TryGetValue(key, out var shared))
            foreach (var pair in shared) style[pair.Key] = pair.Value?.DeepClone();
        foreach (var pair in point.Style) style[pair.Key] = pair.Value?.DeepClone();
        ClassColor Color(string key, ClassColor fallback) => style[key] is { } raw && SequenceColors.TryParse(ChartConfiguration.Raw(raw), out var color) ? color : fallback;
        double Number(string key, double fallback) => style[key] is { } raw ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(raw)) : fallback;
        return new(Color("color", defaults.Color("theme.quadrantPointFill", defaults.Ink)), Number("radius", defaults.N("pointRadius")),
            Color("stroke-color", defaults.Ink), Number("stroke-width", 0));
    }

    private static void Pie(Builder b, PieChart pie, ChartTarget? hovered)
    {
        var labelFont = b.Font(b.N("theme.pieLegendTextSize"));
        var legend = pie.Slices.Select(s => (Target: new ChartTarget(ChartElementRole.Slice, s.Id), Text: s.Label +
            (pie.ShowData ? " : " + ChartDiagramAdapter.Number(s.Value) : ""))).ToArray();
        var legendWidth = legend.Select(l => b.Measure(l.Text, labelFont).Width + 28).DefaultIfEmpty(0).Max();
        var legendHeight = legend.Length * (labelFont.Size * 1.4 + 8);
        var position = b.S("legendPosition");
        var radius = 160d;
        var titleHeight = pie.Title.Length == 0 ? 20 : b.Measure(pie.Title, b.Font(b.N("theme.pieTitleTextSize"))).Height + 30;
        var center = new DiagramPoint(190 + (position == "left" ? legendWidth + 20 : 0),
            titleHeight + 175 + (position == "top" ? legendHeight + 10 : 0));
        b.Width = Math.Max(400, center.X + radius + 30 + (position == "right" ? legendWidth + 24 : 0));
        b.Height = center.Y + radius + 30 + (position == "bottom" ? legendHeight + 15 : 0);
        if (position is "left" or "right") b.Height = Math.Max(b.Height, titleHeight + legendHeight + 30);
        b.Title(pie.Title, b.N("theme.pieTitleTextSize"), b.Color("theme.pieTitleTextColor", b.Ink));
        var max = pie.Slices.Select(s => s.Value).DefaultIfEmpty(0).Max();
        var total = max == 0 ? 0 : pie.Slices.Sum(s => s.Value / max);
        var angle = -Math.PI / 2;
        for (var i = 0; i < pie.Slices.Count; i++)
        {
            var slice = pie.Slices[i];
            var target = new ChartTarget(ChartElementRole.Slice, slice.Id);
            var proportion = total == 0 ? 0 : (slice.Value / max) / total;
            var sweep = proportion * 2 * Math.PI;
            var highlight = b.S("highlightSlice");
            var active = highlight == "hover" ? hovered == target : highlight.Length > 0 && highlight == slice.Label;
            var middle = angle + sweep / 2;
            var origin = active ? new DiagramPoint(center.X + 9 * Math.Cos(middle), center.Y + 9 * Math.Sin(middle)) : center;
            var color = b.Color("theme.pie" + (i % 12 + 1), b.SeriesColor(i));
            if (sweep > 0)
            {
                var hole = radius * b.N("donutHole");
                b.Path(slice.Id, target, Sector(origin, radius, hole, angle, sweep), Alpha(color, b.N("theme.pieOpacity")),
                    b.Color("theme.pieStrokeColor", b.Background), b.N("theme.pieStrokeWidth"), 2);
                b.Regions.Add(new(Owner(target), DrawingHitKind.Sector, new(origin.X - radius, origin.Y - radius, radius * 2, radius * 2), [], angle, sweep, hole));
                var text = (proportion * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                var point = new DiagramPoint(origin.X + Math.Cos(middle) * radius * b.N("textPosition"), origin.Y + Math.Sin(middle) * radius * b.N("textPosition"));
                b.Label("percentage-" + slice.Id, target, text, b.Font(b.N("theme.pieSectionTextSize")), point, "center",
                    b.Color("theme.pieSectionTextColor", Contrast(color)));
            }
            angle += sweep;
        }
        b.Ellipse("pie-outline", new(ChartElementRole.Chart, ""), new(center.X - radius, center.Y - radius, radius * 2, radius * 2),
            null, b.Color("theme.pieOuterStrokeColor", b.Ink), b.N("theme.pieOuterStrokeWidth"), 3);
        var legendX = position switch { "left" => 20, "right" => center.X + radius + 25, _ => center.X - legendWidth / 2 };
        var legendY = position switch
        {
            "top" => titleHeight + 5, "bottom" => center.Y + radius + 18, "center" => center.Y - legendHeight / 2,
            _ => titleHeight + 10,
        };
        for (var i = 0; i < legend.Length; i++)
            b.Legend(legend[i].Target, legend[i].Text, legendX, legendY + i * (labelFont.Size * 1.4 + 8),
                b.Color("theme.pie" + (i % 12 + 1), b.SeriesColor(i)), labelFont, b.Color("theme.pieLegendTextColor", b.Ink));
    }

    private static ChartCoordinateMap Quadrant(Builder b, QuadrantChart chart)
    {
        b.Width = b.N("chartWidth"); b.Height = b.N("chartHeight");
        var titleHeight = chart.Title.Length == 0 ? 0 : b.N("titleFontSize") * 1.4 + b.N("titlePadding") * 2;
        b.Title(chart.Title, b.N("titleFontSize"), b.Color("theme.quadrantTitleFill", b.Ink));
        var hasPoints = chart.Points.Count > 0;
        var xTop = !hasPoints && b.S("xAxisPosition") == "top";
        var yLeft = b.S("yAxisPosition") == "left";
        var xSpace = b.N("xAxisLabelFontSize") * 1.4 + b.N("xAxisLabelPadding") * 2;
        var ySpace = b.N("yAxisLabelFontSize") * 1.4 + b.N("yAxisLabelPadding") * 2;
        var padding = b.N("quadrantPadding");
        var plot = new DiagramRect(padding + (yLeft ? ySpace : 0), titleHeight + padding + (xTop ? xSpace : 0),
            Math.Max(30, b.Width - ySpace - padding * 2), Math.Max(30, b.Height - titleHeight - xSpace - padding * 2));
        var map = new ChartCoordinateMap(plot, 0, 1, 0, 1);
        var boxes = new[]
        {
            new DiagramRect(plot.X + plot.Width / 2, plot.Y, plot.Width / 2, plot.Height / 2),
            new DiagramRect(plot.X, plot.Y, plot.Width / 2, plot.Height / 2),
            new DiagramRect(plot.X, plot.Y + plot.Height / 2, plot.Width / 2, plot.Height / 2),
            new DiagramRect(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, plot.Width / 2, plot.Height / 2),
        };
        for (var i = 0; i < 4; i++)
        {
            var target = new ChartTarget(ChartElementRole.Chart, "quadrant-" + (i + 1));
            var fill = b.Color("theme.quadrant" + (i + 1) + "Fill", b.Background);
            b.Rectangle("quadrant-" + i, target, boxes[i], fill, null, 0, 0);
            b.Label("quadrant-label-" + i, target, chart.Quadrants[i], b.Font(b.N("quadrantLabelFontSize")),
                new(boxes[i].X + boxes[i].Width / 2, hasPoints ? boxes[i].Y + b.N("quadrantTextTopPadding") : boxes[i].Y + boxes[i].Height / 2),
                "center", b.Color("theme.quadrant" + (i + 1) + "TextFill", Contrast(fill)));
        }
        b.Rectangle("quadrant-border", new(ChartElementRole.Chart, ""), plot, null, b.Color("theme.quadrantExternalBorderStrokeFill", b.Ink), b.N("quadrantExternalBorderStrokeWidth"), 1);
        b.Line("quadrant-h", new(ChartElementRole.Chart, ""), [new(plot.X, plot.Y + plot.Height / 2), new(plot.X + plot.Width, plot.Y + plot.Height / 2)], b.Color("theme.quadrantInternalBorderStrokeFill", b.Ink), b.N("quadrantInternalBorderStrokeWidth"), 1);
        b.Line("quadrant-v", new(ChartElementRole.Chart, ""), [new(plot.X + plot.Width / 2, plot.Y), new(plot.X + plot.Width / 2, plot.Y + plot.Height)], b.Color("theme.quadrantInternalBorderStrokeFill", b.Ink), b.N("quadrantInternalBorderStrokeWidth"), 1);
        var xY = xTop ? titleHeight + b.N("xAxisLabelPadding") : plot.Y + plot.Height + b.N("xAxisLabelPadding");
        b.Label("x-low", new(ChartElementRole.Axis, "x"), chart.XLow, b.Font(b.N("xAxisLabelFontSize")), new(plot.X + plot.Width / 4, xY), "center", b.Color("theme.quadrantXAxisTextFill", b.Ink));
        b.Label("x-high", new(ChartElementRole.Axis, "x"), chart.XHigh, b.Font(b.N("xAxisLabelFontSize")), new(plot.X + 3 * plot.Width / 4, xY), "center", b.Color("theme.quadrantXAxisTextFill", b.Ink));
        var yX = yLeft ? ySpace / 2 : plot.X + plot.Width + ySpace / 2;
        b.Label("y-low", new(ChartElementRole.Axis, "y"), chart.YLow, b.Font(b.N("yAxisLabelFontSize")), new(yX, plot.Y + 3 * plot.Height / 4), "center", b.Color("theme.quadrantYAxisTextFill", b.Ink), -90);
        b.Label("y-high", new(ChartElementRole.Axis, "y"), chart.YHigh, b.Font(b.N("yAxisLabelFontSize")), new(yX, plot.Y + plot.Height / 4), "center", b.Color("theme.quadrantYAxisTextFill", b.Ink), -90);
        foreach (var point in chart.Points)
        {
            var at = map.ToPoint(point.X, point.Y);
            var style = PointAppearance(chart, point, b.Theme);
            var target = new ChartTarget(ChartElementRole.Point, point.Id);
            var bounds = new DiagramRect(at.X - style.Radius, at.Y - style.Radius, style.Radius * 2, style.Radius * 2);
            b.Ellipse(point.Id, target, bounds, style.Fill, style.Stroke, style.StrokeWidth, 3);
            b.Regions.Add(new(Owner(target), DrawingHitKind.Circle, bounds, []));
            b.Label(point.Id + "-label", target, point.Label, b.Font(b.N("pointLabelFontSize")),
                new(at.X, at.Y + style.Radius + b.N("pointTextPadding")), "center", b.Color("theme.quadrantPointTextFill", b.Ink));
        }
        return map;
    }

    private static ChartCoordinateMap Xy(Builder b, XyChart chart)
    {
        b.Width = b.N("width"); b.Height = b.N("height");
        var horizontal = chart.OrientationInBody ? chart.Horizontal : b.S("chartOrientation") == "horizontal";
        var all = chart.Series.SelectMany(s => s.Values).Select(v => v.Value).ToArray();
        var yMin = chart.YAxis.Minimum ?? (all.Length == 0 ? 0 : all.Min());
        var yMax = chart.YAxis.Maximum ?? (all.Length == 0 ? 1 : all.Max());
        var category = chart.XAxis.Kind == ChartAxisKind.Categories;
        var count = chart.Series.FirstOrDefault(s => s.Values.Count > 0)?.Values.Count ?? 1;
        var xMin = chart.XAxis.Minimum ?? (category ? 0 : 1);
        var xMax = chart.XAxis.Maximum ?? (category ? Math.Max(0, chart.XAxis.Categories.Count - 1) : count);
        var xTicks = category ? Enumerable.Range(0, chart.XAxis.Categories.Count).Select(i => (Value: (double)i, Text: chart.XAxis.Categories[i])).ToArray()
            : Ticks(xMin, xMax).Select(n => (Value: n, Text: Short(n))).ToArray();
        var yTicks = Ticks(yMin, yMax).Select(n => (Value: n, Text: Short(n))).ToArray();
        var titleHeight = b.B("showTitle") && chart.Title.Length > 0 ? b.N("titleFontSize") * 1.4 + b.N("titlePadding") * 2 : 0;
        if (titleHeight > 0) b.Title(chart.Title, b.N("titleFontSize"), b.Color("theme.xyChart.titleColor", b.Ink));
        var legends = b.B("showLegend") ? chart.Series.Select((s, i) => (s, i)).Where(p => p.s.Name is not null).ToArray() : [];
        var legendHeight = legends.Length == 0 ? 0 : legends.Length * (b.N("legendFontSize") * 1.4 + b.N("legendPadding"));
        for (var i = 0; i < legends.Length; i++)
            b.Legend(new(ChartElementRole.Series, legends[i].s.Id), legends[i].s.Name!, 30, titleHeight + i * (b.N("legendFontSize") * 1.4 + b.N("legendPadding")),
                b.XyColor(legends[i].i), b.Font(b.N("legendFontSize")), b.Color("theme.xyChart.legendTextColor", b.Ink));
        var leftTicks = horizontal ? xTicks : yTicks;
        var bottomTicks = horizontal ? yTicks : xTicks;
        var leftKey = horizontal ? "xAxis" : "yAxis";
        var bottomKey = horizontal ? "yAxis" : "xAxis";
        var leftTitle = horizontal ? chart.XAxis.Title : chart.YAxis.Title;
        var bottomTitle = horizontal ? chart.YAxis.Title : chart.XAxis.Title;
        var leftWidth = (b.B(leftKey + ".showLabel") ? leftTicks.Select(t => b.Measure(t.Text, b.Font(b.N(leftKey + ".labelFontSize"))).Width).DefaultIfEmpty(0).Max() + b.N(leftKey + ".labelPadding") * 2 : 0)
            + (b.B(leftKey + ".showTitle") && leftTitle.Length > 0 ? b.N(leftKey + ".titleFontSize") * 1.4 + b.N(leftKey + ".titlePadding") * 2 : 0) + 15;
        var rotation = horizontal ? 0 : b.N("xAxis.labelRotation");
        if (Math.Abs(rotation) > 90) rotation = 0;
        var bottomLabel = bottomTicks.Select(t => b.Measure(t.Text, b.Font(b.N(bottomKey + ".labelFontSize")))).DefaultIfEmpty(new()).ToArray();
        var bottomHeight = (b.B(bottomKey + ".showLabel") ? bottomLabel.Max(t => Math.Abs(Math.Sin(rotation * Math.PI / 180) * t.Width) + Math.Abs(Math.Cos(rotation * Math.PI / 180) * t.Height)) + b.N(bottomKey + ".labelPadding") * 2 : 0)
            + (b.B(bottomKey + ".showTitle") && bottomTitle.Length > 0 ? b.N(bottomKey + ".titleFontSize") * 1.4 + b.N(bottomKey + ".titlePadding") * 2 : 0) + 20;
        var plot = new DiagramRect(leftWidth, titleHeight + legendHeight + 10, Math.Max(30, b.Width - leftWidth - 20), Math.Max(30, b.Height - titleHeight - legendHeight - bottomHeight - 10));
        var xSpan = horizontal ? plot.Height : plot.Width;
        var ySpan = horizontal ? plot.Width : plot.Height;
        var xPad = b.B("xAxis.showLabel") ? Math.Min(xTicks.Select(t => b.Measure(t.Text, b.Font(b.N("xAxis.labelFontSize")))).Select(s => horizontal ? s.Height / 2 : s.Width / 2).DefaultIfEmpty(0).Max(), xSpan * .2) : 0;
        if (chart.Series.Any(s => s.Kind == ChartSeriesKind.Bar))
            xPad = Math.Max(xPad, Math.Floor(.7 * Math.Max(0, xSpan - 2 * xPad) / Math.Max(1, xTicks.Length) / 2));
        var yPad = b.B("yAxis.showLabel") ? Math.Min(yTicks.Select(t => b.Measure(t.Text, b.Font(b.N("yAxis.labelFontSize")))).Select(s => horizontal ? s.Width / 2 : s.Height / 2).DefaultIfEmpty(0).Max(), ySpan * .2) : 0;
        var data = horizontal ? new DiagramRect(plot.X + yPad, plot.Y + xPad, Math.Max(1, plot.Width - 2 * yPad), Math.Max(1, plot.Height - 2 * xPad))
            : new DiagramRect(plot.X + xPad, plot.Y + yPad, Math.Max(1, plot.Width - 2 * xPad), Math.Max(1, plot.Height - 2 * yPad));
        var map = new ChartCoordinateMap(data, xMin, xMax, yMin, yMax, horizontal, category);
        Axis(true, chart.XAxis.Title, xTicks); Axis(false, chart.YAxis.Title, yTicks);
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var color = b.XyColor(seriesIndex);
            var points = new List<DiagramPoint>();
            for (var i = 0; i < series.Values.Count; i++)
            {
                var value = series.Values[i];
                var x = category ? i : series.Values.Count == 1 ? xMin : xMin * (1 - (double)i / (series.Values.Count - 1)) + xMax * ((double)i / (series.Values.Count - 1));
                var at = map.ToPoint(x, value.Value);
                var target = new ChartTarget(ChartElementRole.Point, value.Id, series.Id);
                points.Add(at);
                if (series.Kind == ChartSeriesKind.Bar)
                {
                    var width = Math.Min(2 * xPad, Math.Max(1, xSpan - 2 * xPad) / Math.Max(1, xTicks.Length)) * .95;
                    var bounds = horizontal ? new DiagramRect(Math.Min(plot.X, at.X), at.Y - width / 2, Math.Abs(at.X - plot.X), width)
                        : new DiagramRect(at.X - width / 2, Math.Min(at.Y, plot.Y + plot.Height), width, Math.Abs(plot.Y + plot.Height - at.Y));
                    var clipped = Intersect(bounds, plot);
                    if (clipped.Width > 0 && clipped.Height > 0)
                    {
                        b.Rectangle(value.Id, target, clipped, color, null, 0, 3 + seriesIndex);
                        b.Regions.Add(new(Owner(target), DrawingHitKind.Rectangle, clipped, []));
                    }
                    if (b.B("showDataLabel"))
                    {
                        var outside = b.B("showDataLabelOutsideBar");
                        var labelPoint = horizontal ? new DiagramPoint(outside ? at.X + 5 : (at.X + plot.X) / 2, at.Y)
                            : new DiagramPoint(at.X, outside ? at.Y - 18 : (at.Y + plot.Y + plot.Height) / 2);
                        b.Label(value.Id + "-value", target, Short(value.Value), b.Font(12), labelPoint, horizontal && outside ? "left" : "center",
                            b.Color("theme.xyChart.dataLabelColor", outside ? b.Ink : Contrast(color)));
                    }
                }
                else
                {
                    b.Regions.Add(new(Owner(target), DrawingHitKind.Circle, new(at.X - 6, at.Y - 6, 12, 12), [], Clip: plot));
                    if (value.Label is { } label)
                        b.Label(value.Id + "-label", target, label, b.Font(12), horizontal ? new(at.X + 5, at.Y) : new(at.X, at.Y - 18), horizontal ? "left" : "center", color);
                }
            }
            if (series.Kind == ChartSeriesKind.Line)
            {
                b.Line(series.Id, new(ChartElementRole.Series, series.Id), points, color, 2, 3 + seriesIndex, plot);
                b.Regions.Add(new(Owner(new(ChartElementRole.Series, series.Id)), DrawingHitKind.Polyline, Bounds(points), points, Clip: plot));
            }
        }
        return map;

        void Axis(bool isX, string title, (double Value, string Text)[] ticks)
        {
            var key = isX ? "xAxis" : "yAxis";
            var bottom = horizontal ? !isX : isX;
            var target = new ChartTarget(ChartElementRole.Axis, isX ? "x" : "y");
            var colorKey = "theme.xyChart." + key;
            if (b.B(key + ".showAxisLine"))
                b.Line(key + "-axis", target, bottom ? [new(plot.X, plot.Y + plot.Height), new(plot.X + plot.Width, plot.Y + plot.Height)]
                    : [new(plot.X, plot.Y), new(plot.X, plot.Y + plot.Height)], b.Color(colorKey + "LineColor", b.Ink), b.N(key + ".axisLineWidth"), 2);
            foreach (var tick in ticks)
            {
                var at = isX ? map.ToPoint(tick.Value, yMin) : map.ToPoint(xMin, tick.Value);
                if (b.B(key + ".showTick"))
                    b.Line(key + "-tick-" + tick.Value, target, bottom ? [new(at.X, plot.Y + plot.Height), new(at.X, plot.Y + plot.Height + b.N(key + ".tickLength"))]
                        : [new(plot.X - b.N(key + ".tickLength"), at.Y), new(plot.X, at.Y)], b.Color(colorKey + "TickColor", b.Ink), b.N(key + ".tickWidth"), 2);
                if (b.B(key + ".showLabel"))
                    b.Label(key + "-label-" + tick.Value, target, tick.Text, b.Font(b.N(key + ".labelFontSize")),
                        bottom ? new(at.X, plot.Y + plot.Height + b.N(key + ".tickLength") + b.N(key + ".labelPadding")) : new(plot.X - 8, at.Y),
                        bottom ? "center" : "right", b.Color(colorKey + "LabelColor", b.Ink), bottom && isX ? rotation : 0);
            }
            if (b.B(key + ".showTitle") && title.Length > 0)
                b.Label(key + "-title", target, title, b.Font(b.N(key + ".titleFontSize")), bottom ? new(plot.X + plot.Width / 2, b.Height - b.N(key + ".titleFontSize") * 1.4 - b.N(key + ".titlePadding"))
                    : new(b.N(key + ".titleFontSize"), plot.Y + plot.Height / 2), "center", b.Color(colorKey + "TitleColor", b.Ink), bottom ? 0 : -90);
        }
    }

    private static RadarCoordinateMap Radar(Builder b, RadarChart chart)
    {
        var width = b.N("width"); var height = b.N("height");
        b.Width = width + b.N("marginLeft") + b.N("marginRight");
        b.Height = height + b.N("marginTop") + b.N("marginBottom");
        var center = new DiagramPoint(b.N("marginLeft") + width / 2, b.N("marginTop") + height / 2);
        var radius = Math.Min(width, height) / 2;
        var maximum = chart.Maximum ?? chart.Curves.SelectMany(c => c.Values).Select(v => v.Value).DefaultIfEmpty(chart.Minimum + 1).Max();
        if (maximum <= chart.Minimum) maximum = chart.Minimum + 1;
        var map = new RadarCoordinateMap(center, radius, chart.Minimum, maximum, chart.Axes.Select(a => a.Id).ToArray());
        b.Title(chart.Title, b.N("theme.fontSize"), b.Color("theme.titleColor", b.Ink));
        for (var tick = 1; tick <= chart.Ticks; tick++)
        {
            var r = radius * tick / chart.Ticks;
            var fill = Alpha(b.Color("theme.radar.graticuleColor", b.Ink), b.N("theme.radar.graticuleOpacity"));
            if (chart.Grid == RadarGridKind.Circle)
                b.Ellipse("grid-" + tick, new(ChartElementRole.Chart, ""), new(center.X - r, center.Y - r, r * 2, r * 2), fill, b.Color("theme.radar.graticuleColor", b.Ink), b.N("theme.radar.graticuleStrokeWidth"), 0);
            else if (chart.Axes.Count > 0) b.Polygon("grid-" + tick, new(ChartElementRole.Chart, ""), chart.Axes.Select((_, i) => At(i, r)).ToArray(), fill, b.Color("theme.radar.graticuleColor", b.Ink), b.N("theme.radar.graticuleStrokeWidth"), 0);
        }
        for (var index = 0; index < chart.Axes.Count; index++)
        {
            var axis = chart.Axes[index]; var target = new ChartTarget(ChartElementRole.Axis, axis.Id);
            b.Line("axis-" + axis.Id, target, [center, At(index, radius * b.N("axisScaleFactor"))], b.Color("theme.radar.axisColor", b.Ink), b.N("theme.radar.axisStrokeWidth"), 1);
            var at = At(index, radius * b.N("axisLabelFactor") + 4);
            b.Label("axis-label-" + axis.Id, target, axis.Display, b.Font(b.N("theme.radar.axisLabelFontSize")), at,
                at.X > center.X + 1 ? "left" : at.X < center.X - 1 ? "right" : "center", b.Color("theme.radar.axisColor", b.Ink));
        }
        for (var index = 0; index < chart.Curves.Count; index++)
        {
            var curve = chart.Curves[index]; var target = new ChartTarget(ChartElementRole.Curve, curve.Id);
            var points = chart.Axes.Select((axis, i) => At(i, radius * Math.Clamp(ChartCoordinateMap.Fraction(curve.Values.Single(v => v.AxisId == axis.Id).Value, chart.Minimum, maximum), 0, 1))).ToArray();
            if (points.Length == 0) continue;
            var color = b.Color("theme.cScale" + index % 12, b.SeriesColor(index));
            if (chart.Grid == RadarGridKind.Circle && points.Length > 2)
                b.Path("curve-" + curve.Id, target, Rounded(points, b.N("curveTension")), Alpha(color, b.N("theme.radar.curveOpacity")), color, b.N("theme.radar.curveStrokeWidth"), 3 + index);
            else b.Polygon("curve-" + curve.Id, target, points, Alpha(color, b.N("theme.radar.curveOpacity")), color, b.N("theme.radar.curveStrokeWidth"), 3 + index);
            b.Regions.Add(new(Owner(target), DrawingHitKind.Polygon, Bounds(points), points));
            for (var i = 0; i < points.Length; i++)
                b.Regions.Add(new(Owner(new(ChartElementRole.CurveValue, curve.Id, chart.Axes[i].Id)), DrawingHitKind.Circle, new(points[i].X - 6, points[i].Y - 6, 12, 12), []));
            if (chart.ShowLegend)
                b.Legend(target, curve.Display, b.Width - Math.Max(130, b.N("marginRight")), 45 + index * (b.N("theme.radar.legendFontSize") * 1.4 + 6),
                    color, b.Font(b.N("theme.radar.legendFontSize")), b.Ink, b.N("theme.radar.legendBoxSize"));
        }
        return map;
        DiagramPoint At(int index, double r)
        {
            var angle = index * 2 * Math.PI / Math.Max(1, chart.Axes.Count) - Math.PI / 2;
            return new(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
        }
    }

    public static IReadOnlyList<DrawingPathPart> Sector(DiagramPoint center, double outer, double inner, double start, double sweep)
    {
        var path = new List<DrawingPathPart>();
        DiagramPoint At(double radius, double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        path.Add(new(DrawingPathKind.Move, At(outer, start)));
        Arc(outer, start, sweep);
        if (inner > 0)
        {
            path.Add(new(DrawingPathKind.Line, At(inner, start + sweep)));
            Arc(inner, start + sweep, -sweep);
        }
        else path.Add(new(DrawingPathKind.Line, center));
        path.Add(new(DrawingPathKind.Close, default));
        return path;
        void Arc(double radius, double angle, double delta)
        {
            var parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
            var step = delta / parts;
            for (var i = 0; i < parts; i++)
            {
                var a = angle + i * step; var z = a + step; var k = 4d / 3 * Math.Tan(step / 4);
                var p = At(radius, a); var q = At(radius, z);
                path.Add(new(DrawingPathKind.Cubic, q, new(p.X - k * radius * Math.Sin(a), p.Y + k * radius * Math.Cos(a)),
                    new(q.X + k * radius * Math.Sin(z), q.Y - k * radius * Math.Cos(z))));
            }
        }
    }
    public static IReadOnlyList<DrawingPathPart> Rounded(IReadOnlyList<DiagramPoint> points, double tension)
    {
        var path = new List<DrawingPathPart> { new(DrawingPathKind.Move, points[0]) };
        for (var i = 0; i < points.Count; i++)
        {
            var p0 = points[(i - 1 + points.Count) % points.Count]; var p1 = points[i];
            var p2 = points[(i + 1) % points.Count]; var p3 = points[(i + 2) % points.Count];
            path.Add(new(DrawingPathKind.Cubic, p2, new(p1.X + (p2.X - p0.X) * tension, p1.Y + (p2.Y - p0.Y) * tension),
                new(p2.X - (p3.X - p1.X) * tension, p2.Y - (p3.Y - p1.Y) * tension)));
        }
        path.Add(new(DrawingPathKind.Close, default));
        return path;
    }
    private static IReadOnlyList<double> Ticks(double min, double max)
    {
        if (min == max) return [min];
        var raw = Math.Abs(max / 10 - min / 10);
        if (raw == 0 || !double.IsFinite(raw)) return [min, max];
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var error = raw / power;
        var step = power * (error >= Math.Sqrt(50) ? 10 : error >= Math.Sqrt(10) ? 5 : error >= Math.Sqrt(2) ? 2 : 1);
        var start = Math.Ceiling(min / step); var end = Math.Floor(max / step);
        var values = new List<double>();
        for (var i = start; i <= end && values.Count < 100; i++) values.Add(i * step);
        return values.Count == 0 ? [min, max] : values;
    }
    private static string Short(double number) => number.ToString("G5", CultureInfo.InvariantCulture);
    private static DrawingOwner Owner(ChartTarget target) => new(target.Role.ToString(), target.Id, target.DetailId);
    private static ClassColor Alpha(ClassColor color, double opacity) => color with { Alpha = (byte)Math.Round(color.Alpha * opacity) };
    private static ClassColor Contrast(ClassColor color) => .2126 * color.Red + .7152 * color.Green + .0722 * color.Blue > 145 ? new(0, 0, 0) : new(255, 255, 255);
    private static DiagramRect Bounds(IReadOnlyList<DiagramPoint> points) => points.Count == 0 ? new(0, 0, 0, 0) :
        new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
    private static DiagramRect Intersect(DiagramRect a, DiagramRect b)
    {
        var x = Math.Max(a.X, b.X); var y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    private sealed class Builder(ChartDocument chart, DrawingTextMeasure? measure, string? theme)
    {
        public double Width { get; set; } = 700;
        public double Height { get; set; } = 500;
        public string? Theme => theme;
        public ClassColor Background => theme == "dark" ? new(31, 32, 32) : new(255, 255, 255);
        public ClassColor Ink => theme == "dark" ? new(238, 242, 247) : new(31, 41, 55);
        public List<DrawingFigure> Figures { get; } = new();
        public List<DrawingLabel> Labels { get; } = new();
        public List<DrawingRegion> Regions { get; } = new();
        public double N(string key) => ChartConfiguration.Number(chart, key);
        public bool B(string key) => ChartConfiguration.Boolean(chart, key);
        public string S(string key) => ChartConfiguration.Text(chart, key);
        public DrawingFont Font(double size) => new(ChartConfiguration.Value(chart, "global.fontFamily")?.GetValue<string>() ??
            chart.ThemeVariables["fontFamily"]?.GetValue<string>() ?? "Segoe UI", size);
        public DrawingTextSize Measure(string text, DrawingFont font)
        {
            var value = measure?.Invoke(text, font) ?? new(text.Length * font.Size * .6, font.Size * 1.4);
            return double.IsFinite(value.Width) && double.IsFinite(value.Height) && value.Width >= 0 && value.Height >= 0
                ? value : throw new ArgumentException("Drawing text metrics must be finite and nonnegative.");
        }
        public ClassColor Color(string key, ClassColor fallback)
        {
            var explicitValue = ChartConfiguration.Value(chart, key);
            var raw = explicitValue is null ? S(key) : ChartConfiguration.Raw(explicitValue);
            if (explicitValue is null && key is "theme.pieSectionTextColor" or "theme.xyChart.dataLabelColor") return fallback;
            if (explicitValue is null && theme == "dark" && key == "theme.xyChart.backgroundColor") return Background;
            if (explicitValue is null && theme == "dark" && key.StartsWith("theme.quadrant", StringComparison.Ordinal) &&
                key.Length == "theme.quadrant1Fill".Length && key.EndsWith("Fill", StringComparison.Ordinal))
                return new(42, 53, (byte)(70 + (key["theme.quadrant".Length] - '1') * 9));
            if (explicitValue is null && theme == "dark" && raw is "#1F2937" or "#111827" or "#374151") return Ink;
            return SequenceColors.TryParse(raw, out var color) ? color : fallback;
        }
        public ClassColor SeriesColor(int index) { SequenceColors.TryParse(ChartConfiguration.Palette[index % ChartConfiguration.Palette.Length], out var color); return color; }
        public ClassColor XyColor(int index)
        {
            var palette = ChartDiagramAdapter.SplitList(S("theme.xyChart.plotColorPalette"));
            return palette.Count > 0 && SequenceColors.TryParse(palette[index % palette.Count], out var color) ? color : SeriesColor(index);
        }
        public void Title(string title, double size, ClassColor color)
        {
            if (title.Length > 0) Label("title", new(ChartElementRole.Chart, ""), title, Font(size), new(Width / 2, 10), "center", color);
        }
        public void Rectangle(string id, ChartTarget owner, DiagramRect bounds, ClassColor? fill, ClassColor? stroke, double width, int layer) =>
            Figures.Add(new(id, Owner(owner), DrawingFigureKind.Rectangle, bounds, [], [], fill, stroke, width, Layer: layer));
        public void Ellipse(string id, ChartTarget owner, DiagramRect bounds, ClassColor? fill, ClassColor? stroke, double width, int layer) =>
            Figures.Add(new(id, Owner(owner), DrawingFigureKind.Ellipse, bounds, [], [], fill, stroke, width, Layer: layer));
        public void Polygon(string id, ChartTarget owner, IReadOnlyList<DiagramPoint> points, ClassColor? fill, ClassColor? stroke, double width, int layer) =>
            Figures.Add(new(id, Owner(owner), DrawingFigureKind.Polygon, Bounds(points), points, [], fill, stroke, width, Layer: layer));
        public void Line(string id, ChartTarget owner, IReadOnlyList<DiagramPoint> points, ClassColor? stroke, double width, int layer, DiagramRect? clip = null) =>
            Figures.Add(new(id, Owner(owner), DrawingFigureKind.Polyline, Bounds(points), points, [], null, stroke, width, Layer: layer, Clip: clip));
        public void Path(string id, ChartTarget owner, IReadOnlyList<DrawingPathPart> path, ClassColor? fill, ClassColor? stroke, double width, int layer)
        {
            var points = path.Where(p => p.Kind != DrawingPathKind.Close).SelectMany(p => p.Kind == DrawingPathKind.Cubic ? new[] { p.End, p.Control1, p.Control2 } : new[] { p.End }).ToArray();
            Figures.Add(new(id, Owner(owner), DrawingFigureKind.Path, Bounds(points), [], path, fill, stroke, width, Layer: layer));
        }
        public void Label(string id, ChartTarget owner, string text, DrawingFont font, DiagramPoint anchor, string alignment, ClassColor color, double rotation = 0)
        {
            if (text.Length == 0) return;
            var size = Measure(text, font);
            var x = anchor.X - (alignment == "center" ? size.Width / 2 : alignment == "right" ? size.Width : 0);
            var bounds = new DiagramRect(x, anchor.Y, size.Width, size.Height);
            Labels.Add(new(id, Owner(owner), text, font, color, bounds, alignment, rotation));
            Regions.Add(new(Owner(owner), DrawingHitKind.Rectangle, bounds, []));
        }
        public void Legend(ChartTarget owner, string text, double x, double y, ClassColor color, DrawingFont font, ClassColor ink, double size = 12)
        {
            Rectangle("legend-" + owner.Id, owner, new(x, y + 2, size, size), color, null, 0, 10);
            Label("legend-label-" + owner.Id, owner, text, font, new(x + size + 8, y), "left", ink);
        }
        public DrawingScene Scene()
        {
            var background = chart is XyChart ? Color("theme.xyChart.backgroundColor", Background) : Background;
            var figures = Figures.OrderBy(f => f.Layer).ToList();
            figures.Insert(0, new("background", Owner(new(ChartElementRole.Chart, "")), DrawingFigureKind.Rectangle,
                new(0, 0, Width, Height), [], [], background, null, 0, Layer: -1));
            return new(Width, Height, figures, Labels,
                Regions.OrderBy(r => r.Owner.Role is "Point" or "CurveValue" ? 1 : 0).ToArray(), measure is null);
        }
    }
}
