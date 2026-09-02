using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Serialises a <see cref="DiagramModel"/> back to mermaid text.
///
/// <para>The contract is <b>semantic</b> round-trip, not byte-identical output:
/// parsing the result yields an equivalent model, and every line the parser did
/// not understand is re-emitted verbatim. Byte-identity is neither achievable
/// nor needed, because position edits live in frontmatter and never touch the
/// body at all.</para>
/// </summary>
public static class FlowchartWriter
{
    public static string Write(DiagramModel m)
    {
        var sb = new StringBuilder();
        var dir = m.Direction switch
        {
            Direction.LR => "LR", Direction.RL => "RL",
            Direction.BT => "BT", _ => "TD",
        };
        sb.Append(m.Kind).Append(' ').Append(dir).Append('\n');

        var emitted = new HashSet<string>();

        foreach (var sg in m.Subgraphs)
        {
            sb.Append("    subgraph ").Append(sg.Id);
            if (!string.IsNullOrEmpty(sg.Title)) sb.Append(" [").Append(sg.Title).Append(']');
            sb.Append('\n');
            foreach (var id in sg.NodeIds)
            {
                var n = m.Node(id);
                if (n is null || !emitted.Add(id)) continue;
                sb.Append("        ").Append(Declare(n)).Append('\n');
            }
            sb.Append("    end\n");
        }

        foreach (var n in m.Nodes)
        {
            if (!emitted.Add(n.Id)) continue;
            // A bare id with no label carries no information on its own line;
            // it will appear in an edge instead.
            if (n.Label is null && n.Shape == NodeShape.Rectangle
                && m.Edges.Any(e => e.From == n.Id || e.To == n.Id))
                continue;
            sb.Append("    ").Append(Declare(n)).Append('\n');
        }

        foreach (var e in m.Edges)
        {
            sb.Append("    ").Append(e.From).Append(' ').Append(LinkOp(e.Kind));
            if (!string.IsNullOrEmpty(e.Label)) sb.Append('|').Append(e.Label).Append('|');
            sb.Append(' ').Append(e.To).Append('\n');
        }

        foreach (var (_, text) in m.Passthrough)
        {
            if (text.Trim().Length == 0) continue;
            sb.Append(text.TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    internal static string Declare(DiagramNode n)
    {
        if (n.Label is null && n.Shape == NodeShape.Rectangle) return n.Id;
        var label = n.Label ?? n.Id;
        if (NeedsQuotes(label)) label = '"' + label.Replace("\"", "&quot;") + '"';
        var (o, c) = Delimiters(n.Shape);
        return $"{n.Id}{o}{label}{c}";
    }

    /// <summary>Render one connection in flowchart syntax.</summary>
    internal static string EdgeStatement(DiagramEdge e)
    {
        var op = LinkOp(e.Kind);
        if (string.IsNullOrEmpty(e.Label)) return $"{e.From} {op} {e.To}";

        // A pipe inside the label would close the pair early and the link would
        // be lost — it parses back as a different, broken statement. Quoting the
        // label is what mermaid itself accepts here.
        var label = e.Label.Contains('|') ? Quote(e.Label) : e.Label;
        return $"{e.From} {op}|{label}| {e.To}";
    }

    /// <summary>
    /// Wrap a label in quotes so its punctuation is taken literally.
    /// </summary>
    /// <remarks>
    /// A quote inside becomes <c>&amp;quot;</c>, the same escape the node writer
    /// uses — mermaid reads HTML entities in labels, and a raw quote would
    /// close the pair.
    /// </remarks>
    private static string Quote(string label) =>
        '"' + label.Replace("\"", "&quot;") + '"';

    /// <summary>
    /// Just the operator, for patching one link without touching its endpoints.
    /// </summary>
    /// <remarks>
    /// Quotes a label containing a pipe for the same reason
    /// <see cref="EdgeStatement"/> does: an unquoted <c>|</c> closes the pipe
    /// pair early and the link parses back as something else entirely.
    /// </remarks>
    internal static string LinkOperator(DiagramEdge e)
    {
        var op = LinkOp(e.Kind);
        if (string.IsNullOrEmpty(e.Label)) return op;

        var label = e.Label.Contains('|') ? Quote(e.Label) : e.Label;
        return $"{op}|{label}|";
    }

    private static bool NeedsQuotes(string label) =>
        label.IndexOfAny(new[] { '[', ']', '(', ')', '{', '}', '|', '"' }) >= 0;

    private static (string, string) Delimiters(NodeShape s) => s switch
    {
        NodeShape.RoundedRect => ("(", ")"),
        NodeShape.Stadium => ("([", "])"),
        NodeShape.Subroutine => ("[[", "]]"),
        NodeShape.Cylinder => ("[(", ")]"),
        NodeShape.Circle => ("((", "))"),
        NodeShape.DoubleCircle => ("(((", ")))"),
        NodeShape.Rhombus => ("{", "}"),
        NodeShape.Hexagon => ("{{", "}}"),
        NodeShape.Parallelogram => ("[/", "/]"),
        NodeShape.ParallelogramAlt => ("[\\", "\\]"),
        NodeShape.Trapezoid => ("[/", "\\]"),
        NodeShape.TrapezoidAlt => ("[\\", "/]"),
        NodeShape.Asymmetric => (">", "]"),
        _ => ("[", "]"),
    };

    private static string LinkOp(EdgeKind k) => k switch
    {
        EdgeKind.Open => "---",
        EdgeKind.Dotted => "-.-",
        EdgeKind.DottedArrow => "-.->",
        EdgeKind.Thick => "===",
        EdgeKind.ThickArrow => "==>",
        EdgeKind.Invisible => "~~~",
        _ => "-->",
    };
}

