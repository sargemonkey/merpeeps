using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class OutlineDiagramAdapter(OutlineFamily family) : IDiagramAdapter
{
    public OutlineFamily Family => family;
    public string Kind => family == OutlineFamily.Mindmap ? "mindmap" : "treeView-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new OutlineDocument { Family = family };
        var key = family == OutlineFamily.Mindmap ? "mindmap" : "treeView";
        var settings = DataDiagramSupport.Configuration(source, key);
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var lines = MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n');
        var identity = new DataIdentity(source, key);
        var roots = new List<Builder>();
        var stack = new Stack<(int Indent, Builder Node)>();
        Builder? previous = null;
        var header = false;
        var plainRoot = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Replace("\t", "    ");
            var text = raw.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(text); continue; }
            if (!header) { if (text != Kind) throw new FormatException("Unexpected outline directive."); header = true; continue; }
            if (family == OutlineFamily.Mindmap && (text.StartsWith("::icon(", StringComparison.Ordinal) || text.StartsWith(":::", StringComparison.Ordinal)))
            {
                if (previous is null) throw new FormatException("An annotation needs a preceding mindmap node.");
                if (text.StartsWith("::icon(", StringComparison.Ordinal))
                    previous.Node = previous.Node with { Icon = text[7..].TrimEnd(')') };
                else previous.Node = previous.Node with { Classes = text[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries) };
                continue;
            }
            var indent = raw.Length - raw.TrimStart().Length;
            var branch = raw.IndexOfAny(['├', '└', '┣', '┗']);
            if (family == OutlineFamily.TreeView && branch >= 0)
            {
                document.BoxDrawingSource = true;
                indent = branch + (plainRoot ? 4 : 0);
                text = raw[(branch + 1)..].TrimStart('─', '━', ' ');
            }
            else if (family == OutlineFamily.TreeView && roots.Count == 0 && lines.Skip(i + 1).Any(l => l.IndexOfAny(['├', '└', '┣', '┗']) >= 0))
                plainRoot = true;
            if (family == OutlineFamily.Mindmap && text.Contains("\"`", StringComparison.Ordinal) && !text.Contains("`\"", StringComparison.Ordinal))
            {
                while (++i < lines.Length) { text += "\n" + lines[i]; if (lines[i].Contains("`\"", StringComparison.Ordinal)) break; }
            }
            var node = family == OutlineFamily.Mindmap ? ParseMindmap(text) : ParseTree(text);
            node = node with { Id = identity.Next(Signature(node)) };
            var builder = new Builder(node);
            while (stack.Count > 0 && stack.Peek().Indent >= indent) stack.Pop();
            if (stack.TryPeek(out var parent)) parent.Node.Children.Add(builder); else roots.Add(builder);
            stack.Push((indent, builder)); previous = builder;
        }
        if (!header) throw new FormatException("Outline directive is missing.");
        document.Roots.AddRange(roots.Select(b => b.Finish()));
        var metadata = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
        var positions = ((metadata?.GetValueOrDefault("layout") as Dictionary<string, object?>)?.GetValueOrDefault("outline") as Dictionary<string, object?>);
        if (positions is not null)
            foreach (var pair in positions)
                if (pair.Value is Dictionary<string, object?> value && value.TryGetValue("x", out var x) && value.TryGetValue("y", out var y) && x is not null && y is not null)
                    document.Positions[pair.Key] = new(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture), Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture));
        var issues = Validate(document);
        if (issues.Any(d => d.Blocking)) throw new DataDiagramNotEditableException(issues);
        return new() { Kind = Kind, Outline = document };
    }
    public string Write(DiagramModel model) => Write(model.Outline ?? throw new ArgumentException("Outline content is missing."));
    public static string Write(OutlineDocument document)
    {
        var issues = Validate(document);
        if (issues.Any(d => d.Blocking)) throw new DataDiagramNotEditableException(issues);
        var builder = new StringBuilder(document.Family == OutlineFamily.Mindmap ? "mindmap\n" : "treeView-beta\n");
        WriteNodes(document.Roots, 0, "");
        foreach (var comment in document.Comments) builder.Append(comment).Append('\n');
        return builder.ToString();
        void WriteNodes(IReadOnlyList<OutlineNode> nodes, int depth, string boxPrefix)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var line = document.Family == OutlineFamily.Mindmap ? MindmapText(node) : TreeText(node);
                if (document.Family == OutlineFamily.TreeView && document.BoxDrawingSource)
                    builder.Append(boxPrefix).Append(i == nodes.Count - 1 ? "└── " : "├── ").Append(line).Append('\n');
                else builder.Append(new string(' ', depth * 4)).Append(line).Append('\n');
                if (document.Family == OutlineFamily.Mindmap)
                {
                    if (node.Icon is not null) builder.Append(new string(' ', depth * 4)).Append("::icon(").Append(node.Icon).Append(")\n");
                    if (node.Classes.Count > 0) builder.Append(new string(' ', depth * 4)).Append(":::").Append(string.Join(' ', node.Classes)).Append('\n');
                }
                WriteNodes(node.Children, depth + 1, boxPrefix + (i == nodes.Count - 1 ? "    " : "│   "));
            }
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Outline ?? throw new ArgumentException("Outline content is missing.");
        var before = Parse(source).Outline!;
        var result = source;
        if (Write(before) != Write(document))
        {
            var body = Write(document); if (MerpeepsDoc.BodyOf(source).Contains("\r\n", StringComparison.Ordinal)) body = body.Replace("\n", "\r\n");
            result = MerpeepsDoc.ReplaceBody(result, body);
        }
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, document.Family == OutlineFamily.Mindmap ? "mindmap" : "treeView", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, OutlineDocument document)
    {
        var identity = new DataIdentity(source, document.Family == OutlineFamily.Mindmap ? "mindmap" : "treeView");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = document.Nodes.Select(n => identity.Entry(n.Id, Signature(n))).ToList() };
    }
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(OutlineDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        if (document.Family == OutlineFamily.Mindmap && document.Roots.Count > 1) issues.Add(new("root", "", "Mindmaps have one root.", true));
        var ids = new HashSet<string>();
        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id)) issues.Add(new("identity", node.Id, "Outline node identities must be unique.", true));
            if (node.Label.Length == 0) issues.Add(new("label", node.Id, "Enter an outline label.", true));
            if (document.Family == OutlineFamily.Mindmap && node.SourceId is not null &&
                (node.Shape == OutlineShape.Default || !Regex.IsMatch(node.SourceId, @"^[\w-]+$")))
                issues.Add(new("source-id", node.Id, "An explicit mindmap source ID requires a named shape and letters/digits/underscores/hyphens.", true));
            if (node.Markdown && node.Shape == OutlineShape.Default)
                issues.Add(new("markdown-shape", node.Id, "Choose a named shape for a Markdown label.", true));
            if (document.Family == OutlineFamily.TreeView && node.Label.IndexOfAny(['\r', '\n', '"']) >= 0) issues.Add(new("label", node.Id, "Tree labels must be single-line text without quotes.", true));
            if (node.Icon is { Length: > 0 } icon && icon is not ("file" or "folder" or "none"))
                issues.Add(new("icon-pack", node.Id, $"Icon '{icon}' is preserved; the native view uses an unresolved-icon indicator."));
            if (node.Classes.Any(c => c != "highlight")) issues.Add(new("external-class", node.Id, "External CSS classes are preserved but are not executed by the native editor."));
            if (node.Icon?.IndexOfAny([')', '\r', '\n']) >= 0) issues.Add(new("icon", node.Id, "Icon references cannot contain closing parentheses or line breaks.", true));
            if (node.Classes.Any(c => !Regex.IsMatch(c, @"^[\w-]+$"))) issues.Add(new("classes", node.Id, "Use separate CSS class names containing letters, digits, underscores or hyphens.", true));
        }
        foreach (var position in document.Positions)
            if (!double.IsFinite(position.Value.X) || !double.IsFinite(position.Value.Y)) issues.Add(new("position", position.Key, "Outline positions must be finite.", true));
        issues.AddRange(DataDiagramSupport.ValidateOptions(document.Configuration, document.ThemeVariables, document.GlobalConfiguration,
            document.Family == OutlineFamily.TreeView ? "treeView" : "mindmap", ["rowIndent", "paddingX", "paddingY", "lineThickness"], ["showIcons"]));
        return issues;
    }
    private static OutlineNode ParseMindmap(string text)
    {
        var shapes = new[] { ("((", "))", OutlineShape.Circle), ("))", "((", OutlineShape.Bang), ("{{", "}}", OutlineShape.Hexagon), ("[", "]", OutlineShape.Square), ("(", ")", OutlineShape.Rounded), (")", "(", OutlineShape.Cloud) };
        foreach (var (open, close, shape) in shapes)
        {
            var at = text.IndexOf(open, StringComparison.Ordinal);
            if (at < 0 || !text.EndsWith(close, StringComparison.Ordinal)) continue;
            var id = text[..at].Trim();
            var label = text[(at + open.Length)..^close.Length];
            var markdown = label.StartsWith("\"`", StringComparison.Ordinal) && label.EndsWith("`\"", StringComparison.Ordinal);
            if (markdown) label = label[2..^2];
            else label = label.Trim('"');
            return new("", SequenceText.Decode(label), []) { SourceId = id.Length == 0 ? null : id, Shape = shape, Markdown = markdown };
        }
        return new("", SequenceText.Decode(text), []);
    }
    private static OutlineNode ParseTree(string text)
    {
        var annotation = Regex.Match(text, @"\s+(?=:::|icon\(|##)");
        var label = annotation.Success ? text[..annotation.Index] : text;
        var rest = annotation.Success ? text[annotation.Index..] : "";
        label = label.Trim().Trim('"');
        var directory = label.EndsWith('/');
        if (directory) label = label[..^1];
        var icon = Regex.Match(rest, @"(?:^|\s)icon\((?<icon>[^)]*)\)");
        var classes = Regex.Matches(rest, @":::(?<class>[\w-]+)").Select(m => m.Groups["class"].Value).ToArray();
        var descriptionAt = rest.IndexOf("##", StringComparison.Ordinal);
        var description = descriptionAt < 0 ? null : rest[(descriptionAt + 2)..].Trim();
        if (description is not null)
        {
            description = Regex.Replace(description, @"(?:^|\s)icon\([^)]*\)|:::[\w-]+", "").Trim();
        }
        return new("", label, []) { Directory = directory, Description = description, Icon = icon.Success ? icon.Groups["icon"].Value : null, Classes = classes };
    }
    private static string MindmapText(OutlineNode node)
    {
        var encoded = DataDiagramSupport.Encode(node.Label).Replace("[", "#91;").Replace("]", "#93;").Replace("(", "#40;").Replace(")", "#41;").Replace("{", "#123;").Replace("}", "#125;");
        var text = node.Markdown ? "\"`" + node.Label + "`\"" : encoded;
        var id = node.SourceId ?? "";
        return node.Shape switch
        {
            OutlineShape.Square => id + "[" + text + "]", OutlineShape.Rounded => id + "(" + text + ")",
            OutlineShape.Circle => id + "((" + text + "))", OutlineShape.Bang => id + "))" + text + "((",
            OutlineShape.Cloud => id + ")" + text + "(", OutlineShape.Hexagon => id + "{{" + text + "}}",
            _ when node.Markdown => id + "[" + text + "]",
            _ => text,
        };
    }
    private static string TreeText(OutlineNode node) => "\"" + node.Label + (node.Directory ? "/" : "") + "\"" +
        (node.Classes.Count > 0 ? " " + string.Join(" ", node.Classes.Select(c => ":::" + c)) : "") +
        (node.Icon is not null ? " icon(" + node.Icon + ")" : "") +
        (node.Description is not null ? " ## " + node.Description : "");
    private static string Signature(OutlineNode node) => $"{node.SourceId}\0{node.Label}\0{node.Shape}\0{node.Directory}\0{node.Markdown}";
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Outline nodes use their hierarchy, not graph syntax.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Outline links are parent/child relationships.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
    private sealed class Builder(OutlineNode node)
    {
        public OutlineNode Node { get; set; } = node;
        public List<Builder> Children { get; } = new();
        public OutlineNode Finish() => Node with { Children = Children.Select(c => c.Finish()).ToArray() };
    }
}
