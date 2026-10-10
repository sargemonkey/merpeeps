using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class PacketDiagramAdapter : IDiagramAdapter
{
    public string Kind => "packet";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) is "packet" or "packet-beta";
    public DiagramModel Parse(string source)
    {
        var packet = new PacketDocument();
        var settings = DataDiagramSupport.Configuration(source, "packet");
        packet.Configuration = settings.Configuration; packet.ThemeVariables = settings.Theme; packet.GlobalConfiguration = settings.Global;
        if (MerpeepsDoc.Split(source).Frontmatter.GetValueOrDefault("title") is string title) { packet.Title = title; packet.TitleInFrontmatter = true; }
        var identities = new DataIdentity(source, "packet");
        var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { packet.Comments.Add(line); continue; }
            if (!header) { if (!CanParse(line)) throw new FormatException("Expected packet."); packet.Directive = line; header = true; continue; }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { packet.Title = line[6..]; packet.TitleInFrontmatter = false; continue; }
            var field = Regex.Match(line, "^\\s*(?:(?<relative>\\+)\\s*(?<bits>\\d+)|(?<start>\\d+)(?:\\s*-\\s*(?<end>\\d+))?)\\s*:\\s*\"(?<label>[^\"]*)\"(?:\\s*%%.*)?$");
            if (!field.Success) throw new FormatException("Packet field syntax is start-end: \"label\" or +bits: \"label\".");
            int? start = field.Groups["relative"].Success ? null : int.Parse(field.Groups["start"].Value, CultureInfo.InvariantCulture);
            var bits = start is null ? int.Parse(field.Groups["bits"].Value, CultureInfo.InvariantCulture)
                : field.Groups["end"].Success ? checked(int.Parse(field.Groups["end"].Value, CultureInfo.InvariantCulture) - start.Value + 1) : 1;
            var label = field.Groups["label"].Value;
            packet.Fields.Add(new(identities.Next("field:" + label), label, bits, start));
        }
        if (!header) throw new FormatException("Packet directive is missing.");
        Validate(packet);
        return new() { Kind = packet.Directive, Packet = packet };
    }
    public static void Validate(PacketDocument packet)
    {
        _ = packet.Resolve();
        if (packet.Fields.Select(f => f.Id).Distinct().Count() != packet.Fields.Count) throw new ArgumentException("Packet field identities must be unique.");
        foreach (var field in packet.Fields) ChartDiagramAdapter.Quote(field.Label);
        foreach (var key in new[] { "bitsPerRow", "bitWidth", "rowHeight" })
            if (packet.Configuration[key] is { } value && ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) <= 0) throw new ArgumentException(key + " must be positive.");
        if (packet.Configuration["bitsPerRow"] is { } bits && ChartConfiguration.ParseNumber(ChartConfiguration.Raw(bits)) is var count &&
            (count != Math.Truncate(count) || count > 4096)) throw new ArgumentException("bitsPerRow must be a positive integer up to 4096.");
        if (packet.Configuration["showBits"] is { } show && (show is not JsonValue flag || !flag.TryGetValue<bool>(out _))) throw new ArgumentException("showBits must be true or false.");
    }
    public string Write(DiagramModel model) => Write(model.Packet ?? throw new ArgumentException("Packet content is missing."));
    public static string Write(PacketDocument packet)
    {
        Validate(packet);
        var output = new StringBuilder(packet.Directive).Append('\n');
        if (!packet.TitleInFrontmatter && packet.Title.Length > 0) output.Append("title ").Append(packet.Title).Append('\n');
        foreach (var item in packet.Resolve())
            output.Append(item.Field.ExplicitStart is null ? "+" + item.Field.Bits : item.Field.Bits == 1 ? item.Start.ToString() : item.Start + "-" + item.End)
                .Append(": ").Append(ChartDiagramAdapter.Quote(item.Field.Label)).Append('\n');
        foreach (var comment in packet.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var packet = model.Packet!; var before = Parse(source).Packet!;
        var result = Write(before) == Write(packet) ? source : MerpeepsDoc.ReplaceBody(source, Write(packet).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, packet.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, packet.ThemeVariables) ||
            !JsonNode.DeepEquals(before.GlobalConfiguration, packet.GlobalConfiguration) || packet.TitleInFrontmatter && before.Title != packet.Title)
            result = DataDiagramSupport.ApplyConfiguration(result, "packet", packet.Configuration, packet.ThemeVariables, packet.GlobalConfiguration, packet.TitleInFrontmatter, packet.Title);
        return result;
    }
    internal static object Identity(string source, PacketDocument packet)
    {
        var identity = new DataIdentity(source, "packet");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = packet.Fields.Select(f => identity.Entry(f.Id, "field:" + f.Label)).ToList() };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Packet fields are not graph nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Packets do not contain graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}

public sealed class KanbanDiagramAdapter : IDiagramAdapter
{
    public string Kind => "kanban";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var board = new KanbanDocument();
        var settings = DataDiagramSupport.Configuration(source, Kind);
        board.Configuration = settings.Configuration; board.ThemeVariables = settings.Theme; board.GlobalConfiguration = settings.Global;
        var identity = new DataIdentity(source, Kind);
        var header = false; int? columnIndent = null;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { board.Comments.Add(line); continue; }
            if (!header) { if (line != "kanban") throw new FormatException("Expected kanban."); header = true; continue; }
            var indent = raw.Replace("\t", "    ").TakeWhile(char.IsWhiteSpace).Count();
            columnIndent ??= indent;
            if (indent < columnIndent) throw new FormatException("Kanban item is outside its column.");
            var match = Regex.Match(line, @"^(?<head>.*?)(?:@\s*(?<meta>\{.*\}))?$");
            var head = match.Groups["head"].Value.Trim();
            var bracket = head.IndexOf('[');
            var id = bracket >= 0 ? head[..bracket].Trim() : head;
            var label = bracket >= 0 && head.EndsWith(']') ? head[(bracket + 1)..^1] : head;
            if (label.Length == 0) throw new FormatException("A Kanban column/card needs a label.");
            var sourceId = id.Length == 0 ? null : id;
            if (indent == columnIndent)
                board.Columns.Add(new(identity.Next("column:" + sourceId + "\0" + label), label, [], sourceId));
            else
            {
                if (board.Columns.Count == 0) throw new FormatException("A card needs a preceding column.");
                var metadata = match.Groups["meta"].Success ? DataDiagramSupport.Object(MerpeepsDoc.FromYaml(match.Groups["meta"].Value)) : new JsonObject();
                var card = new KanbanCard(identity.Next("card:" + sourceId + "\0" + label), label, sourceId) { Metadata = metadata };
                board.Columns[^1] = board.Columns[^1] with { Cards = board.Columns[^1].Cards.Append(card).ToArray() };
            }
        }
        if (!header) throw new FormatException("Kanban directive is missing.");
        Validate(board); return new() { Kind = Kind, Kanban = board };
    }
    public static void Validate(KanbanDocument board)
    {
        var ids = new HashSet<string>();
        var columnSourceIds = new HashSet<string>();
        foreach (var column in board.Columns)
        {
            if (!ids.Add(column.Id)) throw new ArgumentException("Kanban internal identities must be unique.");
            if (column.SourceId is { } source && !columnSourceIds.Add(source)) throw new ArgumentException("Kanban column source identities must be unique.");
            Text(column.Label);
            foreach (var card in column.Cards)
            {
                if (!ids.Add(card.Id)) throw new ArgumentException("Kanban internal card identities must be unique.");
                Text(card.Label);
                if (card.Metadata["priority"]?.GetValue<string>() is { } priority && priority is not ("Very High" or "High" or "Low" or "Very Low"))
                    throw new ArgumentException("Choose Very High, High, Low or Very Low priority.");
            }
        }
        static void Text(string value) { if (value.Length == 0 || value.IndexOfAny([']', '\r', '\n']) >= 0) throw new ArgumentException("Kanban labels must be nonempty and not contain closing brackets or line breaks."); }
    }
    public string Write(DiagramModel model) => Write(model.Kanban ?? throw new ArgumentException("Kanban content is missing."));
    public static string Write(KanbanDocument board)
    {
        Validate(board);
        var output = new StringBuilder("kanban\n");
        foreach (var column in board.Columns)
        {
            output.Append("  ").Append(column.SourceId).Append('[').Append(column.Label).Append("]\n");
            foreach (var card in column.Cards)
                output.Append("    ").Append(card.SourceId).Append('[').Append(card.Label).Append(']')
                    .Append(card.Metadata.Count > 0 ? "@" + card.Metadata.ToJsonString() : "").Append('\n');
        }
        foreach (var comment in board.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var board = model.Kanban!; var before = Parse(source).Kanban!;
        var result = Write(before) == Write(board) ? source : MerpeepsDoc.ReplaceBody(source, Write(board).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, board.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, board.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, board.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "kanban", board.Configuration, board.ThemeVariables, board.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, KanbanDocument board)
    {
        var identity = new DataIdentity(source, "kanban");
        var entries = new List<object?>();
        foreach (var column in board.Columns)
        {
            entries.Add(identity.Entry(column.Id, "column:" + column.SourceId + "\0" + column.Label));
            entries.AddRange(column.Cards.Select(c => identity.Entry(c.Id, "card:" + c.SourceId + "\0" + c.Label)));
        }
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Kanban cards are ordered board data.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Kanban has no graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
