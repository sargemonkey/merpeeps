using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public enum ChartOptionType { Number, Boolean, Text, Color, Choice }
public sealed record ChartOptionDefinition(string Key, string Label, ChartOptionType Type, string DefaultValue,
    double? Minimum = null, double? Maximum = null, IReadOnlyList<string>? Choices = null);

public static class ChartConfiguration
{
    private static readonly Lazy<IReadOnlyDictionary<ChartFamily, IReadOnlyList<ChartOptionDefinition>>> Options =
        new(() => Enum.GetValues<ChartFamily>().ToDictionary(f => f, BuildDefinitions));
    public static IReadOnlyList<ChartOptionDefinition> Definitions(ChartFamily family) => Options.Value[family];
    private static IReadOnlyList<ChartOptionDefinition> BuildDefinitions(ChartFamily family)
    {
        var list = new List<ChartOptionDefinition>
        {
            new("global.fontFamily", "Font family", ChartOptionType.Text, "Segoe UI"),
        };
        void N(string key, double value, double min = 0, double? max = null) => list.Add(new(key, key, ChartOptionType.Number, value.ToString(CultureInfo.InvariantCulture), min, max));
        void B(string key, bool value = true) => list.Add(new(key, key, ChartOptionType.Boolean, value ? "true" : "false"));
        void C(string key, string value) => list.Add(new(key, key, ChartOptionType.Color, value));
        void S(string key, string value) => list.Add(new(key, key, ChartOptionType.Text, value));
        void E(string key, string value, params string[] choices) => list.Add(new(key, key, ChartOptionType.Choice, value, Choices: choices));
        switch (family)
        {
            case ChartFamily.Pie:
                N("textPosition", .75, 0, 1); N("donutHole", 0, 0, .9);
                E("legendPosition", "right", "top", "bottom", "left", "right", "center");
                S("highlightSlice", "");
                N("theme.pieOuterStrokeWidth", 2); N("theme.pieStrokeWidth", 1);
                N("theme.pieSectionTextSize", 14, 1); N("theme.pieTitleTextSize", 22, 1); N("theme.pieLegendTextSize", 14, 1);
                N("theme.pieOpacity", 1, 0, 1);
                C("theme.pieStrokeColor", "#FFFFFF"); C("theme.pieOuterStrokeColor", "#6B7280");
                C("theme.pieTitleTextColor", "#1F2937"); C("theme.pieSectionTextColor", "#111827"); C("theme.pieLegendTextColor", "#1F2937");
                for (var i = 1; i <= 12; i++) C("theme.pie" + i, Palette[(i - 1) % Palette.Length]);
                break;
            case ChartFamily.Quadrant:
                N("chartWidth", 500, 160); N("chartHeight", 500, 160);
                N("titlePadding", 10); N("titleFontSize", 20, 1); N("quadrantPadding", 5);
                N("quadrantTextTopPadding", 5); N("quadrantLabelFontSize", 16, 1);
                N("quadrantInternalBorderStrokeWidth", 1); N("quadrantExternalBorderStrokeWidth", 2);
                N("xAxisLabelPadding", 5); N("xAxisLabelFontSize", 16, 1); E("xAxisPosition", "top", "top", "bottom");
                N("yAxisLabelPadding", 5); N("yAxisLabelFontSize", 16, 1); E("yAxisPosition", "left", "left", "right");
                N("pointTextPadding", 5); N("pointLabelFontSize", 12, 1); N("pointRadius", 5);
                for (var i = 1; i <= 4; i++) { C("theme.quadrant" + i + "Fill", new[] { "#DBEAFE", "#E0E7FF", "#EDE9FE", "#E0F2FE" }[i - 1]); C("theme.quadrant" + i + "TextFill", "#1F2937"); }
                foreach (var key in new[] { "quadrantPointFill", "quadrantPointTextFill", "quadrantXAxisTextFill", "quadrantYAxisTextFill", "quadrantInternalBorderStrokeFill", "quadrantExternalBorderStrokeFill", "quadrantTitleFill" })
                    C("theme." + key, "#1F2937");
                break;
            case ChartFamily.Xy:
                N("width", 700, 180); N("height", 500, 180);
                N("titlePadding", 10); N("titleFontSize", 20, 1); B("showTitle"); B("showLegend");
                N("legendFontSize", 14, 1); N("legendPadding", 10); N("plotReservedSpacePercent", 50, 1, 95);
                E("chartOrientation", "vertical", "vertical", "horizontal");
                B("showDataLabel", false); B("showDataLabelOutsideBar", false);
                foreach (var axis in new[] { "xAxis", "yAxis" })
                {
                    B(axis + ".showLabel"); N(axis + ".labelFontSize", 14, 1); N(axis + ".labelPadding", 5);
                    B(axis + ".showTitle"); N(axis + ".titleFontSize", 16, 1); N(axis + ".titlePadding", 5);
                    B(axis + ".showTick"); N(axis + ".tickLength", 5); N(axis + ".tickWidth", 2);
                    B(axis + ".showAxisLine"); N(axis + ".axisLineWidth", 2); N(axis + ".labelRotation", 0, -360, 360);
                }
                foreach (var key in new[] { "titleColor", "dataLabelColor", "legendTextColor", "xAxisLabelColor", "xAxisTitleColor", "xAxisTickColor", "xAxisLineColor", "yAxisLabelColor", "yAxisTitleColor", "yAxisTickColor", "yAxisLineColor" })
                    C("theme.xyChart." + key, "#1F2937");
                C("theme.xyChart.backgroundColor", "#FFFFFF");
                S("theme.xyChart.plotColorPalette", string.Join(", ", Palette));
                break;
            case ChartFamily.Radar:
                N("width", 600, 100); N("height", 600, 100);
                foreach (var side in new[] { "Top", "Bottom", "Left", "Right" }) N("margin" + side, 50);
                N("axisScaleFactor", 1); N("axisLabelFactor", 1.05); N("curveTension", .17);
                N("theme.fontSize", 22, 1); C("theme.titleColor", "#1F2937");
                C("theme.radar.axisColor", "#374151"); N("theme.radar.axisStrokeWidth", 1);
                N("theme.radar.axisLabelFontSize", 12, 1); N("theme.radar.curveOpacity", .7, 0, 1);
                N("theme.radar.curveStrokeWidth", 2); C("theme.radar.graticuleColor", "#6B7280");
                N("theme.radar.graticuleOpacity", .5, 0, 1); N("theme.radar.graticuleStrokeWidth", 1);
                N("theme.radar.legendBoxSize", 10); N("theme.radar.legendFontSize", 14, 1);
                for (var i = 0; i < 12; i++) C("theme.cScale" + i, Palette[i % Palette.Length]);
                break;
        }
        return list;
    }

    public static readonly string[] Palette = ["#4F86C6", "#5EAD91", "#E8AE5B", "#CA7891", "#9183C4", "#68ADBC", "#B5B36B", "#DB8A68"];

    public static JsonNode? Value(ChartDocument chart, string key)
    {
        var (root, path) = Root(chart, key);
        JsonNode? node = root;
        foreach (var part in path.Split('.')) node = node is JsonObject map ? map[part] : null;
        return node;
    }
    public static void Set(ChartDocument chart, string key, string? value)
    {
        var definition = Definitions(chart.Family).SingleOrDefault(d => d.Key == key) ?? throw new ArgumentException("Unknown chart option.");
        var (root, path) = Root(chart, key);
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (root[parts[i]] is not JsonObject next) { next = new JsonObject(); root[parts[i]] = next; }
            root = next;
        }
        if (value is null) { root.Remove(parts[^1]); return; }
        root[parts[^1]] = definition.Type switch
        {
            ChartOptionType.Number => JsonValue.Create(ParseNumber(value)),
            ChartOptionType.Boolean => JsonValue.Create(bool.Parse(value)),
            _ => JsonValue.Create(value),
        };
    }
    public static double Number(ChartDocument chart, string key)
    {
        var definition = Definitions(chart.Family).Single(d => d.Key == key);
        return Value(chart, key) is { } value ? ParseNumber(Raw(value)) : ParseNumber(definition.DefaultValue);
    }
    public static bool Boolean(ChartDocument chart, string key) =>
        Value(chart, key) is { } value ? bool.Parse(Raw(value)) : bool.Parse(Definitions(chart.Family).Single(d => d.Key == key).DefaultValue);
    public static string Text(ChartDocument chart, string key) =>
        Value(chart, key) is { } value ? Raw(value) : Definitions(chart.Family).Single(d => d.Key == key).DefaultValue;

    public static IReadOnlyList<ChartDiagnostic> Validate(ChartDocument chart)
    {
        var issues = new List<ChartDiagnostic>();
        var target = new ChartTarget(ChartElementRole.Chart, "");
        foreach (var definition in Definitions(chart.Family))
        {
            if (Value(chart, definition.Key) is not { } value) continue;
            var raw = Raw(value);
            try
            {
                if (definition.Type == ChartOptionType.Number)
                {
                    var number = ParseNumber(raw);
                    if (definition.Minimum is { } min && number < min || definition.Maximum is { } max && number > max)
                        throw new ArgumentException("outside its supported range");
                }
                else if (definition.Type == ChartOptionType.Boolean && raw is not ("true" or "false"))
                    throw new ArgumentException("must be true or false");
                else if (definition.Type is ChartOptionType.Text or ChartOptionType.Color or ChartOptionType.Choice &&
                    !(value is JsonValue textValue && textValue.TryGetValue<string>(out _)))
                    throw new ArgumentException("must be text");
                else if (definition.Type == ChartOptionType.Choice && definition.Choices?.Contains(raw) != true)
                    throw new ArgumentException("is not a supported choice");
                else if (definition.Type == ChartOptionType.Color && !SequenceColors.TryParse(raw, out _))
                    throw new ArgumentException("is not a supported color");
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            {
                issues.Add(new("chart-option", target, $"{definition.Key}: {error.Message}", ChartDiagnosticKind.NativeUnsupported));
            }
        }
        var known = Definitions(chart.Family).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var path in Paths(chart.Configuration))
            if (!known.Contains(path) && path is not "useMaxWidth")
                issues.Add(new("unknown-chart-option", target, $"'{path}' is preserved but has no native interpretation.", ChartDiagnosticKind.NativeUnsupported));
        if (chart is XyChart && Value(chart, "theme.xyChart.plotColorPalette") is { } palette)
        {
            var colors = ChartDiagramAdapter.SplitList(Raw(palette));
            if (colors.Count == 0 || colors.Any(c => !SequenceColors.TryParse(c, out _)))
                issues.Add(new("plot-palette", target, "The XY palette must contain valid colors.", ChartDiagnosticKind.NativeUnsupported));
        }
        return issues;
    }

    internal static double ParseNumber(string value)
    {
        var factor = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? 96d / 72 : 1;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase) || value.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return ChartDiagramAdapter.ParseNumber(value) * factor;
    }
    internal static string Raw(JsonNode value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    private static (JsonObject Root, string Path) Root(ChartDocument chart, string key) =>
        key.StartsWith("theme.", StringComparison.Ordinal) ? (chart.ThemeVariables, key[6..])
        : key.StartsWith("global.", StringComparison.Ordinal) ? (chart.GlobalConfiguration, key[7..])
        : (chart.Configuration, key);
    private static IEnumerable<string> Paths(JsonObject root, string prefix = "")
    {
        foreach (var entry in root)
        {
            var path = prefix.Length == 0 ? entry.Key : prefix + "." + entry.Key;
            if (entry.Value is JsonObject nested)
            {
                foreach (var child in Paths(nested, path)) yield return child;
            }
            else yield return path;
        }
    }
}
