using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class ChartDiagramAdapter(ChartFamily family) : IDiagramAdapter
{
    public ChartFamily Family => family;
    internal const string Numeric = @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?";
    public string Kind => family switch { ChartFamily.Pie => "pie", ChartFamily.Quadrant => "quadrantChart", ChartFamily.Xy => "xychart", _ => "radar-beta" };
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body)
    {
        var key = DiagramAdapters.FirstKeyword(body);
        return key == Kind || family == ChartFamily.Xy && key == "xychart-beta";
    }
    public DiagramModel Parse(string source)
    {
        var read = ReadSource(source);
        if (!read.CanEdit) throw new ChartNotEditableException(read);
        return new() { Kind = read.Chart is XyChart xy ? xy.Directive : Kind, Chart = read.Chart };
    }
    public ChartReadResult ReadSource(string source)
    {
        var split = MerpeepsDoc.Split(source);
        ChartDocument chart = family switch { ChartFamily.Pie => new PieChart(), ChartFamily.Quadrant => new QuadrantChart(), ChartFamily.Xy => new XyChart(), _ => new RadarChart() };
        if (split.Frontmatter.GetValueOrDefault("title") is string title) { chart.Title = title; chart.TitleInFrontmatter = true; }
        var config = split.Frontmatter.GetValueOrDefault("config") as Dictionary<string, object?>;
        chart.Configuration = Object(config?.GetValueOrDefault(ConfigurationKey(family)));
        chart.ThemeVariables = Object(config?.GetValueOrDefault("themeVariables"));
        chart.GlobalConfiguration = Object(config);
        chart.GlobalConfiguration.Remove(ConfigurationKey(family));
        chart.GlobalConfiguration.Remove("themeVariables");
        var identity = new ChartIdentity(source, family);
        var radarCurves = new List<(string Id, string? Label, string Values)>();
        var header = false;
        foreach (var raw in Statements(split.Body))
        {
            var line = raw.Trim();
            if (line.StartsWith("%%", StringComparison.Ordinal)) { chart.Comments.Add(line); continue; }
            if (!header)
            {
                if (!CanParse(line)) throw new FormatException("Unexpected chart directive.");
                header = true;
                var keyword = DiagramAdapters.FirstKeyword(line);
                var tail = line[keyword.Length..].Trim();
                if (chart is PieChart pie)
                {
                    if (tail.StartsWith("showData", StringComparison.Ordinal)) { pie.ShowData = true; tail = tail[8..].Trim(); }
                    if (tail.Length > 0) SetTitle(tail);
                }
                else if (chart is XyChart xy)
                {
                    xy.Directive = keyword;
                    xy.Horizontal = tail == "horizontal";
                    xy.OrientationInBody = tail.Length > 0;
                    if (tail is not ("" or "horizontal" or "vertical")) throw new FormatException("Unknown XY orientation.");
                }
                else if (tail.Length > 0) throw new FormatException("Unexpected chart directive suffix.");
                continue;
            }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { SetTitle(line); continue; }
            switch (chart)
            {
                case PieChart pie:
                    var slice = Regex.Match(line, "^\"(?<label>[^\"]*)\"\\s*:\\s*(?<value>" + Numeric + ")$");
                    if (!slice.Success) throw new FormatException("Pie entries require a quoted label and numeric value.");
                    var label = slice.Groups["label"].Value;
                    var number = ParseNumber(slice.Groups["value"].Value);
                    pie.Slices.Add(new(identity.Next("slice", label + "\0" + Number(number)), label, number));
                    break;
                case QuadrantChart quadrant:
                    if (line.StartsWith("x-axis ", StringComparison.Ordinal)) { (quadrant.XLow, quadrant.XHigh) = AxisEnds(line[7..]); break; }
                    if (line.StartsWith("y-axis ", StringComparison.Ordinal)) { (quadrant.YLow, quadrant.YHigh) = AxisEnds(line[7..]); break; }
                    var region = Regex.Match(line, @"^quadrant-(?<id>[1-4])\s+(?<label>.*)$");
                    if (region.Success) { quadrant.Quadrants[int.Parse(region.Groups["id"].Value, CultureInfo.InvariantCulture) - 1] = Text(region.Groups["label"].Value); break; }
                    if (line.StartsWith("classDef ", StringComparison.Ordinal))
                    {
                        var at = line.IndexOf(' ', 9);
                        if (at < 0) throw new FormatException("Quadrant class definition requires style properties.");
                        quadrant.StyleClasses[line[9..at]] = ParseStyle(line[(at + 1)..]);
                        break;
                    }
                    var point = Regex.Match(line, @"^(?<label>.+?)(?:[:]{3}(?<class>[\w-]+))?\s*:\s*\[\s*(?<x>" + Numeric + @")\s*,\s*(?<y>" + Numeric + @")\s*\](?<style>.*)$");
                    if (!point.Success) throw new FormatException("Quadrant points require label: [x, y].");
                    var x = ParseNumber(point.Groups["x"].Value); var y = ParseNumber(point.Groups["y"].Value);
                    var name = Text(point.Groups["label"].Value);
                    quadrant.Points.Add(new(identity.Next("point", name + "\0" + Number(x) + "\0" + Number(y)), name, x, y,
                        point.Groups["class"].Success ? point.Groups["class"].Value : null)
                    { Style = ParseStyle(point.Groups["style"].Value) });
                    break;
                case XyChart xy:
                    if (line.StartsWith("x-axis", StringComparison.Ordinal)) { xy.XAxis = ParseAxis(line[6..]); break; }
                    if (line.StartsWith("y-axis", StringComparison.Ordinal)) { xy.YAxis = ParseAxis(line[6..]); break; }
                    var series = Regex.Match(line, @"^(?<kind>line|bar)\s*(?<name>.*?)\s*\[(?<values>.*)\]$");
                    if (!series.Success) throw new FormatException("XY data requires a line or bar value list.");
                    var kind = series.Groups["kind"].Value == "line" ? ChartSeriesKind.Line : ChartSeriesKind.Bar;
                    var seriesName = Empty(Text(series.Groups["name"].Value));
                    var seriesId = identity.Next("series", kind + "\0" + seriesName);
                    var values = SplitList(series.Groups["values"].Value).Select(part =>
                    {
                        var value = Regex.Match(part, @"^\s*(?<value>" + Numeric + @")\s*(?<label>"".*"")?\s*$");
                        if (!value.Success) throw new FormatException("Each XY data point must be numeric, with an optional quoted label.");
                        var number = ParseNumber(value.Groups["value"].Value);
                        var label = value.Groups["label"].Success ? Text(value.Groups["label"].Value) : null;
                        return new ChartValue(identity.Next("value", seriesId + "\0" + Number(number) + "\0" + label), number, label);
                    }).ToArray();
                    xy.Series.Add(new(seriesId, kind, seriesName, values));
                    break;
                case RadarChart radar:
                    if (line.StartsWith("axis ", StringComparison.Ordinal))
                    {
                        foreach (var axis in SplitList(line[5..]))
                        {
                            var (axisId, axisLabel) = RadarName(axis);
                            radar.Axes.Add(new(axisId, axisLabel));
                        }
                        break;
                    }
                    if (line.StartsWith("curve ", StringComparison.Ordinal))
                    {
                        foreach (var curve in SplitList(line[6..]))
                        {
                            var at = Outside(curve, '{');
                            if (at < 0 || !curve.EndsWith('}')) throw new FormatException("Radar curves require {values}.");
                            var (id, curveLabel) = RadarName(curve[..at]);
                            radarCurves.Add((id, curveLabel, curve[(at + 1)..^1]));
                        }
                        break;
                    }
                    var pair = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                    if (pair.Length != 2) throw new FormatException("Radar option requires a value.");
                    switch (pair[0])
                    {
                        case "max": radar.Maximum = ParseNumber(pair[1]); break;
                        case "min": radar.Minimum = ParseNumber(pair[1]); break;
                        case "showLegend": radar.ShowLegend = bool.Parse(pair[1]); break;
                        case "ticks": radar.Ticks = int.Parse(pair[1], CultureInfo.InvariantCulture); break;
                        case "graticule": radar.Grid = pair[1] switch { "circle" => RadarGridKind.Circle, "polygon" => RadarGridKind.Polygon, _ => throw new FormatException("Unknown radar graticule.") }; break;
                        default: throw new FormatException("Unknown radar statement: " + line);
                    }
                    break;
            }
        }
        if (!header) throw new FormatException("Chart directive is missing.");
        if (chart is RadarChart radarChart)
            foreach (var curve in radarCurves)
            {
                var values = SplitList(curve.Values);
                var named = values.Any(v => v.Contains(':'));
                var parsed = values.Select((value, index) =>
                {
                    if (!named)
                        return new RadarValue(index < radarChart.Axes.Count ? radarChart.Axes[index].Id : "$extra-" + index, ParseNumber(value));
                    var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2) throw new FormatException("Radar values cannot mix positional and named syntax.");
                    return new RadarValue(Text(parts[0]), ParseNumber(parts[1]));
                }).ToArray();
                radarChart.Curves.Add(new(curve.Id, curve.Label, parsed, named));
            }
        return new(chart, ChartValidation.Validate(chart));

        void SetTitle(string text)
        {
            if (!text.StartsWith("title ", StringComparison.Ordinal)) throw new FormatException("Expected title.");
            chart.Title = Text(text[6..]); chart.TitleInFrontmatter = false;
        }
    }

    public string Write(DiagramModel model) => Write(model.Chart ?? throw new ArgumentException("Chart content is missing."));
    public static string Write(ChartDocument chart)
    {
        var diagnostics = ChartValidation.Validate(chart);
        if (diagnostics.Any(d => d.Kind != ChartDiagnosticKind.Warning)) throw new ChartNotEditableException(new(chart, diagnostics));
        var builder = new StringBuilder(chart switch
        {
            PieChart pie => "pie" + (pie.ShowData ? " showData" : ""),
            QuadrantChart => "quadrantChart",
            XyChart xy => xy.Directive + (xy.OrientationInBody ? xy.Horizontal ? " horizontal" : " vertical" : ""),
            RadarChart => "radar-beta",
            _ => throw new ArgumentException("Unknown chart family."),
        }).Append('\n');
        if (!chart.TitleInFrontmatter && chart.Title.Length > 0)
            builder.Append("title ").Append(chart is XyChart ? Quote(chart.Title) : chart.Title).Append('\n');
        switch (chart)
        {
            case PieChart pie:
                foreach (var slice in pie.Slices) builder.Append(Quote(slice.Label)).Append(" : ").Append(Number(slice.Value)).Append('\n');
                break;
            case QuadrantChart quadrant:
                if (quadrant.XLow.Length + quadrant.XHigh.Length > 0) builder.Append("x-axis ").Append(Quote(quadrant.XLow)).Append(quadrant.XHigh.Length > 0 ? " --> " + Quote(quadrant.XHigh) : "").Append('\n');
                if (quadrant.YLow.Length + quadrant.YHigh.Length > 0) builder.Append("y-axis ").Append(Quote(quadrant.YLow)).Append(quadrant.YHigh.Length > 0 ? " --> " + Quote(quadrant.YHigh) : "").Append('\n');
                for (var i = 0; i < 4; i++)
                    if (quadrant.Quadrants[i].Length > 0) builder.Append("quadrant-").Append(i + 1).Append(' ').Append(Quote(quadrant.Quadrants[i])).Append('\n');
                foreach (var point in quadrant.Points)
                    builder.Append(Quote(point.Label)).Append(point.StyleClass is { } style ? ":::" + style : "")
                        .Append(": [").Append(Number(point.X)).Append(", ").Append(Number(point.Y)).Append("]")
                        .Append(point.Style.Count > 0 ? " " + StyleText(point.Style) : "").Append('\n');
                foreach (var style in quadrant.StyleClasses)
                    builder.Append("classDef ").Append(style.Key).Append(' ').Append(StyleText(style.Value)).Append('\n');
                break;
            case XyChart xy:
                WriteAxis("x-axis", xy.XAxis); WriteAxis("y-axis", xy.YAxis);
                foreach (var series in xy.Series)
                    builder.Append(series.Kind == ChartSeriesKind.Bar ? "bar" : "line").Append(series.Name is { } name ? " " + Quote(name) : "")
                        .Append(" [").Append(string.Join(", ", series.Values.Select(v => Number(v.Value) + (v.Label is { } label ? " " + Quote(label) : "")))).Append("]\n");
                break;
            case RadarChart radar:
                foreach (var axis in radar.Axes) builder.Append("axis ").Append(Identifier(axis.Id)).Append(axis.Label is { } name ? "[" + Quote(name) + "]" : "").Append('\n');
                foreach (var curve in radar.Curves)
                {
                    var values = curve.NamedValues ? curve.Values : radar.Axes.Select(a => curve.Values.Single(v => v.AxisId == a.Id));
                    builder.Append("curve ").Append(Identifier(curve.Id)).Append(curve.Label is { } name ? "[" + Quote(name) + "]" : "")
                        .Append('{').Append(string.Join(", ", values.Select(v => (curve.NamedValues ? Identifier(v.AxisId) + ": " : "") + Number(v.Value)))).Append("}\n");
                }
                builder.Append("min ").Append(Number(radar.Minimum)).Append('\n');
                if (radar.Maximum is { } max) builder.Append("max ").Append(Number(max)).Append('\n');
                builder.Append("showLegend ").Append(radar.ShowLegend ? "true" : "false").Append('\n')
                    .Append("graticule ").Append(radar.Grid.ToString().ToLowerInvariant()).Append('\n')
                    .Append("ticks ").Append(radar.Ticks).Append('\n');
                break;
        }
        foreach (var comment in chart.Comments) builder.Append(comment).Append('\n');
        return builder.ToString();

        void WriteAxis(string keyword, ChartAxis axis)
        {
            if (axis.Kind == ChartAxisKind.Auto && axis.Title.Length == 0) return;
            builder.Append(keyword);
            if (axis.Title.Length > 0) builder.Append(' ').Append(Quote(axis.Title));
            if (axis.Kind == ChartAxisKind.Categories) builder.Append(" [").Append(string.Join(", ", axis.Categories.Select(Quote))).Append(']');
            else if (axis.Kind == ChartAxisKind.Range) builder.Append(' ').Append(Number(axis.Minimum!.Value)).Append(" --> ").Append(Number(axis.Maximum!.Value));
            builder.Append('\n');
        }
    }

    internal string Apply(string source, DiagramModel model)
    {
        var chart = model.Chart ?? throw new ArgumentException("Chart content is missing.");
        var before = ReadSource(source).Chart;
        var result = source;
        if (Write(before) != Write(chart))
        {
            var body = Write(chart);
            if (MerpeepsDoc.BodyOf(source).Contains("\r\n", StringComparison.Ordinal)) body = body.Replace("\n", "\r\n");
            result = MerpeepsDoc.ReplaceBody(result, body);
        }
        if (!JsonNode.DeepEquals(before.Configuration, chart.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, chart.ThemeVariables) ||
            !JsonNode.DeepEquals(before.GlobalConfiguration, chart.GlobalConfiguration) ||
            before.TitleInFrontmatter && before.Title != chart.Title)
        {
            var merged = (JsonObject)chart.GlobalConfiguration.DeepClone();
            merged[ConfigurationKey(chart.Family)] = chart.Configuration.DeepClone();
            merged["themeVariables"] = chart.ThemeVariables.DeepClone();
            using var json = System.Text.Json.JsonDocument.Parse(merged.ToJsonString());
            result = MerpeepsDoc.WithChartConfiguration(result, MerpeepsDoc.Normalise(json.RootElement)!, chart.TitleInFrontmatter, chart.Title);
        }
        return result;
    }

    internal static object Identity(string source, ChartDocument chart)
    {
        var map = new ChartIdentity(source, chart.Family);
        var entries = new List<object?>();
        switch (chart)
        {
            case PieChart pie:
                entries.AddRange(pie.Slices.Select(s => map.Record("slice", s.Id, s.Label + "\0" + Number(s.Value))));
                break;
            case QuadrantChart quadrant:
                entries.AddRange(quadrant.Points.Select(p => map.Record("point", p.Id, p.Label + "\0" + Number(p.X) + "\0" + Number(p.Y))));
                break;
            case XyChart xy:
                foreach (var series in xy.Series)
                {
                    entries.Add(map.Record("series", series.Id, series.Kind + "\0" + series.Name));
                    entries.AddRange(series.Values.Select(v => map.Record("value", v.Id, series.Id + "\0" + Number(v.Value) + "\0" + v.Label)));
                }
                break;
        }
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Chart entries are data, not graph nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Charts do not use graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
    public static string ConfigurationKey(ChartFamily family) => family switch { ChartFamily.Pie => "pie", ChartFamily.Quadrant => "quadrantChart", ChartFamily.Xy => "xyChart", _ => "radar" };
    internal static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Chart numbers must be finite.");
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        if (exponentIndex < 0) return text;
        var exponent = int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var mantissa = text[..exponentIndex];
        var sign = mantissa.StartsWith('-') ? "-" : "";
        mantissa = mantissa.TrimStart('-', '+');
        var point = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "");
        var position = (point < 0 ? digits.Length : point) + exponent;
        return sign + (position <= 0 ? "0." + new string('0', -position) + digits
            : position >= digits.Length ? digits + new string('0', position - digits.Length)
            : digits.Insert(position, "."));
    }
    internal static double ParseNumber(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value :
            throw new FormatException("Chart values must be finite numbers.");
    internal static string Quote(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("Chart labels must be single-line text without double quotes.");
        return "\"" + value + "\"";
    }
    internal static string Text(string text)
    {
        text = text.Trim();
        return text.StartsWith('"') && text.EndsWith('"') ? text[1..^1] : text;
    }
    internal static string Identifier(string id)
    {
        if (!Regex.IsMatch(id, @"^[\p{L}\p{N}_-]+$")) throw new ArgumentException("Chart axis/curve identifiers must contain letters, digits, underscores or hyphens.");
        return id;
    }
    private static string? Empty(string value) => value.Length == 0 ? null : value;
    private static JsonObject Object(object? value) => value is null ? new() : JsonNode.Parse(MerpeepsDoc.ToJson(value)) as JsonObject ?? throw new FormatException("Chart settings must be an object.");
    private static (string Low, string High) AxisEnds(string text)
    {
        var at = text.IndexOf("-->", StringComparison.Ordinal);
        return at < 0 ? (Text(text), "") : (Text(text[..at]), Text(text[(at + 3)..]));
    }
    private static ChartAxis ParseAxis(string text)
    {
        text = text.Trim();
        var bracket = Outside(text, '[');
        if (bracket >= 0 && text.EndsWith(']'))
            return new(ChartAxisKind.Categories, Text(text[..bracket])) { Categories = SplitList(text[(bracket + 1)..^1]).Select(Text).ToArray() };
        var range = Regex.Match(text, @"(?<min>" + Numeric + @")\s*-->\s*(?<max>" + Numeric + @")$");
        return range.Success ? new(ChartAxisKind.Range, Text(text[..range.Index]), ParseNumber(range.Groups["min"].Value), ParseNumber(range.Groups["max"].Value))
            : new(ChartAxisKind.Auto, Text(text));
    }
    private static (string Id, string? Label) RadarName(string text)
    {
        var match = Regex.Match(text.Trim(), @"^(?<id>[\p{L}\p{N}_-]+)(?:\s*\[\s*(?<label>.+)\s*\])?$");
        if (!match.Success) throw new FormatException("Radar names require an identity and optional [label].");
        return (match.Groups["id"].Value, match.Groups["label"].Success ? Text(match.Groups["label"].Value) : null);
    }
    internal static JsonObject ParseStyle(string text)
    {
        var result = new JsonObject();
        foreach (var part in SplitList(text))
        {
            var at = part.IndexOf(':');
            if (at < 1) throw new FormatException("A point style requires property: value.");
            var value = part[(at + 1)..].Trim();
            result[part[..at].Trim()] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? JsonValue.Create(number) : JsonValue.Create(value);
        }
        return result;
    }
    private static string StyleText(JsonObject style) => string.Join(", ", style.Select(p => p.Key + ": " + (p.Value is JsonValue v && v.TryGetValue<string>(out var text) ? text : p.Value?.ToJsonString())));
    /// <summary>Split a chart list without separating commas inside quoted text or color functions.</summary>
    public static IReadOnlyList<string> SplitList(string text)
    {
        var result = new List<string>();
        var quoted = false; var depth = 0; var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                if (text[i] == '"') quoted = !quoted;
                else if (!quoted && text[i] is '[' or '{' or '(') depth++;
                else if (!quoted && text[i] is ']' or '}' or ')') depth--;
            }
            if (i == text.Length || text[i] == ',' && !quoted && depth == 0)
            {
                var item = text[start..i].Trim();
                if (item.Length > 0) result.Add(item);
                start = i + 1;
            }
        }
        if (quoted || depth != 0) throw new FormatException("Unbalanced chart list.");
        return result;
    }
    private static int Outside(string text, char target)
    {
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            else if (!quoted && text[i] == target) return i;
        }
        return -1;
    }
    private static IEnumerable<string> Statements(string body)
    {
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("%%", StringComparison.Ordinal)) { yield return line.Trim(); continue; }
            var quoted = false; var start = 0;
            for (var i = 0; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] == '"') quoted = !quoted;
                if (i != line.Length && (line[i] != ';' || quoted)) continue;
                var text = line[start..i].Trim(); if (text.Length > 0) yield return text;
                start = i + 1;
            }
        }
    }
    private sealed class ChartIdentity
    {
        private readonly List<Dictionary<string, object?>> _prior;
        private readonly Dictionary<string, int> _seen = new();
        public ChartIdentity(string source, ChartFamily family)
        {
            var metadata = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
            var identity = (metadata?.GetValueOrDefault("identity") as Dictionary<string, object?>)?.GetValueOrDefault(ConfigurationKey(family)) as Dictionary<string, object?>;
            if (identity?.GetValueOrDefault("spec") is { } spec && spec is not (1 or 1L)) throw new FormatException("Unknown chart identity version.");
            _prior = (identity?.GetValueOrDefault("entries") as List<object?>)?.Select(e => e as Dictionary<string, object?> ?? throw new FormatException("Invalid chart identity entry.")).ToList() ?? new();
        }
        public string Next(string role, string signature)
        {
            var fingerprint = Hash(role + "\0" + signature);
            var index = _seen.GetValueOrDefault(fingerprint); _seen[fingerprint] = index + 1;
            var found = _prior.Where(p => p.GetValueOrDefault("fingerprint") as string == fingerprint).ToArray();
            return index < found.Length && found[index].GetValueOrDefault("id") is string id ? id : "__chart_" + fingerprint[..24] + "_" + index;
        }
        public object Record(string role, string id, string signature)
        {
            var prior = _prior.FirstOrDefault(e => e.GetValueOrDefault("id") as string == id);
            var record = prior is null ? new Dictionary<string, object?>() : new(prior);
            record["id"] = id; record["role"] = role; record["fingerprint"] = Hash(role + "\0" + signature);
            return record;
        }
        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
