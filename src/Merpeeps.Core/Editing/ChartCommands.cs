using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class ChartEditCommand : IEditCommand
{
    private readonly Action<ChartDocument> _change;
    private ChartDocument? _before;
    public string Description { get; }
    public ChartTarget? Result { get; }
    internal ChartEditCommand(string description, ChartTarget? result, Action<ChartDocument> change) =>
        (Description, Result, _change) = (description, result, change);
    public void Do(DiagramModel model)
    {
        var original = model.Chart ?? throw new InvalidOperationException("This operation requires chart data.");
        var candidate = original.Clone();
        _change(candidate);
        var diagnostics = ChartValidation.Validate(candidate);
        if (diagnostics.Any(d => d.Kind != ChartDiagnosticKind.Warning)) throw new ChartNotEditableException(new(candidate, diagnostics));
        _ = ChartDiagramAdapter.Write(candidate);
        _before = original;
        model.Chart = candidate;
    }
    public void Undo(DiagramModel model) => model.Chart = _before ?? throw new InvalidOperationException("Chart edit has not been applied.");
}

public static class ChartEditing
{
    public static string NewId() => "c" + Guid.NewGuid().ToString("N");
    public static ChartEditCommand SetTitle(string title) => new("Rename chart", new(ChartElementRole.Chart, ""), chart => chart.Title = title);
    public static ChartEditCommand SetOption(string key, string? value) => new("Set chart option", new(ChartElementRole.Chart, ""), chart => ChartConfiguration.Set(chart, key, value));
    public static ChartEditCommand AddSlice(PieSlice slice, int? index = null) => new("Add pie slice", new(ChartElementRole.Slice, slice.Id), chart =>
    {
        var pie = Require<PieChart>(chart);
        pie.Slices.Insert(index ?? pie.Slices.Count, slice);
    });
    public static ChartEditCommand UpdateSlice(PieSlice slice) => new("Edit pie slice", new(ChartElementRole.Slice, slice.Id), chart =>
    {
        var pie = Require<PieChart>(chart);
        pie.Slices[Index(pie.Slices, s => s.Id == slice.Id)] = slice;
    });
    public static ChartEditCommand MoveSlice(string id, int index) => new("Reorder pie slices", new(ChartElementRole.Slice, id), chart => Move(Require<PieChart>(chart).Slices, s => s.Id == id, index));
    public static ChartEditCommand DeleteSlice(string id) => new("Delete pie slice", null, chart =>
    {
        var slices = Require<PieChart>(chart).Slices;
        slices.RemoveAt(Index(slices, s => s.Id == id));
    });
    public static ChartEditCommand ShowPieData(bool show) => new("Change pie labels", new(ChartElementRole.Chart, ""), chart => Require<PieChart>(chart).ShowData = show);

    public static ChartEditCommand AddQuadrantPoint(QuadrantPoint point)
    {
        var copy = point.DeepCopy();
        return new("Add quadrant point", new(ChartElementRole.Point, point.Id), chart => Require<QuadrantChart>(chart).Points.Add(copy.DeepCopy()));
    }
    public static ChartEditCommand UpdateQuadrantPoint(QuadrantPoint point)
    {
        var copy = point.DeepCopy();
        return new("Edit quadrant point", new(ChartElementRole.Point, point.Id), chart =>
        {
            var points = Require<QuadrantChart>(chart).Points;
            points[Index(points, p => p.Id == point.Id)] = copy.DeepCopy();
        });
    }
    public static ChartEditCommand MoveQuadrantPoint(string id, double x, double y) => new("Move quadrant data point", new(ChartElementRole.Point, id), chart =>
    {
        var points = Require<QuadrantChart>(chart).Points;
        var index = Index(points, p => p.Id == id);
        points[index] = points[index] with { X = x, Y = y };
    });
    public static ChartEditCommand DeleteQuadrantPoint(string id) => new("Delete quadrant point", null, chart =>
    {
        var points = Require<QuadrantChart>(chart).Points;
        points.RemoveAt(Index(points, p => p.Id == id));
    });
    public static ChartEditCommand SetQuadrantLabels(string xLow, string xHigh, string yLow, string yHigh, IReadOnlyList<string> quadrants)
    {
        var labels = quadrants.ToArray();
        return new("Edit quadrant labels", new(ChartElementRole.Chart, ""), chart =>
        {
            var quadrant = Require<QuadrantChart>(chart);
            quadrant.XLow = xLow; quadrant.XHigh = xHigh; quadrant.YLow = yLow; quadrant.YHigh = yHigh;
            quadrant.Quadrants = labels.ToArray();
        });
    }
    public static ChartEditCommand SetPointStyleClass(string id, JsonObject? style)
    {
        var copy = style?.DeepClone().AsObject();
        return new("Edit point style class", new(ChartElementRole.Chart, ""), chart =>
        {
            var quadrant = Require<QuadrantChart>(chart);
            if (copy is null) quadrant.StyleClasses.Remove(id);
            else quadrant.StyleClasses[id] = (JsonObject)copy.DeepClone();
        });
    }

    public static ChartEditCommand SetXyAxis(bool xAxis, ChartAxis axis)
    {
        var copy = axis.DeepCopy();
        return new("Edit chart axis", new(ChartElementRole.Axis, xAxis ? "x" : "y"), chart =>
        {
            var xy = Require<XyChart>(chart);
            if (xAxis) xy.XAxis = copy.DeepCopy(); else xy.YAxis = copy.DeepCopy();
        });
    }
    public static ChartEditCommand SetXyOrientation(bool horizontal) => new("Rotate chart orientation", new(ChartElementRole.Chart, ""), chart =>
    {
        var xy = Require<XyChart>(chart); xy.Horizontal = horizontal; xy.OrientationInBody = true;
    });
    public static ChartEditCommand AddSeries(XySeries series)
    {
        var copy = series with { Values = series.Values.ToArray() };
        return new("Add chart series", new(ChartElementRole.Series, series.Id), chart => Require<XyChart>(chart).Series.Add(copy with { Values = copy.Values.ToArray() }));
    }
    public static ChartEditCommand UpdateSeries(string id, ChartSeriesKind kind, string? name) => new("Edit chart series", new(ChartElementRole.Series, id), chart =>
    {
        var series = Require<XyChart>(chart).Series;
        var index = Index(series, s => s.Id == id);
        series[index] = series[index] with { Kind = kind, Name = name };
    });
    public static ChartEditCommand MoveSeries(string id, int index) => new("Reorder chart series", new(ChartElementRole.Series, id), chart => Move(Require<XyChart>(chart).Series, s => s.Id == id, index));
    public static ChartEditCommand DeleteSeries(string id) => new("Delete chart series", null, chart =>
    {
        var series = Require<XyChart>(chart).Series; series.RemoveAt(Index(series, s => s.Id == id));
    });
    public static ChartEditCommand AddValue(string seriesId, ChartValue value, int? index = null) =>
        ChangeValues("Add data value", seriesId, value.Id, values => values.Insert(index ?? values.Count, value));
    public static ChartEditCommand UpdateValue(string seriesId, ChartValue value) =>
        ChangeValues("Edit data value", seriesId, value.Id, values => values[Index(values, v => v.Id == value.Id)] = value);
    public static ChartEditCommand MoveValue(string seriesId, string valueId, int index) =>
        ChangeValues("Reorder data value", seriesId, valueId, values => Move(values, v => v.Id == valueId, index));
    public static ChartEditCommand DeleteValue(string seriesId, string valueId) =>
        ChangeValues("Delete data value", seriesId, null, values => values.RemoveAt(Index(values, v => v.Id == valueId)));

    public static ChartEditCommand AddRadarAxis(RadarAxis axis, IReadOnlyDictionary<string, double>? curveValues = null)
    {
        var values = curveValues is null ? new Dictionary<string, double>() : new Dictionary<string, double>(curveValues);
        return new("Add radar axis", new(ChartElementRole.Axis, axis.Id), chart =>
        {
            var radar = Require<RadarChart>(chart);
            if (radar.Axes.Any(a => a.Id == axis.Id)) throw new ArgumentException("Axis identity already exists.");
            foreach (var curve in radar.Curves)
                if (!values.ContainsKey(curve.Id)) throw new ArgumentException("Supply a value for every existing curve on the new axis.");
            radar.Axes.Add(axis);
            for (var i = 0; i < radar.Curves.Count; i++)
                radar.Curves[i] = radar.Curves[i] with { Values = radar.Curves[i].Values.Append(new RadarValue(axis.Id, values[radar.Curves[i].Id])).ToArray() };
        });
    }
    public static ChartEditCommand UpdateRadarAxis(string id, RadarAxis replacement) => new("Edit radar axis", new(ChartElementRole.Axis, replacement.Id), chart =>
    {
        var radar = Require<RadarChart>(chart);
        var index = Index(radar.Axes, a => a.Id == id);
        if (replacement.Id != id && radar.Axes.Any(a => a.Id == replacement.Id)) throw new ArgumentException("Axis identity already exists.");
        radar.Axes[index] = replacement;
        for (var i = 0; i < radar.Curves.Count; i++)
            radar.Curves[i] = radar.Curves[i] with { Values = radar.Curves[i].Values.Select(v => v.AxisId == id ? v with { AxisId = replacement.Id } : v).ToArray() };
    });
    public static ChartEditCommand MoveRadarAxis(string id, int index) => new("Reorder radar axes", new(ChartElementRole.Axis, id), chart => Move(Require<RadarChart>(chart).Axes, a => a.Id == id, index));
    public static ChartEditCommand DeleteRadarAxis(string id) => new("Delete radar axis and its curve values", null, chart =>
    {
        var radar = Require<RadarChart>(chart);
        radar.Axes.RemoveAt(Index(radar.Axes, a => a.Id == id));
        for (var i = 0; i < radar.Curves.Count; i++)
            radar.Curves[i] = radar.Curves[i] with { Values = radar.Curves[i].Values.Where(v => v.AxisId != id).ToArray() };
    });
    public static ChartEditCommand AddRadarCurve(RadarCurve curve)
    {
        var copy = curve with { Values = curve.Values.ToArray() };
        return new("Add radar curve", new(ChartElementRole.Curve, curve.Id), chart =>
        {
            var radar = Require<RadarChart>(chart);
            if (radar.Axes.Count == 0) throw new ArgumentException("Add radar axes before adding a curve.");
            radar.Curves.Add(copy with { Values = copy.Values.ToArray() });
        });
    }
    public static ChartEditCommand UpdateRadarCurve(string id, RadarCurve replacement)
    {
        var copy = replacement with { Values = replacement.Values.ToArray() };
        return new("Edit radar curve", new(ChartElementRole.Curve, replacement.Id), chart =>
        {
            var radar = Require<RadarChart>(chart); radar.Curves[Index(radar.Curves, c => c.Id == id)] = copy with { Values = copy.Values.ToArray() };
        });
    }
    public static ChartEditCommand SetRadarValue(string curveId, string axisId, double value) => new("Edit radar value", new(ChartElementRole.CurveValue, curveId, axisId), chart =>
    {
        var curves = Require<RadarChart>(chart).Curves;
        var index = Index(curves, c => c.Id == curveId);
        if (!curves[index].Values.Any(v => v.AxisId == axisId)) throw new ArgumentException("Axis value does not exist.");
        curves[index] = curves[index] with { Values = curves[index].Values.Select(v => v.AxisId == axisId ? v with { Value = value } : v).ToArray() };
    });
    public static ChartEditCommand DeleteRadarCurve(string id) => new("Delete radar curve", null, chart =>
    {
        var curves = Require<RadarChart>(chart).Curves; curves.RemoveAt(Index(curves, c => c.Id == id));
    });
    public static ChartEditCommand SetRadarOptions(double minimum, double? maximum, int ticks, RadarGridKind grid, bool showLegend) =>
        new("Edit radar scale", new(ChartElementRole.Chart, ""), chart =>
        {
            var radar = Require<RadarChart>(chart); radar.Minimum = minimum; radar.Maximum = maximum; radar.Ticks = ticks; radar.Grid = grid; radar.ShowLegend = showLegend;
        });

    private static ChartEditCommand ChangeValues(string description, string seriesId, string? valueId, Action<List<ChartValue>> change) =>
        new(description, valueId is null ? new(ChartElementRole.Series, seriesId) : new(ChartElementRole.Point, valueId, seriesId), chart =>
        {
            var series = Require<XyChart>(chart).Series;
            var index = Index(series, s => s.Id == seriesId);
            var values = series[index].Values.ToList(); change(values); series[index] = series[index] with { Values = values };
        });
    private static T Require<T>(ChartDocument chart) where T : ChartDocument => chart as T ?? throw new InvalidOperationException("This operation belongs to a different chart family.");
    private static int Index<T>(IReadOnlyList<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Count; i++) if (match(items[i])) return i;
        throw new ArgumentException("The selected chart item no longer exists.");
    }
    private static void Move<T>(List<T> items, Func<T, bool> match, int target)
    {
        if (target < 0 || target >= items.Count) throw new ArgumentOutOfRangeException(nameof(target));
        var index = Index(items, match); var value = items[index]; items.RemoveAt(index); items.Insert(target, value);
    }
}
