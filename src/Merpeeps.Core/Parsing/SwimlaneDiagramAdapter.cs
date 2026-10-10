using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class SwimlaneDiagramAdapter : IDiagramAdapter
{
    public string Kind => "swimlane-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var d = new SwimlaneDiagramDocument(); var config = DataDiagramSupport.Configuration(source, "swimlane");
        d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var identity = new DataIdentity(source, "swimlane");
        var body = MerpeepsDoc.BodyOf(source);
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var header = Array.FindIndex(lines, line => Regex.IsMatch(line.Trim(), @"^swimlane-beta(?:\s|$)"));
        if (header < 0) throw new FormatException("Expected swimlane-beta.");
        var transformed = lines.ToArray(); transformed[header] = Regex.Replace(transformed[header], "swimlane-beta", "flowchart", RegexOptions.CultureInvariant);
        // The grammar deliberately reuses flowchart declarations; only the native lane model/layout differs.
        var graph = FlowchartParser.Parse(MerpeepsDoc.ReplaceBody(source, string.Join('\n', transformed)));
        d.Direction = graph.Direction;
        var groupIds = graph.Subgraphs.ToDictionary(g => g.Id, g => identity.Next("group:" + g.Id), StringComparer.Ordinal);
        var nodeIds = graph.Nodes.ToDictionary(n => n.Id, n => identity.Next("node:" + n.Id), StringComparer.Ordinal);
        var stack = new Stack<string>(); var scopeAtLine = new Dictionary<int, string?>();
        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var group = Regex.Match(line, @"^subgraph\s+(?<id>[^\s\[]+)\s*(?:\[(?<label>.*)\])?\s*$");
            if (group.Success)
            {
                var name = group.Groups["id"].Value;
                if (!groupIds.TryGetValue(name, out var id)) throw new FormatException("Cannot identify swimlane group.");
                if (d.Groups.Any(g => g.Id == id)) throw new ArgumentException("Swimlane groups need unique source identifiers.");
                d.Groups.Add(new(id, name, group.Groups["label"].Success ? Decode(group.Groups["label"].Value) : name, stack.TryPeek(out var parent) ? parent : null));
                stack.Push(id); continue;
            }
            if (line == "end") { if (!stack.TryPop(out _)) throw new FormatException("Unmatched swimlane end."); continue; }
            scopeAtLine[i] = stack.TryPeek(out var owner) ? owner : null;
            if (line.StartsWith("direction ", StringComparison.Ordinal) && stack.TryPeek(out var current))
            {
                var index = d.Groups.FindIndex(g => g.Id == current);
                d.Groups[index] = d.Groups[index] with { Direction = ParseDirection(line[10..].Trim()) };
            }
        }
        if (stack.Count > 0) throw new FormatException("Unclosed swimlane group.");
        foreach (var n in graph.Nodes)
            d.Nodes.Add(new(nodeIds[n.Id], n.Id, Decode(n.Label ?? n.Id), n.Shape, scopeAtLine.GetValueOrDefault(n.SourceLine)));
        foreach (var e in graph.Edges)
        {
            // Unsupported marker combinations are not downgraded to a single arrow.
            if (e.OpStart >= 0 && e.SourceLine >= 0 && e.OpStart + e.OpLength <= lines[e.SourceLine].Length)
            {
                var op = lines[e.SourceLine].Substring(e.OpStart, e.OpLength);
                if (op.StartsWith('<')) throw new DataDiagramNotEditableException([new("swimlane-marker", "", "This flowchart-style marker combination is preserved but not yet supported in native lane editing.", true)]);
            }
            d.Links.Add(new(identity.Next("link:" + e.Key), nodeIds[e.From], nodeIds[e.To], e.Kind, e.Label ?? ""));
        }
        foreach (var pair in graph.Passthrough)
        {
            var line = pair.Value.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { d.Comments.Add(line); continue; }
            if (line.StartsWith("direction ", StringComparison.Ordinal)) continue;
            if (line.StartsWith("accTitle:", StringComparison.Ordinal)) { d.AccessibilityTitle = line[9..].Trim(); continue; }
            if (line.StartsWith("accDescr:", StringComparison.Ordinal)) { d.AccessibilityDescription = line[9..].Trim(); continue; }
            var declaration = Regex.Match(line, @"^(?<kind>classDef|class|style)\s+(?<names>\S+)\s+(?<value>.+?);?$");
            if (!declaration.Success) throw new DataDiagramNotEditableException([new("swimlane-statement", "", "Unmodeled swimlane source is preserved instead of silently omitted: " + line, true)]);
            var names = declaration.Groups["names"].Value.Split(','); var value = declaration.Groups["value"].Value.TrimEnd(';').Trim();
            if (declaration.Groups["kind"].Value == "classDef")
            { foreach (var name in names) d.Classes[name] = Css(value); continue; }
            foreach (var name in names)
            {
                var nodeIndex = d.Nodes.FindIndex(n => n.SourceId == name); var groupIndex = d.Groups.FindIndex(g => g.SourceId == name);
                if (nodeIndex < 0 && groupIndex < 0) throw new FormatException("Unknown swimlane style target: " + name);
                if (declaration.Groups["kind"].Value == "class")
                {
                    var classes = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (nodeIndex >= 0) d.Nodes[nodeIndex] = d.Nodes[nodeIndex] with { Classes = d.Nodes[nodeIndex].Classes.Concat(classes).ToArray() };
                    else d.Groups[groupIndex] = d.Groups[groupIndex] with { Classes = d.Groups[groupIndex].Classes.Concat(classes).ToArray() };
                }
                else if (nodeIndex >= 0) d.Nodes[nodeIndex] = d.Nodes[nodeIndex] with { Style = Merge(d.Nodes[nodeIndex].Style, Css(value)) };
                else d.Groups[groupIndex] = d.Groups[groupIndex] with { Style = Merge(d.Groups[groupIndex].Style, Css(value)) };
            }
        }
        SwimlaneValidation.Check(d); return new() { Kind = Kind, Swimlane = d };
    }
    public string Write(DiagramModel model) => Write(model.Swimlane ?? throw new ArgumentException("Swimlane content is missing."));
    public static string Write(SwimlaneDiagramDocument d)
    {
        SwimlaneValidation.Check(d); var output = new StringBuilder("swimlane-beta ").Append(d.Direction).Append('\n');
        Groups(null, 0);
        foreach (var node in d.Nodes.Where(n => n.GroupId is null)) Node(node, 0);
        foreach (var link in d.Links)
            output.Append(FlowchartWriter.EdgeStatement(new DiagramEdge { From = d.Nodes.Single(n => n.Id == link.From).SourceId,
                To = d.Nodes.Single(n => n.Id == link.To).SourceId, Kind = link.Kind, Label = link.Label })).Append('\n');
        foreach (var pair in d.Classes) output.Append("classDef ").Append(pair.Key).Append(' ').Append(Style(pair.Value)).Append('\n');
        foreach (var n in d.Nodes) Appearance(n.SourceId, n.Classes, n.Style);
        foreach (var g in d.Groups) Appearance(g.SourceId, g.Classes, g.Style);
        if (d.AccessibilityTitle.Length > 0) output.Append("accTitle: ").Append(d.AccessibilityTitle).Append('\n');
        if (d.AccessibilityDescription.Length > 0) output.Append("accDescr: ").Append(d.AccessibilityDescription).Append('\n');
        foreach (var comment in d.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Groups(string? parent, int depth)
        {
            foreach (var group in d.Groups.Where(g => g.ParentId == parent))
            {
                output.Append(' ', depth * 2).Append("subgraph ").Append(group.SourceId).Append(" [\"").Append(Encode(group.Label)).Append("\"]\n");
                if (group.Direction is { } direction) output.Append(' ', depth * 2 + 2).Append("direction ").Append(direction).Append('\n');
                foreach (var node in d.Nodes.Where(n => n.GroupId == group.Id)) Node(node, depth + 1);
                Groups(group.Id, depth + 1); output.Append(' ', depth * 2).Append("end\n");
            }
        }
        void Node(SwimlaneNode node, int depth) => output.Append(' ', depth * 2).Append(FlowchartWriter.Declare(new DiagramNode { Id = node.SourceId, Label = node.Label, Shape = node.Shape })).Append('\n');
        void Appearance(string sourceId, IReadOnlyList<string> classes, JsonObject style)
        {
            if (classes.Count > 0) output.Append("class ").Append(sourceId).Append(' ').Append(string.Join(",", classes)).Append('\n');
            if (style.Count > 0) output.Append("style ").Append(sourceId).Append(' ').Append(Style(style)).Append('\n');
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.Swimlane!; var before = Parse(source).Swimlane!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "swimlane", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, SwimlaneDiagramDocument d)
    {
        var identities = new DataIdentity(source, "swimlane");
        var entries = d.Groups.Select(g => identities.Entry(g.Id, "group:" + g.SourceId)).ToList();
        entries.AddRange(d.Nodes.Select(n => identities.Entry(n.Id, "node:" + n.SourceId)));
        var seen = new Dictionary<(string, string), int>();
        foreach (var link in d.Links)
        {
            var from = d.Nodes.Single(n => n.Id == link.From).SourceId; var to = d.Nodes.Single(n => n.Id == link.To).SourceId;
            var ordinal = seen.GetValueOrDefault((from, to)); seen[(from, to)] = ordinal + 1;
            entries.Add(identities.Entry(link.Id, $"link:{from}->{to}#{ordinal}"));
        }
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static Direction ParseDirection(string text) => text == "TD" ? Direction.TB : Enum.TryParse<Direction>(text, out var d) && Enum.IsDefined(d) ? d : throw new FormatException("Unknown swimlane direction.");
    internal static JsonObject Css(string text)
    {
        var result = new JsonObject();
        foreach (var part in Regex.Split(text, @"(?<!\\),\s*(?=[a-zA-Z-]+\s*:)"))
        {
            var colon = part.IndexOf(':'); if (colon < 0) throw new FormatException("Styles use property:value.");
            result[part[..colon].Trim()] = part[(colon + 1)..].Trim().Replace("\\,", ",");
        }
        return result;
    }
    private static JsonObject Merge(JsonObject old, JsonObject added)
    { var merged = (JsonObject)old.DeepClone(); foreach (var pair in added) merged[pair.Key] = pair.Value?.DeepClone(); return merged; }
    private static string Style(JsonObject style) => string.Join(",", style.Select(p => p.Key + ":" + p.Value?.ToString().Replace(",", "\\,")));
    private static string Decode(string text) => WebUtility.HtmlDecode(text.Trim().Trim('"'));
    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("\"", "&quot;");
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Swimlane steps belong to responsibility lanes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use typed swimlane handoff commands.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
