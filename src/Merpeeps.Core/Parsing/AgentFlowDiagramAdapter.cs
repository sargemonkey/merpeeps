using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class AgentFlowDiagramAdapter : IDiagramAdapter
{
    public string Kind => "agentflow-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var d = new AgentFlowDocument(); var identity = new DataIdentity(source, "agentflow"); var header = false;
        var config = DataDiagramSupport.Configuration(source, "agentflow"); d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var scopes = new Stack<(string? Parent, bool Global)>();
        foreach (var line in UseCaseDiagramAdapter.Statements(MerpeepsDoc.BodyOf(source)))
        {
            if (line.StartsWith("%%", StringComparison.Ordinal)) { d.Comments.Add(line); continue; }
            if (!header)
            {
                var match = Regex.Match(line, @"^agentflow-beta(?:\s+(TB|TD|BT|LR|RL))?$");
                if (!match.Success) throw new FormatException("Expected agentflow-beta and optional direction.");
                d.Direction = match.Groups[1].Value switch { "LR" => Direction.LR, "RL" => Direction.RL, "BT" => Direction.BT, _ => Direction.TB };
                header = true; continue;
            }
            if (line == "end") { if (!scopes.TryPop(out _)) throw new FormatException("Unmatched Agentflow end."); continue; }
            if (line == "global") { scopes.Push((null, true)); continue; }
            var flow = line.StartsWith("flow ", StringComparison.Ordinal); var connector = line.StartsWith("connector ", StringComparison.Ordinal);
            var text = flow ? line[5..] : connector ? line[10..] : line;
            var reader = new ElementReader(text); var first = Element(reader, flow ? AgentFlowElementKind.Flow : connector ? AgentFlowElementKind.Connector : null);
            if (flow)
            {
                reader.End(); scopes.Push((first.Id, false)); continue;
            }
            if (connector) { reader.End(); continue; }
            var previous = first;
            while (reader.Remaining.Trim().Length > 0)
            {
                var relation = reader.Edge();
                var target = Element(reader, null);
                var sourceId = relation.Id; var id = identity.Next(sourceId is null ? "link:" + previous.Id + "\0" + target.Id + "\0" + relation.Kind + "\0" + relation.Label : "link-id:" + sourceId);
                d.Links.Add(new(id, previous.Id, target.Id, relation.Kind, relation.Label) { SourceId = sourceId, Metadata = relation.Metadata });
                previous = target;
            }
        }
        if (!header || scopes.Count > 0) throw new FormatException("Agentflow header is missing or a flow/global scope is unclosed.");
        AgentFlowSemantics.Check(d); return new() { Kind = Kind, AgentFlow = d };
        AgentFlowElement Element(ElementReader reader, AgentFlowElementKind? declaredKind)
        {
            var name = reader.Identifier(); var label = reader.Label(); var metadata = reader.Metadata();
            var old = d.Elements.FirstOrDefault(e => e.SourceId == name);
            if (old is null && d.Links.FirstOrDefault(e => e.SourceId == name) is { } edge)
            {
                if (label is not null || declaredKind is not null) throw new FormatException("An edge ID cannot be redeclared as an Agentflow node.");
                d.Links[d.Links.IndexOf(edge)] = edge with { Metadata = Merge(edge.Metadata, metadata) };
                reader.End(); return d.Elements.First(e => e.Id == edge.From);
            }
            var scope = scopes.TryPeek(out var current) ? current : (null, false);
            var item = old ?? new AgentFlowElement(identity.Next("element:" + name), name, declaredKind ?? AgentFlowElementKind.Node, label ?? name);
            var merged = Merge(item.Metadata, metadata);
            item = item with
            {
                Kind = declaredKind ?? item.Kind, Label = label ?? item.Label, Metadata = merged,
                Global = item.Global || scope.Item2,
                ParentId = item.Global || scope.Item2 || declaredKind == AgentFlowElementKind.Connector ? null : item.ParentId ?? scope.Item1,
            };
            if (old is null) d.Elements.Add(item); else d.Elements[d.Elements.IndexOf(old)] = item;
            return item;
        }
    }
    public string Write(DiagramModel model) => Write(model.AgentFlow ?? throw new ArgumentException("Agentflow content is missing."));
    public static string Write(AgentFlowDocument d)
    {
        AgentFlowSemantics.Check(d); var b = new StringBuilder("agentflow-beta ").Append(d.Direction).Append('\n');
        foreach (var connector in d.Elements.Where(e => e.Kind == AgentFlowElementKind.Connector)) Element(connector, 0);
        var globals = d.Elements.Where(e => e.Global && e.Kind != AgentFlowElementKind.Connector).ToArray();
        if (globals.Length > 0) { b.Append("global\n"); foreach (var element in globals) Element(element, 1); b.Append("end\n"); }
        Children(null, 0);
        foreach (var link in d.Links)
        {
            b.Append(d.Elements.Single(e => e.Id == link.From).SourceId).Append(' ');
            if (link.SourceId is not null) b.Append(link.SourceId).Append('@');
            var op = link.Kind switch { AgentFlowLinkKind.Reference => "-.-", AgentFlowLinkKind.Failure => "--x", _ => "-->" };
            if (link.Label.Length > 0) b.Append("-- \"").Append(Encode(link.Label)).Append("\" ");
            b.Append(op).Append(' ').Append(d.Elements.Single(e => e.Id == link.To).SourceId).Append('\n');
            if (link.Metadata.Count > 0 && link.SourceId is not null) b.Append(link.SourceId).Append('@').Append(link.Metadata.ToJsonString()).Append('\n');
        }
        foreach (var comment in d.Comments) b.Append(comment).Append('\n');
        return b.ToString();
        void Children(string? parent, int depth)
        {
            foreach (var element in d.Elements.Where(e => e.ParentId == parent && !e.Global && e.Kind != AgentFlowElementKind.Connector))
            {
                Element(element, depth);
                if (element.Kind == AgentFlowElementKind.Flow) { Children(element.Id, depth + 1); b.Append(' ', depth * 2).Append("end\n"); }
            }
        }
        void Element(AgentFlowElement element, int depth)
        {
            b.Append(' ', depth * 2).Append(element.Kind switch { AgentFlowElementKind.Flow => "flow ", AgentFlowElementKind.Connector => "connector ", _ => "" })
                .Append(element.SourceId).Append("[\"").Append(Encode(element.Label)).Append("\"]");
            if (element.Metadata.Count > 0) b.Append('@').Append(element.Metadata.ToJsonString());
            b.Append('\n');
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.AgentFlow!; var before = Parse(source).AgentFlow!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "agentflow", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, AgentFlowDocument d)
    {
        var ids = new DataIdentity(source, "agentflow");
        var entries = d.Elements.Select(e => ids.Entry(e.Id, "element:" + e.SourceId)).ToList();
        entries.AddRange(d.Links.Select(l => ids.Entry(l.Id, l.SourceId is null ? "link:" + l.From + "\0" + l.To + "\0" + l.Kind + "\0" + l.Label : "link-id:" + l.SourceId)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static JsonObject Merge(JsonObject before, JsonObject after)
    { var result = (JsonObject)before.DeepClone(); foreach (var pair in after) result[pair.Key] = pair.Value?.DeepClone(); return result; }
    public static JsonObject Metadata(string source)
    {
        var value = DataDiagramSupport.Object(MerpeepsDoc.FromYaml(source));
        Sanitize(value); return value;
        static void Sanitize(JsonNode? node)
        {
            if (node is JsonObject map)
            {
                foreach (var name in new[] { "__proto__", "constructor", "prototype" }) map.Remove(name);
                foreach (var pair in map) Sanitize(pair.Value);
            }
            else if (node is JsonArray array) foreach (var child in array) Sanitize(child);
        }
    }
    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("\n", "<br/>");
    private sealed class ElementReader(string source)
    {
        private int _at;
        public string Remaining => source[_at..];
        private void Space() { while (_at < source.Length && char.IsWhiteSpace(source[_at])) _at++; }
        public string Identifier()
        {
            Space(); var match = Regex.Match(Remaining, @"^[A-Za-z0-9_]+");
            if (!match.Success) throw new FormatException("Expected Agentflow identifier.");
            _at += match.Length; return match.Value;
        }
        public string? Label()
        {
            Space(); if (_at >= source.Length || source[_at] != '[') return null;
            _at++; var start = _at; var quoted = false;
            for (; _at < source.Length; _at++)
            {
                var c = source[_at]; if (c == '"') quoted = !quoted;
                if (!quoted && c == ']')
                {
                    var text = source[start.._at].Trim().Trim('"'); _at++;
                    return System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase));
                }
            }
            throw new FormatException("Unclosed Agentflow label.");
        }
        public JsonObject Metadata()
        {
            Space(); if (!Remaining.StartsWith("@{", StringComparison.Ordinal)) return new();
            _at++; var start = _at; var depth = 0; var quote = '\0'; var escaped = false;
            for (; _at < source.Length; _at++)
            {
                var c = source[_at];
                if (quote != '\0') { if (c == quote && !escaped) quote = '\0'; escaped = c == '\\' && !escaped; continue; }
                if (c is '"' or '\'') { quote = c; continue; }
                if (c == '{') depth++; else if (c == '}' && --depth == 0) { _at++; return AgentFlowDiagramAdapter.Metadata(source[start.._at]); }
            }
            throw new FormatException("Unclosed Agentflow metadata.");
        }
        public (string? Id, AgentFlowLinkKind Kind, string Label, JsonObject Metadata) Edge()
        {
            Space(); string? id = null;
            var named = Regex.Match(Remaining, @"^(?<id>[A-Za-z0-9_]+)@(?=-->|-\.-|--x)");
            if (named.Success) { id = named.Groups["id"].Value; _at += named.Length; }
            var op = Regex.Match(Remaining, @"^(-->|-\.-|--x)");
            var label = "";
            if (!op.Success)
            {
                var labeled = Regex.Match(Remaining, "^--\\s+(?<label>\"[^\"]*\"|.+?)\\s+(?<op>-->|-\\.-|--x)\\s*");
                if (!labeled.Success) throw new FormatException("Unknown Agentflow link syntax.");
                label = System.Net.WebUtility.HtmlDecode(labeled.Groups["label"].Value.Trim('"')); _at += labeled.Length;
                var kind = labeled.Groups["op"].Value;
                return (id, kind == "-.-" ? AgentFlowLinkKind.Reference : kind == "--x" ? AgentFlowLinkKind.Failure : AgentFlowLinkKind.Sequence, label, new());
            }
            _at += op.Length; var token = op.Value;
            return (id, token == "-.-" ? AgentFlowLinkKind.Reference : token == "--x" ? AgentFlowLinkKind.Failure : AgentFlowLinkKind.Sequence, label, new());
        }
        public void End() { Space(); if (_at != source.Length) throw new FormatException("Extra Agentflow tokens."); }
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Agentflow elements carry semantic roles and containment.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use Agentflow sequence/reference/failure links.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
