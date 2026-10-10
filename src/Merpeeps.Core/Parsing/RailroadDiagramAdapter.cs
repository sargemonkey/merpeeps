using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class RailroadDiagramAdapter : IDiagramAdapter
{
    public string Kind => "railroad-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) is "railroad-beta" or "railroad-ebnf-beta" or "railroad-abnf-beta" or "railroad-peg-beta";
    public DiagramModel Parse(string source) => new Parser(source).Parse();
    public string Write(DiagramModel model) => Write(model.Railroad ?? throw new ArgumentException("Railroad content is missing."));
    public static string Write(RailroadDiagramDocument document)
    {
        RailroadValidation.Check(document);
        var output = new StringBuilder(document.Directive).Append('\n');
        if (document.Title.Length > 0) output.Append("title ").Append(Quote(document.Title)).Append('\n');
        if (document.AccessibilityTitle.Length > 0) output.Append("accTitle: ").Append(document.AccessibilityTitle).Append('\n');
        if (document.AccessibilityDescription.Length > 0) output.Append("accDescr: ").Append(document.AccessibilityDescription).Append('\n');
        foreach (var rule in document.Rules)
            output.Append(rule.Name).Append(document.Notation == RailroadNotation.Peg ? " <- " : " = ").Append(Expression(rule.Expression, document.Notation)).Append(" ;\n");
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    public static string Expression(RailroadExpression expression, RailroadNotation notation)
    {
        var children = expression.Children;
        string Child(int i) => Expression(children[i], notation);
        if (notation == RailroadNotation.Primitives)
            return expression.Kind switch
            {
                RailroadExpressionKind.Terminal => "terminal(" + Quote(expression.Text) + ")",
                RailroadExpressionKind.Nonterminal => "nonterminal(" + Quote(expression.Text) + ")",
                RailroadExpressionKind.Special => "special(" + Quote(expression.Text) + ")",
                RailroadExpressionKind.Sequence => "sequence(" + string.Join(", ", children.Select(c => Expression(c, notation))) + ")",
                RailroadExpressionKind.Choice => "choice(" + string.Join(", ", children.Select(c => Expression(c, notation))) + ")",
                RailroadExpressionKind.Optional => "optional(" + Child(0) + ")",
                RailroadExpressionKind.Repeat when expression.Minimum == 0 && expression.Maximum is null => "zeroOrMore(" + Child(0) + ")",
                RailroadExpressionKind.Repeat when expression.Minimum == 1 && expression.Maximum is null => "oneOrMore(" + Child(0) + ")",
                RailroadExpressionKind.Group => Child(0),
                _ => throw new NotSupportedException("This expression cannot be written with the documented Railroad primitive constructors."),
            };
        return expression.Kind switch
        {
            RailroadExpressionKind.Terminal => Quote(expression.Text),
            RailroadExpressionKind.Nonterminal or RailroadExpressionKind.NumericLiteral => expression.Text,
            RailroadExpressionKind.AnyCharacter => ".",
            RailroadExpressionKind.Sequence => string.Join(" ", children.Select(c => Expression(c, notation))),
            RailroadExpressionKind.Choice or RailroadExpressionKind.OrderedChoice => "(" + string.Join(notation == RailroadNotation.Ebnf ? " | " : " / ", children.Select(c => Expression(c, notation))) + ")",
            RailroadExpressionKind.Group => "(" + Child(0) + ")",
            RailroadExpressionKind.Optional => notation == RailroadNotation.Abnf ? "[ " + Child(0) + " ]" : "(" + Child(0) + ")?",
            RailroadExpressionKind.Repeat when notation == RailroadNotation.Abnf =>
                (expression.Maximum == expression.Minimum ? expression.Minimum.ToString(CultureInfo.InvariantCulture) :
                    (expression.Minimum == 0 ? "" : expression.Minimum.ToString(CultureInfo.InvariantCulture)) + "*" + expression.Maximum?.ToString(CultureInfo.InvariantCulture))
                + "(" + Child(0) + ")",
            RailroadExpressionKind.Repeat when expression.Maximum is null && expression.Minimum is 0 or 1 => "(" + Child(0) + ")" + (expression.Minimum == 0 ? "*" : "+"),
            RailroadExpressionKind.Special => "? " + expression.Text + " ?",
            RailroadExpressionKind.Exception => "(" + Child(0) + ") - (" + Child(1) + ")",
            RailroadExpressionKind.AndPredicate => "&(" + Child(0) + ")",
            RailroadExpressionKind.NotPredicate => "!(" + Child(0) + ")",
            _ => throw new NotSupportedException("The selected Railroad notation cannot express this construct."),
        };
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Railroad!; var before = Parse(source).Railroad!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "railroad", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, RailroadDiagramDocument document)
    {
        var identity = new DataIdentity(source, "railroad");
        var entries = new List<object>();
        foreach (var rule in document.Rules)
        {
            entries.Add(identity.Entry(rule.Id, "rule:" + rule.Name));
            foreach (var expression in RailroadDiagramDocument.Walk(rule.Expression))
                entries.Add(identity.Entry(expression.Id, Signature(rule.Id, expression)));
        }
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Signature(string ruleId, RailroadExpression e) => ruleId + "\0" + e.Kind + "\0" + e.Text + "\0" + e.Minimum + "\0" + e.Maximum;
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Railroad symbols belong to a grammar expression tree.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Railroad paths are derived from grammar constructs.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    private sealed record Token(string Kind, string Text);
    private sealed class Parser
    {
        private readonly string _body;
        private readonly DataIdentity _identity;
        private readonly RailroadDiagramDocument _document = new();
        private readonly List<Token> _tokens = new();
        private string _ruleId = "";
        private int _at;
        public Parser(string source)
        {
            _body = MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n"); _identity = new(source, "railroad");
            var settings = DataDiagramSupport.Configuration(source, "railroad");
            _document.Configuration = settings.Configuration; _document.ThemeVariables = settings.Theme; _document.GlobalConfiguration = settings.Global;
        }
        public DiagramModel Parse()
        {
            var body = new StringBuilder(); var header = false;
            foreach (var raw in _body.Split('\n'))
            {
                var line = raw.Trim();
                if (!header && line.Length == 0) continue;
                if (!header && line.StartsWith("%%", StringComparison.Ordinal)) { _document.Comments.Add(line); continue; }
                if (!header)
                {
                    _document.Notation = line switch
                    {
                        "railroad-ebnf-beta" => RailroadNotation.Ebnf, "railroad-abnf-beta" => RailroadNotation.Abnf,
                        "railroad-peg-beta" => RailroadNotation.Peg, "railroad-beta" => RailroadNotation.Primitives,
                        _ => throw new FormatException("Expected a Railroad notation directive."),
                    };
                    header = true; continue;
                }
                if (line.StartsWith("title ", StringComparison.Ordinal)) { _document.Title = DecodeDirective(line[6..]); continue; }
                if (line.StartsWith("accTitle:", StringComparison.Ordinal)) { _document.AccessibilityTitle = line[9..].Trim(); continue; }
                if (line.StartsWith("accDescr:", StringComparison.Ordinal)) { _document.AccessibilityDescription = line[9..].Trim(); continue; }
                body.Append(raw).Append('\n');
            }
            if (!header) throw new FormatException("Railroad header is missing.");
            Lex(body.ToString());
            while (Peek.Kind != "eof")
            {
                if (TakeIf(";")) continue;
                var name = Take("id").Text; _ruleId = _identity.Next("rule:" + name);
                if (_document.Notation == RailroadNotation.Peg) Take("<-"); else if (!TakeIf("::=")) Take("=");
                var expression = _document.Notation == RailroadNotation.Primitives ? Primitive() : Choice();
                Take(";"); _document.Rules.Add(new(_ruleId, name, Identify(expression)));
            }
            RailroadValidation.Check(_document);
            return new() { Kind = _document.Directive, Railroad = _document };
        }
        private RailroadExpression Choice()
        {
            var delimiter = _document.Notation == RailroadNotation.Ebnf ? "|" : "/";
            var alternatives = new List<RailroadExpression> { Sequence() };
            while (TakeIf(delimiter)) alternatives.Add(Sequence());
            return alternatives.Count == 1 ? alternatives[0] : Node(_document.Notation == RailroadNotation.Peg ? RailroadExpressionKind.OrderedChoice : RailroadExpressionKind.Choice, "", alternatives);
        }
        private RailroadExpression Sequence()
        {
            var children = new List<RailroadExpression>();
            while (Peek.Kind is not ("eof" or ";" or ")" or "]" or "}" or "|" or "/"))
            {
                if (_document.Notation == RailroadNotation.Ebnf && TakeIf(",")) continue;
                children.Add(Term());
            }
            if (children.Count == 0) throw new FormatException("Railroad sequences need an expression.");
            return children.Count == 1 ? children[0] : Node(RailroadExpressionKind.Sequence, "", children);
        }
        private RailroadExpression Term()
        {
            if (_document.Notation == RailroadNotation.Peg && Peek.Kind is "&" or "!")
            {
                var kind = Next().Kind == "&" ? RailroadExpressionKind.AndPredicate : RailroadExpressionKind.NotPredicate;
                return Node(kind, "", [Term()]);
            }
            if (_document.Notation == RailroadNotation.Abnf && Peek.Kind is "number" or "*")
            {
                var minimum = Peek.Kind == "number" ? int.Parse(Next().Text, CultureInfo.InvariantCulture) : 0;
                int? maximum = minimum;
                if (TakeIf("*")) maximum = Peek.Kind == "number" ? int.Parse(Next().Text, CultureInfo.InvariantCulture) : null;
                return Repeat(Primary(), minimum, maximum);
            }
            var expression = Primary();
            if (_document.Notation == RailroadNotation.Abnf) return expression;
            while (true)
            {
                if (TakeIf("?")) expression = Node(RailroadExpressionKind.Optional, "", [expression]);
                else if (TakeIf("*")) expression = Repeat(expression, 0, null);
                else if (TakeIf("+")) expression = Repeat(expression, 1, null);
                else if (_document.Notation == RailroadNotation.Ebnf && TakeIf("-")) expression = Node(RailroadExpressionKind.Exception, "", [expression, Primary()]);
                else break;
            }
            return expression;
        }
        private RailroadExpression Primary()
        {
            if (Peek.Kind == "string") return Node(RailroadExpressionKind.Terminal, Next().Text, []);
            if (Peek.Kind == "numeric" && _document.Notation == RailroadNotation.Abnf) return Node(RailroadExpressionKind.NumericLiteral, Next().Text, []);
            if (Peek.Kind == "id") return Node(RailroadExpressionKind.Nonterminal, Next().Text, []);
            if (TakeIf("(")) { var inner = Choice(); Take(")"); return inner; }
            if (TakeIf("["))
            {
                if (_document.Notation == RailroadNotation.Peg) throw new NotSupportedException("PEG character classes are outside the documented native notation.");
                var inner = Choice(); Take("]"); return Node(RailroadExpressionKind.Optional, "", [inner]);
            }
            if (_document.Notation == RailroadNotation.Ebnf && TakeIf("{")) { var inner = Choice(); Take("}"); return Repeat(inner, 0, null); }
            if (_document.Notation == RailroadNotation.Peg && TakeIf(".")) return Node(RailroadExpressionKind.AnyCharacter, "", []);
            if (_document.Notation == RailroadNotation.Ebnf && TakeIf("?"))
            {
                var text = new List<string>(); while (Peek.Kind is not ("?" or "eof")) text.Add(Next().Text);
                Take("?"); return Node(RailroadExpressionKind.Special, string.Join(" ", text), []);
            }
            throw new FormatException("Unexpected Railroad expression token: " + Peek.Text);
        }
        private RailroadExpression Primitive()
        {
            var name = Take("id").Text; Take("(");
            if (name is "terminal" or "nonterminal" or "special")
            {
                var text = Take("string").Text; Take(")");
                return Node(name == "terminal" ? RailroadExpressionKind.Terminal : name == "nonterminal" ? RailroadExpressionKind.Nonterminal : RailroadExpressionKind.Special, text, []);
            }
            var children = new List<RailroadExpression>();
            if (Peek.Kind != ")") { children.Add(Primitive()); while (TakeIf(",")) children.Add(Primitive()); }
            Take(")");
            if (name is "sequence" or "choice" && children.Count == 1) return children[0];
            return name switch
            {
                "sequence" => Node(RailroadExpressionKind.Sequence, "", children),
                "choice" => Node(RailroadExpressionKind.Choice, "", children),
                "optional" when children.Count == 1 => Node(RailroadExpressionKind.Optional, "", children),
                "zeroOrMore" when children.Count == 1 => Repeat(children[0], 0, null),
                "oneOrMore" when children.Count == 1 => Repeat(children[0], 1, null),
                _ => throw new NotSupportedException("Unsupported Railroad constructor or argument count: " + name),
            };
        }
        private RailroadExpression Repeat(RailroadExpression child, int minimum, int? maximum)
        {
            var expression = new RailroadExpression("", RailroadExpressionKind.Repeat, "", [child]) { Minimum = minimum, Maximum = maximum };
            return expression;
        }
        private RailroadExpression Node(RailroadExpressionKind kind, string text, IReadOnlyList<RailroadExpression> children)
        {
            var node = new RailroadExpression("", kind, text, children);
            return node;
        }
        private RailroadExpression Identify(RailroadExpression expression)
        {
            var id = _identity.Next(Signature(_ruleId, expression));
            return expression with { Id = id, Children = expression.Children.Select(Identify).ToArray() };
        }
        private Token Peek => _tokens[_at];
        private Token Next() => _tokens[_at++];
        private bool TakeIf(string kind) { if (Peek.Kind != kind) return false; _at++; return true; }
        private Token Take(string kind) => Peek.Kind == kind ? Next() : throw new FormatException("Expected " + kind + ", found " + Peek.Text + ".");
        private static string DecodeDirective(string value)
        {
            value = value.Trim(); return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
        }
        private void Lex(string text)
        {
            for (var i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                if (text.AsSpan(i).StartsWith("%%") || _document.Notation == RailroadNotation.Peg && text[i] == '#')
                {
                    var end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                    _document.Comments.Add("%% " + text[i..end].TrimStart('%', '#', ' ')); i = end; continue;
                }
                if (_document.Notation == RailroadNotation.Ebnf && (text.AsSpan(i).StartsWith("/*") || text.AsSpan(i).StartsWith("(*")))
                {
                    var close = text[i] == '/' ? "*/" : "*)"; var end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                    if (end < 0) throw new FormatException("Unclosed EBNF comment.");
                    foreach (var line in text[(i + 2)..end].Split('\n')) _document.Comments.Add("%% " + line);
                    i = end + 2; continue;
                }
                if (text[i] is '"' or '\'')
                {
                    var quote = text[i++]; var value = new StringBuilder(); var closed = false;
                    while (i < text.Length)
                    {
                        var c = text[i++];
                        if (c == quote) { closed = true; break; }
                        if (c == '\\' && i < text.Length)
                        {
                            var next = text[i++]; c = next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => next };
                        }
                        value.Append(c);
                    }
                    if (!closed) throw new FormatException("Unclosed Railroad literal.");
                    _tokens.Add(new("string", value.ToString())); continue;
                }
                if (_document.Notation == RailroadNotation.Abnf && text[i] == ';')
                {
                    _tokens.Add(new(";", ";")); var end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                    if (text[(i + 1)..end].Trim().Length > 0) _document.Comments.Add("%% " + text[(i + 1)..end].Trim());
                    i = end; continue;
                }
                var numeric = Regex.Match(text[i..], @"^%[xXdDbB][0-9a-fA-F]+(?:[.-][0-9a-fA-F]+)*");
                if (numeric.Success) { _tokens.Add(new("numeric", numeric.Value)); i += numeric.Length; continue; }
                var name = Regex.Match(text[i..], @"^[\p{L}_][\p{L}\p{N}_-]*");
                if (name.Success) { _tokens.Add(new("id", name.Value)); i += name.Length; continue; }
                var number = Regex.Match(text[i..], @"^\d+");
                if (number.Success) { _tokens.Add(new("number", number.Value)); i += number.Length; continue; }
                var symbol = text.AsSpan(i).StartsWith("::=") ? "::=" : text.AsSpan(i).StartsWith("<-") ? "<-" : text[i].ToString();
                if (!new[] { "::=", "<-", "=", "|", "/", "(", ")", "[", "]", "{", "}", "?", "*", "+", "-", "&", "!", ".", ",", ";" }.Contains(symbol))
                    throw new FormatException("Unexpected Railroad character: " + text[i]);
                _tokens.Add(new(symbol, symbol)); i += symbol.Length;
            }
            _tokens.Add(new("eof", ""));
        }
    }
}
