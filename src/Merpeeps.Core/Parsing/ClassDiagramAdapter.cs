using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class ClassDiagramAdapter : IDiagramAdapter
{
    private const string Identifier = @"(?:`[^`]+`|[\p{L}\p{N}_\-.]+)";
    private static readonly Regex Relation = new(
        @"^(?<from>" + Identifier + @")\s*(?:""(?<fm>[^""]*)""\s*)?" +
        @"(?<left><\||<|\*|o|\(\))?(?<line>--|\.\.)(?<right>\|>|>|\*|o|\(\))?\s*" +
        @"(?:""(?<tm>[^""]*)""\s*)?(?<to>" + Identifier + @")\s*(?::\s*(?<label>.*))?$");

    public string Kind => "classDiagram";
    public bool CanParse(string body) =>
        DiagramAdapters.FirstKeyword(body) is "classDiagram" or "classDiagram-v2";

    public DiagramModel Parse(string source) => Read(source).Model;

    private static Document Read(string source)
    {
        var parts = MerpeepsDoc.Split(source);
        var model = new DiagramModel { Kind = "classDiagram", ClassOptions = new() };
        if (parts.Frontmatter.TryGetValue("config", out var config) &&
            config is Dictionary<string, object?> configMap &&
            configMap.TryGetValue("class", out var settings) && settings is Dictionary<string, object?> classMap)
        {
            model.ClassOptions.HideEmptyMembersBox = classMap.GetValueOrDefault("hideEmptyMembersBox") is true;
            model.ClassOptions.HierarchicalNamespaces = classMap.GetValueOrDefault("hierarchicalNamespaces") is not false;
        }
        var document = new Document(model, parts.Body);
        var identity = new ClassIdentity(source);
        var scopes = new Stack<(string Kind, string Id)>();
        var tokens = Tokenize(parts.Body);
        var ordinals = new Dictionary<(string, string), int>();
        var header = false;

        foreach (var token in tokens)
        {
            var text = token.Text;
            if (text.StartsWith("%%", StringComparison.Ordinal))
            {
                document.Entries.Add(new(token, EntryKind.Comment, ""));
                continue;
            }
            if (!header)
            {
                if (text is not ("classDiagram" or "classDiagram-v2"))
                    throw Error(token, "Expected a classDiagram directive.");
                model.Kind = text;
                header = true;
                document.Entries.Add(new(token, EntryKind.Header, ""));
                continue;
            }
            if (text == "}")
            {
                if (scopes.Count == 0) throw Error(token, "Unexpected closing class or namespace block.");
                var scope = scopes.Pop();
                document.Entries.Add(new(token, EntryKind.Close, scope.Id));
                continue;
            }
            if (scopes.TryPeek(out var current) && current.Kind == "class")
            {
                var node = model.Node(current.Id)!;
                if (text.StartsWith("<<", StringComparison.Ordinal))
                {
                    node.Class!.Annotations.Add(Annotation(text, token));
                    document.Entries.Add(new(token, EntryKind.Annotation, node.Id));
                }
                else
                {
                    var parsed = ClassMember.Parse("member", text);
                    var member = ClassMember.Parse(identity.MemberId(node.Id, parsed.Declaration), text);
                    node.Class!.Members.Add(member);
                    document.Entries.Add(new(token, EntryKind.Member, node.Id, node.Class.Members.Count - 1));
                }
                continue;
            }
            if (text.StartsWith("direction ", StringComparison.Ordinal))
            {
                model.Direction = text[10..].Trim() switch
                {
                    "LR" => Direction.LR,
                    "RL" => Direction.RL,
                    "BT" => Direction.BT,
                    "TB" or "TD" => Direction.TB,
                    _ => throw Error(token, "Unknown class diagram direction."),
                };
                document.Entries.Add(new(token, EntryKind.Direction, ""));
                continue;
            }
            if (text.StartsWith("namespace ", StringComparison.Ordinal))
            {
                if (!text.EndsWith('{')) throw Error(token, "A namespace requires a block.");
                var rest = text[10..^1].Trim();
                var (id, tail) = ReadIdentifier(rest, token);
                var parent = scopes.Count == 0 ? null : scopes.Peek().Id;
                var qualified = parent is null ? id : parent + "." + id;
                var ns = EnsureNamespace(model, qualified, explicitDeclaration: true);
                ns.Title = ReadLabel(tail, token);
                scopes.Push(("namespace", qualified));
                document.Entries.Add(new(token, EntryKind.Namespace, qualified));
                continue;
            }
            if (text.StartsWith("class ", StringComparison.Ordinal))
            {
                var block = text.EndsWith('{');
                var rest = text[6..(block ? ^1 : ^0)].Trim();
                var (id, tail) = ReadIdentifier(rest, token);
                var node = EnsureClass(model, id);
                if (scopes.Count > 0)
                {
                    foreach (var previous in model.Subgraphs) previous.NodeIds.Remove(id);
                    node.SubgraphId = scopes.Peek().Id;
                    var ns = model.Subgraphs.Single(g => g.Id == node.SubgraphId);
                    if (!ns.NodeIds.Contains(id)) ns.NodeIds.Add(id);
                }
                if (tail.StartsWith('~'))
                {
                    var end = tail.LastIndexOf('~');
                    if (end <= 0) throw Error(token, "Unclosed generic class type.");
                    node.Class!.GenericType = tail[1..end];
                    tail = tail[(end + 1)..].Trim();
                }
                if (tail.StartsWith('['))
                {
                    var end = tail.LastIndexOf(']');
                    if (end < 0) throw Error(token, "Unclosed class label.");
                    node.Label = ReadLabel(tail[..(end + 1)], token);
                    tail = tail[(end + 1)..].Trim();
                }
                if (tail.StartsWith("<<", StringComparison.Ordinal))
                {
                    var end = tail.IndexOf(">>", StringComparison.Ordinal);
                    if (end < 0) throw Error(token, "Unclosed class annotation.");
                    node.Class!.Annotations.Add(Annotation(tail[..(end + 2)], token));
                    tail = tail[(end + 2)..].Trim();
                }
                if (tail.StartsWith(":::", StringComparison.Ordinal))
                {
                    node.Class!.CssClasses.AddRange(tail[3..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    tail = "";
                }
                if (tail.Length != 0) throw Error(token, $"Unsupported class declaration suffix '{tail}'.");
                document.Entries.Add(new(token, EntryKind.Class, id));
                if (block) scopes.Push(("class", id));
                continue;
            }
            if (text.StartsWith("note ", StringComparison.Ordinal))
            {
                var rest = text[5..].Trim();
                string? target = null;
                if (rest.StartsWith("for ", StringComparison.Ordinal))
                {
                    (target, rest) = ReadIdentifier(rest[4..], token);
                    EnsureClass(model, target);
                }
                var noteText = Quoted(rest, token);
                var noteId = identity.NoteId(target, noteText);
                if (model.Node(noteId) is not null) throw Error(token, "Duplicate class note identity.");
                var note = new DiagramNode
                {
                    Id = noteId,
                    Label = noteText,
                    Class = new() { Kind = ClassNodeKind.Note, NoteFor = target },
                    Width = 240,
                    Height = 100,
                };
                model.Nodes.Add(note);
                document.Entries.Add(new(token, EntryKind.Note, note.Id));
                continue;
            }
            if (text.StartsWith("<<", StringComparison.Ordinal))
            {
                var end = text.IndexOf(">>", StringComparison.Ordinal);
                if (end < 0) throw Error(token, "Unclosed annotation.");
                var (id, rest) = ReadIdentifier(text[(end + 2)..], token);
                if (rest.Length != 0) throw Error(token, "Unexpected annotation suffix.");
                EnsureClass(model, id).Class!.Annotations.Add(Annotation(text[..(end + 2)], token));
                document.Entries.Add(new(token, EntryKind.Annotation, id));
                continue;
            }
            var relation = Relation.Match(text);
            if (relation.Success)
            {
                var fromName = UnquoteIdentifier(relation.Groups["from"].Value);
                var toName = UnquoteIdentifier(relation.Groups["to"].Value);
                var fromEnd = Marker(relation.Groups["left"].Value);
                var toEnd = Marker(relation.Groups["right"].Value);
                var from = fromEnd == ClassRelationEnd.Lollipop
                    ? AddLollipop(model, fromName, toName, true, identity) : EnsureClass(model, fromName);
                var to = toEnd == ClassRelationEnd.Lollipop
                    ? AddLollipop(model, toName, fromName, false, identity) : EnsureClass(model, toName);
                var key = (from.Id, to.Id);
                var ordinal = ordinals.GetValueOrDefault(key);
                ordinals[key] = ordinal + 1;
                var dashed = relation.Groups["line"].Value == "..";
                var edge = new DiagramEdge
                {
                    From = from.Id,
                    To = to.Id,
                    Ordinal = ordinal,
                    Label = relation.Groups["label"].Success ? Decode(relation.Groups["label"].Value.Trim()) : null,
                    Kind = dashed ? EdgeKind.Dotted : EdgeKind.Open,
                    ClassRelationship = new(fromEnd, toEnd, dashed,
                        relation.Groups["fm"].Success ? Decode(relation.Groups["fm"].Value) : null,
                        relation.Groups["tm"].Success ? Decode(relation.Groups["tm"].Value) : null),
                };
                model.Edges.Add(edge);
                document.Entries.Add(new(token, EntryKind.Relation, edge.Key));
                continue;
            }
            if (text.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var rest = text[9..].Trim();
                var split = rest.IndexOfAny([' ', '\t']);
                if (split <= 0) throw Error(token, "A classDef needs a name and style.");
                model.ClassOptions.StyleDefinitions[rest[..split]] = ParseStyle(rest[(split + 1)..]);
                document.Entries.Add(new(token, EntryKind.StyleDefinition, rest[..split]));
                continue;
            }
            if (text.StartsWith("style ", StringComparison.Ordinal))
            {
                var (id, rest) = ReadIdentifier(text[6..], token);
                foreach (var entry in ParseStyle(rest)) EnsureClass(model, id).Class!.InlineStyle[entry.Key] = entry.Value;
                document.Entries.Add(new(token, EntryKind.Style, id));
                continue;
            }
            if (text.StartsWith("cssClass ", StringComparison.Ordinal))
            {
                var rest = text[9..].Trim();
                var split = rest.IndexOfAny([' ', '\t']);
                if (split <= 0) throw Error(token, "A cssClass needs target classes and a style class.");
                foreach (var id in rest[..split].Split(',', StringSplitOptions.TrimEntries))
                    EnsureClass(model, id).Class!.CssClasses.Add(UnquoteIdentifier(rest[(split + 1)..].Trim('"', ' ', '\t')));
                document.Entries.Add(new(token, EntryKind.CssAssignment, ""));
                continue;
            }
            if (text.StartsWith("click ", StringComparison.Ordinal) ||
                text.StartsWith("link ", StringComparison.Ordinal) || text.StartsWith("callback ", StringComparison.Ordinal))
            {
                var (id, rest) = ReadIdentifier(text[(text.IndexOf(' ') + 1)..], token);
                EnsureClass(model, id).Class!.Interaction = text[..text.IndexOf(' ')] + " " + rest;
                document.Entries.Add(new(token, EntryKind.Interaction, id));
                continue;
            }
            var memberMatch = Regex.Match(text, @"^(?<id>" + Identifier + @")\s*:\s*(?<member>.+)$");
            if (memberMatch.Success)
            {
                var node = EnsureClass(model, UnquoteIdentifier(memberMatch.Groups["id"].Value));
                var declaration = memberMatch.Groups["member"].Value;
                var parsed = ClassMember.Parse("member", declaration);
                node.Class!.Members.Add(ClassMember.Parse(identity.MemberId(node.Id, parsed.Declaration), declaration));
                document.Entries.Add(new(token, EntryKind.Member, node.Id, node.Class.Members.Count - 1, true));
                continue;
            }
            throw Error(token, $"The native class editor cannot represent '{text}'. The source must be preserved.");
        }
        if (!header) throw new FormatException("No class diagram directive was found.");
        if (scopes.Count != 0) throw new FormatException($"Unclosed {scopes.Peek().Kind} block.");
        ResolveStyles(model);
        foreach (var node in model.Nodes.Where(n => n.Class!.Kind == ClassNodeKind.Class))
        {
            var content = ClassDiagramText.Content(node, model.ClassOptions);
            var fontSize = ClassDiagramStyle.Resolve(node).FontSize;
            var rows = content.Header.Concat(content.Attributes).Concat(content.Operations).ToArray();
            node.Width = Math.Max(200, rows.Max(r => r.Text.Length) * fontSize * .55 + 28);
            node.Height = ClassDiagramGeometry.CardRegions(node, model.ClassOptions, fontSize * 1.4).RequiredHeight;
        }
        model.LineCount = parts.Body.Split('\n').Length;
        return document;
    }

    public string Write(DiagramModel model)
    {
        if (model.ClassOptions is null) throw new ArgumentException("Class diagram options are missing.", nameof(model));
        foreach (var node in model.Nodes)
        {
            if (node.Class is null) throw new ArgumentException($"Element '{node.Id}' has no class semantics.");
            if (node.Class.Kind == ClassNodeKind.Lollipop &&
                model.Edges.Count(e => e.From == node.Id || e.To == node.Id) != 1)
                throw new ArgumentException($"Interface occurrence '{node.Id}' must have exactly one relationship.");
            if (node.Class.NoteFor is { } target && model.Node(target)?.Class?.Kind != ClassNodeKind.Class)
                throw new ArgumentException($"Note '{node.Id}' has an invalid class target.");
        }
        foreach (var edge in model.Edges) Editing.AddClassRelationshipCommand.Validate(model, edge, edge.ClassRelationship);
        var sb = new StringBuilder(model.Kind + "\n");
        if (model.Direction != Direction.TB) sb.Append("direction ").Append(model.Direction).Append('\n');
        foreach (var node in model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Class && n.SubgraphId is null))
            sb.Append(RenderNodeStatement(node)).Append('\n');
        foreach (var ns in model.Subgraphs.Where(g => g.IsExplicit && ExplicitParent(model, g) is null))
            WriteNamespace(sb, model, ns, "");
        foreach (var node in model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Note))
            sb.Append(RenderNodeStatement(node)).Append('\n');
        foreach (var edge in model.Edges) sb.Append(RenderEdge(model, edge)).Append('\n');
        foreach (var entry in model.ClassOptions.StyleDefinitions)
            sb.Append("classDef ").Append(entry.Key).Append(' ').Append(StyleText(entry.Value)).Append('\n');
        foreach (var node in model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Class))
        {
            if (node.Class!.InlineStyle.Count > 0)
                sb.Append("style ").Append(Id(node.Id)).Append(' ').Append(StyleText(node.Class.InlineStyle)).Append('\n');
            if (node.Class.Interaction is { } interaction)
            {
                var split = interaction.IndexOf(' ');
                sb.Append(interaction[..split]).Append(' ').Append(Id(node.Id)).Append(interaction[split..]).Append('\n');
            }
        }
        foreach (var line in model.Passthrough.Values) sb.Append(line).Append('\n');
        return sb.ToString();
    }

    private void WriteNamespace(StringBuilder sb, DiagramModel model, DiagramSubgraph ns, string indent)
    {
        var explicitParent = ExplicitParent(model, ns);
        var localName = explicitParent is not null ? ns.Id[(explicitParent.Length + 1)..] : ns.Id;
        sb.Append(indent).Append("namespace ").Append(Id(localName));
        if (ns.Title is { } title) sb.Append("[\"").Append(Encode(title)).Append("\"]");
        sb.Append(" {\n");
        foreach (var node in model.Nodes.Where(n => n.SubgraphId == ns.Id && n.Class?.Kind == ClassNodeKind.Class))
            sb.Append(indent).Append("  ").Append(RenderNodeStatement(node)?.Replace("\n", "\n" + indent + "  ")).Append('\n');
        foreach (var child in model.Subgraphs.Where(g => g.IsExplicit && ExplicitParent(model, g) == ns.Id))
            WriteNamespace(sb, model, child, indent + "  ");
        sb.Append(indent).Append("}\n");
    }

    private static string? ExplicitParent(DiagramModel model, DiagramSubgraph ns)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { ns.Id };
        var parent = ns.ParentId;
        while (parent is not null)
        {
            if (!visited.Add(parent)) throw new ArgumentException("Namespace nesting contains a cycle.");
            var group = model.Subgraphs.SingleOrDefault(g => g.Id == parent)
                ?? throw new ArgumentException($"Namespace '{parent}' does not exist.");
            if (group.IsExplicit) return group.Id;
            parent = group.ParentId;
        }
        return null;
    }

    public string? RenderNodeStatement(DiagramNode node)
    {
        var definition = node.Class ?? throw new ArgumentException("A class node needs typed content.", nameof(node));
        if (definition.Kind == ClassNodeKind.Lollipop) return null;
        if (definition.Kind == ClassNodeKind.Note)
            return "note " + (definition.NoteFor is { } target ? "for " + Id(target) + " " : "")
                + "\"" + Encode(node.Display) + "\"";
        var text = ClassHeader(node);
        if (definition.Annotations.Count + definition.Members.Count == 0) return text;
        return text + " {\n" +
            string.Join("\n", definition.Annotations.Select(a => "  <<" + a + ">>")
                .Concat(definition.Members.Select(m => "  " + m.Declaration))) + "\n}";
    }

    private static string ClassHeader(DiagramNode node)
    {
        var definition = node.Class!;
        var text = "class " + Id(node.Id);
        if (!string.IsNullOrEmpty(definition.GenericType)) text += "~" + definition.GenericType + "~";
        if (node.Label is { } label && label != node.Id) text += "[\"" + Encode(label) + "\"]";
        if (definition.CssClasses.Count > 0) text += ":::" + string.Join(",", definition.CssClasses);
        return text;
    }

    public string RenderEdgeStatement(DiagramEdge edge) =>
        edge.ClassRelationship is { FromEnd: ClassRelationEnd.Lollipop } or { ToEnd: ClassRelationEnd.Lollipop }
            ? throw new InvalidOperationException("Write lollipop relationships with the complete class model.")
            : EdgeText(edge, Id(edge.From), Id(edge.To));
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    private static string RenderEdge(DiagramModel model, DiagramEdge edge) =>
        EdgeText(edge, Id(model.Node(edge.From)?.Class?.ReferenceName ?? edge.From),
            Id(model.Node(edge.To)?.Class?.ReferenceName ?? edge.To));

    private static string EdgeText(DiagramEdge edge, string from, string to)
    {
        var relation = edge.ClassRelationship ?? throw new ArgumentException("A class relationship needs typed endpoints.", nameof(edge));
        return from + (relation.FromMultiplicity is { } fm ? " \"" + Encode(fm) + "\"" : "")
            + " " + MarkerText(relation.FromEnd, true) + (relation.Dashed ? ".." : "--")
            + MarkerText(relation.ToEnd, false)
            + (relation.ToMultiplicity is { } tm ? " \"" + Encode(tm) + "\"" : "")
            + " " + to + (string.IsNullOrEmpty(edge.Label) ? "" : " : " + edge.Label);
    }

    internal string Apply(string source, DiagramModel edited)
    {
        var original = Read(source);
        var result = ApplyBody(source, original, edited);
        var before = original.Model.ClassOptions!;
        var after = edited.ClassOptions ?? throw new ArgumentException("Class diagram options are missing.");
        return before.HideEmptyMembersBox == after.HideEmptyMembersBox &&
               before.HierarchicalNamespaces == after.HierarchicalNamespaces
            ? result
            : MerpeepsDoc.WithClassOptions(result, after.HideEmptyMembersBox, after.HierarchicalNamespaces);
    }

    private string ApplyBody(string source, Document original, DiagramModel edited)
    {
        if (Write(original.Model) == Write(edited)) return source;
        // Member and label edits are local; changes in object topology use the
        // canonical writer while retaining all comment/opaque source content.
        if (original.Model.Nodes.Select(n => n.Id).SequenceEqual(edited.Nodes.Select(n => n.Id)) &&
            original.Model.Edges.Select(e => e.Key).SequenceEqual(edited.Edges.Select(e => e.Key)) &&
            SameNamespaces(original.Model, edited) &&
            original.Model.Direction == edited.Direction &&
            SameDefinitions(original.Model, edited) && SameNodeStyles(original.Model, edited))
        {
            var patches = new Dictionary<Token, string>();
            var appended = new List<string>();
            foreach (var before in original.Model.Nodes)
            {
                var after = edited.Node(before.Id)!;
                if (NodeDefinitionText(before) == NodeDefinitionText(after)) continue;
                if (before.Class?.Kind != ClassNodeKind.Class || after.Class?.Kind != ClassNodeKind.Class)
                    return Regenerate(source, original, edited);
                if (before.SubgraphId != after.SubgraphId)
                    return Regenerate(source, original, edited);
                var headers = original.Entries.Where(e => e.Owner == before.Id && e.Kind == EntryKind.Class).ToArray();
                if (ClassHeader(before) != ClassHeader(after) ||
                    !before.Class.Annotations.SequenceEqual(after.Class.Annotations))
                {
                    if (headers.Length == 0) appended.Add(ClassHeader(after));
                    foreach (var header in headers)
                        patches[header.Token] = ClassHeader(after) + (header.Token.Text.EndsWith('{') ? " {" : "");
                    foreach (var annotation in original.Entries.Where(e => e.Owner == before.Id && e.Kind == EntryKind.Annotation))
                        patches[annotation.Token] = "";
                    appended.AddRange(after.Class.Annotations.Select(a => "<<" + a + ">> " + Id(after.Id)));
                }
                var members = original.Entries.Where(e => e.Owner == before.Id && e.Kind == EntryKind.Member).ToArray();
                for (var i = 0; i < members.Length; i++)
                {
                    var entry = members[i];
                    if (i >= after.Class.Members.Count) patches[entry.Token] = "";
                    else if (before.Class.Members[i].Declaration != after.Class.Members[i].Declaration)
                        patches[entry.Token] = (entry.Standalone ? Id(after.Id) + " : " : "") + after.Class.Members[i].Declaration;
                }
                appended.AddRange(after.Class.Members.Skip(members.Length)
                    .Select(m => Id(after.Id) + " : " + m.Declaration));
            }
            foreach (var before in original.Model.Edges)
            {
                var after = edited.Edges.Single(e => e.Key == before.Key);
                if (RenderEdge(original.Model, before) == RenderEdge(edited, after)) continue;
                var entry = original.Entries.Single(e => e.Kind == EntryKind.Relation && e.Owner == before.Key);
                patches[entry.Token] = RenderEdge(edited, after);
            }
            if (patches.Count > 0 || appended.Count > 0)
            {
                var body = original.Body;
                foreach (var patch in patches.OrderByDescending(p => p.Key.Start))
                    body = body[..patch.Key.Start] + patch.Value + body[(patch.Key.Start + patch.Key.Length)..];
                if (appended.Count > 0)
                {
                    var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                    if (!body.EndsWith('\n')) body += newline;
                    body += string.Join(newline, appended) + newline;
                }
                return MerpeepsDoc.ReplaceBody(source, body);
            }
        }
        return Regenerate(source, original, edited);
    }

    private string Regenerate(string source, Document original, DiagramModel edited)
    {
        var comments = original.Entries.Where(e => e.Kind == EntryKind.Comment)
            .Select(e => e.Token.Text).ToArray();
        var body = Write(edited);
        if (comments.Length > 0) body += string.Join("\n", comments) + "\n";
        if (original.Body.Contains("\r\n", StringComparison.Ordinal)) body = body.Replace("\n", "\r\n");
        return MerpeepsDoc.ReplaceBody(source, body);
    }

    private string NodeDefinitionText(DiagramNode node) => RenderNodeStatement(node) ?? node.Display;
    private static bool SameNamespaces(DiagramModel a, DiagramModel b) =>
        a.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.IsExplicit))
            .SequenceEqual(b.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.IsExplicit)));
    private static bool SameDefinitions(DiagramModel a, DiagramModel b) =>
        MerpeepsDoc.ToJson(a.ClassOptions!.StyleDefinitions) == MerpeepsDoc.ToJson(b.ClassOptions!.StyleDefinitions);
    private static bool SameNodeStyles(DiagramModel a, DiagramModel b) =>
        a.Nodes.All(n => b.Node(n.Id) is { Class: { } other } &&
            n.Class!.Interaction == other.Interaction &&
            MerpeepsDoc.ToJson(n.Class.InlineStyle) == MerpeepsDoc.ToJson(other.InlineStyle));

    public static void ResolveStyles(DiagramModel model)
    {
        if (model.ClassOptions is null) return;
        foreach (var node in model.Nodes.Where(n => n.Class is not null))
        {
            var definition = node.Class!;
            definition.ResolvedStyle.Clear();
            foreach (var name in new[] { "default" }.Concat(definition.CssClasses))
                if (model.ClassOptions.StyleDefinitions.TryGetValue(name, out var style))
                    foreach (var entry in style) definition.ResolvedStyle[entry.Key] = entry.Value;
            foreach (var entry in definition.InlineStyle) definition.ResolvedStyle[entry.Key] = entry.Value;
        }
    }

    private static DiagramNode EnsureClass(DiagramModel model, string id)
    {
        var existing = model.Node(id);
        if (existing is not null)
        {
            if (existing.Class?.Kind != ClassNodeKind.Class)
                throw new FormatException($"Class '{id}' conflicts with another diagram element identity.");
            return existing;
        }
        var node = new DiagramNode { Id = id, Class = new(), Width = 220, Height = 100 };
        model.Nodes.Add(node);
        return node;
    }

    private static DiagramNode AddLollipop(DiagramModel model, string name, string owner, bool from, ClassIdentity identity)
    {
        var id = identity.InterfaceId(owner, name, from);
        if (model.Node(id) is not null) throw new FormatException("Duplicate lollipop occurrence identity.");
        var node = new DiagramNode
        {
            Id = id,
            Label = name,
            Class = new() { Kind = ClassNodeKind.Lollipop, ReferenceName = name },
            Width = Math.Max(80, name.Length * 7 + 16),
            Height = 60,
        };
        model.Nodes.Add(node);
        return node;
    }


    private static DiagramSubgraph EnsureNamespace(DiagramModel model, string id, bool explicitDeclaration)
    {
        var found = model.Subgraphs.FirstOrDefault(g => g.Id == id);
        if (found is not null) { found.IsExplicit |= explicitDeclaration; return found; }
        var separator = id.LastIndexOf('.');
        var parent = separator > 0 ? id[..separator] : null;
        if (parent is not null) EnsureNamespace(model, parent, false);
        var ns = new DiagramSubgraph { Id = id, ParentId = parent, IsExplicit = explicitDeclaration };
        model.Subgraphs.Add(ns);
        return ns;
    }

    private static (string Id, string Tail) ReadIdentifier(string text, Token token)
    {
        text = text.TrimStart();
        var match = Regex.Match(text, "^" + Identifier);
        if (!match.Success) throw Error(token, "Expected a class or namespace identifier.");
        return (UnquoteIdentifier(match.Value), text[match.Length..].Trim());
    }

    private static string? ReadLabel(string tail, Token token) =>
        tail.Length == 0 ? null : tail.StartsWith("[\"", StringComparison.Ordinal) && tail.EndsWith("\"]", StringComparison.Ordinal)
            ? Decode(tail[2..^2]) : throw Error(token, "Expected a quoted display label.");
    private static string Annotation(string text, Token token) =>
        text.StartsWith("<<", StringComparison.Ordinal) && text.EndsWith(">>", StringComparison.Ordinal) && text.Length > 4
            ? text[2..^2] : throw Error(token, "Expected a class annotation between << and >>.");
    private static string Quoted(string text, Token token) =>
        text.StartsWith('"') && text.EndsWith('"') ? Decode(text[1..^1])
            : throw Error(token, "Expected quoted note text.");
    private static string UnquoteIdentifier(string text) => text.Trim('`');
    internal static string Id(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.IndexOfAny(['`', '\n', '\r']) >= 0)
            throw new ArgumentException("An identifier cannot contain backticks or line breaks.", nameof(text));
        return Regex.IsMatch(text, @"^[\p{L}\p{N}_\-.]+$") ? text : "`" + text + "`";
    }
    private static string Decode(string text) => WebUtility.HtmlDecode(
        Regex.Replace(text.Replace("\\n", "\n"), @"<br\s*/?>", "\n", RegexOptions.IgnoreCase));
    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("\\", "&#92;")
        .Replace("\"", "&quot;").Replace("\r\n", "\n").Replace("\n", "\\n");
    private static ClassRelationEnd Marker(string marker) => marker switch
    {
        "<|" or "|>" => ClassRelationEnd.Triangle,
        "*" => ClassRelationEnd.Composition,
        "o" => ClassRelationEnd.Aggregation,
        "<" or ">" => ClassRelationEnd.Arrow,
        "()" => ClassRelationEnd.Lollipop,
        _ => ClassRelationEnd.None,
    };
    private static string MarkerText(ClassRelationEnd marker, bool left) => marker switch
    {
        ClassRelationEnd.Triangle => left ? "<|" : "|>",
        ClassRelationEnd.Composition => "*",
        ClassRelationEnd.Aggregation => "o",
        ClassRelationEnd.Arrow => left ? "<" : ">",
        ClassRelationEnd.Lollipop => "()",
        _ => "",
    };
    private static Dictionary<string, string> ParseStyle(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in Regex.Split(text, @"(?<!\\),(?![^(]*\))"))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0) throw new FormatException("Expected CSS property:value in a class style.");
            result[part[..colon].Trim().ToLowerInvariant()] = part[(colon + 1)..].Trim().Replace("\\,", ",");
        }
        return result;
    }
    private static string StyleText(Dictionary<string, string> style) =>
        string.Join(",", style.Select(p => p.Key + ":" + p.Value.Replace(",", "\\,")));
    private static FormatException Error(Token token, string message) =>
        new($"Class diagram near character {token.Start}: {message}");

    private sealed record Token(string Text, int Start, int Length);
    private enum EntryKind { Header, Class, Member, Annotation, Namespace, Note, Relation, Direction, Style, StyleDefinition, Interaction, Comment, CssAssignment, Close }
    private sealed record Entry(Token Token, EntryKind Kind, string Owner, int MemberIndex = -1, bool Standalone = false);
    private sealed record Document(DiagramModel Model, string Body)
    {
        public List<Entry> Entries { get; } = new();
    }
    private static List<Token> Tokenize(string body)
    {
        var tokens = new List<Token>();
        var start = 0;
        char quote = '\0';
        var comment = false;
        var blocks = new Stack<bool>();
        void Emit(int end)
        {
            var raw = body[start..end];
            var text = raw.Trim();
            if (text.Length > 0)
            {
                var offset = start + raw.IndexOf(text, StringComparison.Ordinal);
                tokens.Add(new(text, offset, text.Length));
            }
            start = end;
        }
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (comment)
            {
                if (ch == '\n') { Emit(i); start = i + 1; comment = false; }
                continue;
            }
            if (quote != '\0')
            {
                if (ch == quote && (i == 0 || body[i - 1] != '\\')) quote = '\0';
                continue;
            }
            if (ch is '"' or '`') { quote = ch; continue; }
            if (ch == '%' && i + 1 < body.Length && body[i + 1] == '%' &&
                body[(body.LastIndexOf('\n', Math.Max(0, i - 1)) + 1)..i].Trim().Length == 0)
            {
                Emit(i);
                start = i;
                comment = true;
                continue;
            }
            if (ch == '{')
            {
                blocks.Push(body[start..i].TrimStart().StartsWith("class ", StringComparison.Ordinal));
                Emit(i + 1); start = i + 1;
            }
            else if (ch == '}')
            {
                Emit(i); start = i; Emit(i + 1);
                if (blocks.Count > 0) blocks.Pop();
            }
            else if (ch == '\n' || ch == ';' && (!blocks.TryPeek(out var inClass) || !inClass))
            {
                Emit(i); start = i + 1;
            }
        }
        if (quote != '\0') throw new FormatException("Unclosed quoted class diagram text.");
        Emit(body.Length);
        return tokens;
    }
}
