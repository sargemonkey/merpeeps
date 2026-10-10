using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class ChartValidation
{
    public static IReadOnlyList<ChartDiagnostic> Validate(ChartDocument chart)
    {
        var diagnostics = new List<ChartDiagnostic>();
        void Error(string code, ChartElementRole role, string id, string message, ChartDiagnosticKind kind = ChartDiagnosticKind.InvalidData) =>
            diagnostics.Add(new(code, new(role, id), message, kind));
        void Text(string text, ChartElementRole role, string id)
        {
            try { ChartDiagramAdapter.Quote(text); }
            catch (ArgumentException error) { Error("text", role, id, error.Message); }
        }
        Text(chart.Title, ChartElementRole.Chart, "");
        switch (chart)
        {
            case PieChart pie:
                var labels = new HashSet<string>();
                var sliceIds = new HashSet<string>();
                foreach (var slice in pie.Slices)
                {
                    Text(slice.Label, ChartElementRole.Slice, slice.Id);
                    if (!sliceIds.Add(slice.Id)) Error("identity", ChartElementRole.Slice, slice.Id, "Slice identities must be unique.");
                    if (!labels.Add(slice.Label)) Error("duplicate-label", ChartElementRole.Slice, slice.Id,
                        "Mermaid ignores later slices with the same label. Rename duplicate labels before native editing.", ChartDiagnosticKind.NativeUnsupported);
                    if (!double.IsFinite(slice.Value) || slice.Value < 0 || Math.Abs(slice.Value * 100 - Math.Round(slice.Value * 100)) > 1e-6)
                        Error("slice-value", ChartElementRole.Slice, slice.Id, "Pie values must be nonnegative and have at most two decimal places.");
                }
                if (pie.Slices.Count > 0 && pie.Slices.All(s => s.Value == 0))
                    Error("zero-total", ChartElementRole.Chart, "", "Zero-valued slices have no area; percentages are not shown.", ChartDiagnosticKind.Warning);
                break;
            case QuadrantChart quadrant:
                if (quadrant.Quadrants.Length != 4) Error("quadrants", ChartElementRole.Chart, "", "A quadrant chart has exactly four regions.");
                var pointIds = new HashSet<string>();
                foreach (var point in quadrant.Points)
                {
                    Text(point.Label, ChartElementRole.Point, point.Id);
                    if (!pointIds.Add(point.Id)) Error("identity", ChartElementRole.Point, point.Id, "Point identities must be unique.");
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X is < 0 or > 1 || point.Y is < 0 or > 1)
                        Error("point-range", ChartElementRole.Point, point.Id, "Quadrant coordinates must lie between 0 and 1.");
                    if (point.StyleClass is { } style && !quadrant.StyleClasses.ContainsKey(style))
                        Error("point-class", ChartElementRole.Point, point.Id, "Point style class does not exist.", ChartDiagnosticKind.NativeUnsupported);
                    foreach (var values in new[] { point.Style, point.StyleClass is { } classId ? quadrant.StyleClasses.GetValueOrDefault(classId) : null }.Where(s => s is not null))
                        foreach (var pair in values!)
                        {
                            try
                            {
                                var raw = pair.Value is null ? "" : ChartConfiguration.Raw(pair.Value);
                                if (pair.Key is "color" or "stroke-color")
                                {
                                    if (!SequenceColors.TryParse(raw, out _)) throw new ArgumentException("Unsupported point color.");
                                }
                                else if (pair.Key is "radius" or "stroke-width")
                                {
                                    if (ChartConfiguration.ParseNumber(raw) < 0) throw new ArgumentException("Point dimensions must be nonnegative.");
                                }
                                else throw new ArgumentException("Unknown point style: " + pair.Key);
                            }
                            catch (Exception error) when (error is ArgumentException or FormatException)
                            { Error("point-style", ChartElementRole.Point, point.Id, error.Message, ChartDiagnosticKind.NativeUnsupported); }
                        }
                }
                break;
            case XyChart xy:
                if (xy.YAxis.Kind == ChartAxisKind.Categories) Error("y-axis", ChartElementRole.Axis, "y", "The Y axis must be numeric.");
                foreach (var (axis, id) in new[] { (xy.XAxis, "x"), (xy.YAxis, "y") })
                {
                    if (!Enum.IsDefined(axis.Kind)) Error("axis-kind", ChartElementRole.Axis, id, "Unknown axis type.");
                    Text(axis.Title, ChartElementRole.Axis, id);
                    foreach (var category in axis.Categories) Text(category, ChartElementRole.Axis, id);
                    if (axis.Kind == ChartAxisKind.Range && (axis.Minimum is not { } min || axis.Maximum is not { } max || !double.IsFinite(min) || !double.IsFinite(max) || min >= max))
                        Error("axis-range", ChartElementRole.Axis, id, "Explicit axis ranges require finite minimum < maximum.");
                }
                var ids = new HashSet<string>();
                foreach (var series in xy.Series)
                {
                    if (!ids.Add(series.Id)) Error("identity", ChartElementRole.Series, series.Id, "Series identities must be unique.");
                    if (!Enum.IsDefined(series.Kind)) Error("series-kind", ChartElementRole.Series, series.Id, "Unknown XY series kind.");
                    if (series.Name is { } name) Text(name, ChartElementRole.Series, series.Id);
                    if (xy.XAxis.Kind == ChartAxisKind.Categories && series.Values.Count > xy.XAxis.Categories.Count)
                        Error("category-count", ChartElementRole.Series, series.Id, "There are more values than named X categories.", ChartDiagnosticKind.NativeUnsupported);
                    foreach (var value in series.Values)
                    {
                        if (!ids.Add(value.Id)) Error("identity", ChartElementRole.Point, value.Id, "Point identities must be unique.");
                        if (!double.IsFinite(value.Value)) Error("point-value", ChartElementRole.Point, value.Id, "XY values must be finite.");
                        if (value.Label is { } label) Text(label, ChartElementRole.Point, value.Id);
                        if (series.Kind == ChartSeriesKind.Bar && value.Label is not null)
                            Error("bar-point-label", ChartElementRole.Point, value.Id, "Mermaid retains but does not render per-point labels on bars.", ChartDiagnosticKind.Warning);
                    }
                }
                break;
            case RadarChart radar:
                var axes = new HashSet<string>();
                foreach (var axis in radar.Axes)
                {
                    if (!axes.Add(axis.Id)) Error("axis-id", ChartElementRole.Axis, axis.Id, "Radar axis identities must be unique.");
                    try { ChartDiagramAdapter.Identifier(axis.Id); }
                    catch (ArgumentException error) { Error("axis-id", ChartElementRole.Axis, axis.Id, error.Message); }
                    if (axis.Label is { } label) Text(label, ChartElementRole.Axis, axis.Id);
                }
                var curves = new HashSet<string>();
                foreach (var curve in radar.Curves)
                {
                    if (!curves.Add(curve.Id)) Error("curve-id", ChartElementRole.Curve, curve.Id, "Radar curve identities must be unique within curves.");
                    try { ChartDiagramAdapter.Identifier(curve.Id); }
                    catch (ArgumentException error) { Error("curve-id", ChartElementRole.Curve, curve.Id, error.Message); }
                    if (curve.Label is { } label) Text(label, ChartElementRole.Curve, curve.Id);
                    if (curve.Values.Count != axes.Count || curve.Values.Select(v => v.AxisId).Distinct().Count() != axes.Count || curve.Values.Any(v => !axes.Contains(v.AxisId)))
                        Error("curve-axis-values", ChartElementRole.Curve, curve.Id, "Every radar curve needs exactly one value for each axis; missing data is not invented.");
                    foreach (var value in curve.Values)
                        if (!double.IsFinite(value.Value)) Error("curve-value", ChartElementRole.Curve, curve.Id, "Radar values must be finite.");
                }
                if (!double.IsFinite(radar.Minimum) || radar.Maximum is { } upper && (!double.IsFinite(upper) || upper <= radar.Minimum))
                    Error("radar-range", ChartElementRole.Chart, "", "Radar bounds require finite minimum < maximum.");
                if (radar.Ticks is < 1 or > 1000) Error("radar-ticks", ChartElementRole.Chart, "", "Choose between 1 and 1000 radar ticks.");
                if (!Enum.IsDefined(radar.Grid)) Error("radar-grid", ChartElementRole.Chart, "", "Unknown radar graticule.");
                break;
        }
        diagnostics.AddRange(ChartConfiguration.Validate(chart));
        return diagnostics;
    }
}
