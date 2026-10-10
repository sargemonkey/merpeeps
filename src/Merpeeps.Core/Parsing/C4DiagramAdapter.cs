using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class C4DiagramAdapter : IDiagramAdapter
{
    public string Kind => "C4Context";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) is "C4Context" or "C4Container" or "C4Component" or "C4Dynamic" or "C4Deployment";
    public DiagramModel Parse(string source) => new Parser(source).Parse();
    public string Write(DiagramModel model) => Write(model.C4 ?? throw new ArgumentException("C4 content is missing."));
    public static string Write(C4DiagramDocument document)
    {
        C4DiagramValidation.Check(document);
        var output = new StringBuilder(document.Directive).Append('\n');
        if (document.Title.Length > 0) output.Append("title ").Append(document.Title).Append('\n');
        Elements(null, 0);
        foreach (var relation in document.Relationships)
        {
            var values = new List<C4Argument>();
            if (relation.Kind == C4RelationshipKind.Indexed) values.Add(new(null, relation.IndexArgument ?? "0"));
            values.Add(new(null, document.Elements.Single(e => e.Id == relation.From).Alias));
            values.Add(new(null, document.Elements.Single(e => e.Id == relation.To).Alias));
            values.Add(new(null, relation.Label));
            if (relation.Technology.Length > 0) values.Add(new("techn", relation.Technology));
            if (relation.Description.Length > 0) values.Add(new("descr", relation.Description));
            values.AddRange(relation.ExtraArguments);
            Call(RelationMacro(relation.Kind), values, relation.Kind == C4RelationshipKind.Indexed ? 3 : 2);
        }
        foreach (var element in document.Elements.Where(e => e.Style.Count > 0))
            Call("UpdateElementStyle", new[] { new C4Argument(null, element.Alias) }.Concat(element.Style.Select(p => new C4Argument(p.Key, p.Value?.ToString() ?? ""))), 1);
        foreach (var relation in document.Relationships.Where(e => e.Style.Count > 0))
            Call("UpdateRelStyle", new[] { new C4Argument(null, document.Elements.Single(e => e.Id == relation.From).Alias),
                new C4Argument(null, document.Elements.Single(e => e.Id == relation.To).Alias) }
                .Concat(relation.Style.Select(p => new C4Argument(p.Key, p.Value?.ToString() ?? ""))), 2);
        if (document.LayoutOptions.Count > 0)
            Call("UpdateLayoutConfig", document.LayoutOptions.Select(p => new C4Argument(p.Key, p.Value?.ToString() ?? "")), 0);
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Elements(string? parent, int depth)
        {
            foreach (var element in document.Elements.Where(e => e.ParentId == parent))
            {
                var values = new List<C4Argument> { new(null, element.Alias), new(null, element.Label) };
                if (element.Level is C4ElementLevel.Container or C4ElementLevel.Component && element.Technology.Length > 0) values.Add(new("techn", element.Technology));
                if (element.Level == C4ElementLevel.DeploymentNode && element.Technology.Length > 0) values.Add(new("type", element.Technology));
                if (element.Level == C4ElementLevel.Boundary && element.BoundaryKind == C4BoundaryKind.Generic && element.BoundaryType.Length > 0) values.Add(new("type", element.BoundaryType));
                if (element.Description.Length > 0) values.Add(new("descr", element.Description));
                values.AddRange(element.ExtraArguments);
                output.Append(' ', depth * 2);
                Call(ElementMacro(element), values, 1, element.IsContainer);
                if (element.IsContainer) { Elements(element.Id, depth + 1); output.Append(' ', depth * 2).Append("}\n"); }
            }
        }
        void Call(string name, IEnumerable<C4Argument> arguments, int bare, bool open = false)
        {
            output.Append(name).Append('(');
            var index = 0;
            foreach (var argument in arguments)
            {
                if (index > 0) output.Append(", ");
                if (argument.Name is { } key) output.Append('$').Append(key).Append('=');
                output.Append(argument.Name is null && index < bare ? argument.Value : Quote(argument.Value));
                index++;
            }
            output.Append(open ? ") {\n" : ")\n");
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.C4!; var before = Parse(source).C4!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source,
            Write(document).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "c4", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, C4DiagramDocument document)
    {
        var ids = new DataIdentity(source, "c4");
        var entries = document.Elements.Select(e => ids.Entry(e.Id, "element:" + e.Alias)).ToList();
        entries.AddRange(document.Relationships.Select(r => ids.Entry(r.Id, RelationSignature(document, r))));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string RelationSignature(C4DiagramDocument document, C4Relationship relation) =>
        "relation:" + document.Elements.Single(e => e.Id == relation.From).Alias + "\0" + document.Elements.Single(e => e.Id == relation.To).Alias +
        "\0" + relation.Kind + "\0" + relation.Label;
    internal static string ElementMacro(C4Element element)
    {
        if (element.Level == C4ElementLevel.DeploymentNode)
            return element.Alignment switch { C4HorizontalAlignment.Left => "Node_L", C4HorizontalAlignment.Right => "Node_R", _ => "Deployment_Node" };
        if (element.Level == C4ElementLevel.Boundary)
            return element.BoundaryKind switch { C4BoundaryKind.Enterprise => "Enterprise_Boundary", C4BoundaryKind.System => "System_Boundary", C4BoundaryKind.Container => "Container_Boundary", _ => "Boundary" };
        var prefix = element.Level.ToString();
        if (element.Shape == C4ElementShape.Database) prefix += "Db"; else if (element.Shape == C4ElementShape.Queue) prefix += "Queue";
        return prefix + (element.External ? "_Ext" : "");
    }
    private static string RelationMacro(C4RelationshipKind kind) => kind switch
    {
        C4RelationshipKind.Bidirectional => "BiRel", C4RelationshipKind.Up => "Rel_U", C4RelationshipKind.Down => "Rel_D",
        C4RelationshipKind.Left => "Rel_L", C4RelationshipKind.Right => "Rel_R", C4RelationshipKind.Back => "Rel_Back",
        C4RelationshipKind.Indexed => "RelIndex", _ => "Rel",
    };
    private static string Quote(string value) => "\"" + value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("\r\n", "\n").Replace("\n", "<br/>") + "\"";
    private static string Decode(string value) => WebUtility.HtmlDecode(Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase));
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("C4 declarations carry typed levels and containment.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use typed C4 relationships.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    private sealed class Parser
    {
        private readonly string _body;
        private readonly DataIdentity _identities;
        private readonly C4DiagramDocument _document = new();
        private readonly Stack<string> _parents = new();
        private readonly List<(string Macro, IReadOnlyList<C4Argument> Args)> _relations = new();
        private readonly List<(string Macro, IReadOnlyList<C4Argument> Args)> _styles = new();
        private int _at;
        public Parser(string source)
        {
            _body = MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n"); _identities = new(source, "c4");
            var settings = DataDiagramSupport.Configuration(source, "c4");
            _document.Configuration = settings.Configuration; _document.ThemeVariables = settings.Theme; _document.GlobalConfiguration = settings.Global;
        }
        public DiagramModel Parse()
        {
            Skip(); var directive = Line();
            _document.Kind = directive switch
            {
                "C4Context" => C4DiagramKind.Context, "C4Container" => C4DiagramKind.Container, "C4Component" => C4DiagramKind.Component,
                "C4Dynamic" => C4DiagramKind.Dynamic, "C4Deployment" => C4DiagramKind.Deployment, _ => throw new FormatException("Expected a C4 diagram directive."),
            };
            while (true)
            {
                Skip(); if (_at == _body.Length) break;
                if (_body[_at] == '}')
                {
                    if (!_parents.TryPop(out _)) throw new FormatException("Unmatched C4 container close.");
                    _at++; continue;
                }
                if (_body.AsSpan(_at).StartsWith("title ", StringComparison.OrdinalIgnoreCase))
                {
                    _at += 6; _document.Title = Line(); continue;
                }
                var match = Regex.Match(_body[_at..], @"^[A-Za-z_][A-Za-z0-9_]*");
                if (!match.Success) throw new FormatException("Expected a C4 declaration.");
                var macro = match.Value; _at += match.Length; Skip(); Take('(');
                var arguments = Arguments();
                if (macro.StartsWith("Update", StringComparison.OrdinalIgnoreCase)) { _styles.Add((macro, arguments)); continue; }
                if (macro.StartsWith("Rel", StringComparison.OrdinalIgnoreCase) || macro.Equals("BiRel", StringComparison.OrdinalIgnoreCase))
                { _relations.Add((macro, arguments)); continue; }
                var element = Element(macro, arguments);
                if (_document.Elements.Any(e => e.Alias == element.Alias)) throw new FormatException("C4 alias is declared more than once: " + element.Alias);
                _document.Elements.Add(element);
                Skip();
                if (_at < _body.Length && _body[_at] == '{')
                {
                    if (!element.IsContainer) throw new FormatException("Only a C4 boundary or deployment node may contain elements.");
                    _at++; _parents.Push(element.Id);
                }
                else if (element.IsContainer) throw new FormatException("A C4 container declaration needs braces.");
            }
            if (_parents.Count > 0) throw new FormatException("Unclosed C4 container.");
            foreach (var (macro, args) in _relations)
            {
                var indexed = macro.Equals("RelIndex", StringComparison.OrdinalIgnoreCase);
                var names = indexed ? new[] { "index", "from", "to", "label", "tags", "link" } : new[] { "from", "to", "label", "techn", "descr", "sprite", "tags", "link" };
                var bound = Bind(args, names);
                var from = ElementId(Get(bound, "from")); var to = ElementId(Get(bound, "to"));
                var kind = macro.ToLowerInvariant() switch
                {
                    "rel" => C4RelationshipKind.Rel, "birel" => C4RelationshipKind.Bidirectional, "rel_u" or "rel_up" => C4RelationshipKind.Up,
                    "rel_d" or "rel_down" => C4RelationshipKind.Down, "rel_l" or "rel_left" => C4RelationshipKind.Left,
                    "rel_r" or "rel_right" => C4RelationshipKind.Right, "rel_back" => C4RelationshipKind.Back, "relindex" => C4RelationshipKind.Indexed,
                    _ => throw new FormatException("Unsupported C4 relationship: " + macro),
                };
                var relation = new C4Relationship("", from, to, Get(bound, "label"))
                {
                    Kind = kind, IndexArgument = indexed ? Get(bound, "index") : null,
                    Technology = Get(bound, "techn"), Description = Get(bound, "descr"),
                    ExtraArguments = Extras(bound, "index", "from", "to", "label", "techn", "descr"),
                };
                _document.Relationships.Add(relation with { Id = _identities.Next(RelationSignature(_document, relation)) });
            }
            foreach (var (macro, args) in _styles) Style(macro, args);
            C4DiagramValidation.Check(_document);
            return new() { Kind = _document.Directive, C4 = _document };
        }
        private C4Element Element(string macro, IReadOnlyList<C4Argument> args)
        {
            var lower = macro.ToLowerInvariant();
            var external = lower.EndsWith("_ext", StringComparison.Ordinal);
            if (external) lower = lower[..^4];
            C4ElementLevel level; C4BoundaryKind boundary = C4BoundaryKind.Generic;
            var alignment = C4HorizontalAlignment.Default;
            if (lower is "deployment_node" or "node" or "node_l" or "node_r")
            {
                level = C4ElementLevel.DeploymentNode;
                alignment = lower == "node_l" ? C4HorizontalAlignment.Left : lower == "node_r" ? C4HorizontalAlignment.Right : C4HorizontalAlignment.Default;
            }
            else if (lower is "boundary" or "enterprise_boundary" or "system_boundary" or "container_boundary")
            {
                level = C4ElementLevel.Boundary;
                boundary = lower switch { "enterprise_boundary" => C4BoundaryKind.Enterprise, "system_boundary" => C4BoundaryKind.System, "container_boundary" => C4BoundaryKind.Container, _ => C4BoundaryKind.Generic };
            }
            else level = lower.StartsWith("person", StringComparison.Ordinal) ? C4ElementLevel.Person
                : lower.StartsWith("system", StringComparison.Ordinal) ? C4ElementLevel.System
                : lower.StartsWith("container", StringComparison.Ordinal) ? C4ElementLevel.Container
                : lower.StartsWith("component", StringComparison.Ordinal) ? C4ElementLevel.Component
                : throw new FormatException("Unsupported C4 declaration: " + macro);
            var shape = lower.EndsWith("db", StringComparison.Ordinal) ? C4ElementShape.Database : lower.EndsWith("queue", StringComparison.Ordinal) ? C4ElementShape.Queue : C4ElementShape.Box;
            if (level is C4ElementLevel.Person or C4ElementLevel.System or C4ElementLevel.Container or C4ElementLevel.Component)
            {
                var expected = level.ToString().ToLowerInvariant() + (shape == C4ElementShape.Database ? "db" : shape == C4ElementShape.Queue ? "queue" : "");
                if (lower != expected || level == C4ElementLevel.Person && shape != C4ElementShape.Box) throw new FormatException("Unknown C4 declaration: " + macro);
            }
            var parameters = level switch
            {
                C4ElementLevel.Container or C4ElementLevel.Component => new[] { "alias", "label", "techn", "descr", "sprite", "tags", "link" },
                C4ElementLevel.DeploymentNode => new[] { "alias", "label", "type", "descr", "sprite", "tags", "link" },
                C4ElementLevel.Boundary when boundary == C4BoundaryKind.Generic => new[] { "alias", "label", "type", "tags", "link" },
                C4ElementLevel.Boundary => new[] { "alias", "label", "tags", "link" },
                _ => new[] { "alias", "label", "descr", "sprite", "tags", "link" },
            };
            var values = Bind(args, parameters); var alias = Get(values, "alias");
            return new(_identities.Next("element:" + alias), alias, Get(values, "label"), level)
            {
                External = external, Shape = shape, BoundaryKind = boundary, Alignment = alignment,
                Description = Get(values, "descr"), Technology = Get(values, level == C4ElementLevel.DeploymentNode ? "type" : "techn"),
                BoundaryType = level == C4ElementLevel.Boundary ? Get(values, "type") : "",
                ParentId = _parents.TryPeek(out var parent) ? parent : null,
                ExtraArguments = Extras(values, "alias", "label", "techn", "descr", "type"),
            };
        }
        private void Style(string macro, IReadOnlyList<C4Argument> arguments)
        {
            if (macro.Equals("UpdateLayoutConfig", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in Bind(arguments, ["c4ShapeInRow", "c4BoundaryInRow"])) _document.LayoutOptions[pair.Key] = pair.Value;
                return;
            }
            if (macro.Equals("UpdateElementStyle", StringComparison.OrdinalIgnoreCase))
            {
                var values = Bind(arguments, ["elementName", "bgColor", "fontColor", "borderColor", "shadowing", "shape", "sprite", "techn", "legendText", "legendSprite"]);
                var id = ElementId(Get(values, "elementName"));
                var index = _document.Elements.FindIndex(e => e.Id == id);
                var style = (JsonObject)_document.Elements[index].Style.DeepClone();
                foreach (var pair in values.Where(p => p.Key != "elementName")) style[pair.Key] = pair.Value;
                _document.Elements[index] = _document.Elements[index] with { Style = style }; return;
            }
            if (macro.Equals("UpdateRelStyle", StringComparison.OrdinalIgnoreCase))
            {
                var values = Bind(arguments, ["from", "to", "textColor", "lineColor", "offsetX", "offsetY"]);
                var from = ElementId(Get(values, "from")); var to = ElementId(Get(values, "to"));
                var index = _document.Relationships.FindIndex(r => r.From == from && r.To == to);
                if (index < 0) throw new FormatException("Relationship style targets an unknown pair.");
                var style = (JsonObject)_document.Relationships[index].Style.DeepClone();
                foreach (var pair in values.Where(p => p.Key is not ("from" or "to"))) style[pair.Key] = pair.Value;
                _document.Relationships[index] = _document.Relationships[index] with { Style = style }; return;
            }
            throw new FormatException("Unsupported C4 update: " + macro);
        }
        private string ElementId(string alias) => _document.Elements.FirstOrDefault(e => e.Alias == alias)?.Id ?? throw new FormatException("Unknown C4 alias: " + alias);
        private static Dictionary<string, string> Bind(IReadOnlyList<C4Argument> values, IReadOnlyList<string> names)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var position = 0;
            foreach (var value in values)
            {
                var key = value.Name is { } name ? names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name
                    : position < names.Count ? names[position] : throw new FormatException("Too many positional C4 arguments.");
                if (value.Name is null) position++;
                result[key] = value.Value;
            }
            return result;
        }
        private static string Get(Dictionary<string, string> values, string key) => values.GetValueOrDefault(key) ?? "";
        private static IReadOnlyList<C4Argument> Extras(Dictionary<string, string> values, params string[] consumed) =>
            values.Where(p => !consumed.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).Select(p => new C4Argument(p.Key, p.Value)).ToArray();
        private IReadOnlyList<C4Argument> Arguments()
        {
            var arguments = new List<C4Argument>();
            while (true)
            {
                Skip(); if (_at >= _body.Length) throw new FormatException("Unclosed C4 call.");
                if (_body[_at] == ')') { _at++; return arguments; }
                string? name = null;
                if (_body[_at] == '$')
                {
                    _at++; var match = Regex.Match(_body[_at..], @"^\w+");
                    if (!match.Success) throw new FormatException("Invalid named C4 argument.");
                    name = match.Value; _at += match.Length; Skip(); Take('='); Skip();
                }
                string value;
                if (_at < _body.Length && _body[_at] == '"')
                {
                    _at++; var text = new StringBuilder(); var closed = false;
                    while (_at < _body.Length)
                    {
                        var character = _body[_at++];
                        if (character == '"') { closed = true; break; }
                        if (character == '\\' && _at < _body.Length && _body[_at] == '"') character = _body[_at++];
                        text.Append(character);
                    }
                    if (!closed) throw new FormatException("Unclosed quoted C4 argument.");
                    value = Decode(text.ToString());
                }
                else
                {
                    var start = _at;
                    while (_at < _body.Length && _body[_at] is not (',' or ')')) _at++;
                    value = Decode(_body[start.._at].Trim());
                }
                arguments.Add(new(name, value));
                Skip(); if (_at < _body.Length && _body[_at] == ',') { _at++; continue; }
                Take(')'); return arguments;
            }
        }
        private void Skip()
        {
            while (_at < _body.Length)
            {
                if (char.IsWhiteSpace(_body[_at]) || _body[_at] == ';') { _at++; continue; }
                if (!_body.AsSpan(_at).StartsWith("%%", StringComparison.Ordinal)) return;
                _document.Comments.Add(Line());
            }
        }
        private string Line()
        {
            var end = _body.IndexOf('\n', _at); if (end < 0) end = _body.Length;
            var text = _body[_at..end].Trim(); _at = end; return text;
        }
        private void Take(char character)
        {
            if (_at >= _body.Length || _body[_at] != character) throw new FormatException("Expected '" + character + "' in C4 source.");
            _at++;
        }
    }
}
