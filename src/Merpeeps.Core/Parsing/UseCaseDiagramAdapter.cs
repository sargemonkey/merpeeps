using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class UseCaseDiagramAdapter : IDiagramAdapter
{
    public string Kind => "usecase-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var d = new UseCaseDiagramDocument(); var config = DataDiagramSupport.Configuration(source, "usecase");
        d.Configuration = config.Configuration; d.ThemeVariables = config.Theme; d.GlobalConfiguration = config.Global;
        var identity = new DataIdentity(source, "usecase"); var statements = Statements(MerpeepsDoc.BodyOf(source)).ToArray();
        var header = false; string? parent = null;
        var pendingStyles = new List<(string Kind, string Names, string Value)>();
        var pendingMetadata = new List<(string Name, JsonObject Value)>();
        foreach (var line in statements)
        {
            if (line.StartsWith("%%", StringComparison.Ordinal)) { d.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected usecase-beta."); header = true; continue; }
            if (line == "end") { if (parent is null) throw new FormatException("Unmatched system boundary end."); parent = null; continue; }
            if (line.StartsWith("systemBoundary ", StringComparison.Ordinal))
            {
                if (parent is not null) throw new FormatException("Use-case boundaries are one level deep.");
                var reader = new Reader(line[15..]); var name = reader.IdentifierOrLabel(out var implicitTitle);
                var text = implicitTitle ?? reader.OptionalLabel(out _) ?? new(name);
                var metadata = reader.Metadata(); var stereotype = reader.Stereotype(); var classes = reader.Classes(); reader.End();
                if (stereotype is not null) throw new FormatException("Boundaries do not have stereotypes.");
                Only(metadata, "type");
                var type = metadata["type"]?.ToString() ?? "rect";
                if (type is not ("rect" or "package")) throw new FormatException("Boundary type must be rect or package.");
                var id = identity.Next("boundary:" + name);
                if (d.Boundaries.Any(b => b.SourceId == name)) throw new FormatException("Duplicate system boundary.");
                d.Boundaries.Add(new(id, name, text, type == "package") { Classes = classes }); parent = id; continue;
            }
            if (parent is null && line.StartsWith("direction ", StringComparison.Ordinal))
            {
                d.Direction = line[10..].Trim() switch { "LR" => Direction.LR, "RL" => Direction.RL, "BT" => Direction.BT, "TB" or "TD" => Direction.TB, _ => throw new FormatException("Invalid use-case direction.") }; continue;
            }
            if (parent is null && line.StartsWith("accTitle:", StringComparison.Ordinal)) { d.AccessibilityTitle = line[9..].Trim(); continue; }
            if (parent is null && line.StartsWith("accDescr", StringComparison.Ordinal))
            {
                var rest = line[8..].Trim(); d.AccessibilityDescription = rest.StartsWith(':') ? rest[1..].Trim() : rest.StartsWith('{') && rest.EndsWith('}') ? rest[1..^1].Trim() : throw new FormatException("Invalid accessibility description."); continue;
            }
            var styles = Regex.Match(line, @"^(?<kind>classDef|class|style)\s+(?<names>\S+)\s+(?<value>.+)$", RegexOptions.Singleline);
            if (styles.Success)
            {
                if (parent is not null) throw new FormatException("Styles belong outside system boundaries.");
                pendingStyles.Add((styles.Groups["kind"].Value, styles.Groups["names"].Value, styles.Groups["value"].Value)); continue;
            }
            if (line.StartsWith("json ", StringComparison.Ordinal))
            {
                if (parent is not null) throw new FormatException("JSON tables belong at the top level.");
                var reader = new Reader(line[5..]); var name = reader.Identifier(); var json = reader.ObjectText(); var classes = reader.Classes(); reader.End();
                var node = Ensure(name);
                Set(node with { Kind = UseCaseNodeKind.JsonTable, Declared = true, Label = new(name), Data = UseCaseJson.Parse(json), Classes = classes }); continue;
            }
            if (line.StartsWith("note for ", StringComparison.Ordinal))
            {
                if (parent is not null) throw new FormatException("Notes belong at the top level.");
                var reader = new Reader(line[9..]); var target = Ensure(reader.Identifier()); var text = reader.Text(); reader.End();
                d.Nodes.Add(new(identity.Next("note:" + target.Id + "\0" + text.Text), "", UseCaseNodeKind.Note, text) { NoteTargetId = target.Id }); continue;
            }
            var actor = line.StartsWith("actor ", StringComparison.Ordinal);
            var declaration = new Reader(actor ? line[6..] : line);
            var sourceId = declaration.IdentifierOrLabel(out var labelOnly);
            var whitespaceRemaining = declaration.Remaining.TrimStart();
            if (!actor && labelOnly is null && IsRelationship(whitespaceRemaining))
            {
                if (parent is not null) throw new FormatException("Relationships belong outside system boundaries.");
                var from = Ensure(sourceId); var edge = ParseRelationship(declaration, from.Id);
                d.Relationships.Add(edge); continue;
            }
            if (!actor && labelOnly is null && whitespaceRemaining.StartsWith("@{", StringComparison.Ordinal))
            {
                var meta = declaration.Metadata(); var stereo = declaration.Stereotype(); var classes = declaration.Classes(); declaration.End();
                if (parent is not null) throw new FormatException("Separate metadata statements belong at the top level.");
                pendingMetadata.Add((sourceId, meta));
                if (stereo is not null || classes.Count > 0)
                {
                    var original = Ensure(sourceId); Set(original with { Stereotype = stereo ?? original.Stereotype, Classes = original.Classes.Concat(classes).ToArray() });
                }
                continue;
            }
            var label = labelOnly ?? declaration.OptionalLabel(out _) ?? new UseCaseText(sourceId);
            var rectangular = labelOnly is null && declaration.LastLabelRectangle;
            var metadataForNode = declaration.Metadata(); var stereotypeForNode = declaration.Stereotype(); var classNames = declaration.Classes(); declaration.End();
            var nodeBefore = Ensure(sourceId);
            if (nodeBefore.Declared && nodeBefore.Kind != (actor ? UseCaseNodeKind.Actor : UseCaseNodeKind.UseCase)) throw new FormatException("Conflicting use-case element declarations.");
            if (nodeBefore.BoundaryId is not null && parent is not null && nodeBefore.BoundaryId != parent) throw new FormatException("An element cannot belong to two boundaries.");
            var nodeAfter = nodeBefore with
            {
                Declared = true, Kind = actor ? UseCaseNodeKind.Actor : UseCaseNodeKind.UseCase, Label = label,
                Shape = rectangular ? UseCaseShape.Rectangle : UseCaseShape.Ellipse, BoundaryId = parent ?? nodeBefore.BoundaryId,
                Stereotype = stereotypeForNode, Classes = nodeBefore.Classes.Concat(classNames).ToArray(),
            };
            nodeAfter = ApplyMetadata(nodeAfter, metadataForNode);
            Set(nodeAfter);
        }
        if (!header || parent is not null) throw new FormatException("Missing use-case header or unclosed system boundary.");
        foreach (var (name, metadata) in pendingMetadata)
        {
            var b = d.Boundaries.FindIndex(x => x.SourceId == name); var e = d.Relationships.FindIndex(x => x.SourceId == name);
            if (b >= 0)
            {
                Only(metadata, "type"); var kind = metadata["type"]?.ToString() ?? "rect";
                if (kind is not ("rect" or "package")) throw new FormatException("Unknown boundary type.");
                d.Boundaries[b] = d.Boundaries[b] with { Package = kind == "package" };
            }
            else if (e >= 0)
            {
                Only(metadata, "animate", "animation");
                var speed = metadata["animation"]?.ToString();
                if (speed is not (null or "slow" or "fast")) throw new FormatException("Unknown relationship animation speed.");
                var animated = metadata["animate"]?.GetValue<bool>() ?? speed is not null;
                d.Relationships[e] = d.Relationships[e] with { Animated = animated, AnimationSpeed = speed };
            }
            else Set(ApplyMetadata(d.Nodes.FirstOrDefault(n => n.SourceId == name) ?? throw new FormatException("Unknown metadata target: " + name), metadata));
        }
        foreach (var (kind, names, value) in pendingStyles)
        {
            if (value.Contains(';')) throw new FormatException("Use-case styles reject semicolons.");
            if (kind == "classDef") { foreach (var name in names.Split(',')) d.Classes[name] = SwimlaneDiagramAdapter.Css(value); continue; }
            foreach (var name in names.Split(','))
            {
                var n = d.Nodes.FindIndex(x => x.SourceId == name); var b = d.Boundaries.FindIndex(x => x.SourceId == name); var e = d.Relationships.FindIndex(x => x.SourceId == name);
                if (n < 0 && b < 0 && e < 0) throw new FormatException("Unknown style target: " + name);
                var classes = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (kind == "class")
                {
                    if (n >= 0) d.Nodes[n] = d.Nodes[n] with { Classes = d.Nodes[n].Classes.Concat(classes).ToArray() };
                    if (b >= 0) d.Boundaries[b] = d.Boundaries[b] with { Classes = d.Boundaries[b].Classes.Concat(classes).ToArray() };
                    if (e >= 0) d.Relationships[e] = d.Relationships[e] with { Classes = d.Relationships[e].Classes.Concat(classes).ToArray() };
                }
                else
                {
                    var style = SwimlaneDiagramAdapter.Css(value);
                    if (n >= 0) d.Nodes[n] = d.Nodes[n] with { Style = Merge(d.Nodes[n].Style, style) };
                    if (b >= 0) d.Boundaries[b] = d.Boundaries[b] with { Style = Merge(d.Boundaries[b].Style, style) };
                    if (e >= 0) d.Relationships[e] = d.Relationships[e] with { Style = Merge(d.Relationships[e].Style, style) };
                }
            }
        }
        UseCaseValidation.Check(d); return new() { Kind = Kind, UseCase = d };
        UseCaseNode Ensure(string name)
        {
            var old = d.Nodes.FirstOrDefault(n => n.SourceId == name);
            if (old is not null) return old;
            var node = new UseCaseNode(identity.Next("node:" + name), name, UseCaseNodeKind.UseCase, new(name)) { Declared = false };
            d.Nodes.Add(node); return node;
        }
        void Set(UseCaseNode node) => d.Nodes[d.Nodes.FindIndex(n => n.Id == node.Id)] = node;
        UseCaseRelationship ParseRelationship(Reader reader, string from)
        {
            var text = reader.Remaining.Trim(); string? edgeId = null;
            var named = Regex.Match(text, @"^(?<id>[A-Za-z0-9_]+)@(?=[-.ox<])");
            if (named.Success) { edgeId = named.Groups["id"].Value; text = text[named.Length..]; }
            var match = Regex.Match(text, @"^(?<op>--\|>|\.\.>|<--+|o--|x--|--+[>ox]?)");
            if (!match.Success) throw new FormatException("Invalid use-case relationship operator.");
            var op = match.Groups["op"].Value; var rest = text[match.Length..].Trim();
            var kind = UseCaseRelationshipKind.Association; var label = new UseCaseText("");
            if (op == "..>")
            {
                var semantic = Regex.Match(rest, @"^:\s*(?<kind>include|extend)\s+(?<to>[A-Za-z0-9_]+)$");
                if (!semantic.Success) throw new FormatException("Dashed UML relations require ': include Target' or ': extend Target'.");
                kind = semantic.Groups["kind"].Value == "include" ? UseCaseRelationshipKind.Include : UseCaseRelationshipKind.Extend;
                rest = semantic.Groups["to"].Value;
            }
            else if (op == "--|>") kind = UseCaseRelationshipKind.Generalization;
            else if (op == "--")
            {
                var labeled = SplitLabeledRelation(rest);
                if (labeled is { } item) { label = item.Label; op = item.Operator; rest = item.Target; }
            }
            if (!Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$")) throw new FormatException("Use-case relationships need one target and one physical statement per line.");
            var to = Ensure(rest);
            var fromEnd = op.StartsWith('<') ? UseCaseEnd.Arrow : op.StartsWith('o') ? UseCaseEnd.Circle : op.StartsWith('x') ? UseCaseEnd.Cross : UseCaseEnd.None;
            var toEnd = kind == UseCaseRelationshipKind.Generalization ? UseCaseEnd.Triangle : op.EndsWith('o') ? UseCaseEnd.Circle : op.EndsWith('x') ? UseCaseEnd.Cross : op.EndsWith('>') ? UseCaseEnd.Arrow : UseCaseEnd.None;
            var minlen = kind == UseCaseRelationshipKind.Association ? Math.Max(1, op.Count(c => c == '-') - 1) : 1;
            return new(identity.Next(edgeId is null ? "edge:" + from + "\0" + to.Id + "\0" + kind + "\0" + label.Text : "edge-id:" + edgeId), from, to.Id)
            { SourceId = edgeId, Kind = kind, FromEnd = fromEnd, ToEnd = toEnd, MinimumLength = minlen, Label = label };
        }
    }
    private static bool IsRelationship(string text) => Regex.IsMatch(text, @"^(?:[A-Za-z0-9_]+@)?(?:--|\.\.>|<--|o--|x--)");
    private static (UseCaseText Label, string Operator, string Target)? SplitLabeledRelation(string text)
    {
        var quoted = false; var markdown = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') { quoted = !quoted; if (quoted && i + 1 < text.Length && text[i + 1] == '`') markdown = true; else if (!quoted) markdown = false; }
            if (quoted || markdown) continue;
            var op = Regex.Match(text[i..], @"^(?<op>--+[>ox]?|<--+|o--|x--)\s*(?<target>[A-Za-z0-9_]+)$");
            if (op.Success) return (Reader.Label(text[..i].Trim()), op.Groups["op"].Value, op.Groups["target"].Value);
        }
        return null;
    }
    private static UseCaseNode ApplyMetadata(UseCaseNode node, JsonObject metadata)
    {
        Only(metadata, node.Kind == UseCaseNodeKind.Actor ? ["type", "icon", "business"] : ["business"]);
        var actor = node.ActorKind; var icon = metadata["icon"]?.ToString() ?? node.Icon;
        if (metadata["type"] is { } type) actor = type.ToString() switch
        { "normal" => UseCaseActorKind.Normal, "hollow" => UseCaseActorKind.Hollow, "awesome" => UseCaseActorKind.Awesome, _ => throw new FormatException("Unknown actor variant.") };
        if (icon is not null)
        {
            if (actor is not (UseCaseActorKind.Normal or UseCaseActorKind.Icon)) throw new FormatException("An icon cannot be combined with a non-normal actor type.");
            actor = UseCaseActorKind.Icon;
        }
        return node with { ActorKind = actor, Icon = icon, Business = metadata["business"]?.GetValue<bool>() ?? node.Business };
    }
    private static void Only(JsonObject metadata, params string[] keys)
    { if (metadata.Any(p => !keys.Contains(p.Key))) throw new FormatException("Unsupported typed metadata key."); }
    private static JsonObject Merge(JsonObject before, JsonObject after)
    { var result = (JsonObject)before.DeepClone(); foreach (var pair in after) result[pair.Key] = pair.Value?.DeepClone(); return result; }
    internal static IReadOnlyList<string> Statements(string body)
    {
        var lines = new List<string>(); var statement = new StringBuilder(); var quoted = false; var braces = 0;
        foreach (var c in body.Replace("\r\n", "\n"))
        {
            if (c == '"') quoted = !quoted;
            if (!quoted) { if (c == '{') braces++; if (c == '}') braces--; }
            if (c == '\n' && !quoted && braces == 0)
            {
                if (statement.ToString().Trim() is { Length: > 0 } line) lines.Add(line); statement.Clear();
            }
            else statement.Append(c);
        }
        if (quoted || braces != 0) throw new FormatException("Unclosed string or metadata/JSON block.");
        if (statement.ToString().Trim() is { Length: > 0 } final) lines.Add(final);
        return lines;
    }
    internal sealed class Reader(string source)
    {
        private int _at;
        public bool LastLabelRectangle { get; private set; }
        public string Remaining => source[_at..];
        private void Space() { while (_at < source.Length && char.IsWhiteSpace(source[_at])) _at++; }
        public string Identifier()
        {
            Space(); var match = Regex.Match(source[_at..], @"^[A-Za-z0-9_]+");
            if (!match.Success) throw new FormatException("Expected a use-case identifier.");
            _at += match.Length; return match.Value;
        }
        public string IdentifierOrLabel(out UseCaseText? label)
        {
            Space(); label = null;
            if (_at < source.Length && source[_at] == '"') { label = Text(); return Regex.Replace(label.Text, @"\W", "_"); }
            return Identifier();
        }
        public UseCaseText Text()
        {
            Space();
            if (_at >= source.Length || source[_at] != '"') throw new FormatException("Expected quoted use-case text.");
            var start = _at++; var end = source.IndexOf('"', _at);
            if (end < 0) throw new FormatException("Unclosed use-case label.");
            _at = end + 1; return Label(source[start.._at]);
        }
        public UseCaseText? OptionalLabel(out UseCaseShape shape)
        {
            Space(); shape = UseCaseShape.Ellipse; LastLabelRectangle = false;
            if (_at >= source.Length || source[_at] is not ('(' or '[')) return null;
            var close = source[_at] == '(' ? ')' : ']'; LastLabelRectangle = close == ']'; shape = LastLabelRectangle ? UseCaseShape.Rectangle : UseCaseShape.Ellipse;
            _at++; Space();
            if (_at < source.Length && source[_at] == '"')
            {
                var value = Text(); Space(); Take(close); return value;
            }
            var end = source.IndexOf(close, _at); if (end < 0) throw new FormatException("Unclosed element label.");
            var text = source[_at..end]; _at = end + 1; return Label(text);
        }
        public string ObjectText()
        {
            Space(); if (!source.AsSpan(_at).StartsWith("@{")) throw new FormatException("Expected @{...}.");
            _at++; var start = _at; var braces = 0; var quoted = false; var escaped = false;
            for (; _at < source.Length; _at++)
            {
                var c = source[_at]; if (c == '"' && !escaped) quoted = !quoted;
                if (!quoted) { if (c == '{') braces++; if (c == '}' && --braces == 0) { _at++; return source[start.._at]; } }
                escaped = c == '\\' && !escaped;
            }
            throw new FormatException("Unclosed object metadata.");
        }
        public JsonObject Metadata()
        {
            Space(); if (!source.AsSpan(_at).StartsWith("@{")) return new();
            var text = ObjectText();
            var body = text[1..^1]; var offset = 0; var result = new JsonObject();
            while (offset < body.Length)
            {
                while (offset < body.Length && char.IsWhiteSpace(body[offset])) offset++;
                if (offset == body.Length) break;
                var key = Regex.Match(body[offset..], @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*");
                if (!key.Success) throw new FormatException("Expected a typed metadata key followed by ':'.");
                offset += key.Length;
                if (offset < body.Length && body[offset] is '"' or '\'')
                {
                    var quote = body[offset++]; var end = body.IndexOf(quote, offset);
                    if (end < 0) throw new FormatException("Unclosed metadata string.");
                    result[key.Groups["name"].Value] = body[offset..end]; offset = end + 1;
                }
                else
                {
                    var value = Regex.Match(body[offset..], @"^[A-Za-z_][A-Za-z0-9_-]*");
                    if (!value.Success) throw new FormatException("Expected a typed metadata string or boolean.");
                    result[key.Groups["name"].Value] = value.Value switch
                    {
                        "true" => JsonValue.Create(true), "false" => JsonValue.Create(false), _ => JsonValue.Create(value.Value),
                    };
                    offset += value.Length;
                }
                while (offset < body.Length && char.IsWhiteSpace(body[offset])) offset++;
                if (offset < body.Length && body[offset++] != ',') throw new FormatException("Metadata properties must be comma-separated.");
            }
            return result;
        }
        public string? Stereotype()
        {
            Space(); if (!source.AsSpan(_at).StartsWith("<<")) return null;
            var end = source.IndexOf(">>", _at + 2, StringComparison.Ordinal); if (end < 0) throw new FormatException("Unclosed stereotype.");
            var value = source[(_at + 2)..end]; _at = end + 2; return value;
        }
        public IReadOnlyList<string> Classes()
        {
            Space(); if (!source.AsSpan(_at).StartsWith(":::")) return [];
            _at += 3; var text = source[_at..].Trim(); _at = source.Length;
            if (!Regex.IsMatch(text, @"^[A-Za-z0-9_,]+$")) throw new FormatException("Invalid use-case class suffix.");
            return text.Split(',');
        }
        public void End() { Space(); if (_at != source.Length) throw new FormatException("Extra tokens after use-case statement."); }
        private void Take(char c) { if (_at >= source.Length || source[_at++] != c) throw new FormatException("Expected label delimiter."); }
        public static UseCaseText Label(string value)
        {
            value = value.Trim(); var markdown = value.StartsWith("\"`", StringComparison.Ordinal) && value.EndsWith("`\"", StringComparison.Ordinal);
            var text = markdown ? value[2..^2] : value.StartsWith('"') && value.EndsWith('"') ? value[1..^1] : value;
            return new(SequenceText.Decode(text), markdown);
        }
    }
    public string Write(DiagramModel model) => Write(model.UseCase ?? throw new ArgumentException("Use-case content is missing."));
    public static string Write(UseCaseDiagramDocument d)
    {
        UseCaseValidation.Check(d); var output = new StringBuilder("usecase-beta\n").Append("direction ").Append(d.Direction).Append('\n');
        if (d.AccessibilityTitle.Length > 0) output.Append("accTitle: ").Append(d.AccessibilityTitle).Append('\n');
        if (d.AccessibilityDescription.Length > 0) output.Append("accDescr {\n").Append(d.AccessibilityDescription).Append("\n}\n");
        foreach (var node in d.Nodes.Where(n => n.BoundaryId is null && n.Kind != UseCaseNodeKind.Note)) Node(node, false);
        foreach (var boundary in d.Boundaries)
        {
            output.Append("systemBoundary ").Append(boundary.SourceId).Append('[').Append(Text(boundary.Title)).Append(']');
            if (boundary.Package) output.Append("@{ type: package }");
            Classes(boundary.Classes); output.Append('\n');
            foreach (var node in d.Nodes.Where(n => n.BoundaryId == boundary.Id)) Node(node, true);
            output.Append("end\n");
        }
        foreach (var note in d.Nodes.Where(n => n.Kind == UseCaseNodeKind.Note))
            output.Append("note for ").Append(d.Nodes.Single(n => n.Id == note.NoteTargetId).SourceId).Append(' ').Append(Text(note.Label)).Append('\n');
        foreach (var edge in d.Relationships)
        {
            output.Append(d.Nodes.Single(n => n.Id == edge.From).SourceId).Append(' ');
            if (edge.SourceId is { } name) output.Append(name).Append('@');
            if (edge.Kind is UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend) output.Append("..> : ").Append(edge.Kind.ToString().ToLowerInvariant()).Append(' ');
            else if (edge.Kind == UseCaseRelationshipKind.Generalization) output.Append("--|> ");
            else
            {
                var op = new string('-', edge.MinimumLength + 1);
                op = edge.FromEnd switch { UseCaseEnd.Arrow => "<" + op, UseCaseEnd.Circle => "o" + op, UseCaseEnd.Cross => "x" + op, _ => op };
                op += edge.ToEnd switch { UseCaseEnd.Arrow => ">", UseCaseEnd.Circle => "o", UseCaseEnd.Cross => "x", _ => "" };
                if (edge.Label.Text.Length > 0) output.Append("-- ").Append(Text(edge.Label)).Append(' ');
                output.Append(op).Append(' ');
            }
            output.Append(d.Nodes.Single(n => n.Id == edge.To).SourceId).Append('\n');
            if (edge.SourceId is not null && (edge.Animated || edge.AnimationSpeed is not null))
                output.Append(edge.SourceId).Append("@{ animate: ").Append(edge.Animated ? "true" : "false").Append(edge.AnimationSpeed is null ? "" : ", animation: " + edge.AnimationSpeed).Append(" }\n");
        }
        foreach (var pair in d.Classes) output.Append("classDef ").Append(pair.Key).Append(' ').Append(Css(pair.Value)).Append('\n');
        foreach (var n in d.Nodes.Where(n => n.Kind != UseCaseNodeKind.Note)) Appearance(n.SourceId, [], n.Style);
        foreach (var boundary in d.Boundaries) Appearance(boundary.SourceId, [], boundary.Style);
        foreach (var edge in d.Relationships.Where(e => e.SourceId is not null)) Appearance(edge.SourceId!, edge.Classes, edge.Style);
        foreach (var comment in d.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Node(UseCaseNode node, bool indent)
        {
            if (indent) output.Append("  ");
            if (node.Kind == UseCaseNodeKind.JsonTable) { output.Append("json ").Append(node.SourceId).Append('@').Append(node.Data!.ToJsonString()); Classes(node.Classes); output.Append('\n'); return; }
            if (node.Kind == UseCaseNodeKind.Actor) output.Append("actor ");
            output.Append(node.SourceId).Append(node.Shape == UseCaseShape.Rectangle && node.Kind == UseCaseNodeKind.UseCase ? "[" : "(").Append(Text(node.Label))
                .Append(node.Shape == UseCaseShape.Rectangle && node.Kind == UseCaseNodeKind.UseCase ? "]" : ")");
            var properties = new List<string>();
            if (node.Kind == UseCaseNodeKind.Actor)
            {
                if (node.ActorKind is UseCaseActorKind.Hollow or UseCaseActorKind.Awesome) properties.Add("type: " + node.ActorKind.ToString().ToLowerInvariant());
                if (node.Icon is not null) properties.Add("icon: \"" + node.Icon.Replace("\"", "") + "\"");
            }
            if (node.Business) properties.Add("business: true");
            if (properties.Count > 0) output.Append("@{ ").Append(string.Join(", ", properties)).Append(" }");
            if (node.Stereotype is not null) output.Append(" <<").Append(node.Stereotype).Append(">>");
            Classes(node.Classes); output.Append('\n');
        }
        void Classes(IReadOnlyList<string> values) { if (values.Count > 0) output.Append(":::").Append(string.Join(",", values)); }
        void Appearance(string name, IReadOnlyList<string> classes, JsonObject style)
        {
            if (classes.Count > 0) output.Append("class ").Append(name).Append(' ').Append(string.Join(",", classes)).Append('\n');
            if (style.Count > 0) output.Append("style ").Append(name).Append(' ').Append(Css(style)).Append('\n');
        }
    }
    private static string Text(UseCaseText text)
    {
        var encoded = text.Text.Replace("#", "#35;").Replace("\"", "#quot;");
        return text.Markdown ? "\"`" + encoded.Replace("`", "#96;") + "`\"" : "\"" + encoded + "\"";
    }
    private static string Css(JsonObject style) => string.Join(",", style.Select(p => p.Key + ":" + p.Value?.ToString().Replace(",", "\\,")));
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.UseCase!; var before = Parse(source).UseCase!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, d.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "usecase", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, UseCaseDiagramDocument d)
    {
        var identity = new DataIdentity(source, "usecase");
        var entries = d.Nodes.Select(n => identity.Entry(n.Id, n.Kind == UseCaseNodeKind.Note ? "note:" + n.NoteTargetId + "\0" + n.Label.Text : "node:" + n.SourceId)).ToList();
        entries.AddRange(d.Boundaries.Select(b => identity.Entry(b.Id, "boundary:" + b.SourceId)));
        entries.AddRange(d.Relationships.Select(r => identity.Entry(r.Id, r.SourceId is null ? "edge:" + r.From + "\0" + r.To + "\0" + r.Kind + "\0" + r.Label.Text : "edge-id:" + r.SourceId)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Use-case nodes require typed actor/use-case/table semantics.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use-case UML relations require typed endpoints and markers.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
