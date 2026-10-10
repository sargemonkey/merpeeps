using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class EntityDiagramAdapter : IDiagramAdapter
{
    private const string Identifier = "(?:\"[^\"\\r\\n]*\"|[^\\s{}\\[\\]:;,|]+)";
    private const string StyledIdentifier = Identifier + @"(?:\s*\[[^\]\r\n]*\])?(?:[:]{3}[^\s{}:]+)?";
    private const string Cardinality = @"(?:\|\||\|o|o\||}o|o\{|}\||\|\{|one\s+or\s+zero|zero\s+or\s+one|one\s+or\s+more|one\s+or\s+many|zero\s+or\s+more|zero\s+or\s+many|many\([01]\)|[01]\+|only\s+one|1)";
    private static readonly Regex Relationship = new(
        @"^(?<from>" + StyledIdentifier + @")\s*(?<fc>" + Cardinality + @")\s*(?<line>--|\.\.|optionally\s+to|to)\s*(?<tc>" +
        Cardinality + @")\s*(?<to>" + StyledIdentifier + @")\s*:\s*(?<role>.*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex EntityName = new(
        @"^(?<id>" + Identifier + @")(?<alias>\s*\[[^\]\r\n]*\])?(?:[:]{3}(?<classes>[^\s{}:]+))?$");
    private static readonly Regex Attribute = new(
        @"(?<type>[\p{L}_][\p{L}\p{N}_\-\[\]()]*(?:\?)?)\s+(?<name>\*?[\p{L}_][\p{L}\p{N}_\-\[\]()]*(?:\?)?)" +
        @"(?:\s+(?<keys>(?:PK|FK|UK)(?:\s*,\s*(?:PK|FK|UK))*))?(?:\s+""(?<comment>[^""]*)"")?");

    public string Kind => "erDiagram";
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;

    public DiagramModel Parse(string source) => Read(source).Model;

    private Document Read(string source)
    {
        var body = MerpeepsDoc.Split(source).Body;
        var model = new DiagramModel { Kind = Kind, EntityOptions = new() };
        var document = new Document(model, body);
        var metadata = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
        var identities = (metadata?.GetValueOrDefault("identity") as Dictionary<string, object?>)?.GetValueOrDefault("erDiagram") as Dictionary<string, object?>;
        if (identities?.GetValueOrDefault("spec") is { } version && version is not (1 or 1L))
            throw new FormatException("Unsupported entity identity metadata version.");
        var statements = Statements(body);
        var groups = new Stack<DiagramSubgraph>();
        string? inEntity = null;
        var header = false;
        var declarations = statements.Where(s => s.Text.StartsWith("subgraph ", StringComparison.Ordinal))
            .Select(s => ReadGroup(s.Text[9..]).Id).ToHashSet(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            var text = statement.Text;
            if (text.StartsWith("%%", StringComparison.Ordinal))
            {
                document.Entries.Add(new(statement, "comment", ""));
                continue;
            }
            if (!header)
            {
                if (text != Kind) throw Error(statement, "Expected erDiagram.");
                header = true;
                document.Entries.Add(new(statement, "header", ""));
                continue;
            }
            if (inEntity is { } owner)
            {
                var closes = text.EndsWith('}');
                var attributes = closes ? text[..^1].TrimEnd() : text;
                ParseAttributes(attributes, statement, model.Node(owner)!, identities, document);
                if (closes) { inEntity = null; document.Entries.Add(new(statement, "close", owner)); }
                continue;
            }
            if (text == "end")
            {
                if (groups.Count == 0) throw Error(statement, "Unexpected end outside a subgraph.");
                document.Entries.Add(new(statement, "end", groups.Pop().Id));
                continue;
            }
            if (text.StartsWith("subgraph ", StringComparison.Ordinal))
            {
                var definition = ReadGroup(text[9..]);
                if (model.Subgraphs.Any(g => g.Id == definition.Id)) throw Error(statement, $"Duplicate subgraph '{definition.Id}'.");
                var group = new DiagramSubgraph { Id = definition.Id, Title = definition.Title, ParentId = groups.TryPeek(out var parent) ? parent.Id : null };
                model.Subgraphs.Add(group); groups.Push(group);
                document.Entries.Add(new(statement, "group", group.Id));
                continue;
            }
            if (text.StartsWith("direction ", StringComparison.Ordinal))
            {
                var direction = text[10..].Trim() switch
                {
                    "LR" => Direction.LR,
                    "RL" => Direction.RL,
                    "BT" => Direction.BT,
                    "TB" or "TD" => Direction.TB,
                    _ => throw Error(statement, "Expected TB, BT, LR or RL."),
                };
                if (groups.TryPeek(out var group)) group.Direction = direction;
                else model.Direction = direction;
                document.Entries.Add(new(statement, "direction", groups.TryPeek(out group) ? group.Id : ""));
                continue;
            }
            if (text.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var (names, style) = SplitFirst(text[9..], statement);
                foreach (var name in names.Split(',', StringSplitOptions.TrimEntries))
                    model.EntityOptions.StyleDefinitions[name] = ParseStyle(style);
                document.Entries.Add(new(statement, "style-definition", names));
                continue;
            }
            if (text.StartsWith("style ", StringComparison.Ordinal))
            {
                var (names, style) = SplitFirst(text[6..], statement);
                foreach (var name in SplitNames(names))
                {
                    if (declarations.Contains(name)) throw Error(statement, "Native entity styles apply to entities, not subgraph frames.");
                    foreach (var pair in ParseStyle(style)) Ensure(model, name).Entity!.InlineStyle[pair.Key] = pair.Value;
                }
                document.Entries.Add(new(statement, "style", names));
                continue;
            }
            if (text.StartsWith("class ", StringComparison.Ordinal))
            {
                var (names, styles) = SplitFirst(text[6..], statement);
                foreach (var name in SplitNames(names))
                    Ensure(model, name).Entity!.StyleClasses.AddRange(styles.Split(',', StringSplitOptions.TrimEntries));
                document.Entries.Add(new(statement, "style-assignment", names));
                continue;
            }
            var relationship = Relationship.Match(text);
            if (relationship.Success)
            {
                var from = Declare(relationship.Groups["from"].Value, statement);
                var to = Declare(relationship.Groups["to"].Value, statement);
                var role = ReadText(relationship.Groups["role"].Value);
                var edge = new DiagramEdge
                {
                    From = from,
                    To = to,
                    Label = role,
                    Kind = relationship.Groups["line"].Value is ".." or "optionally to" ? EdgeKind.Dotted : EdgeKind.Open,
                    EntityRelationship = new(ParseCardinality(relationship.Groups["fc"].Value),
                        ParseCardinality(relationship.Groups["tc"].Value), relationship.Groups["line"].Value is "--" or "to"),
                    Ordinal = model.Edges.Where(e => e.From == from && e.To == to).Select(e => e.Ordinal + 1).DefaultIfEmpty(0).Max(),
                };
                model.Edges.Add(edge);
                document.Entries.Add(new(statement, "relationship", edge.Key));
                continue;
            }
            var brace = IndexOutsideQuotes(text, '{');
            var declaration = brace < 0 ? text : text[..brace].TrimEnd();
            var entityId = Declare(declaration, statement);
            if (declarations.Contains(entityId) && brace >= 0)
                throw Error(statement, "A subgraph cannot also be an entity attribute block.");
            document.Entries.Add(new(statement, "entity", entityId));
            if (brace >= 0)
            {
                var attributes = text[(brace + 1)..].Trim();
                var closes = attributes.EndsWith('}');
                if (closes) attributes = attributes[..^1].TrimEnd();
                var offset = brace + 1 + text[(brace + 1)..].Length - text[(brace + 1)..].TrimStart().Length;
                ParseAttributes(attributes, statement, model.Node(entityId)!, identities, document, offset);
                if (!closes) inEntity = entityId;
            }
        }
        if (!header || inEntity is not null || groups.Count > 0) throw new FormatException("An ER diagram directive or block is incomplete.");
        ResolveStyles(model);
        foreach (var node in model.Nodes)
        {
            node.Width = Math.Max(240, node.Entity!.Attributes.Select(a => a.Type.Length + a.Name.Length + a.KeyText.Length + (a.Comment?.Length ?? 0))
                .Append(node.Display.Length).Max() * 7 + 48);
            node.Height = 38 + Math.Max(1, node.Entity.Attributes.Count) * 26;
        }
        model.LineCount = body.Split('\n').Length;
        return document;

        string Declare(string value, Statement statement)
        {
            var match = EntityName.Match(value.Trim());
            if (!match.Success) throw Error(statement, $"Cannot interpret entity declaration '{value}'.");
            var id = ReadText(match.Groups["id"].Value);
            if (declarations.Contains(id)) return id;
            var node = Ensure(model, id);
            if (match.Groups["alias"].Success) node.Label = ReadText(match.Groups["alias"].Value.Trim()[1..^1].Trim());
            if (match.Groups["classes"].Success)
                node.Entity!.StyleClasses.AddRange(match.Groups["classes"].Value.Split(',', StringSplitOptions.TrimEntries));
            if (groups.TryPeek(out var scope))
            {
                foreach (var group in model.Subgraphs) group.NodeIds.Remove(node.Id);
                node.SubgraphId = scope.Id;
                if (!scope.NodeIds.Contains(node.Id)) scope.NodeIds.Add(node.Id);
            }
            return id;
        }
    }

    private static void ParseAttributes(string text, Statement statement, DiagramNode node,
        Dictionary<string, object?>? identity, Document document, int offset = 0)
    {
        var cursor = 0;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length) break;
            var match = Attribute.Match(text, cursor);
            if (!match.Success || match.Index != cursor) throw Error(statement, "Expected an attribute type, name, optional PK/FK/UK and quoted comment.");
            var keys = EntityKey.None;
            foreach (var key in match.Groups["keys"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                keys |= key switch { "PK" => EntityKey.Primary, "FK" => EntityKey.Foreign, "UK" => EntityKey.Unique, _ => throw Error(statement, "Unknown key.") };
            var parsed = new EntityAttribute("attribute", match.Groups["type"].Value, match.Groups["name"].Value, keys,
                match.Groups["comment"].Success ? match.Groups["comment"].Value : null);
            var fingerprint = Fingerprint(parsed.Declaration);
            var prior = IdentityEntries(identity, node.Id).Where(e => e.GetValueOrDefault("fingerprint") as string == fingerprint).ToArray();
            var index = node.Entity!.Attributes.Count(a => a.Declaration == parsed.Declaration);
            var id = index < prior.Length && prior[index].GetValueOrDefault("id") is string savedId
                ? savedId : "__attribute_" + Fingerprint(node.Id + "\0" + parsed.Declaration) + "_" + index;
            if (node.Entity.Attributes.Any(a => a.Id == id)) throw Error(statement, "Duplicate attribute identity metadata.");
            node.Entity.Attributes.Add(new(id, parsed.Type, parsed.Name, parsed.Keys, parsed.Comment));
            document.Entries.Add(new(new(match.Value, statement.Start + offset + match.Index, match.Length), "attribute", node.Id, id));
            cursor += match.Length;
        }
    }

    public string Write(DiagramModel model)
    {
        if (model.EntityOptions is null) throw new ArgumentException("ER options are missing.");
        var ids = model.Nodes.Select(n => n.Id).Concat(model.Subgraphs.Select(g => g.Id)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("Entity and subgraph identities must be unique.");
        if (model.Nodes.Any(n => n.Entity is null)) throw new ArgumentException("An ER document cannot contain untyped graph nodes.");
        foreach (var edge in model.Edges)
        {
            if (edge.EntityRelationship is null || !ids.Contains(edge.From) || !ids.Contains(edge.To))
                throw new ArgumentException("An ER relationship requires two existing typed endpoints.");
        }
        var builder = new StringBuilder("erDiagram\n");
        if (model.Direction != Direction.TB) builder.Append("direction ").Append(model.Direction).Append('\n');
        foreach (var group in model.Subgraphs.Where(g => g.ParentId is null)) WriteGroup(group, "");
        foreach (var node in model.Nodes.Where(n => n.SubgraphId is null)) builder.Append(RenderNodeStatement(node)).Append('\n');
        foreach (var edge in model.Edges) builder.Append(RenderEdgeStatement(edge)).Append('\n');
        foreach (var style in model.EntityOptions.StyleDefinitions)
            builder.Append("classDef ").Append(style.Key).Append(' ').Append(StyleText(style.Value)).Append('\n');
        foreach (var node in model.Nodes)
            if (node.Entity!.InlineStyle.Count > 0)
                builder.Append("style ").Append(IdentifierText(node.Id)).Append(' ').Append(StyleText(node.Entity.InlineStyle)).Append('\n');
        return builder.ToString();

        void WriteGroup(DiagramSubgraph group, string indent)
        {
            builder.Append(indent).Append("subgraph ").Append(IdentifierText(group.Id));
            if (group.Title is { } title && title != group.Id) builder.Append(" [").Append(Quoted(title)).Append(']');
            builder.Append('\n');
            if (group.Direction is { } direction) builder.Append(indent).Append("  direction ").Append(direction).Append('\n');
            foreach (var node in model.Nodes.Where(n => n.SubgraphId == group.Id))
                builder.Append(indent).Append("  ").Append(RenderNodeStatement(node)!.Replace("\n", "\n" + indent + "  ")).Append('\n');
            foreach (var child in model.Subgraphs.Where(g => g.ParentId == group.Id)) WriteGroup(child, indent + "  ");
            builder.Append(indent).Append("end\n");
        }
    }

    public string RenderNodeStatement(DiagramNode node)
    {
        var entity = node.Entity ?? throw new ArgumentException("Entity content is missing.");
        var declaration = EntityHeader(node);
        return entity.Attributes.Count == 0 ? declaration :
            declaration + " {\n" + string.Join("\n", entity.Attributes.Select(a => "  " + a.Declaration)) + "\n}";
    }

    private static string EntityHeader(DiagramNode node)
    {
        var entity = node.Entity!;
        var declaration = IdentifierText(node.Id);
        if (node.Label is { } label && label != node.Id) declaration += "[" + Quoted(label) + "]";
        if (entity.StyleClasses.Count > 0) declaration += ":::" + string.Join(",", entity.StyleClasses.Distinct());
        return declaration;
    }

    public string RenderEdgeStatement(DiagramEdge edge)
    {
        var relation = edge.EntityRelationship ?? throw new ArgumentException("ER cardinalities are missing.");
        return IdentifierText(edge.From) + " " + CardinalityText(relation.FromCardinality, true) +
            (relation.Identifying ? "--" : "..") + CardinalityText(relation.ToCardinality, false) +
            " " + IdentifierText(edge.To) + " : " + Quoted(edge.Label ?? "");
    }
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    internal string Apply(string source, DiagramModel model)
    {
        var original = Read(source);
        if (Write(original.Model) == Write(model)) return source;
        var newline = original.Body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (SameStructure(original.Model, model))
        {
            var replacements = new List<(Statement Statement, string Text)>();
            var appended = new List<string>();
            foreach (var before in original.Model.Edges)
            {
                var after = model.Edges.Single(e => e.Key == before.Key);
                if (RenderEdgeStatement(before) != RenderEdgeStatement(after))
                    replacements.Add((original.Entries.Single(e => e.Kind == "relationship" && e.Owner == before.Key).Statement, RenderEdgeStatement(after)));
            }
            foreach (var before in original.Model.Nodes)
            {
                var after = model.Node(before.Id)!;
                if (RenderNodeStatement(before) == RenderNodeStatement(after)) continue;
                if (EntityHeader(before) != EntityHeader(after)) return Regenerate();
                var entries = original.Entries.Where(e => e.Kind == "attribute" && e.Owner == before.Id).ToArray();
                for (var index = 0; index < entries.Length; index++)
                {
                    if (index >= after.Entity!.Attributes.Count) replacements.Add((entries[index].Statement, ""));
                    else if (before.Entity!.Attributes[index].Declaration != after.Entity.Attributes[index].Declaration)
                        replacements.Add((entries[index].Statement, after.Entity.Attributes[index].Declaration));
                }
                if (after.Entity!.Attributes.Count > entries.Length)
                    appended.Add(IdentifierText(after.Id) + " {\n" +
                        string.Join("\n", after.Entity.Attributes.Skip(entries.Length).Select(a => "  " + a.Declaration)) + "\n}");
            }
            if (replacements.Count > 0 || appended.Count > 0)
            {
                var body = original.Body;
                foreach (var (statement, text) in replacements.OrderByDescending(r => r.Statement.Start))
                    body = body[..statement.Start] + text + body[(statement.Start + statement.Length)..];
                if (appended.Count > 0)
                    body += (body.EndsWith('\n') ? "" : newline) + string.Join("\n", appended).Replace("\n", newline) + newline;
                return MerpeepsDoc.ReplaceBody(source, body);
            }
        }
        return Regenerate();

        string Regenerate()
        {
            var comments = original.Entries.Where(e => e.Kind == "comment").Select(e => e.Statement.Text);
            var body = Write(model) + string.Join("\n", comments);
            if (!body.EndsWith('\n')) body += "\n";
            return MerpeepsDoc.ReplaceBody(source, newline == "\n" ? body : body.Replace("\n", newline));
        }
    }

    internal static object Identity(string source, DiagramModel model)
    {
        var metadata = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
        var previous = (metadata?.GetValueOrDefault("identity") as Dictionary<string, object?>)?.GetValueOrDefault("erDiagram") as Dictionary<string, object?>;
        return new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["attributes"] = model.Nodes.ToDictionary(n => n.Id, n => (object?)n.Entity!.Attributes.Select(a =>
            {
                var old = IdentityEntries(previous, n.Id).FirstOrDefault(e => e.GetValueOrDefault("id") as string == a.Id);
                var entry = old is null ? new Dictionary<string, object?>() : new(old);
                entry["id"] = a.Id;
                entry["fingerprint"] = Fingerprint(a.Declaration);
                return (object?)entry;
            }).ToList()),
        };
    }

    private static IEnumerable<Dictionary<string, object?>> IdentityEntries(Dictionary<string, object?>? identity, string owner)
    {
        if (identity?.GetValueOrDefault("attributes") is not { } raw) return [];
        if (raw is not Dictionary<string, object?> map) throw new FormatException("ER attribute identities must be an entity map.");
        if (map.GetValueOrDefault(owner) is not { } entries) return [];
        if (entries is not List<object?> list || list.Any(e => e is not Dictionary<string, object?>))
            throw new FormatException("ER attribute identities must be lists of identity records.");
        return list.Cast<Dictionary<string, object?>>();
    }

    public static void ResolveStyles(DiagramModel model)
    {
        if (model.EntityOptions is null) return;
        foreach (var node in model.Nodes)
        {
            var entity = node.Entity!;
            entity.ResolvedStyle.Clear();
            foreach (var name in new[] { "default" }.Concat(entity.StyleClasses))
                if (model.EntityOptions.StyleDefinitions.TryGetValue(name, out var style))
                    foreach (var pair in style) entity.ResolvedStyle[pair.Key] = pair.Value;
            foreach (var pair in entity.InlineStyle) entity.ResolvedStyle[pair.Key] = pair.Value;
        }
    }

    private static bool SameStructure(DiagramModel a, DiagramModel b) =>
        a.Nodes.Select(n => (n.Id, n.SubgraphId)).SequenceEqual(b.Nodes.Select(n => (n.Id, n.SubgraphId))) &&
        a.Edges.Select(e => e.Key).SequenceEqual(b.Edges.Select(e => e.Key)) &&
        a.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.Direction)).SequenceEqual(b.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.Direction))) &&
        a.Direction == b.Direction &&
        MerpeepsDoc.ToJson(a.EntityOptions!.StyleDefinitions) == MerpeepsDoc.ToJson(b.EntityOptions!.StyleDefinitions) &&
        a.Nodes.All(n => MerpeepsDoc.ToJson(n.Entity!.InlineStyle) == MerpeepsDoc.ToJson(b.Node(n.Id)!.Entity!.InlineStyle));

    private static DiagramNode Ensure(DiagramModel model, string id)
    {
        var node = model.Node(id);
        if (node is not null) return node;
        node = new() { Id = id, Entity = new(), Width = 260, Height = 100 };
        model.Nodes.Add(node);
        return node;
    }

    public static EntityCardinality ParseCardinality(string value) => Regex.Replace(value, @"\s+", " ") switch
    {
        "|o" or "o|" or "one or zero" or "zero or one" => EntityCardinality.ZeroOrOne,
        "||" or "only one" or "1" => EntityCardinality.ExactlyOne,
        "}o" or "o{" or "zero or more" or "zero or many" or "many(0)" or "0+" => EntityCardinality.ZeroOrMore,
        "}|" or "|{" or "one or more" or "one or many" or "many(1)" or "1+" => EntityCardinality.OneOrMore,
        _ => throw new ArgumentException($"Unknown ER cardinality '{value}'."),
    };
    private static string CardinalityText(EntityCardinality cardinality, bool left) => cardinality switch
    {
        EntityCardinality.ExactlyOne => "||",
        EntityCardinality.ZeroOrOne => left ? "|o" : "o|",
        EntityCardinality.ZeroOrMore => left ? "}o" : "o{",
        EntityCardinality.OneOrMore => left ? "}|" : "|{",
        _ => throw new ArgumentOutOfRangeException(nameof(cardinality)),
    };
    internal static string IdentifierText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("ER identifiers cannot contain double quotes or line breaks.");
        return Regex.IsMatch(value, @"^[\p{L}\p{N}_\-.]+$") ? value : Quoted(value);
    }
    private static string Quoted(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException("Use a single-line ER name or label without double quotes.");
        return "\"" + value + "\"";
    }
    private static string ReadText(string value)
    {
        value = value.Trim();
        return value.StartsWith('"') && value.EndsWith('"') ? value[1..^1] : value;
    }
    private static (string Id, string? Title) ReadGroup(string value)
    {
        var index = IndexOutsideQuotes(value, '[');
        return index < 0 ? (ReadText(value.Trim()), null)
            : (ReadText(value[..index].Trim()), ReadText(value[(index + 1)..].TrimEnd(']').Trim()));
    }
    private static (string First, string Tail) SplitFirst(string text, Statement statement)
    {
        var inQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') inQuote = !inQuote;
            else if (!inQuote && char.IsWhiteSpace(text[i])) return (text[..i], text[i..].Trim());
        }
        throw Error(statement, "Expected target identifiers and a value.");
    }
    private static IEnumerable<string> SplitNames(string text) => Regex.Matches(text, Identifier).Select(m => ReadText(m.Value));
    private static int IndexOutsideQuotes(string text, char value)
    {
        var quote = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') quote = !quote;
            else if (!quote && text[i] == value) return i;
        }
        return -1;
    }
    private static Dictionary<string, string> ParseStyle(string text)
    {
        var styles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Regex.Split(text, @"(?<!\\),(?![^(]*\))"))
        {
            var separator = field.IndexOf(':');
            if (separator <= 0) throw new FormatException("Expected a CSS style property and value.");
            styles[field[..separator].Trim().ToLowerInvariant()] = field[(separator + 1)..].Trim().Replace("\\,", ",");
        }
        return styles;
    }
    private static string StyleText(Dictionary<string, string> style) => string.Join(",", style.Select(p => p.Key + ":" + p.Value.Replace(",", "\\,")));
    private static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static FormatException Error(Statement statement, string message) => new($"ER diagram near character {statement.Start}: {message}");
    private sealed record Statement(string Text, int Start, int Length);
    private sealed record Entry(Statement Statement, string Kind, string Owner, string? AttributeId = null);
    private sealed record Document(DiagramModel Model, string Body)
    {
        public List<Entry> Entries { get; } = new();
    }
    private static List<Statement> Statements(string source)
    {
        var result = new List<Statement>();
        var quote = false;
        var start = 0;
        var comment = false;
        for (var i = 0; i <= source.Length; i++)
        {
            if (i < source.Length && source[i] == '%' && i + 1 < source.Length && source[i + 1] == '%' &&
                source[start..i].Trim().Length == 0) comment = true;
            if (!comment && i < source.Length && source[i] == '"') quote = !quote;
            if (i != source.Length && source[i] != '\n' && (source[i] != ';' || quote)) continue;
            if (comment && i < source.Length && source[i] != '\n') continue;
            var raw = source[start..i];
            var text = raw.Trim();
            if (text.Length > 0) result.Add(new(text, start + raw.IndexOf(text, StringComparison.Ordinal), text.Length));
            start = i + 1;
            comment = false;
        }
        if (quote) throw new FormatException("Unclosed ER quote.");
        return result;
    }
}
