using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class SankeyDiagramAdapter : IDiagramAdapter
{
    public string Kind => "sankey";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) is "sankey" or "sankey-beta";
    public DiagramModel Parse(string source)
    {
        var document = new SankeyDocument();
        var config = DataDiagramSupport.Configuration(source, Kind);
        document.Configuration = config.Configuration; document.ThemeVariables = config.Theme; document.GlobalConfiguration = config.Global;
        var identity = new DataIdentity(source, Kind);
        var rows = Csv(MerpeepsDoc.BodyOf(source));
        var header = false;
        foreach (var row in rows)
        {
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])) continue;
            if (row.Count > 0 && row[0].TrimStart().StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(string.Join(",", row)); continue; }
            if (!header)
            {
                if (row.Count != 1 || !CanParse(row[0].Trim())) throw new FormatException("Expected sankey.");
                document.Directive = row[0].Trim(); header = true; continue;
            }
            if (row.Count != 3) throw new FormatException("Sankey CSV requires exactly source,target,value.");
            var from = Node(row[0].Trim()); var to = Node(row[1].Trim()); var value = ChartDiagramAdapter.ParseNumber(row[2]);
            document.Links.Add(new(identity.Next("link:" + from.Label + "\0" + to.Label + "\0" + ChartDiagramAdapter.Number(value)), from.Id, to.Id, value));
        }
        if (!header) throw new FormatException("Sankey directive is missing.");
        Validate(document); return new() { Kind = document.Directive, Sankey = document };
        SankeyNode Node(string label)
        {
            var current = document.Nodes.FirstOrDefault(n => n.Label == label);
            if (current is not null) return current;
            current = new(identity.Next("node:" + label), label); document.Nodes.Add(current); return current;
        }
    }
    public static void Validate(SankeyDocument document)
    {
        if (document.Nodes.Select(n => n.Id).Distinct().Count() != document.Nodes.Count || document.Nodes.Select(n => n.Label).Distinct().Count() != document.Nodes.Count)
            throw new ArgumentException("Sankey identities and labels must be unique.");
        var incoming = document.Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var link in document.Links)
        {
            if (!incoming.ContainsKey(link.From) || !incoming.ContainsKey(link.To) || !double.IsFinite(link.Value) || link.Value < 0) throw new ArgumentException("Sankey links require valid endpoints and finite nonnegative weights.");
            incoming[link.To]++;
        }
        var ready = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key)); var seen = 0;
        while (ready.TryDequeue(out var id))
        {
            seen++; foreach (var link in document.Links.Where(l => l.From == id)) if (--incoming[link.To] == 0) ready.Enqueue(link.To);
        }
        if (seen != document.Nodes.Count) throw new ArgumentException("Sankey flow must be acyclic.");
        foreach (var key in new[] { "width", "height", "nodeWidth", "nodePadding" })
            if (document.Configuration[key] is { } value && ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) < (key == "nodePadding" ? 0 : 1))
                throw new ArgumentException(key + " has an invalid size.");
        if (document.Configuration["linkColor"]?.ToString() is { } linkColor && linkColor is not ("source" or "target" or "gradient") && !SequenceColors.TryParse(linkColor, out _))
            throw new ArgumentException("Sankey linkColor must be source, target, gradient or a color.");
        if (document.Configuration["nodeAlignment"]?.ToString() is { } alignment && alignment is not ("left" or "right" or "center" or "justify"))
            throw new ArgumentException("Unknown Sankey alignment.");
        if (document.Configuration["labelStyle"] is { } labelStyle &&
            (labelStyle is not JsonValue text || !text.TryGetValue<string>(out var style) || style is not ("legacy" or "outlined")))
            throw new ArgumentException("Sankey labelStyle must be legacy or outlined.");
        if (document.Configuration["labelStyle"]?.ToString() == "outlined" &&
            (document.ThemeVariables["mainBkg"] ?? document.ThemeVariables["background"]) is { } background &&
            !SequenceColors.TryParse(ChartConfiguration.Raw(background), out _))
            throw new ArgumentException("The Sankey label outline background must be a color.");
        if (document.Configuration["nodeColors"] is { } nodeColors && nodeColors is not JsonObject)
            throw new ArgumentException("Sankey nodeColors must map node labels to colors.");
        if (document.Configuration["nodeColors"] is JsonObject colors)
            foreach (var value in colors) if (value.Value is not null && !SequenceColors.TryParse(value.Value.ToString(), out _)) throw new ArgumentException("Invalid custom Sankey node color.");
    }
    public string Write(DiagramModel model) => Write(model.Sankey ?? throw new ArgumentException("Sankey content is missing."));
    public static string Write(SankeyDocument document)
    {
        Validate(document);
        var output = new StringBuilder(document.Directive).Append('\n');
        foreach (var link in document.Links)
            output.Append(Quote(document.Nodes.Single(n => n.Id == link.From).Label)).Append(',')
                .Append(Quote(document.Nodes.Single(n => n.Id == link.To).Label)).Append(',').Append(ChartDiagramAdapter.Number(link.Value)).Append('\n');
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Sankey!; var before = Parse(source).Sankey!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, Kind, document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, SankeyDocument document)
    {
        var map = new DataIdentity(source, "sankey");
        var entries = document.Nodes.Select(n => map.Entry(n.Id, "node:" + n.Label)).ToList();
        entries.AddRange(document.Links.Select(l => map.Entry(l.Id, "link:" + document.Nodes.Single(n => n.Id == l.From).Label + "\0" + document.Nodes.Single(n => n.Id == l.To).Label + "\0" + ChartDiagramAdapter.Number(l.Value))));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    private static IEnumerable<IReadOnlyList<string>> Csv(string input)
    {
        var row = new List<string>(); var value = new StringBuilder(); var quote = false;
        for (var i = 0; i <= input.Length; i++)
        {
            if (i < input.Length && input[i] == '"')
            {
                if (quote && i + 1 < input.Length && input[i + 1] == '"') { value.Append('"'); i++; }
                else quote = !quote;
                continue;
            }
            if (i == input.Length || !quote && input[i] is ',' or '\n')
            {
                row.Add(value.ToString().TrimEnd('\r')); value.Clear();
                if (i == input.Length || input[i] == '\n') { yield return row.ToArray(); row.Clear(); }
            }
            else value.Append(input[i]);
        }
        if (quote) throw new FormatException("Unclosed CSV quote.");
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Use weighted flow data.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Sankey links require weights.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}

public sealed class TreemapDiagramAdapter : IDiagramAdapter
{
    public string Kind => "treemap-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new TreemapDocument(); var settings = DataDiagramSupport.Configuration(source, "treemap");
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var map = new DataIdentity(source, "treemap"); var roots = new List<Builder>(); var stack = new Stack<(int Indent, Builder Node)>();
        var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimEnd(';'); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected treemap-beta."); header = true; continue; }
            if (line.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var at = line.IndexOf(' ', 9); if (at < 0) throw new FormatException("Class definition requires style data.");
                document.Classes[line[9..at]] = ChartDiagramAdapter.ParseStyle(line[(at + 1)..]); continue;
            }
            var match = Regex.Match(line, "^\"(?<label>[^\"]*)\"\\s*(?::\\s*(?<value>" + ChartDiagramAdapter.Numeric + "))?\\s*(?:[:]{3}(?<class>[\\w,-]+))?$");
            if (!match.Success) throw new FormatException("Treemap nodes require quoted labels and optional values/classes.");
            var label = match.Groups["label"].Value;
            double? value = match.Groups["value"].Success ? ChartDiagramAdapter.ParseNumber(match.Groups["value"].Value) : null;
            var node = new Builder(new(map.Next("node:" + label), label, value, []) { Classes = match.Groups["class"].Success ? match.Groups["class"].Value.Split(',') : [] });
            var indent = raw.Replace("\t", "    ").TakeWhile(char.IsWhiteSpace).Count();
            while (stack.Count > 0 && stack.Peek().Indent >= indent) stack.Pop();
            if (stack.TryPeek(out var parent)) parent.Node.Children.Add(node); else roots.Add(node);
            stack.Push((indent, node));
        }
        document.Roots.AddRange(roots.Select(n => n.Build()));
        if (!header) throw new FormatException("Treemap directive is missing.");
        Validate(document); return new() { Kind = Kind, Treemap = document };
    }
    public static void Validate(TreemapDocument document)
    {
        var ids = new HashSet<string>();
        foreach (var node in document.Nodes)
        {
            if (!ids.Add(node.Id)) throw new ArgumentException("Treemap node identities must be unique.");
            ChartDiagramAdapter.Quote(node.Label);
            if (node.Value is { } value && (!double.IsFinite(value) || value < 0 || node.Children.Count > 0))
                throw new ArgumentException("Treemap values must be finite and nonnegative on leaf nodes only.");
            foreach (var style in node.Classes) if (!document.Classes.ContainsKey(style)) throw new ArgumentException("Undefined treemap style class: " + style);
            var styles = new Dictionary<string, string>();
            foreach (var key in node.Classes) foreach (var pair in document.Classes[key]) if (pair.Value is not null) styles[pair.Key] = ChartConfiguration.Raw(pair.Value);
            var resolved = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = node.Id }, styles, false);
            if (resolved.Diagnostics.Count > 0) throw new NotSupportedException(string.Join("\n", resolved.Diagnostics));
        }
        foreach (var key in new[] { "padding", "diagramPadding", "nodeWidth", "nodeHeight", "borderWidth", "valueFontSize", "labelFontSize" })
            if (document.Configuration[key] is { } value && ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) < 0) throw new ArgumentException(key + " cannot be negative.");
        if (document.Configuration["valueFormat"] is { } format) _ = Layout.WeightedDiagramLayout.Format(1.25, format.ToString());
    }
    public string Write(DiagramModel model) => Write(model.Treemap ?? throw new ArgumentException("Treemap content is missing."));
    public static string Write(TreemapDocument document)
    {
        Validate(document); var output = new StringBuilder("treemap-beta\n"); Nodes(document.Roots, 0);
        foreach (var item in document.Classes) output.Append("classDef ").Append(item.Key).Append(' ').Append(string.Join(",", item.Value.Select(p => p.Key + ":" + (p.Value is null ? "" : ChartConfiguration.Raw(p.Value))))).Append('\n');
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Nodes(IEnumerable<TreemapNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                output.Append(new string(' ', depth * 4)).Append(ChartDiagramAdapter.Quote(node.Label));
                if (node.Value is { } value) output.Append(": ").Append(ChartDiagramAdapter.Number(value));
                if (node.Classes.Count > 0) output.Append(":::").Append(string.Join(",", node.Classes));
                output.Append('\n'); Nodes(node.Children, depth + 1);
            }
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Treemap!; var before = Parse(source).Treemap!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "treemap", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, TreemapDocument document)
    {
        var identity = new DataIdentity(source, "treemap");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = document.Nodes.Select(n => identity.Entry(n.Id, "node:" + n.Label)).ToList() };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Treemap nodes carry weights and hierarchy.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Treemaps do not use graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
    private sealed class Builder(TreemapNode node)
    {
        public List<Builder> Children { get; } = new();
        public TreemapNode Build() => node with { Children = Children.Select(c => c.Build()).ToArray() };
    }
}
