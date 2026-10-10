using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class RequirementDiagramAdapter : IDiagramAdapter
{
    public string Kind => "requirementDiagram";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new RequirementDocument();
        var settings = DataDiagramSupport.Configuration(source, "requirement");
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var identity = new DataIdentity(source, "requirement");
        var lines = MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n');
        var definitions = new List<(string[] Names, string[] Classes, JsonObject? Style)>();
        var pendingRelations = new List<(string From, string To, RequirementRelationKind Kind)>();
        var header = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimEnd(';');
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected requirementDiagram."); header = true; continue; }
            if (line.StartsWith("direction ", StringComparison.Ordinal)) { document.Direction = Enum.Parse<Direction>(line[10..], true); continue; }
            var opening = Regex.Match(line, @"^(?<kind>requirement|functionalRequirement|interfaceRequirement|performanceRequirement|physicalRequirement|designConstraint|element)\s+(?<name>.+?)\s*\{$", RegexOptions.IgnoreCase);
            if (opening.Success)
            {
                var fullName = opening.Groups["name"].Value;
                var marker = fullName.IndexOf(":::", StringComparison.Ordinal);
                var name = Text(marker < 0 ? fullName : fullName[..marker]);
                var classes = marker < 0 ? Array.Empty<string>() : fullName[(marker + 3)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var kind = Enum.Parse<RequirementKind>(opening.Groups["kind"].Value, true);
                var item = new RequirementItem(identity.Next("item:" + name), name, kind) { Classes = classes };
                var closed = false;
                while (++i < lines.Length)
                {
                    var value = lines[i].Trim().TrimEnd(';');
                    if (value == "}") { closed = true; break; }
                    if (value.Length == 0 || value.StartsWith("%%", StringComparison.Ordinal)) continue;
                    var at = value.IndexOf(':');
                    if (at < 1) throw new FormatException("Requirement fields use property: value.");
                    var key = value[..at].Trim().ToLowerInvariant(); var text = Text(value[(at + 1)..]);
                    item = key switch
                    {
                        "id" => item with { DisplayId = text },
                        "text" => item with { Text = text },
                        "risk" => item with { Risk = Enum.Parse<RequirementRisk>(text, true) },
                        "verifymethod" => item with { Verification = Enum.Parse<RequirementVerification>(text, true) },
                        "type" => item with { ElementType = text },
                        "docref" => item with { DocumentReference = text },
                        _ => throw new FormatException("Unsupported requirement field: " + key),
                    };
                }
                if (!closed) throw new FormatException("Unclosed requirement definition.");
                document.Items.Add(item); continue;
            }
            if (line.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var at = line.IndexOf(' ', 9); if (at < 0) throw new FormatException("Class definition needs style properties.");
                document.Classes[line[9..at]] = ChartDiagramAdapter.ParseStyle(line[(at + 1)..]); continue;
            }
            if (line.StartsWith("style ", StringComparison.Ordinal) || line.StartsWith("class ", StringComparison.Ordinal))
            {
                var style = line.StartsWith("style ", StringComparison.Ordinal);
                var at = FindSpace(line, 6);
                if (at < 0) throw new FormatException("Style/class assignment requires a target and value.");
                var names = line[6..at].Split(',', StringSplitOptions.TrimEntries).Select(Text).ToArray();
                definitions.Add((names, style ? [] : line[(at + 1)..].Split(',', StringSplitOptions.TrimEntries),
                    style ? ChartDiagramAdapter.ParseStyle(line[(at + 1)..]) : null));
                continue;
            }
            var relationship = Regex.Match(line, @"^(?<a>.+?)\s*(?<left><-|-)\s*(?<kind>contains|copies|derives|satisfies|verifies|refines|traces)\s*(?<right>->|-)\s*(?<b>.+)$", RegexOptions.IgnoreCase);
            if (relationship.Success)
            {
                var reverse = relationship.Groups["left"].Value == "<-";
                var a = Text(relationship.Groups["a"].Value); var b = Text(relationship.Groups["b"].Value);
                pendingRelations.Add((reverse ? b : a, reverse ? a : b, Enum.Parse<RequirementRelationKind>(relationship.Groups["kind"].Value, true)));
                continue;
            }
            var shorthand = line.IndexOf(":::", StringComparison.Ordinal);
            if (shorthand >= 0) { definitions.Add(([Text(line[..shorthand])], line[(shorthand + 3)..].Split(',', StringSplitOptions.TrimEntries), null)); continue; }
            throw new FormatException("Unknown requirement statement: " + line);
        }
        if (!header) throw new FormatException("Requirement directive is missing.");
        foreach (var assignment in definitions)
            foreach (var name in assignment.Names)
            {
                var index = document.Items.FindIndex(n => n.Name == name);
                if (index < 0) throw new FormatException("Style targets an unknown requirement element.");
                if (assignment.Style is not null)
                {
                    var style = (JsonObject)document.Items[index].Style.DeepClone();
                    foreach (var value in assignment.Style) style[value.Key] = value.Value?.DeepClone();
                    document.Items[index] = document.Items[index] with { Style = style };
                }
                else document.Items[index] = document.Items[index] with { Classes = document.Items[index].Classes.Concat(assignment.Classes).Distinct().ToArray() };
            }
        foreach (var relation in pendingRelations)
        {
            var from = document.Items.SingleOrDefault(n => n.Name == relation.From) ?? throw new FormatException("Unknown source requirement: " + relation.From);
            var to = document.Items.SingleOrDefault(n => n.Name == relation.To) ?? throw new FormatException("Unknown destination requirement: " + relation.To);
            document.Relations.Add(new(identity.Next("relation:" + from.Name + "\0" + to.Name + "\0" + relation.Kind), from.Id, to.Id, relation.Kind));
        }
        Validate(document);
        return new() { Kind = Kind, Requirements = document };
    }
    public static void Validate(RequirementDocument document)
    {
        var names = new HashSet<string>(); var ids = new HashSet<string>();
        foreach (var item in document.Items)
        {
            if (item.Name.Length == 0 || !names.Add(item.Name) || !ids.Add(item.Id)) throw new ArgumentException("Requirement names and internal identities must be unique.");
            if (!Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Risk) || !Enum.IsDefined(item.Verification)) throw new ArgumentException("Unknown requirement kind/risk/verification method.");
            foreach (var value in new[] { item.Name, item.Text, item.DisplayId, item.ElementType, item.DocumentReference }) Quote(value);
            _ = RequirementAppearance.Resolve(document, item);
        }
        foreach (var relation in document.Relations)
        {
            if (!document.Items.Any(i => i.Id == relation.From) || !document.Items.Any(i => i.Id == relation.To) || !Enum.IsDefined(relation.Kind)) throw new ArgumentException("Relationship endpoints/type are invalid.");
            if (!ids.Add(relation.Id)) throw new ArgumentException("Relationship identities must be distinct from all elements and other relationships.");
            if (relation.From == relation.To)
                throw new DataDiagramNotEditableException([new("requirement-self-route", relation.Id, "Self-relationships are preserved in source but do not yet have a native route.", true)]);
        }
    }
    public string Write(DiagramModel model) => Write(model.Requirements ?? throw new ArgumentException("Requirement content is missing."));
    public static string Write(RequirementDocument document)
    {
        Validate(document);
        var output = new StringBuilder("requirementDiagram\n").Append("direction ").Append(document.Direction).Append('\n');
        foreach (var item in document.Items)
        {
            var kind = char.ToLowerInvariant(item.Kind.ToString()[0]) + item.Kind.ToString()[1..];
            output.Append(kind).Append(' ').Append(Quote(item.Name)).Append(" {\n");
            if (item.Kind == RequirementKind.Element)
            {
                if (item.ElementType.Length > 0) output.Append("  type: ").Append(Quote(item.ElementType)).Append('\n');
                if (item.DocumentReference.Length > 0) output.Append("  docref: ").Append(Quote(item.DocumentReference)).Append('\n');
            }
            else
            {
                if (item.DisplayId.Length > 0) output.Append("  id: ").Append(Quote(item.DisplayId)).Append('\n');
                if (item.Text.Length > 0) output.Append("  text: ").Append(Quote(item.Text)).Append('\n');
                if (item.Risk != RequirementRisk.Unspecified) output.Append("  risk: ").Append(item.Risk.ToString().ToLowerInvariant()).Append('\n');
                if (item.Verification != RequirementVerification.Unspecified) output.Append("  verifymethod: ").Append(item.Verification.ToString().ToLowerInvariant()).Append('\n');
            }
            output.Append("}\n");
        }
        foreach (var relation in document.Relations)
            output.Append(Quote(document.Items.Single(i => i.Id == relation.From).Name)).Append(" - ").Append(relation.Kind.ToString().ToLowerInvariant()).Append(" -> ")
                .Append(Quote(document.Items.Single(i => i.Id == relation.To).Name)).Append('\n');
        foreach (var style in document.Classes) output.Append("classDef ").Append(style.Key).Append(' ').Append(Style(style.Value)).Append('\n');
        foreach (var item in document.Items)
        {
            if (item.Classes.Count > 0) output.Append("class ").Append(Quote(item.Name)).Append(' ').Append(string.Join(",", item.Classes)).Append('\n');
            if (item.Style.Count > 0) output.Append("style ").Append(Quote(item.Name)).Append(' ').Append(Style(item.Style)).Append('\n');
        }
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Requirements!; var before = Parse(source).Requirements!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "requirement", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, RequirementDocument document)
    {
        var map = new DataIdentity(source, "requirement");
        var entries = document.Items.Select(i => map.Entry(i.Id, "item:" + i.Name)).ToList();
        entries.AddRange(document.Relations.Select(r => map.Entry(r.Id, "relation:" + document.Items.Single(i => i.Id == r.From).Name + "\0" + document.Items.Single(i => i.Id == r.To).Name + "\0" + r.Kind)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Text(string text) => text.Trim().Trim('"');
    private static string Quote(string text)
    {
        if (text.Contains('"')) throw new ArgumentException("Requirement text cannot contain a double quote.");
        return "\"" + DataDiagramSupport.Encode(text) + "\"";
    }
    private static string Style(JsonObject values) => string.Join(",", values.Select(p => p.Key + ":" + (p.Value is null ? "" : ChartConfiguration.Raw(p.Value))));
    private static int FindSpace(string text, int from)
    {
        var quote = false;
        for (var i = from; i < text.Length; i++) { if (text[i] == '"') quote = !quote; if (!quote && text[i] == ' ') return i; }
        return -1;
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Use typed requirement content.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use typed requirement relationships.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
