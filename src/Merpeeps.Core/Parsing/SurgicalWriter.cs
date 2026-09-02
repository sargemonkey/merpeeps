using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Writes structural edits back into an existing document by patching only the
/// spans that changed.
///
/// <para>Wholesale regeneration is correct but produces a diff touching every
/// line, which defeats the point of storing diagrams in git. This rewrites a
/// renamed node in place and appends genuinely new statements, leaving every
/// untouched line byte-identical.</para>
/// </summary>
public static class SurgicalWriter
{
    /// <summary>
    /// Apply <paramref name="edited"/> onto the body of <paramref name="source"/>.
    /// Falls back to full regeneration when the change cannot be expressed as a
    /// set of local patches.
    /// </summary>
    public static string Apply(string source, DiagramModel edited)
    {
        var adapter = DiagramAdapters.For(source);
        var original = adapter.Parse(source);
        var doc = MerpeepsDoc.Split(source);

        // Work line-by-line in LF, then rejoin with whatever the document used.
        // A structural edit rewrites body lines by design, so per-line fidelity
        // is not achievable here — but flipping a CRLF file to LF would turn a
        // one-node rename into a whole-file diff, which is.
        var ending = doc.Body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var body = doc.Body.Replace("\r\n", "\n");
        var lines = body.Split('\n').ToList();

        // Per-line replacements, applied right-to-left so earlier spans keep
        // their offsets.
        var edits = new List<(int Line, int Start, int Length, string Text)>();
        var appended = new List<string>();

        // --- nodes -----------------------------------------------------------
        foreach (var node in edited.Nodes)
        {
            var before = original.Node(node.Id);
            if (before is null)
            {
                var decl = adapter.RenderNodeStatement(node);
                if (decl is not null) appended.Add("    " + decl);
                continue;
            }
            if (before.Label == node.Label && before.Shape == node.Shape) continue;

            if (before.DeclStart < 0 || before.SourceLine < 0 ||
                before.SourceLine >= lines.Count)
                return FullRegenerate(source, edited);   // cannot patch precisely

            var replacement = adapter.RenderNodeStatement(node);
            if (replacement is null) return FullRegenerate(source, edited);
            edits.Add((before.SourceLine, before.DeclStart, before.DeclLength,
                       replacement));
        }

        // --- deleted nodes ---------------------------------------------------
        var liveIds = edited.Nodes.Select(n => n.Id).ToHashSet();
        if (original.Nodes.Any(n => !liveIds.Contains(n.Id)))
            return FullRegenerate(source, edited);       // removals reshape lines

        // --- edges -----------------------------------------------------------
        //
        // Matched by identity — endpoints plus ordinal — rather than by content,
        // so a link whose label or arrow style changed is recognised as the same
        // link and patched in place. Matching on content instead made every edit
        // look like "one link vanished and a different one appeared", which fell
        // back to regenerating the document: a retitled arrow reflowed the whole
        // file, which is exactly what this writer exists to prevent.
        var originalEdges = original.Edges.ToDictionary(e => (e.From, e.To, e.Ordinal));
        var editedEdges = edited.Edges.ToDictionary(e => (e.From, e.To, e.Ordinal));

        // A removal still reshapes lines, so it keeps the regeneration path.
        if (originalEdges.Keys.Any(k => !editedEdges.ContainsKey(k)))
            return FullRegenerate(source, edited);

        foreach (var e in edited.Edges)
        {
            if (!originalEdges.TryGetValue((e.From, e.To, e.Ordinal), out var before))
            {
                appended.Add("    " + adapter.RenderEdgeStatement(e));
                continue;
            }

            if (before.Label == e.Label && before.Kind == e.Kind) continue;

            // Patch the operator alone. Without a recorded span there is nothing
            // safe to patch, so regenerate rather than guess at an offset.
            if (before.OpStart < 0 || before.SourceLine < 0 ||
                before.SourceLine >= lines.Count)
                return FullRegenerate(source, edited);

            var replacement = adapter.RenderLinkOperator(e);
            if (replacement is null) return FullRegenerate(source, edited);

            edits.Add((before.SourceLine, before.OpStart, before.OpLength, replacement));
        }

        // --- apply -----------------------------------------------------------
        foreach (var group in edits.GroupBy(x => x.Line))
        {
            var line = lines[group.Key];
            foreach (var (_, start, length, text) in
                     group.OrderByDescending(x => x.Start))
            {
                if (start < 0 || start + length > line.Length)
                    return FullRegenerate(source, edited);
                line = line[..start] + text + line[(start + length)..];
            }
            lines[group.Key] = line;
        }

        if (appended.Count > 0)
        {
            // Insert after the last structural statement rather than at the very
            // end, so new nodes don't land below trailing `classDef` / `class`
            // directives where a reader would not expect them.
            var insertAt = lines.Count;
            var lastStatement = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (original.Passthrough.ContainsKey(i)) continue;
                if (lines[i].Trim().Length == 0) continue;
                lastStatement = i;
            }
            if (lastStatement >= 0) insertAt = lastStatement + 1;
            else while (insertAt > 0 && lines[insertAt - 1].Trim().Length == 0) insertAt--;

            lines.InsertRange(insertAt, appended);
        }

        var newBody = string.Join(ending, lines);

        // Reattach the author's whole frontmatter. Serialising the patched body
        // instead loses every key but ours, because the patched body carries no
        // frontmatter for Serialise to preserve.
        return MerpeepsDoc.ReplaceBody(source, newBody);
    }

    private static string FullRegenerate(string source, DiagramModel edited)
    {
        var body = DiagramAdapters.For(source).Write(edited);

        // Keep the author's whole frontmatter, not just our block. Serialising
        // the regenerated body instead would hand Serialise a document with no
        // frontmatter to preserve, so `title:`, `config:` and anything else the
        // author wrote would vanish on any edit big enough to need a full
        // rewrite — a rename, in practice.
        return MerpeepsDoc.ReplaceBody(source, body);
    }

    private static string Declare(DiagramNode n)
    {
        if (n.Label is null && n.Shape == NodeShape.Rectangle) return n.Id;
        var label = n.Label ?? n.Id;
        if (label.IndexOfAny(new[] { '[', ']', '(', ')', '{', '}', '|', '"' }) >= 0)
            label = '"' + label.Replace("\"", "&quot;") + '"';
        var (o, c) = Delimiters(n.Shape);
        return $"{n.Id}{o}{label}{c}";
    }

    private static string EdgeStatement(DiagramEdge e)
    {
        var op = e.Kind switch
        {
            EdgeKind.Open => "---",
            EdgeKind.Dotted => "-.-",
            EdgeKind.DottedArrow => "-.->",
            EdgeKind.Thick => "===",
            EdgeKind.ThickArrow => "==>",
            EdgeKind.Invisible => "~~~",
            _ => "-->",
        };
        return string.IsNullOrEmpty(e.Label)
            ? $"{e.From} {op} {e.To}"
            : $"{e.From} {op}|{e.Label}| {e.To}";
    }

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
}


