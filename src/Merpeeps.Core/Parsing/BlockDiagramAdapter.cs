using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class BlockDiagramAdapter : IDiagramAdapter
{
    public string Kind => "block";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) is "block" or "block-beta";
    public DiagramModel Parse(string source) => new Parser(source).Parse();
    public string Write(DiagramModel model) => Write(model.Blocks ?? throw new ArgumentException("Block content is missing."));
    public static string Write(BlockDiagramDocument document)
    {
        Check(document);
        var output = new StringBuilder(document.Directive).Append('\n');
        Columns(document.Columns, 0);
        Items(document.Items, 0);
        foreach (var edge in document.Connections)
        {
            var from = document.AllItems.Single(n => n.Id == edge.From).SourceId!;
            var to = document.AllItems.Single(n => n.Id == edge.To).SourceId!;
            output.Append(from);
            if (edge.Label.Length > 0)
                output.Append(edge.Stroke == BlockLinkStroke.Thick ? "== " : edge.Stroke == BlockLinkStroke.Dotted ? "-. " : "-- ").Append(Quote(edge.Label)).Append(' ');
            else output.Append(' ');
            output.Append(Link(edge)).Append(' ').Append(to).Append('\n');
        }
        foreach (var item in document.Classes) output.Append("classDef ").Append(item.Key).Append(' ').Append(Css(item.Value)).Append('\n');
        foreach (var item in document.AllItems.Where(n => n.SourceId is not null))
        {
            if (item.Classes.Count > 0) output.Append("class ").Append(item.SourceId).Append(' ').Append(string.Join(",", item.Classes)).Append('\n');
            if (item.Style.Count > 0) output.Append("style ").Append(item.SourceId).Append(' ').Append(Css(item.Style)).Append('\n');
        }
        if (document.AccessibilityTitle.Length > 0) output.Append("accTitle: ").Append(document.AccessibilityTitle).Append('\n');
        if (document.AccessibilityDescription.Length > 0) output.Append("accDescr {\n").Append(document.AccessibilityDescription).Append("\n}\n");
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();

        void Columns(int? columns, int depth) => output.Append(' ', depth * 2).Append("columns ").Append(columns?.ToString(CultureInfo.InvariantCulture) ?? "auto").Append('\n');
        void Items(IEnumerable<BlockItem> items, int depth)
        {
            foreach (var item in items)
            {
                output.Append(' ', depth * 2);
                if (item.Kind == BlockItemKind.Space)
                    output.Append("space").Append(item.Span == 1 ? "" : ":" + item.Span).Append('\n');
                else if (item.Kind == BlockItemKind.Composite)
                {
                    output.Append("block");
                    if (item.SourceId is { } id)
                    {
                        output.Append(':').Append(id);
                        if (item.Label is not null) output.Append('[').Append(Quote(item.Label, item.Markdown)).Append(']');
                        if (item.Span != 1) output.Append(':').Append(item.Span);
                    }
                    output.Append('\n');
                    Columns(item.Columns, depth + 1); Items(item.Children, depth + 1);
                    output.Append(' ', depth * 2).Append("end\n");
                }
                else
                {
                    output.Append(item.SourceId);
                    if (item.Kind == BlockItemKind.Arrow)
                    {
                        output.Append("<[").Append(Quote(item.Display, item.Markdown)).Append("]>(");
                        output.Append(string.Join(",", new[] { BlockArrowDirections.Left, BlockArrowDirections.Right, BlockArrowDirections.Up, BlockArrowDirections.Down }
                            .Where(direction => item.Arrows.HasFlag(direction)).Select(direction => direction.ToString().ToLowerInvariant()))).Append(')');
                    }
                    else if (item.Label is not null || item.Shape != NodeShape.Rectangle)
                    {
                        var shape = FlowchartParser.ShapeForms.First(s => s.Shape == item.Shape);
                        output.Append(shape.Open).Append(Quote(item.Display, item.Markdown)).Append(shape.Close);
                    }
                    if (item.Span != 1) output.Append(':').Append(item.Span);
                    output.Append('\n');
                }
            }
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Blocks!; var before = Parse(source).Blocks!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source,
            Write(document).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "block", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, BlockDiagramDocument document)
    {
        var identities = new DataIdentity(source, "block");
        var entries = document.AllItems.Select(n => identities.Entry(n.Id, Signature(n))).ToList();
        entries.AddRange(document.Connections.Select(e => identities.Entry(e.Id, EdgeSignature(document, e))));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Signature(BlockItem item) => item.SourceId is { } id ? "named:" + id : item.Kind.ToString();
    private static string EdgeSignature(BlockDiagramDocument d, BlockConnection edge) =>
        "edge:" + d.AllItems.Single(n => n.Id == edge.From).SourceId + "\0" + d.AllItems.Single(n => n.Id == edge.To).SourceId + "\0" + Link(edge) + "\0" + edge.Label;
    private static string Quote(string text, bool markdown = false)
    {
        var value = text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("\r\n", "\n");
        if (!markdown) value = value.Replace("\n", "<br/>");
        return markdown ? "\"`" + value + "`\"" : "\"" + value + "\"";
    }
    private static string Css(JsonObject properties) => string.Join(",", properties.Select(p => p.Key + ":" + (p.Value is null ? "" : ChartConfiguration.Raw(p.Value))));
    private static string Link(BlockConnection edge)
    {
        if (edge.Stroke == BlockLinkStroke.Invisible) return "~~~";
        var start = edge.FromEnd switch { BlockLinkEnd.Arrow => "<", BlockLinkEnd.Circle => "o", BlockLinkEnd.Cross => "x", _ => "" };
        var end = edge.ToEnd switch { BlockLinkEnd.Arrow => ">", BlockLinkEnd.Circle => "o", BlockLinkEnd.Cross => "x", _ => "" };
        var line = edge.Stroke switch
        {
            BlockLinkStroke.Thick => start.Length + end.Length == 0 ? "===" : "==",
            BlockLinkStroke.Dotted => "-.-",
            _ => start.Length + end.Length == 0 ? "---" : "--",
        };
        return start + line + end;
    }
    public static void Check(BlockDiagramDocument document)
    {
        var issues = BlockDiagramValidation.Validate(document);
        if (issues.Any(d => d.Blocking)) throw new DataDiagramNotEditableException(issues);
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Block placement belongs to its ordered grid.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Use typed block connections.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    private sealed class Builder(BlockItem item)
    {
        public BlockItem Item { get; set; } = item;
        public List<Builder> Children { get; } = new();
        public BlockItem Finish() => Item with { Children = Children.Select(n => n.Finish()).ToArray() };
    }
    private sealed class Parser
    {
        private readonly string _text;
        private readonly DataIdentity _identity;
        private readonly BlockDiagramDocument _document = new();
        private readonly Dictionary<string, Builder> _named = new(StringComparer.Ordinal);
        private readonly List<(string[] Ids, string[] Classes, JsonObject? Style)> _styles = new();
        private int _position;
        public Parser(string source)
        {
            _text = MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n");
            _identity = new(source, "block");
            var settings = DataDiagramSupport.Configuration(source, "block");
            _document.Configuration = settings.Configuration; _document.ThemeVariables = settings.Theme; _document.GlobalConfiguration = settings.Global;
        }
        public DiagramModel Parse()
        {
            Skip();
            _document.Directive = Word();
            if (_document.Directive is not ("block" or "block-beta")) throw new FormatException("Expected block or block-beta.");
            var root = new Builder(new("root", BlockItemKind.Composite, []));
            Children(root, false);
            foreach (var assignment in _styles)
                foreach (var name in assignment.Ids)
                {
                    if (!_named.TryGetValue(name, out var item)) throw new FormatException("Block style targets an unknown identifier: " + name);
                    if (assignment.Style is { } style)
                    {
                        var merged = (JsonObject)item.Item.Style.DeepClone();
                        foreach (var pair in style) merged[pair.Key] = pair.Value?.DeepClone();
                        item.Item = item.Item with { Style = merged };
                    }
                    else item.Item = item.Item with { Classes = item.Item.Classes.Concat(assignment.Classes).Distinct().ToArray() };
                }
            _document.Columns = root.Item.Columns;
            _document.Items.AddRange(root.Children.Select(n => n.Finish()));
            for (var i = 0; i < _document.Connections.Count; i++)
                _document.Connections[i] = _document.Connections[i] with { Id = _identity.Next(EdgeSignature(_document, _document.Connections[i])) };
            Check(_document);
            return new() { Kind = _document.Directive, Blocks = _document };
        }
        private void Children(Builder parent, bool nested)
        {
            while (true)
            {
                Skip();
                if (_position >= _text.Length)
                {
                    if (nested) throw new FormatException("Composite block is missing end.");
                    return;
                }
                if (Keyword("end"))
                {
                    if (!nested) throw new FormatException("Unexpected block end.");
                    _position += 3; return;
                }
                if (Keyword("columns"))
                {
                    _position += 7; Skip(); var value = Word();
                    parent.Item = parent.Item with { Columns = value == "auto" ? null : int.Parse(value, CultureInfo.InvariantCulture) }; continue;
                }
                if (Keyword("accTitle") || Keyword("accDescr"))
                {
                    var title = Keyword("accTitle"); _position += 8; Skip();
                    if (_position < _text.Length && _text[_position] == '{')
                    {
                        var end = _text.IndexOf('}', ++_position); if (end < 0) throw new FormatException("Unclosed accessibility description.");
                        _document.AccessibilityDescription = _text[_position..end].Trim(); _position = end + 1;
                    }
                    else
                    {
                        Take(':'); var value = RestOfLine().Trim();
                        if (title) _document.AccessibilityTitle = value; else _document.AccessibilityDescription = value;
                    }
                    continue;
                }
                if (Keyword("style") || Keyword("class") || Keyword("classDef"))
                {
                    var statement = Word(); Skip(); var ids = Word().Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var value = RestOfLine().Trim().TrimEnd(';');
                    if (statement == "classDef") _document.Classes[ids.Single()] = ChartDiagramAdapter.ParseStyle(value);
                    else _styles.Add((ids, statement == "class" ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [],
                        statement == "style" ? ChartDiagramAdapter.ParseStyle(value) : null));
                    continue;
                }
                if (Keyword("block"))
                {
                    _position += 5;
                    Builder composite;
                    if (_position < _text.Length && _text[_position] == ':')
                    {
                        _position++; composite = Node(parent);
                        if (composite.Children.Count > 0) throw new FormatException("Composite is declared more than once.");
                        composite.Item = composite.Item with { Kind = BlockItemKind.Composite };
                    }
                    else
                    {
                        var item = new BlockItem(_identity.Next(BlockItemKind.Composite.ToString()), BlockItemKind.Composite, []);
                        composite = new(item); parent.Children.Add(composite);
                    }
                    Children(composite, true); continue;
                }
                if (Keyword("space"))
                {
                    _position += 5; var span = Span(out _);
                    parent.Children.Add(new(new(_identity.Next(BlockItemKind.Space.ToString()), BlockItemKind.Space, []) { Span = span })); continue;
                }
                var from = Node(parent);
                while (TryLink(out var connection))
                {
                    var to = Node(parent);
                    _document.Connections.Add(connection with { From = from.Item.Id, To = to.Item.Id });
                    from = to;
                }
            }
        }
        private Builder Node(Builder parent)
        {
            Skip();
            var match = Regex.Match(_text[_position..], @"^[^\s\(\)\[\]\{\}<>:=\-]+");
            if (!match.Success) throw new FormatException("Expected a block identifier near " + _text[_position..Math.Min(_text.Length, _position + 30)]);
            var name = match.Value; _position += match.Length;
            string? label = null; var markdown = false; var shape = NodeShape.Rectangle; var kind = BlockItemKind.Shape;
            var directions = BlockArrowDirections.None;
            if (At("<["))
            {
                _position += 2;
                (label, markdown) = Description("]>");
                Skip(); Take('('); var end = _text.IndexOf(')', _position);
                if (end < 0) throw new FormatException("Unclosed block-arrow directions.");
                foreach (var direction in _text[_position..end].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    directions |= direction switch
                    {
                        "x" => BlockArrowDirections.Left | BlockArrowDirections.Right,
                        "y" => BlockArrowDirections.Up | BlockArrowDirections.Down,
                        "left" => BlockArrowDirections.Left, "right" => BlockArrowDirections.Right, "up" => BlockArrowDirections.Up, "down" => BlockArrowDirections.Down,
                        _ => throw new FormatException("Unknown block-arrow direction: " + direction),
                    };
                _position = end + 1; kind = BlockItemKind.Arrow;
            }
            else
            {
                var forms = FlowchartParser.ShapeForms.Where(s => At(s.Open)).OrderByDescending(s => s.Open.Length).ToArray();
                foreach (var form in forms)
                {
                    var start = _position;
                    _position += form.Open.Length;
                    if (TryDescription(form.Close, out var parsed, out var md))
                    {
                        label = parsed; markdown = md; shape = form.Shape; break;
                    }
                    _position = start;
                }
                if (forms.Length > 0 && label is null) throw new FormatException("Unclosed block shape.");
            }
            var span = Span(out var explicitSpan);
            if (_named.TryGetValue(name, out var existing))
            {
                if (label is not null) existing.Item = existing.Item with { Label = label, Markdown = markdown, Shape = shape, Kind = kind, Arrows = directions };
                if (explicitSpan) existing.Item = existing.Item with { Span = span };
                return existing;
            }
            var item = new BlockItem(_identity.Next("named:" + name), kind, [])
            { SourceId = name, Label = label, Markdown = markdown, Shape = shape, Span = span, Arrows = directions };
            var builder = new Builder(item); _named.Add(name, builder); parent.Children.Add(builder); return builder;
        }
        private bool TryLink(out BlockConnection edge)
        {
            Skip();
            edge = new("", "", "");
            var match = Regex.Match(_text[_position..], @"^[<xo]?(?:-{2,}|={2,}|-\.+-|~{3,})[>xo]?");
            if (!match.Success) return false;
            _position += match.Length; var op = match.Value; Skip(); var label = "";
            if (_position < _text.Length && _text[_position] == '"')
            {
                label = Quoted(out _); Skip();
                match = Regex.Match(_text[_position..], @"^[<xo]?(?:-{2,}|={2,}|-\.+-)[>xo]?");
                if (!match.Success) throw new FormatException("A labeled block connection needs its closing link operator.");
                op = match.Value; _position += match.Length;
            }
            if (op is "--" or "==") throw new FormatException("Use a complete block connection operator.");
            edge = edge with
            {
                Label = label, FromEnd = End(op[0], true), ToEnd = End(op[^1], false),
                Stroke = op.Contains('~') ? BlockLinkStroke.Invisible : op.Contains('=') ? BlockLinkStroke.Thick : op.Contains('.') ? BlockLinkStroke.Dotted : BlockLinkStroke.Solid,
            };
            return true;
            static BlockLinkEnd End(char character, bool start) => character switch
            { 'o' => BlockLinkEnd.Circle, 'x' => BlockLinkEnd.Cross, '<' when start => BlockLinkEnd.Arrow, '>' when !start => BlockLinkEnd.Arrow, _ => BlockLinkEnd.None };
        }
        private (string Text, bool Markdown) Description(string close) =>
            TryDescription(close, out var text, out var markdown) ? (text, markdown) : throw new FormatException("Unclosed block description.");
        private bool TryDescription(string close, out string text, out bool markdown)
        {
            var start = _position; text = ""; markdown = false;
            if (_position < _text.Length && _text[_position] == '"')
            {
                text = Quoted(out markdown);
                if (!At(close)) { _position = start; return false; }
                _position += close.Length; return true;
            }
            var end = _text.IndexOf(close, _position, StringComparison.Ordinal);
            if (end < 0) return false;
            text = Decode(_text[_position..end]); _position = end + close.Length; return true;
        }
        private string Quoted(out bool markdown)
        {
            Take('"'); markdown = _position < _text.Length && _text[_position] == '`';
            if (markdown) _position++;
            var close = markdown ? "`\"" : "\"";
            var end = _text.IndexOf(close, _position, StringComparison.Ordinal);
            if (end < 0) throw new FormatException("Unclosed quoted block text.");
            var text = Decode(_text[_position..end]); _position = end + close.Length; return text;
        }
        private int Span(out bool specified)
        {
            specified = _position < _text.Length && _text[_position] == ':';
            if (!specified) return 1;
            _position++; var match = Regex.Match(_text[_position..], @"^\d+");
            if (!match.Success) throw new FormatException("Block span must be a positive integer.");
            _position += match.Length; return int.Parse(match.Value, CultureInfo.InvariantCulture);
        }
        private void Skip()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]) || _text[_position] == ';') { _position++; continue; }
                if (!At("%%")) return;
                _document.Comments.Add(RestOfLine());
            }
        }
        private bool Keyword(string value) => At(value) && (_position + value.Length == _text.Length ||
            char.IsWhiteSpace(_text[_position + value.Length]) || _text[_position + value.Length] is ':' or '{' or ';');
        private bool At(string value) => _text.AsSpan(_position).StartsWith(value, StringComparison.Ordinal);
        private void Take(char value)
        {
            if (_position >= _text.Length || _text[_position] != value) throw new FormatException("Expected '" + value + "'.");
            _position++;
        }
        private string Word()
        {
            var start = _position;
            while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) && _text[_position] != ';') _position++;
            if (_position == start) throw new FormatException("Expected a value.");
            return _text[start.._position];
        }
        private string RestOfLine()
        {
            var end = _text.IndexOf('\n', _position); if (end < 0) end = _text.Length;
            var text = _text[_position..end]; _position = end; return text;
        }
        private static string Decode(string text) => WebUtility.HtmlDecode(Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase));
    }
}
