using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class ArchitectureDiagramAdapter : IDiagramAdapter
{
    public string Kind => "architecture-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new ArchitectureDiagramDocument();
        var settings = DataDiagramSupport.Configuration(source, "architecture");
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var identities = new DataIdentity(source, "architecture");
        var groupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected architecture-beta."); header = true; continue; }
            var declaration = Regex.Match(line, @"^(?<kind>group|service|junction)\s+(?<name>[\p{L}_][\p{L}\p{N}_-]*)(?:\((?<icon>[^)]+)\))?(?:\[(?<label>[^\]]*)\])?(?:\s+in\s+(?<parent>[\p{L}_][\p{L}\p{N}_-]*))?\s*$");
            if (declaration.Success)
            {
                var name = declaration.Groups["name"].Value;
                if (groupNames.ContainsKey(name) || nodeNames.ContainsKey(name)) throw new FormatException("Architecture identifier is declared more than once: " + name);
                string? parent = null;
                if (declaration.Groups["parent"].Success && !groupNames.TryGetValue(declaration.Groups["parent"].Value, out parent))
                    throw new FormatException("Declare the parent group before its members.");
                var icon = declaration.Groups["icon"].Success ? declaration.Groups["icon"].Value : null;
                var label = declaration.Groups["label"].Success ? Decode(declaration.Groups["label"].Value) : null;
                var kind = declaration.Groups["kind"].Value;
                var id = identities.Next(kind + ":" + name);
                if (kind == "group")
                {
                    document.Groups.Add(new(id, name) { Icon = icon, Label = label, ParentId = parent });
                    groupNames.Add(name, id);
                }
                else
                {
                    document.Nodes.Add(new(id, name, kind == "junction" ? ArchitectureNodeKind.Junction : ArchitectureNodeKind.Service)
                    { Icon = icon, Label = label, GroupId = parent });
                    nodeNames.Add(name, id);
                }
                continue;
            }
            var align = Regex.Match(line, @"^align\s+(?<axis>row|column)\s+(?<members>.+)$");
            if (align.Success)
            {
                var names = align.Groups["members"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var members = names.Select(name => nodeNames.TryGetValue(name, out var id) ? id : throw new FormatException("Declare services/junctions before aligning them.")).ToArray();
                var axis = align.Groups["axis"].Value == "row" ? ArchitectureAlignmentAxis.Row : ArchitectureAlignmentAxis.Column;
                document.Alignments.Add(new(identities.Next("align:" + axis + ":" + string.Join("\0", names)), axis, members));
                continue;
            }
            var edge = Regex.Match(line, @"^(?<from>[\p{L}_][\p{L}\p{N}_-]*)(?<fg>\{group\})?:(?<fp>[TBLR])\s*(?<fa><)?--(?<ta>>)?\s*(?<tp>[TBLR]):(?<to>[\p{L}_][\p{L}\p{N}_-]*)(?<tg>\{group\})?\s*$");
            if (!edge.Success) throw new FormatException("Unknown architecture statement: " + line);
            var from = nodeNames.GetValueOrDefault(edge.Groups["from"].Value) ?? throw new FormatException("Architecture edges require declared services/junctions, not group identifiers.");
            var to = nodeNames.GetValueOrDefault(edge.Groups["to"].Value) ?? throw new FormatException("Architecture edges require declared services/junctions, not group identifiers.");
            var connection = new ArchitectureConnection("",
                new(from, Port(edge.Groups["fp"].Value), edge.Groups["fg"].Success),
                new(to, Port(edge.Groups["tp"].Value), edge.Groups["tg"].Success))
            { FromArrow = edge.Groups["fa"].Success, ToArrow = edge.Groups["ta"].Success };
            document.Connections.Add(connection with { Id = identities.Next(ConnectionSignature(document, connection)) });
        }
        if (!header) throw new FormatException("Architecture directive is missing.");
        Check(document);
        var model = new DiagramModel { Kind = Kind, Architecture = document };
        _ = Layout.ArchitectureDiagramLayout.Create(model);
        return model;
    }
    public string Write(DiagramModel model) => Write(model.Architecture ?? throw new ArgumentException("Architecture content is missing."));
    public static string Write(ArchitectureDiagramDocument document)
    {
        Check(document);
        var output = new StringBuilder("architecture-beta\n");
        var written = new HashSet<string>();
        foreach (var group in document.Groups) Group(group);
        foreach (var node in document.Nodes)
        {
            output.Append(node.Kind == ArchitectureNodeKind.Service ? "service " : "junction ").Append(node.SourceId);
            Description(node.Icon, node.Label);
            if (node.GroupId is { } parent) output.Append(" in ").Append(document.Groups.Single(g => g.Id == parent).SourceId);
            output.Append('\n');
        }
        foreach (var edge in document.Connections)
        {
            output.Append(document.Nodes.Single(n => n.Id == edge.From.NodeId).SourceId).Append(edge.From.GroupBoundary ? "{group}" : "")
                .Append(':').Append(Port(edge.From.Port)).Append(edge.FromArrow ? " <--" : " --").Append(edge.ToArrow ? "> " : " ")
                .Append(Port(edge.To.Port)).Append(':').Append(document.Nodes.Single(n => n.Id == edge.To.NodeId).SourceId)
                .Append(edge.To.GroupBoundary ? "{group}" : "").Append('\n');
        }
        foreach (var alignment in document.Alignments)
            output.Append("align ").Append(alignment.Axis == ArchitectureAlignmentAxis.Row ? "row " : "column ")
                .Append(string.Join(" ", alignment.Members.Select(id => document.Nodes.Single(n => n.Id == id).SourceId))).Append('\n');
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Group(ArchitectureGroup group)
        {
            if (!written.Add(group.Id)) return;
            if (group.ParentId is { } parent) Group(document.Groups.Single(g => g.Id == parent));
            output.Append("group ").Append(group.SourceId); Description(group.Icon, group.Label);
            if (group.ParentId is { } owner) output.Append(" in ").Append(document.Groups.Single(g => g.Id == owner).SourceId);
            output.Append('\n');
        }
        void Description(string? icon, string? label)
        {
            if (icon is not null)
            {
                if (icon.IndexOfAny(['(', ')', '\r', '\n']) >= 0) throw new ArgumentException("Architecture icon names cannot contain parentheses or line breaks.");
                output.Append('(').Append(icon).Append(')');
            }
            if (label is not null) output.Append('[').Append(Encode(label)).Append(']');
        }
    }
    public static void Check(ArchitectureDiagramDocument document)
    {
        var issues = ArchitectureDiagramValidation.Validate(document);
        if (issues.Any(d => d.Blocking)) throw new DataDiagramNotEditableException(issues);
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Architecture!; var before = Parse(source).Architecture!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source,
            Write(document).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "architecture", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, ArchitectureDiagramDocument document)
    {
        var identities = new DataIdentity(source, "architecture");
        var entries = document.Groups.Select(g => identities.Entry(g.Id, "group:" + g.SourceId)).ToList();
        entries.AddRange(document.Nodes.Select(n => identities.Entry(n.Id, (n.Kind == ArchitectureNodeKind.Junction ? "junction:" : "service:") + n.SourceId)));
        entries.AddRange(document.Connections.Select(e => identities.Entry(e.Id, ConnectionSignature(document, e))));
        entries.AddRange(document.Alignments.Select(a => identities.Entry(a.Id, "align:" + a.Axis + ":" + string.Join("\0", a.Members.Select(id => document.Nodes.Single(n => n.Id == id).SourceId)))));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string ConnectionSignature(ArchitectureDiagramDocument document, ArchitectureConnection edge) =>
        "edge:" + document.Nodes.Single(n => n.Id == edge.From.NodeId).SourceId + "\0" + edge.From.Port + "\0" + edge.From.GroupBoundary + "\0" + edge.FromArrow +
        "\0" + document.Nodes.Single(n => n.Id == edge.To.NodeId).SourceId + "\0" + edge.To.Port + "\0" + edge.To.GroupBoundary + "\0" + edge.ToArrow;
    private static ArchitecturePort Port(string value) => value switch
    { "L" => ArchitecturePort.Left, "R" => ArchitecturePort.Right, "T" => ArchitecturePort.Top, "B" => ArchitecturePort.Bottom, _ => throw new FormatException("Unknown architecture port.") };
    private static string Port(ArchitecturePort value) => value switch
    { ArchitecturePort.Left => "L", ArchitecturePort.Right => "R", ArchitecturePort.Top => "T", ArchitecturePort.Bottom => "B", _ => throw new ArgumentException("Unknown architecture port.") };
    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("]", "&#93;").Replace("[", "&#91;").Replace("\r\n", "\n").Replace("\n", "<br/>");
    private static string Decode(string text) => WebUtility.HtmlDecode(Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase));
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Architecture services, junctions and groups have typed declarations.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Architecture connections require explicit ports and group-boundary ownership.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
