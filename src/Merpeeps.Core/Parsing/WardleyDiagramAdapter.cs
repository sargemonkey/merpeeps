using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class WardleyDiagramAdapter : IDiagramAdapter
{
    public string Kind => "wardley-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    private const string NumberPattern = @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)";
    private const string NamePattern = "(?:\"[^\"]*\"|[^\\[{}]+?)";
    public DiagramModel Parse(string source)
    {
        var d = new WardleyDiagramDocument(); var config = DataDiagramSupport.Configuration(source, "wardley");
        d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var identity = new DataIdentity(source, "wardley");
        var links = new List<(string From, string To, WardleyLinkKind Kind, string Label)>();
        var evolves = new List<(string Name, double Evolution)>();
        var trends = new List<(string Name, DiagramPoint Point)>();
        string? pipeline = null; var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(raw, d.Comments).Trim(); if (line.Length == 0) continue;
            if (!header) { if (line != Kind) throw new FormatException("Expected wardley-beta."); header = true; continue; }
            if (line == "}")
            {
                if (pipeline is null) throw new FormatException("Unmatched Wardley pipeline close.");
                pipeline = null; continue;
            }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { d.Title = line[6..]; continue; }
            if (line.StartsWith("size ", StringComparison.Ordinal))
            {
                var p = Pair(line[5..]); d.Width = p.X; d.Height = p.Y; d.HasExplicitSize = true; continue;
            }
            if (line.StartsWith("evolution ", StringComparison.Ordinal))
            {
                var parts = line[10..].Split("->", StringSplitOptions.TrimEntries);
                d.Stages.Clear();
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i]; var at = part.LastIndexOf('@');
                    var end = at < 0 ? (i + 1d) / parts.Length : Numeric(part[(at + 1)..]);
                    var labels = (at < 0 ? part : part[..at]).Split('/', 2, StringSplitOptions.TrimEntries);
                    d.Stages.Add(new(labels[0], labels.Length > 1 ? labels[1] : null, end, at >= 0));
                }
                continue;
            }
            var open = Regex.Match(line, "^pipeline\\s+(?<name>" + NamePattern + ")\\s*\\{$");
            if (open.Success)
            {
                if (pipeline is not null) throw new FormatException("Nested Wardley pipelines are not supported.");
                var parent = Resolve(Name(open.Groups["name"].Value));
                if (parent.PipelineParentId is not null) throw new FormatException("A pipeline variant cannot own another pipeline.");
                pipeline = parent.Id; continue;
            }
            var node = Regex.Match(line, "^(?<kind>component|anchor)\\s+(?<name>" + NamePattern + ")\\s*\\[(?<coords>[^\\]]+)\\](?<extra>.*)$");
            if (node.Success)
            {
                var name = Name(node.Groups["name"].Value); var parts = node.Groups["coords"].Value.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != (pipeline is null ? 2 : 1)) throw new FormatException("Wardley positions use [visibility,evolution]; pipeline variants use [evolution].");
                var extra = node.Groups["extra"].Value;
                var offset = Regex.Match(extra, @"\blabel\s*(?<position>\[[^\]]+\])");
                var strategy = WardleyStrategy.None; var inertia = false;
                foreach (Match decorator in Regex.Matches(extra, @"\((?<name>[^)]+)\)"))
                    switch (decorator.Groups["name"].Value)
                    {
                        case "inertia": inertia = true; break;
                        case "build": strategy = WardleyStrategy.Build; break;
                        case "buy": strategy = WardleyStrategy.Buy; break;
                        case "outsource": strategy = WardleyStrategy.Outsource; break;
                        case "market": strategy = WardleyStrategy.Market; break;
                        default: throw new FormatException("Unknown Wardley decorator.");
                    }
                var remainder = Regex.Replace(Regex.Replace(extra, @"\blabel\s*\[[^\]]+\]", ""), @"\([^)]*\)", "").Trim();
                if (remainder.Length > 0) throw new FormatException("Unknown Wardley component options: " + remainder);
                var component = new WardleyComponent(identity.Next("component:" + pipeline + "\0" + name), name, Numeric(parts[^1]))
                {
                    Visibility = pipeline is null ? Numeric(parts[0]) : null, PipelineParentId = pipeline,
                    Anchor = node.Groups["kind"].Value == "anchor", Strategy = strategy, Inertia = inertia,
                    LabelOffset = offset.Success ? Pair(offset.Groups["position"].Value) : default,
                };
                d.Components.Add(component); continue;
            }
            var evolve = Regex.Match(line, "^evolve\\s+(?<name>" + NamePattern + ")\\s+(?<value>" + NumberPattern + ")$");
            if (evolve.Success) { evolves.Add((Name(evolve.Groups["name"].Value), Numeric(evolve.Groups["value"].Value))); continue; }
            if (line.StartsWith("annotations ", StringComparison.Ordinal))
            {
                var p = Pair(line[12..]); d.AnnotationBox = new(p.Y, p.X); continue;
            }
            var annotation = Regex.Match(line, "^(?<kind>note|accelerator|deaccelerator)\\s+\"(?<text>[^\"]*)\"\\s*(?<coords>\\[[^\\]]+\\])$");
            if (annotation.Success)
            {
                var p = Pair(annotation.Groups["coords"].Value);
                var kind = annotation.Groups["kind"].Value switch { "note" => WardleyAnnotationKind.Note, "accelerator" => WardleyAnnotationKind.Accelerator, _ => WardleyAnnotationKind.Deaccelerator };
                var text = WebUtility.HtmlDecode(annotation.Groups["text"].Value);
                d.Annotations.Add(new(identity.Next("annotation:" + kind + "\0" + text), kind, text, p.X, p.Y)); continue;
            }
            var numbered = Regex.Match(line, "^annotation\\s+(?<n>\\d+)\\s*,\\s*(?<coords>\\[[^\\]]+\\])\\s*\"(?<text>[^\"]*)\"$");
            if (numbered.Success)
            {
                var n = int.Parse(numbered.Groups["n"].Value, CultureInfo.InvariantCulture); var p = Pair(numbered.Groups["coords"].Value);
                var text = WebUtility.HtmlDecode(numbered.Groups["text"].Value);
                d.Annotations.Add(new(identity.Next("numbered:" + n), WardleyAnnotationKind.Numbered, text, p.X, p.Y) { Number = n }); continue;
            }
            var trend = Regex.Match(line, "^(?<name>" + NamePattern + ")\\s*-\\.-\\s*\\((?<coords>[^)]+)\\)$");
            if (trend.Success) { trends.Add((Name(trend.Groups["name"].Value), Pair("[" + trend.Groups["coords"].Value + "]"))); continue; }
            var link = Regex.Match(line, "^(?<from>.+?)\\s*(?<op>\\+<>|\\+<|\\+>|\\+'(?<flowLabel>[^']*)'>|-\\.->|-->|->)\\s*(?<to>.+?)(?:\\s*;\\s*(?<label>.*))?$");
            if (!link.Success) throw new FormatException("Unknown Wardley statement: " + line);
            var op = link.Groups["op"].Value;
            links.Add((Name(link.Groups["from"].Value), Name(link.Groups["to"].Value),
                op == "-.->" ? WardleyLinkKind.Dashed : op == "+<" ? WardleyLinkKind.ReverseFlow : op == "+<>" ? WardleyLinkKind.BidirectionalFlow : op.StartsWith('+') ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency,
                link.Groups["flowLabel"].Success ? link.Groups["flowLabel"].Value : link.Groups["label"].Value));
        }
        if (!header || pipeline is not null) throw new FormatException("Missing Wardley header or unclosed pipeline.");
        foreach (var e in evolves)
        {
            var node = Resolve(e.Name); var index = d.Components.IndexOf(node);
            d.Components[index] = node with { EvolveTo = e.Evolution };
        }
        foreach (var trend in trends)
        {
            var node = Resolve(trend.Name); d.Components[d.Components.IndexOf(node)] = node with { Trend = trend.Point };
        }
        foreach (var link in links)
        {
            var from = Resolve(link.From); var to = Resolve(link.To);
            d.Links.Add(new(identity.Next("link:" + from.Id + "\0" + to.Id + "\0" + link.Kind + "\0" + link.Label), from.Id, to.Id, link.Kind, link.Label));
        }
        WardleyValidation.Check(d);
        return new() { Kind = Kind, Wardley = d };
        WardleyComponent Resolve(string name)
        {
            var nodes = d.Components.Where(c => c.Name == name || c.PipelineParentId is { } parent && d.Components.First(p => p.Id == parent).Name + "_" + c.Name == name).ToArray();
            return nodes.Length == 1 ? nodes[0] : throw new FormatException("Wardley reference is unknown or ambiguous: " + name);
        }
    }
    public string Write(DiagramModel model) => Write(model.Wardley ?? throw new ArgumentException("Wardley content is missing."));
    public static string Write(WardleyDiagramDocument d)
    {
        WardleyValidation.Check(d); var output = new StringBuilder("wardley-beta\n");
        if (d.Title.Length > 0) output.Append("title ").Append(d.Title).Append('\n');
        if (d.HasExplicitSize) output.Append("size [").Append(N(d.Width)).Append(", ").Append(N(d.Height)).Append("]\n");
        if (d.Stages.Count > 0) output.Append("evolution ").Append(string.Join(" -> ", d.Stages.Select(s => s.Label + (s.SecondaryLabel is null ? "" : " / " + s.SecondaryLabel) + (s.ExplicitEnd ? "@" + N(s.End) : "")))).Append('\n');
        foreach (var component in d.Components.Where(c => c.PipelineParentId is null)) Node(component, false);
        foreach (var parent in d.Components.Where(c => d.Components.Any(n => n.PipelineParentId == c.Id)))
        {
            output.Append("pipeline ").Append(Quote(parent.Name)).Append(" {\n");
            foreach (var node in d.Components.Where(c => c.PipelineParentId == parent.Id)) Node(node, true);
            output.Append("}\n");
        }
        foreach (var link in d.Links)
        {
            output.Append(Quote(Reference(link.From))).Append(link.Kind switch
            { WardleyLinkKind.Dashed => " -.-> ", WardleyLinkKind.Flow => " +> ", WardleyLinkKind.ReverseFlow => " +< ", WardleyLinkKind.BidirectionalFlow => " +<> ", _ => " -> " }).Append(Quote(Reference(link.To)));
            if (link.Label.Length > 0) output.Append("; ").Append(link.Label);
            output.Append('\n');
        }
        foreach (var node in d.Components)
        {
            if (node.EvolveTo is { } evolve) output.Append("evolve ").Append(Quote(Reference(node.Id))).Append(' ').Append(N(evolve)).Append('\n');
            if (node.Trend is { } trend) output.Append(Quote(Reference(node.Id))).Append(" -.- (").Append(N(trend.X)).Append(", ").Append(N(trend.Y)).Append(")\n");
        }
        if (d.AnnotationBox is { } box) output.Append("annotations [").Append(N(box.Y)).Append(", ").Append(N(box.X)).Append("]\n");
        foreach (var a in d.Annotations)
        {
            if (a.Kind == WardleyAnnotationKind.Numbered)
                output.Append("annotation ").Append(a.Number).Append(",[").Append(N(a.Visibility)).Append(", ").Append(N(a.Evolution)).Append("] ").Append(Quote(a.Text));
            else output.Append(a.Kind.ToString().ToLowerInvariant()).Append(' ').Append(Quote(a.Text)).Append(" [").Append(N(a.Visibility)).Append(", ").Append(N(a.Evolution)).Append(']');
            output.Append('\n');
        }
        foreach (var comment in d.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        string Reference(string id)
        {
            var node = d.Components.Single(c => c.Id == id);
            return node.PipelineParentId is { } parent ? d.Components.Single(c => c.Id == parent).Name + "_" + node.Name : node.Name;
        }
        void Node(WardleyComponent c, bool pipeline)
        {
            output.Append(pipeline ? "  component " : c.Anchor ? "anchor " : "component ").Append(Quote(c.Name)).Append(" [");
            if (!pipeline) output.Append(N(c.Visibility!.Value)).Append(", ");
            output.Append(N(c.Evolution)).Append(']');
            if (c.LabelOffset != default) output.Append(" label [").Append(N(c.LabelOffset.X)).Append(", ").Append(N(c.LabelOffset.Y)).Append(']');
            if (c.Strategy != WardleyStrategy.None) output.Append(" (").Append(c.Strategy.ToString().ToLowerInvariant()).Append(')');
            if (c.Inertia) output.Append(" (inertia)");
            output.Append('\n');
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.Wardley!; var before = Parse(source).Wardley!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "wardley", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, WardleyDiagramDocument d)
    {
        var identity = new DataIdentity(source, "wardley");
        var entries = d.Components.Select(c => identity.Entry(c.Id, "component:" + c.PipelineParentId + "\0" + c.Name)).ToList();
        entries.AddRange(d.Links.Select(l => identity.Entry(l.Id, "link:" + l.From + "\0" + l.To + "\0" + l.Kind + "\0" + l.Label)));
        entries.AddRange(d.Annotations.Select(a => identity.Entry(a.Id, a.Kind == WardleyAnnotationKind.Numbered ? "numbered:" + a.Number : "annotation:" + a.Kind + "\0" + a.Text)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static DiagramPoint Pair(string text)
    {
        var match = Regex.Match(text.Trim(), "^\\[(?<a>" + NumberPattern + ")\\s*,\\s*(?<b>" + NumberPattern + ")\\]$");
        if (!match.Success) throw new FormatException("Expected two numeric coordinates.");
        return new(Numeric(match.Groups["a"].Value), Numeric(match.Groups["b"].Value));
    }
    private static double Numeric(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string N(double number) => number is 0 or 1 ? number.ToString("0.0", CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture);
    private static string Name(string value) => WebUtility.HtmlDecode(value.Trim().Trim('"'));
    private static string Quote(string value) => "\"" + value.Replace("&", "&amp;").Replace("\"", "&quot;") + "\"";
    private static string StripComment(string line, List<string> comments)
    {
        var quote = false;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] == '"') quote = !quote;
            if (!quote && line[i] == '%' && line[i + 1] == '%') { comments.Add(line[i..]); return line[..i]; }
        }
        return line;
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Wardley components use visibility/evolution coordinates.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Wardley links and movement use typed map semantics.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
