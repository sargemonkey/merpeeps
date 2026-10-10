using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private ChartDocument Chart => Model().Chart ?? throw new InvalidOperationException("Open a chart.");
    private static ChartTarget? ChartTargetOf(Draft draft) => draft.Owner as ChartTarget;
    public void EditChartSelection()
    {
        if (_canvas.SelectedChartTarget is null) Open(new(TaskKind.ChartTitle, Model()));
        else ShowChartSelection();
    }

    private void ShowChartSelection()
    {
        if (_canvas.SelectedChartTarget is { } target)
        {
            var task = (Chart.Family, target.Role) switch
            {
                (ChartFamily.Pie, ChartElementRole.Slice) => TaskKind.PieSlice,
                (ChartFamily.Quadrant, ChartElementRole.Point) => TaskKind.QuadrantPoint,
                (ChartFamily.Xy, ChartElementRole.Series) => TaskKind.XySeries,
                (ChartFamily.Xy, ChartElementRole.Point) => TaskKind.XyValue,
                (ChartFamily.Xy, ChartElementRole.Axis) => TaskKind.XyAxis,
                (ChartFamily.Radar, ChartElementRole.Axis) => TaskKind.RadarAxis,
                (ChartFamily.Radar, ChartElementRole.Curve) => TaskKind.RadarCurve,
                (ChartFamily.Radar, ChartElementRole.CurveValue) => TaskKind.RadarValue,
                (ChartFamily.Quadrant, _) => TaskKind.QuadrantLabels,
                _ => TaskKind.ChartTitle,
            };
            Open(new(task, target), focus: false);
            return;
        }
        Heading(Chart.Title.Length > 0 ? Chart.Title : Chart.Family + " chart");
        Button("Chart title…", () => Open(new(TaskKind.ChartTitle, Model())));
        Button("Chart options…", DisplayOptions);
        ChartDataActions();
        foreach (var pending in _drafts.Values.Where(d => d.Dirty).ToArray())
            Button("Resume unapplied chart edit", () => Open(pending));
        foreach (var issue in ChartValidation.Validate(Chart)) Text(issue.Message);
    }

    private void ChartDataActions()
    {
        switch (Chart)
        {
            case PieChart pie:
                Button("Add slice", AddClass);
                Button(pie.ShowData ? "Hide values in legend" : "Show values in legend", () => ApplyChart(ChartEditing.ShowPieData(!pie.ShowData), Model()));
                foreach (var slice in pie.Slices) Button(slice.Label + ": " + slice.Value.ToString(CultureInfo.InvariantCulture), () => _canvas.SelectChart(new(ChartElementRole.Slice, slice.Id)));
                break;
            case QuadrantChart quadrant:
                Button("Add point", AddClass);
                Button("Axes and quadrant labels…", () => Open(new(TaskKind.QuadrantLabels, Model())));
                foreach (var point in quadrant.Points) Button(point.Label, () => _canvas.SelectChart(new(ChartElementRole.Point, point.Id)));
                Text("Drag a point to change its normalized X/Y data.");
                break;
            case XyChart xy:
                Button("Add series", AddClass);
                Button("X axis…", () => Open(new(TaskKind.XyAxis, new ChartTarget(ChartElementRole.Axis, "x"))));
                Button("Y axis…", () => Open(new(TaskKind.XyAxis, new ChartTarget(ChartElementRole.Axis, "y"))));
                Button("Toggle horizontal / vertical", () => ApplyChart(ChartEditing.SetXyOrientation(!(_canvas.ChartScene.Coordinates?.Horizontal ?? false)), Model()));
                foreach (var series in xy.Series) Button(series.Name ?? series.Kind.ToString(), () => _canvas.SelectChart(new(ChartElementRole.Series, series.Id)));
                break;
            case RadarChart radar:
                Button("Add axis", AddClass);
                Button("Add curve", () => Open(new(TaskKind.RadarCurve, Model()))).IsEnabled = radar.Axes.Count > 0;
                Button("Radar scale…", () => Open(new(TaskKind.RadarOptions, Model())));
                foreach (var axis in radar.Axes) Button("Axis: " + axis.Display, () => _canvas.SelectChart(new(ChartElementRole.Axis, axis.Id)));
                foreach (var curve in radar.Curves) Button("Curve: " + curve.Display, () => _canvas.SelectChart(new(ChartElementRole.Curve, curve.Id)));
                break;
        }
    }

    private bool BuildChartEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.ChartTitle:
                Heading("Chart title"); Input(draft, "title", "Title", Chart.Title);
                Footer("Apply chart title", () => ApplyChart(ChartEditing.SetTitle(Value(draft, "title")), draft.Owner)); return true;
            case TaskKind.ChartOptions:
                ChartOptionsEditor(draft); return true;
            case TaskKind.ChartData:
                Heading("Chart data"); ChartDataActions(); Button("Back to selection", CancelEditor); return true;
            case TaskKind.PieSlice:
                SliceEditor(draft); return true;
            case TaskKind.QuadrantPoint:
                PointEditor(draft); return true;
            case TaskKind.QuadrantLabels:
                QuadrantLabelsEditor(draft); return true;
            case TaskKind.XySeries:
                SeriesEditor(draft); return true;
            case TaskKind.XyValue:
                ValueEditor(draft); return true;
            case TaskKind.XyAxis:
                AxisEditor(draft); return true;
            case TaskKind.RadarAxis:
                RadarAxisEditor(draft); return true;
            case TaskKind.RadarCurve:
                RadarCurveEditor(draft); return true;
            case TaskKind.RadarValue:
                var target = ChartTargetOf(draft)!;
                var curve = ((RadarChart)Chart).Curves.Single(c => c.Id == target.Id);
                Heading(curve.Display + " / " + target.DetailId);
                Input(draft, "value", "Value", curve.Values.Single(v => v.AxisId == target.DetailId).Value.ToString("R", CultureInfo.InvariantCulture));
                Footer("Apply radar value", () => ApplyChart(ChartEditing.SetRadarValue(target.Id, target.DetailId!, Numeric(draft, "value")), draft.Owner));
                return true;
            case TaskKind.RadarOptions:
                RadarOptionsEditor(draft); return true;
            default: return false;
        }
    }

    private void SliceEditor(Draft draft)
    {
        var pie = (PieChart)Chart;
        var target = ChartTargetOf(draft);
        var original = target is null ? null : Remember(draft, () => pie.Slices.Single(s => s.Id == target.Id));
        Heading(original is null ? "Add slice" : "Edit slice");
        Input(draft, "label", "Slice label", original?.Label ?? "");
        Input(draft, "value", "Slice value", original?.Value.ToString("R", CultureInfo.InvariantCulture) ?? "");
        if (pie.Slices.Count == 1)
            Text("One slice fills the pie (100%). Add another slice to compare their values.");
        Footer("Apply slice", () =>
        {
            var slice = new PieSlice(original?.Id ?? ChartEditing.NewId(), Value(draft, "label"), Numeric(draft, "value"));
            ApplyChart(original is null ? ChartEditing.AddSlice(slice) : ChartEditing.UpdateSlice(slice), draft.Owner);
        });
        if (original is not null)
        {
            var index = pie.Slices.FindIndex(s => s.Id == original.Id);
            Button("Move slice earlier", () => ApplyChart(ChartEditing.MoveSlice(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move slice later", () => ApplyChart(ChartEditing.MoveSlice(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < pie.Slices.Count;
            Button("Delete slice", () => ApplyChart(ChartEditing.DeleteSlice(original.Id), draft.Owner));
        }
    }

    private void PointEditor(Draft draft)
    {
        var chart = (QuadrantChart)Chart;
        var target = ChartTargetOf(draft);
        var original = target is null ? null : Remember(draft, () => chart.Points.Single(p => p.Id == target.Id));
        Heading(original is null ? "Add quadrant point" : "Edit quadrant point");
        Input(draft, "label", "Point label", original?.Label ?? "");
        Input(draft, "x", "X (0–1)", (original?.X ?? .5).ToString("R", CultureInfo.InvariantCulture));
        Input(draft, "y", "Y (0–1)", (original?.Y ?? .5).ToString("R", CultureInfo.InvariantCulture));
        ChoiceInput(draft, "class", "Point style class", new[] { new Choice("", "(none)") }.Concat(chart.StyleClasses.Keys.Select(id => new Choice(id, id))), original?.StyleClass ?? "");
        foreach (var key in new[] { "color", "radius", "stroke-color", "stroke-width" })
            StyleInput(draft, key, original?.Style[key] is { } value ? ChartValueText(value) : "");
        Footer("Apply point", () =>
        {
            var style = original is null ? new JsonObject() : (JsonObject)original.Style.DeepClone();
            foreach (var key in new[] { "color", "radius", "stroke-color", "stroke-width" })
                if (Optional(draft, key) is { } value) style[key] = value; else style.Remove(key);
            var point = new QuadrantPoint(original?.Id ?? ChartEditing.NewId(), Value(draft, "label"),
                Numeric(draft, "x"), Numeric(draft, "y"), Optional(draft, "class")) { Style = style };
            ApplyChart(original is null ? ChartEditing.AddQuadrantPoint(point) : ChartEditing.UpdateQuadrantPoint(point), draft.Owner);
        });
        if (original is not null) Button("Delete point", () => ApplyChart(ChartEditing.DeleteQuadrantPoint(original.Id), draft.Owner));
    }

    private void QuadrantLabelsEditor(Draft draft)
    {
        var quadrant = (QuadrantChart)Chart;
        Heading("Axes and quadrant labels");
        Input(draft, "xLow", "X left", quadrant.XLow); Input(draft, "xHigh", "X right", quadrant.XHigh);
        Input(draft, "yLow", "Y bottom", quadrant.YLow); Input(draft, "yHigh", "Y top", quadrant.YHigh);
        for (var i = 0; i < 4; i++) Input(draft, "q" + i, new[] { "Top right", "Top left", "Bottom left", "Bottom right" }[i], quadrant.Quadrants[i]);
        Footer("Apply quadrant labels", () => ApplyChart(ChartEditing.SetQuadrantLabels(Value(draft, "xLow"), Value(draft, "xHigh"),
            Value(draft, "yLow"), Value(draft, "yHigh"), Enumerable.Range(0, 4).Select(i => Value(draft, "q" + i)).ToArray()), draft.Owner));
    }

    private void SeriesEditor(Draft draft)
    {
        var chart = (XyChart)Chart;
        var target = ChartTargetOf(draft);
        var original = target is null ? null : Remember(draft, () => chart.Series.Single(s => s.Id == target.Id));
        Heading(original is null ? "Add series" : "Edit series");
        Input(draft, "name", "Series name (optional)", original?.Name ?? "");
        EnumInput(draft, "kind", "Series kind", original?.Kind ?? ChartSeriesKind.Line);
        Footer("Apply series", () => ApplyChart(original is null
            ? ChartEditing.AddSeries(new(ChartEditing.NewId(), draft.Get("kind", ChartSeriesKind.Line), Optional(draft, "name"), []))
            : ChartEditing.UpdateSeries(original.Id, draft.Get("kind", original.Kind), Optional(draft, "name")), draft.Owner));
        if (original is null) return;
        Button("Add data value", () => Open(new(TaskKind.XyValue, Model()) { Values = { ["$series"] = original.Id } }));
        foreach (var value in chart.Series.Single(s => s.Id == original.Id).Values)
            Button(value.Value.ToString("G6", CultureInfo.InvariantCulture) + (value.Label is { } label ? " · " + label : ""),
                () => _canvas.SelectChart(new(ChartElementRole.Point, value.Id, original.Id)));
        Button("Delete series", () => ApplyChart(ChartEditing.DeleteSeries(original.Id), draft.Owner));
    }

    private void ValueEditor(Draft draft)
    {
        var target = ChartTargetOf(draft);
        var seriesId = target?.DetailId ?? draft.Get("$series", "");
        var series = ((XyChart)Chart).Series.Single(s => s.Id == seriesId);
        var original = target is null ? null : Remember(draft, () => series.Values.Single(v => v.Id == target.Id));
        Heading(original is null ? "Add data value" : "Edit data value");
        Input(draft, "value", "Numeric value", original?.Value.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Input(draft, "label", "Point label (line only)", original?.Label ?? "");
        if (series.Kind == ChartSeriesKind.Bar) Text("Bar point labels are preserved, but Mermaid does not draw them.");
        Footer("Apply data value", () =>
        {
            var value = new ChartValue(original?.Id ?? ChartEditing.NewId(), Numeric(draft, "value"), Optional(draft, "label"));
            ApplyChart(original is null ? ChartEditing.AddValue(seriesId, value) : ChartEditing.UpdateValue(seriesId, value), draft.Owner);
        });
        Button("Series data…", () => _canvas.SelectChart(new(ChartElementRole.Series, seriesId)));
        if (original is not null) Button("Delete data value", () => ApplyChart(ChartEditing.DeleteValue(seriesId, original.Id), draft.Owner));
    }

    private void AxisEditor(Draft draft)
    {
        var x = ChartTargetOf(draft)?.Id != "y";
        var chart = (XyChart)Chart;
        var original = draft.Get("$axis", x ? chart.XAxis : chart.YAxis);
        Heading(x ? "X axis" : "Y axis");
        Input(draft, "title", "Axis title", original.Title);
        EnumInput(draft, "kind", "Axis kind", original.Kind, x ? Enum.GetValues<ChartAxisKind>() : new[] { ChartAxisKind.Auto, ChartAxisKind.Range });
        Input(draft, "min", "Minimum", original.Minimum?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Input(draft, "max", "Maximum", original.Maximum?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        if (x) Input(draft, "categories", "Categories (one per line)", string.Join("\n", original.Categories), multiline: true);
        Footer("Apply axis", () =>
        {
            var kind = draft.Get("kind", original.Kind);
            var axis = new ChartAxis(kind, Value(draft, "title"), kind == ChartAxisKind.Range ? Numeric(draft, "min") : null,
                kind == ChartAxisKind.Range ? Numeric(draft, "max") : null)
            { Categories = kind == ChartAxisKind.Categories ? draft.Get("categories", "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries) : [] };
            ApplyChart(ChartEditing.SetXyAxis(x, axis), draft.Owner);
        });
    }

    private void RadarAxisEditor(Draft draft)
    {
        var chart = (RadarChart)Chart;
        var target = ChartTargetOf(draft);
        var original = target is null ? null : Remember(draft, () => chart.Axes.Single(a => a.Id == target.Id));
        Heading(original is null ? "Add radar axis" : "Edit radar axis");
        Input(draft, "id", "Axis identity", original?.Id ?? "");
        Input(draft, "label", "Axis label", original?.Label ?? "");
        if (original is null)
            foreach (var curve in chart.Curves) Input(draft, "curve-" + curve.Id, "Value for " + curve.Display);
        Footer("Apply radar axis", () =>
        {
            var axis = new RadarAxis(Value(draft, "id"), Optional(draft, "label"));
            ApplyChart(original is null ? ChartEditing.AddRadarAxis(axis, chart.Curves.ToDictionary(c => c.Id, c => Numeric(draft, "curve-" + c.Id)))
                : ChartEditing.UpdateRadarAxis(original.Id, axis), draft.Owner);
        });
        if (original is not null)
        {
            var index = chart.Axes.FindIndex(a => a.Id == original.Id);
            Button("Move axis earlier", () => ApplyChart(ChartEditing.MoveRadarAxis(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move axis later", () => ApplyChart(ChartEditing.MoveRadarAxis(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < chart.Axes.Count;
            Button("Delete axis and its values", () => ApplyChart(ChartEditing.DeleteRadarAxis(original.Id), draft.Owner));
        }
    }

    private void RadarCurveEditor(Draft draft)
    {
        var chart = (RadarChart)Chart;
        var target = ChartTargetOf(draft);
        var original = target is null ? null : Remember(draft, () => chart.Curves.Single(c => c.Id == target.Id));
        Heading(original is null ? "Add radar curve" : "Edit radar curve");
        Input(draft, "id", "Curve identity", original?.Id ?? "");
        Input(draft, "label", "Curve label", original?.Label ?? "");
        Check(draft, "named", "Use axis IDs in source", original?.NamedValues ?? true);
        foreach (var axis in chart.Axes)
            Input(draft, "axis-" + axis.Id, "Value for " + axis.Display, original?.Values.FirstOrDefault(v => v.AxisId == axis.Id)?.Value.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Footer("Apply radar curve", () =>
        {
            var curve = new RadarCurve(Value(draft, "id"), Optional(draft, "label"),
                chart.Axes.Select(a => new RadarValue(a.Id, Numeric(draft, "axis-" + a.Id))).ToArray(), draft.Get("named", true));
            ApplyChart(original is null ? ChartEditing.AddRadarCurve(curve) : ChartEditing.UpdateRadarCurve(original.Id, curve), draft.Owner);
        });
        if (original is not null) Button("Delete curve", () => ApplyChart(ChartEditing.DeleteRadarCurve(original.Id), draft.Owner));
    }

    private void RadarOptionsEditor(Draft draft)
    {
        var radar = (RadarChart)Chart;
        Heading("Radar scale");
        Input(draft, "min", "Minimum", radar.Minimum.ToString("R", CultureInfo.InvariantCulture));
        Input(draft, "max", "Maximum (blank is automatic)", radar.Maximum?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Input(draft, "ticks", "Grid ticks", radar.Ticks.ToString(CultureInfo.InvariantCulture));
        EnumInput(draft, "grid", "Grid", radar.Grid);
        Check(draft, "legend", "Show legend", radar.ShowLegend);
        Footer("Apply radar scale", () => ApplyChart(ChartEditing.SetRadarOptions(Numeric(draft, "min"),
            Optional(draft, "max") is null ? null : Numeric(draft, "max"), int.Parse(Value(draft, "ticks"), CultureInfo.InvariantCulture),
            draft.Get("grid", radar.Grid), draft.Get("legend", radar.ShowLegend)), draft.Owner));
    }

    private void ChartOptionsEditor(Draft draft)
    {
        Heading("Chart options");
        var definitions = ChartConfiguration.Definitions(Chart.Family);
        var key = draft.Get("$key", definitions[0].Key);
        var selected = definitions.Single(d => d.Key == key);
        var picker = new ComboBox { ItemsSource = definitions.Select(d => new Choice(d.Key, d.Label)).ToArray() };
        picker.SelectedItem = picker.Items.Cast<Choice>().Single(c => c.Id == key);
        Field("Option", picker);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not Choice choice || choice.Id == draft.Get("$key", key)) return;
            if (draft.Dirty) { _feedback.Text = "Apply or cancel this option before choosing another."; picker.SelectedItem = picker.Items.Cast<Choice>().Single(c => c.Id == draft.Get("$key", key)); return; }
            draft.Values["$key"] = choice.Id; draft.Values.Remove("value"); RebuildEditor(draft);
        };
        var current = ChartConfiguration.Value(Chart, key);
        if (selected.Type == ChartOptionType.Color)
            ColourInput(draft, "value", "Option value", current is null ? selected.DefaultValue : ChartValueText(current), defaultValue: null);
        else if (key == "theme.xyChart.plotColorPalette")
            ColourListInput(draft, "value", "Series color", current is null ? selected.DefaultValue : ChartValueText(current));
        else Input(draft, "value", "Option value", current is null ? selected.DefaultValue : ChartValueText(current));
        Text(selected.Type + (selected.Choices is null ? "" : ": " + string.Join(", ", selected.Choices)));
        Footer("Apply chart option", () => ApplyChart(ChartEditing.SetOption(draft.Get("$key", key), Value(draft, "value")), draft.Owner));
        Button("Reset to default", () => ApplyChart(ChartEditing.SetOption(draft.Get("$key", key), null), draft.Owner));
    }
    private void ApplyChart(ChartEditCommand command, object owner) => Apply(command, owner,
        after: () => _canvas.SelectChart(command.Result is { Role: ChartElementRole.Chart } ? null : command.Result));
    private static string ChartValueText(JsonNode value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    private static double Numeric(Draft draft, string key) => double.TryParse(Value(draft, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new ArgumentException("Enter a finite number.");
}
