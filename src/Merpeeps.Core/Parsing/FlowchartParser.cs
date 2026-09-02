using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Parser for the mermaid flowchart/graph subset.
///
/// <para>Design rule: <b>never lose input</b>. Any line we cannot model is
/// recorded verbatim in <see cref="DiagramModel.Passthrough"/> and re-emitted
/// on write. A visual editor that silently drops <c>classDef</c>, <c>click</c>
/// or <c>linkStyle</c> directives is not safe to point at a real file.</para>
/// </summary>
public static class FlowchartParser
{
    // flowchart TD / graph LR / flowchart-elk TB
    private static readonly Regex Header = new(
        @"^\s*(?<kind>flowchart(?:-elk)?|graph)\s+(?<dir>TB|TD|BT|LR|RL)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SubgraphOpen = new(
        @"^\s*subgraph\s+(?<id>[^\[\s]+)\s*(?:\[(?<title>[^\]]*)\])?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex SubgraphEnd = new(@"^\s*end\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DirectionLine = new(
        @"^\s*direction\s+(?<dir>TB|TD|BT|LR|RL)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The node-with-shape forms. Order matters: longest delimiters first.
    private static readonly (string Open, string Close, NodeShape Shape)[] Shapes =
    {
        ("([", "])", NodeShape.Stadium),
        ("[[", "]]", NodeShape.Subroutine),
        ("[(", ")]", NodeShape.Cylinder),
        ("(((", ")))", NodeShape.DoubleCircle),
        ("((", "))", NodeShape.Circle),
        ("{{", "}}", NodeShape.Hexagon),
        ("[/", "/]", NodeShape.Parallelogram),
        ("[\\", "\\]", NodeShape.ParallelogramAlt),
        ("[/", "\\]", NodeShape.Trapezoid),
        ("[\\", "/]", NodeShape.TrapezoidAlt),
        (">", "]", NodeShape.Asymmetric),
        ("(", ")", NodeShape.RoundedRect),
        ("{", "}", NodeShape.Rhombus),
        ("[", "]", NodeShape.Rectangle),
    };

    // Link operators, longest first so "-.->" is not read as "-".
    private static readonly (string Op, EdgeKind Kind)[] Links =
    {
        ("<-->", EdgeKind.Arrow),
        ("-.->", EdgeKind.DottedArrow),
        ("-.-",  EdgeKind.Dotted),
        ("==>",  EdgeKind.ThickArrow),
        ("===",  EdgeKind.Thick),
        ("-->",  EdgeKind.Arrow),
        ("---",  EdgeKind.Open),
        ("~~~",  EdgeKind.Invisible),
    };

    public static DiagramModel Parse(string source)
    {
        var model = new DiagramModel();

        // Keep frontmatter (which may carry Merpeeps) out of body parsing.
        var doc = MerpeepsDoc.Split(source);
        if (doc.Frontmatter.Count > 0)
        {
            var end = source.IndexOf("\n---", StringComparison.Ordinal);
            if (end > 0) model.Frontmatter = source[..(source.IndexOf("---\n",
                end, StringComparison.Ordinal) + 4)];
        }

        var lines = doc.Body.Replace("\r\n", "\n").Split('\n');
        model.LineCount = lines.Length;

        var subgraphStack = new Stack<DiagramSubgraph>();
        var headerSeen = false;
        var edgeOrdinals = new Dictionary<string, int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = StripInlineComment(raw).Trim();

            if (line.Length == 0) { Keep(model, i, raw); continue; }

            if (!headerSeen)
            {
                var h = Header.Match(line);
                if (h.Success)
                {
                    headerSeen = true;
                    model.Kind = h.Groups["kind"].Value.ToLowerInvariant();
                    var d = h.Groups["dir"].Value.ToUpperInvariant();
                    model.Direction = d switch
                    {
                        "LR" => Direction.LR,
                        "RL" => Direction.RL,
                        "BT" => Direction.BT,
                        _ => Direction.TB,
                    };
                    continue;
                }
            }

            var sg = SubgraphOpen.Match(line);
            if (sg.Success)
            {
                var s = new DiagramSubgraph
                {
                    Id = sg.Groups["id"].Value,
                    Title = sg.Groups["title"].Success
                        ? sg.Groups["title"].Value : null,
                };
                model.Subgraphs.Add(s);
                subgraphStack.Push(s);
                continue;
            }

            if (SubgraphEnd.IsMatch(line) && subgraphStack.Count > 0)
            {
                subgraphStack.Pop();
                continue;
            }

            if (DirectionLine.IsMatch(line) && subgraphStack.Count == 0)
            {
                var d = DirectionLine.Match(line).Groups["dir"].Value.ToUpperInvariant();
                model.Direction = d switch
                {
                    "LR" => Direction.LR, "RL" => Direction.RL,
                    "BT" => Direction.BT, _ => Direction.TB,
                };
                continue;
            }

            var baseOffset = LeadingWhitespace(StripInlineComment(raw));
            if (TryParseStatement(line, i, baseOffset, model, subgraphStack, edgeOrdinals))
                continue;

            Keep(model, i, raw);
        }

        return model;
    }

    private static void Keep(DiagramModel m, int line, string text) =>
        m.Passthrough[line] = text;

    private static string StripInlineComment(string line)
    {
        var idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    /// <summary>
    /// Parse a chain such as <c>A[Start] --&gt;|yes| B{Ok?} --&gt; C</c>.
    /// Returns false when the line is not a node/edge statement at all.
    /// </summary>
    private static bool TryParseStatement(
        string line, int lineNo, int baseOffset, DiagramModel model,
        Stack<DiagramSubgraph> subgraphs, Dictionary<string, int> ordinals)
    {
        var segments = SplitOnLinks(line);
        if (segments.Count == 0) return false;

        // A single segment is a bare node declaration; it must look like one.
        if (segments.Count == 1)
        {
            var only = segments[0].Text.Trim();
            if (only.Length == 0) return false;
            var node = ParseNodeRef(only, lineNo,
                baseOffset + segments[0].Offset + LeadingWhitespace(segments[0].Text),
                model, subgraphs);
            return node is not null;
        }

        DiagramNode? previous = null;
        for (var s = 0; s < segments.Count; s++)
        {
            var node = ParseNodeRef(segments[s].Text.Trim(), lineNo,
                baseOffset + segments[s].Offset + LeadingWhitespace(segments[s].Text),
                model, subgraphs);
            if (node is null) return false;

            if (previous is not null)
            {
                var link = segments[s].IncomingLink!;
                var key = $"{previous.Id}->{node.Id}";
                ordinals.TryGetValue(key, out var ord);
                ordinals[key] = ord + 1;

                model.Edges.Add(new DiagramEdge
                {
                    From = previous.Id,
                    To = node.Id,
                    Kind = link.Kind,
                    Label = link.Label,
                    SourceLine = lineNo,
                    OpStart = baseOffset + link.Start,
                    OpLength = link.Length,
                    Ordinal = ord,
                });
            }
            previous = node;
        }
        return true;
    }

    private sealed record Segment(string Text, LinkInfo? IncomingLink, int Offset);

    /// <param name="Kind">Which arrow was written.</param>
    /// <param name="Label">Its label, if any.</param>
    /// <param name="Start">Where the operator begins in the line.</param>
    /// <param name="Length">How many characters it spans, label included.</param>
    /// <remarks>
    /// The span is what lets a relabel rewrite one operator instead of
    /// regenerating the document. Without it, changing an arrow's text reflowed
    /// every line in the file — unusable in a git repository, which is the whole
    /// reason the surgical writer exists.
    /// </remarks>
    private sealed record LinkInfo(EdgeKind Kind, string? Label, int Start, int Length);

    /// <summary>Split a statement into node segments separated by link operators.</summary>
    private static List<Segment> SplitOnLinks(string line)
    {
        var result = new List<Segment>();
        var buf = new System.Text.StringBuilder();
        LinkInfo? pending = null;
        var depth = 0;
        var segStart = 0;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c is '[' or '(' or '{') depth++;
            else if (c is ']' or ')' or '}') depth = Math.Max(0, depth - 1);

            if (depth == 0)
            {
                var link = MatchLink(line, i, out var consumed, out var label);
                if (link is not null)
                {
                    result.Add(new Segment(buf.ToString(), pending, segStart));
                    buf.Clear();
                    pending = new LinkInfo(link.Value, label, i, consumed);
                    i += consumed - 1;
                    segStart = i + 1;
                    continue;
                }
            }
            buf.Append(c);
        }

        if (buf.Length > 0 || result.Count > 0)
            result.Add(new Segment(buf.ToString(), pending, segStart));

        return result;
    }

    /// <summary>
    /// Match a link operator at <paramref name="i"/>, including the two label
    /// spellings: <c>--&gt;|text|</c> and <c>-- text --&gt;</c>.
    /// </summary>
    private static EdgeKind? MatchLink(string s, int i, out int consumed, out string? label)
    {
        consumed = 0; label = null;

        // -- text --> / -. text .-> / == text ==>
        var mid = Regex.Match(s[i..], @"^(--|-\.|==)\s+(?<lbl>[^-=\.\|]+?)\s+(?<op>-->|---|-\.->|-\.-|==>|===)");
        if (mid.Success)
        {
            consumed = mid.Length;
            label = mid.Groups["lbl"].Value.Trim();
            return KindOf(mid.Groups["op"].Value);
        }

        foreach (var (op, kind) in Links)
        {
            if (i + op.Length > s.Length) continue;
            if (string.CompareOrdinal(s, i, op, 0, op.Length) != 0) continue;

            consumed = op.Length;

            // optional |label| immediately after the operator
            var rest = s[(i + op.Length)..];

            // The quoted form is tried first, because the unquoted pattern stops
            // at the first pipe — on |"a|b"| it would match |"a| and take the
            // label as `"a`, silently truncating it and leaving `b"|` to be
            // parsed as a node.
            var quoted = Regex.Match(rest, @"^\s*\|\s*""(?<lbl>[^""]*)""\s*\|");
            if (quoted.Success)
            {
                label = Unescape(quoted.Groups["lbl"].Value);
                consumed += quoted.Length;
                return kind;
            }

            var pipe = Regex.Match(rest, @"^\s*\|(?<lbl>[^|]*)\|");
            if (pipe.Success)
            {
                label = pipe.Groups["lbl"].Value.Trim();
                consumed += pipe.Length;
            }
            return kind;
        }
        return null;
    }

    private static EdgeKind KindOf(string op) => op switch
    {
        "-.->" => EdgeKind.DottedArrow,
        "-.-" => EdgeKind.Dotted,
        "==>" => EdgeKind.ThickArrow,
        "===" => EdgeKind.Thick,
        "---" => EdgeKind.Open,
        _ => EdgeKind.Arrow,
    };

    /// <summary>Parse "A", "A[Label]", "A@{ shape: rect }" into a node.</summary>
    private static DiagramNode? ParseNodeRef(
        string text, int lineNo, int declStart, DiagramModel model,
        Stack<DiagramSubgraph> subgraphs)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        // 1. @{ ... } metadata form
        var at = text.IndexOf("@{", StringComparison.Ordinal);
        if (at > 0 && text.EndsWith('}'))
        {
            var metaId = text[..at].Trim();
            if (!IsIdentifier(metaId)) return null;

            var body = text[(at + 2)..^1];
            string? metaLabel = null;
            var metaShape = NodeShape.Rectangle;

            var lm = Regex.Match(body, @"label\s*:\s*""?(?<v>[^"",}]+)""?");
            if (lm.Success) metaLabel = lm.Groups["v"].Value.Trim();
            var sm = Regex.Match(body, @"shape\s*:\s*""?(?<v>[A-Za-z-]+)""?");
            if (sm.Success) metaShape = ShapeFromName(sm.Groups["v"].Value);

            return Upsert(metaId, metaLabel, metaShape, hasShape: sm.Success);
        }

        // 2. shaped form: id[Label], id{Label}, id([Label]) ...
        foreach (var (open, close, sh) in Shapes)
        {
            var o = text.IndexOf(open, StringComparison.Ordinal);
            if (o <= 0 || !text.EndsWith(close, StringComparison.Ordinal)) continue;
            if (text.Length < o + open.Length + close.Length) continue;

            var shapedId = text[..o].Trim();
            if (!IsIdentifier(shapedId)) return null;

            var inner = text[(o + open.Length)..^close.Length];
            return Upsert(shapedId, Unquote(inner), sh, hasShape: true);
        }

        // 3. bare identifier
        if (!IsIdentifier(text)) return null;
        return Upsert(text, null, NodeShape.Rectangle, hasShape: false);

        DiagramNode Upsert(string nid, string? lbl, NodeShape sh, bool hasShape)
        {
            var existing = model.Node(nid);
            if (existing is null)
            {
                existing = new DiagramNode { Id = nid, SourceLine = lineNo };
                model.Nodes.Add(existing);
                if (subgraphs.Count > 0)
                {
                    var sg = subgraphs.Peek();
                    existing.SubgraphId = sg.Id;
                    sg.NodeIds.Add(nid);
                }
            }
            // A later, richer declaration wins over a bare reference.
            if (lbl is not null) existing.Label = lbl;
            if (hasShape) existing.Shape = sh;
            if (lbl is not null || hasShape)
            {
                existing.SourceLine = lineNo;
                existing.DeclStart = declStart;
                existing.DeclLength = text.Length;
            }
            return existing;
        }
    }

    private static int LeadingWhitespace(string s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    private static bool IsIdentifier(string s) =>
        s.Length > 0 && Regex.IsMatch(s, @"^[A-Za-z0-9_\-\.]+$");

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return Unescape(s).Trim();
    }

    /// <summary>
    /// Decode the HTML entities the writer emits inside a quoted label.
    /// </summary>
    /// <remarks>
    /// The writer escapes a quote as <c>&amp;quot;</c> because a raw one would
    /// close the pair. Without the matching decode a label survived one save and
    /// then gained a literal <c>&amp;quot;</c> on the next — the asymmetry only
    /// shows on the second round trip, which is why it went unnoticed.
    /// </remarks>
    private static string Unescape(string s) =>
        s.Contains('&') ? s.Replace("&quot;", "\"").Replace("&amp;", "&") : s;

    private static NodeShape ShapeFromName(string n) => n.ToLowerInvariant() switch
    {
        "rounded" or "round-rect" => NodeShape.RoundedRect,
        "stadium" or "pill" => NodeShape.Stadium,
        "subroutine" or "fr-rect" => NodeShape.Subroutine,
        "cylinder" or "cyl" or "database" or "db" => NodeShape.Cylinder,
        "circle" or "circ" => NodeShape.Circle,
        "double-circle" or "dbl-circ" => NodeShape.DoubleCircle,
        "diamond" or "decision" or "diam" => NodeShape.Rhombus,
        "hexagon" or "hex" => NodeShape.Hexagon,
        "lean-r" or "lean-right" => NodeShape.Parallelogram,
        "lean-l" or "lean-left" => NodeShape.ParallelogramAlt,
        "trap-b" or "trapezoid" => NodeShape.Trapezoid,
        "trap-t" or "trapezoid-top" => NodeShape.TrapezoidAlt,
        _ => NodeShape.Rectangle,
    };
}


