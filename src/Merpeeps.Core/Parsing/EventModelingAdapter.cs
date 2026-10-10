using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class EventModelingAdapter : IDiagramAdapter
{
    public string Kind => "eventmodeling";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var d = new EventModelingDocument(); var identity = new DataIdentity(source, Kind); var header = false;
        var config = DataDiagramSupport.Configuration(source, Kind); d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var dataRefs = new Dictionary<string, string>(); var frameRefs = new Dictionary<string, string[]>();
        foreach (var line in UseCaseDiagramAdapter.Statements(MerpeepsDoc.BodyOf(source)))
        {
            if (line.StartsWith("%%", StringComparison.Ordinal)) { d.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected eventmodeling."); header = true; continue; }
            var data = Regex.Match(line, @"^data\s+(?<name>[A-Za-z0-9_]+)\s*(?<data>.*)$", RegexOptions.Singleline);
            if (data.Success)
            {
                var name = data.Groups["name"].Value;
                d.DataBlocks.Add(new(identity.Next("data:" + name), name, ParseData(data.Groups["data"].Value))); continue;
            }
            var frame = Regex.Match(line, @"^(?<frame>tf|timeframe|rf|resetframe)\s+(?<number>\d+)\s+(?<kind>ui|pcr|processor|cmd|command|rmo|readmodel|evt|event)\s+(?<entity>[A-Za-z0-9_.]+)(?<tail>.*)$", RegexOptions.Singleline);
            if (!frame.Success) throw new FormatException("Unknown Event Modeling statement: " + line);
            var number = frame.Groups["number"].Value; var id = identity.Next("frame:" + NormalizeNumber(number));
            var kind = frame.Groups["kind"].Value switch
            {
                "ui" => EventEntityKind.UserInterface, "pcr" or "processor" => EventEntityKind.Processor,
                "cmd" or "command" => EventEntityKind.Command, "rmo" or "readmodel" => EventEntityKind.ReadModel, _ => EventEntityKind.Event,
            };
            var tail = frame.Groups["tail"].Value.Trim(); EventDataValue? inline = null;
            if (tail.StartsWith("[[", StringComparison.Ordinal))
            {
                var end = tail.IndexOf("]]", StringComparison.Ordinal); if (end < 0) throw new FormatException("Unclosed Event Modeling data reference.");
                dataRefs[id] = tail[2..end]; tail = tail[(end + 2)..].Trim();
            }
            else if (tail.StartsWith('{') || tail.StartsWith('`'))
            {
                var end = DataEnd(tail); inline = ParseData(tail[..(end + 1)]); tail = tail[(end + 1)..].Trim();
            }
            var references = Regex.Matches(tail, @"->>\s*(?<number>\d+)").Select(m => m.Groups["number"].Value).ToArray();
            if (Regex.Replace(tail, @"->>\s*\d+", "").Trim().Length > 0) throw new FormatException("Invalid Event Modeling reference syntax.");
            frameRefs[id] = references;
            d.Frames.Add(new(id, number, kind, frame.Groups["entity"].Value)
            {
                Reset = frame.Groups["frame"].Value is "rf" or "resetframe",
                RelaxedSyntax = frame.Groups["frame"].Value is "timeframe" or "resetframe", InlineData = inline,
            });
        }
        if (!header) throw new FormatException("Event Modeling header is missing.");
        for (var i = 0; i < d.Frames.Count; i++)
        {
            var frame = d.Frames[i];
            d.Frames[i] = frame with
            {
                DataBlockId = dataRefs.TryGetValue(frame.Id, out var name) ? d.DataBlocks.FirstOrDefault(b => b.Name == name)?.Id ?? throw new FormatException("Unknown Event Modeling data block: " + name) : null,
                References = frameRefs[frame.Id].Select(n => d.Frames.FirstOrDefault(f => NormalizeNumber(f.Number) == NormalizeNumber(n))?.Id ?? throw new FormatException("Unknown timeframe number: " + n)).ToArray(),
            };
        }
        EventModelingSemantics.Check(d); return new() { Kind = Kind, EventModeling = d };
    }
    internal static string NormalizeNumber(string text) => text.TrimStart('0') is { Length: > 0 } number ? number : "0";
    private static int DataEnd(string text)
    {
        var start = text.IndexOf('{'); if (start < 0) throw new FormatException("Event data needs braces.");
        var quote = '\0'; var depth = 0; var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0') { if (c == quote && !escaped) quote = '\0'; escaped = c == '\\' && !escaped; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '{') depth++; else if (c == '}' && --depth == 0) return i;
        }
        throw new FormatException("Unclosed Event Modeling data.");
    }
    private static EventDataValue ParseData(string text)
    {
        text = text.Trim(); var kind = EventDataKind.JsObject; var explicitKind = false;
        if (text.StartsWith('`'))
        {
            var end = text.IndexOf('`', 1); if (end < 0) throw new FormatException("Unclosed data type.");
            kind = text[1..end] switch
            {
                "json" => EventDataKind.Json, "jsobj" => EventDataKind.JsObject, "figma" => EventDataKind.Figma, "salt" => EventDataKind.Salt,
                "uri" => EventDataKind.Uri, "md" => EventDataKind.Markdown, "html" => EventDataKind.Html, "text" => EventDataKind.Text, _ => throw new FormatException("Unknown Event Modeling data type."),
            };
            explicitKind = true; text = text[(end + 1)..].Trim();
        }
        if (!text.StartsWith('{') || DataEnd(text) != text.Length - 1) throw new FormatException("Invalid Event Modeling data block.");
        return new(kind, text[1..^1], explicitKind);
    }
    public string Write(DiagramModel model) => Write(model.EventModeling ?? throw new ArgumentException("Event Modeling content is missing."));
    public static string Write(EventModelingDocument d)
    {
        EventModelingSemantics.Check(d); var b = new StringBuilder("eventmodeling\n");
        foreach (var frame in d.Frames)
        {
            b.Append(frame.Reset ? frame.RelaxedSyntax ? "resetframe" : "rf" : frame.RelaxedSyntax ? "timeframe" : "tf")
                .Append(' ').Append(frame.Number).Append(' ').Append(KindName(frame.Kind, frame.RelaxedSyntax)).Append(' ').Append(frame.EntityName);
            if (frame.InlineData is { } inline) b.Append(' ').Append(Data(inline));
            if (frame.DataBlockId is { } id) b.Append(" [[").Append(d.DataBlocks.Single(x => x.Id == id).Name).Append("]]");
            foreach (var target in frame.References) b.Append(" ->> ").Append(d.Frames.Single(f => f.Id == target).Number);
            b.Append('\n');
        }
        foreach (var data in d.DataBlocks) b.Append("data ").Append(data.Name).Append(' ').Append(Data(data.Data)).Append('\n');
        foreach (var comment in d.Comments) b.Append(comment).Append('\n');
        return b.ToString();
    }
    private static string KindName(EventEntityKind kind, bool relaxed) => kind switch
    {
        EventEntityKind.UserInterface => "ui", EventEntityKind.Processor => relaxed ? "processor" : "pcr",
        EventEntityKind.Command => relaxed ? "command" : "cmd", EventEntityKind.ReadModel => relaxed ? "readmodel" : "rmo", _ => relaxed ? "event" : "evt",
    };
    private static string Data(EventDataValue data) => (data.ExplicitKind || data.Kind != EventDataKind.JsObject ? "`" + (data.Kind switch { EventDataKind.JsObject => "jsobj", EventDataKind.Markdown => "md", _ => data.Kind.ToString().ToLowerInvariant() }) + "`" : "") + "{" + data.Source + "}";
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.EventModeling!; var before = Parse(source).EventModeling!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "eventmodeling", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, EventModelingDocument d)
    {
        var ids = new DataIdentity(source, "eventmodeling");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = d.Frames.Select(f => ids.Entry(f.Id, "frame:" + NormalizeNumber(f.Number)))
            .Concat(d.DataBlocks.Select(b => ids.Entry(b.Id, "data:" + b.Name))).ToList() };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Event frames are ordered occurrences, not generic nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Event relations are inferred from frames or explicit timeframe references.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
