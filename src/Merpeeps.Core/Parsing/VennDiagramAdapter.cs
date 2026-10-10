using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class VennDiagramAdapter : IDiagramAdapter
{
    public string Kind => "venn-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    private const string Identifier = "\"[^\"]*\"|[\\p{L}_][\\p{L}\\p{N}_-]*";
    public DiagramModel Parse(string source)
    {
        var d = new VennDiagramDocument(); var config = DataDiagramSupport.Configuration(source, "venn");
        d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var identity = new DataIdentity(source, "venn"); var header = false; string? area = null;
        var styles = new List<(string[] Names, JsonObject Style)>();
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { d.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected venn-beta."); header = true; continue; }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { d.Title = Name(line[6..]); continue; }
            var match = Regex.Match(line, "^(?<kind>set|union|text)\\s+(?<names>(?:" + Identifier + ")(?:\\s*,\\s*(?:" + Identifier + "))*)(?:\\s*\\[\"(?<label>[^\"]*)\"\\])?(?:\\s*:\\s*(?<size>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)))?$");
            if (match.Success)
            {
                var kind = match.Groups["kind"].Value; var names = Names(match.Groups["names"].Value);
                var label = match.Groups["label"].Success ? WebUtility.HtmlDecode(match.Groups["label"].Value) : null;
                double? size = match.Groups["size"].Success ? double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture) : null;
                if (kind == "set")
                {
                    if (names.Length != 1) throw new FormatException("A Venn set has one identifier.");
                    var set = new VennSet(identity.Next("set:" + names[0]), names[0], label, size);
                    d.Sets.Add(set); area = set.Id;
                }
                else if (kind == "union")
                {
                    var ids = names.Select(name => d.Sets.FirstOrDefault(s => s.Name == name)?.Id ?? throw new FormatException("Define Venn sets before their overlaps: " + name)).ToArray();
                    var overlap = new VennOverlap(identity.Next("overlap:" + string.Join("\0", names.Order(StringComparer.Ordinal))), ids, label, size);
                    d.Overlaps.Add(overlap); area = overlap.Id;
                }
                else
                {
                    if (names.Length != 1 || size is not null || area is null) throw new FormatException("A text node needs an existing set/overlap and one identifier.");
                    d.Texts.Add(new(identity.Next("text:" + names[0]), names[0], area, label));
                }
                continue;
            }
            var style = Regex.Match(line, "^style\\s+(?<names>(?:" + Identifier + ")(?:\\s*,\\s*(?:" + Identifier + "))*)\\s+(?<style>.+)$");
            if (!style.Success) throw new FormatException("Unknown Venn statement: " + line);
            styles.Add((Names(style.Groups["names"].Value), ParseStyle(style.Groups["style"].Value)));
        }
        if (!header) throw new FormatException("Venn directive is missing.");
        foreach (var (names, style) in styles)
        {
            if (names.Length == 1 && d.Texts.FindIndex(t => t.Name == names[0]) is var textIndex && textIndex >= 0)
            {
                d.Texts[textIndex] = d.Texts[textIndex] with { Style = Merge(d.Texts[textIndex].Style, style) }; continue;
            }
            var setIds = names.Select(name => d.Sets.FirstOrDefault(s => s.Name == name)?.Id ?? throw new FormatException("Unknown Venn style target: " + name)).ToHashSet();
            if (setIds.Count == 1)
            {
                var i = d.Sets.FindIndex(s => setIds.Contains(s.Id)); d.Sets[i] = d.Sets[i] with { Style = Merge(d.Sets[i].Style, style) };
            }
            else
            {
                var i = d.Overlaps.FindIndex(o => setIds.SetEquals(o.SetIds));
                if (i < 0) throw new NotSupportedException("Style targets an undeclared Venn overlap; declare the overlap before styling it.");
                d.Overlaps[i] = d.Overlaps[i] with { Style = Merge(d.Overlaps[i].Style, style) };
            }
        }
        VennValidation.Check(d); return new() { Kind = Kind, Venn = d };
    }
    public string Write(DiagramModel model) => Write(model.Venn ?? throw new ArgumentException("Venn content is missing."));
    public static string Write(VennDiagramDocument d)
    {
        VennValidation.Check(d); var output = new StringBuilder("venn-beta\n");
        if (d.Title.Length > 0) output.Append("title ").Append(Quote(d.Title)).Append('\n');
        foreach (var set in d.Sets)
        {
            output.Append("set ").Append(Quote(set.Name)); LabelAndSize(set.Label, set.Size); Texts(set.Id);
        }
        foreach (var overlap in d.Overlaps)
        {
            output.Append("union ").Append(Target(overlap.SetIds)); LabelAndSize(overlap.Label, overlap.Size); Texts(overlap.Id);
        }
        foreach (var set in d.Sets) Style(Quote(set.Name), set.Style);
        foreach (var overlap in d.Overlaps) Style(Target(overlap.SetIds), overlap.Style);
        foreach (var text in d.Texts) Style(Quote(text.Name), text.Style);
        foreach (var comment in d.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        string Target(IEnumerable<string> ids) => string.Join(",", ids.Select(id => Quote(d.Sets.Single(s => s.Id == id).Name)));
        void LabelAndSize(string? label, double? size)
        {
            if (label is not null) output.Append('[').Append(Quote(label)).Append(']');
            if (size is not null) output.Append(':').Append(ChartDiagramAdapter.Number(size.Value));
            output.Append('\n');
        }
        void Texts(string area)
        {
            foreach (var text in d.Texts.Where(t => t.AreaId == area))
            { output.Append("  text ").Append(Quote(text.Name)); if (text.Label is not null) output.Append('[').Append(Quote(text.Label)).Append(']'); output.Append('\n'); }
        }
        void Style(string target, JsonObject values)
        {
            if (values.Count == 0) return;
            output.Append("style ").Append(target).Append(' ').Append(string.Join(",", values.Select(p => p.Key + ":" + p.Value?.ToString()))).Append('\n');
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.Venn!; var before = Parse(source).Venn!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "venn", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, VennDiagramDocument d)
    {
        var identity = new DataIdentity(source, "venn");
        var entries = d.Sets.Select(s => identity.Entry(s.Id, "set:" + s.Name)).ToList();
        entries.AddRange(d.Overlaps.Select(o => identity.Entry(o.Id, "overlap:" + string.Join("\0", o.SetIds.Select(id => d.Sets.Single(s => s.Id == id).Name).Order(StringComparer.Ordinal)))));
        entries.AddRange(d.Texts.Select(t => identity.Entry(t.Id, "text:" + t.Name)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Name(string value) => WebUtility.HtmlDecode(value.Trim().Trim('"'));
    private static string[] Names(string text) => Regex.Matches(text, Identifier).Select(m => Name(m.Value)).ToArray();
    private static string Quote(string value) => "\"" + value.Replace("&", "&amp;").Replace("\"", "&quot;") + "\"";
    private static JsonObject Merge(JsonObject old, JsonObject added)
    {
        var result = (JsonObject)old.DeepClone(); foreach (var pair in added) result[pair.Key] = pair.Value?.DeepClone(); return result;
    }
    private static JsonObject ParseStyle(string text)
    {
        var result = new JsonObject();
        foreach (var part in Regex.Split(text, @",\s*(?=[a-zA-Z-]+\s*:)"))
        {
            var colon = part.IndexOf(':'); if (colon < 0) throw new FormatException("Venn styles use property:value.");
            result[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }
        return result;
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Venn uses sets and common overlaps.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Venn overlap is not a graph edge.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
