using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core;

/// <summary>
/// The editor-facing entry point: load a mermaid file into a positioned model,
/// and save positions back without ever touching the diagram body.
/// </summary>
public static class MerpeepsDocument
{
    /// <summary>
    /// Parse <paramref name="source"/>, apply any stored Merpeeps positions, and lay
    /// out whatever is left over. The diagram type is detected automatically.
    /// </summary>
    public static DiagramModel Load(string source)
    {
        var model = DiagramAdapters.For(source).Parse(source);
        ApplyMetadata(model, MerpeepsDoc.Parse(source));
        model.Theme = MerpeepsDoc.ThemeOf(source);

        // Only nodes without stored coordinates get auto-placed, so a user's
        // manual arrangement is never overwritten on open. New nodes added to
        // the text since the last save are laid out with whichever engine the
        // document last used, so they land somewhere consistent with the rest.
        if (model.Nodes.Any(n => !n.HasExplicitPosition))
            LayoutEngines.Apply(model, model.LayoutEngine);

        return model;
    }

    /// <summary>
    /// Full save: regenerate the diagram body from the model (structural edits
    /// change it) and write positions into Merpeeps.
    ///
    /// <para>Use <see cref="SavePositions"/> when only positions changed — it
    /// guarantees the body is untouched. This method deliberately does not,
    /// because rewriting the body is the whole point.</para>
    /// </summary>
    public static string Save(string source, DiagramModel model)
    {
        // Patch in place where possible so a rename is a one-token diff rather
        // than a rewrite of every line in the document. Line endings are handled
        // by the writer and the serialiser, each at its own level.
        //
        // writeLayout is false unless the document is already positioned: a
        // structural edit must not silently convert an ordinary mermaid file
        // into one carrying a coordinate map. Callers that genuinely mean to
        // persist geometry say so by calling SavePositions.
        return SavePositionsUnchecked(
            SurgicalWriter.Apply(source, model), model,
            writeLayout: model.AnyExplicitPositions);
    }

    /// <summary>
    /// Write current node positions and styles into <paramref name="source"/>'s
    /// Merpeeps block. The diagram body is returned byte-for-byte unchanged.
    /// </summary>
    public static string SavePositions(string source, DiagramModel model)
    {
        // Naming the method is the request: this always records geometry, even
        // for a document that had none, because that is what the caller asked
        // for.
        var result = SavePositionsUnchecked(source, model, writeLayout: true);

        // Enforce the core promise at the boundary, not just in tests.
        if (MerpeepsDoc.BodyOf(result) != MerpeepsDoc.BodyOf(source))
            throw new InvalidOperationException(
                "save would have modified the diagram body; refusing");

        return result;
    }

    private static string SavePositionsUnchecked(
        string source, DiagramModel model, bool writeLayout)
    {
        var style = new Dictionary<string, object?>();
        foreach (var n in model.Nodes)
        {
            var s = new Dictionary<string, object?>();
            if (n.Fill is not null) s["fill"] = n.Fill;
            if (n.Stroke is not null) s["stroke"] = n.Stroke;
            if (n.TextColor is not null) s["text"] = n.TextColor;
            if (n.StrokeWidth is { } sw) s["strokeWidth"] = sw;
            if (n.Dashed) s["dashed"] = true;
            if (n.Icon is not null) s["icon"] = n.Icon;
            if (s.Count > 0) style[n.Id] = s;
        }

        var patch = new Dictionary<string, object?> { ["spec"] = 1 };

        // Constraints are written whenever they exist, independently of
        // coordinates. They are the low-coupling half of layout: sparse,
        // relationship-based, and harmless when stale — so unlike a coordinate
        // map there is no reason to withhold them.
        var layoutBlock = new Dictionary<string, object?>();

        if (!model.Constraints.IsEmpty)
        {
            var list = new List<object?>();
            foreach (var group in model.Constraints.SameRank)
                list.Add(new Dictionary<string, object?> { ["same-rank"] = group.ToList<object?>() });
            foreach (var group in model.Constraints.Order)
                list.Add(new Dictionary<string, object?> { ["order"] = group.ToList<object?>() });

            // Entries this build does not model go back exactly as they came.
            list.AddRange(model.Constraints.Unrecognised);

            layoutBlock["constraints"] = list;
        }

        // Only record geometry when the caller means to. Writing it
        // unconditionally meant that adding one node to an ordinary mermaid file
        // silently converted it into a positioned document, handing the author a
        // coordinate map they never asked for and now have to keep in step with
        // the node list. That is the coupling this format is supposed to make
        // optional, and imposing it was the opposite of optional. Styling has no
        // such problem: it is only written for nodes that actually carry it.
        if (writeLayout)
        {
            var nodes = new Dictionary<string, object?>();
            foreach (var n in model.Nodes)
            {
                var entry = new Dictionary<string, object?>
                {
                    ["x"] = Math.Round(n.X, 2),
                    ["y"] = Math.Round(n.Y, 2),
                };
                if (Math.Abs(n.Width - 160) > 0.01) entry["w"] = Math.Round(n.Width, 2);
                if (Math.Abs(n.Height - 56) > 0.01) entry["h"] = Math.Round(n.Height, 2);
                nodes[n.Id] = entry;
            }

            layoutBlock["nodes"] = nodes;
            // Records which engine produced these coordinates. "manual" means a
            // person placed them; an engine id means it was generated, and a
            // reopened document keeps using the same one so newly added nodes
            // land consistently.
            layoutBlock["engine"] = model.LayoutEngine ?? "manual";
        }

        if (layoutBlock.Count > 0) patch["layout"] = layoutBlock;

        if (style.Count > 0)
            patch["style"] = new Dictionary<string, object?> { ["nodes"] = style };

        // The theme is mermaid's key, not ours, so it is written outside the
        // Merpeeps block — and only when it differs from what is already there,
        // so a document nobody themed keeps a frontmatter free of our fingerprints.
        var written = MerpeepsDoc.Inject(source, patch);
        return model.Theme == MerpeepsDoc.ThemeOf(written)
            ? written
            : MerpeepsDoc.WithTheme(written, model.Theme);
    }

    /// <summary>
    /// Read the constraint list.
    ///
    /// <para>Shape is a sequence of single-key mappings, so a document reads as
    /// a list of statements rather than a bag of settings:</para>
    /// <code>
    /// constraints:
    ///   - same-rank: [api, cache]
    ///   - order: [web, api, db]
    /// </code>
    /// <para>Anything unrecognised is skipped rather than rejected — a future
    /// constraint kind must not stop today's reader opening the file.</para>
    /// </summary>
    private static void ReadConstraints(DiagramModel model, object? raw)
    {
        if (raw is not List<object?> items) return;

        foreach (var item in items)
        {
            if (item is not Dictionary<string, object?> entry)
            {
                model.Constraints.Unrecognised.Add(item);
                continue;
            }

            var understood = false;

            foreach (var (kind, value) in entry)
            {
                var ids = Ids(value);
                if (ids.Count == 0) continue;

                switch (kind.ToLowerInvariant())
                {
                    case "same-rank" or "samerank":
                        model.Constraints.SameRank.Add(ids);
                        understood = true;
                        break;
                    case "order":
                        model.Constraints.Order.Add(ids);
                        understood = true;
                        break;
                }
            }

            // Anything we could not interpret is kept whole rather than dropped.
            if (!understood) model.Constraints.Unrecognised.Add(item);
        }
    }

    private static List<string> Ids(object? value) => value switch
    {
        List<object?> list => list.Select(v => v?.ToString() ?? "")
                                  .Where(s => s.Length > 0).ToList(),
        string s when s.Length > 0 => [s],
        _ => [],
    };

    private static void ApplyMetadata(DiagramModel model, object? merpeeps)
    {
        if (merpeeps is not Dictionary<string, object?> root) return;
        if (Get(root, "layout") is Dictionary<string, object?> layout)
        {
            if (layout.TryGetValue("engine", out var engine))
                model.LayoutEngine = engine?.ToString();

            if (Get(layout, "nodes") is Dictionary<string, object?> nodes)
            {
                foreach (var (id, raw) in nodes)
                {
                    if (raw is not Dictionary<string, object?> p) continue;
                    var node = model.Node(id);
                    if (node is null) continue;

                    if (TryNum(p, "x", out var x) && TryNum(p, "y", out var y))
                    {
                        node.X = x; node.Y = y;
                        node.HasExplicitPosition = true;
                    }
                    if (TryNum(p, "w", out var w)) node.Width = w;
                    if (TryNum(p, "h", out var h)) node.Height = h;
                }
            }

            ReadConstraints(model, Get(layout, "constraints"));
        }

        if (Get(root, "style") is Dictionary<string, object?> style &&
            Get(style, "nodes") is Dictionary<string, object?> snodes)
        {
            foreach (var (id, raw) in snodes)
            {
                if (raw is not Dictionary<string, object?> s) continue;
                var node = model.Node(id);
                if (node is null) continue;

                if (s.TryGetValue("fill", out var f)) node.Fill = f?.ToString();
                if (s.TryGetValue("stroke", out var st)) node.Stroke = st?.ToString();
                if (s.TryGetValue("text", out var tc)) node.TextColor = tc?.ToString();
                if (s.TryGetValue("icon", out var ic)) node.Icon = ic?.ToString();
                if (TryNum(s, "strokeWidth", out var sw)) node.StrokeWidth = sw;
                if (s.TryGetValue("dashed", out var d))
                    node.Dashed = d is bool b ? b
                        : bool.TryParse(d?.ToString(), out var pb) && pb;
            }
        }
    }

    private static object? Get(Dictionary<string, object?> d, string k) =>
        d.TryGetValue(k, out var v) ? v : null;

    /// <summary>
    /// Read a number from the metadata, whatever CLR type it arrived as and
    /// whatever locale the machine is set to.
    ///
    /// <para>The obvious implementation — <c>double.TryParse(raw.ToString(),
    /// …, InvariantCulture)</c> — is broken, and silently. When the value is
    /// already a <c>double</c>, <c>ToString()</c> formats it in the *current*
    /// culture, so a German machine turns 12.5 into "12,5", which then fails
    /// to parse as invariant and the coordinate is dropped. The position is
    /// lost with no error anywhere.</para>
    /// </summary>
    private static bool TryNum(Dictionary<string, object?> d, string k, out double v)
    {
        v = 0;
        if (!d.TryGetValue(k, out var raw) || raw is null) return false;

        // Numbers that survived deserialisation as numbers need no parsing.
        switch (raw)
        {
            case double dd: v = dd; return true;
            case float f: v = f; return true;
            case decimal m: v = (double)m; return true;
            case long l: v = l; return true;
            case int i: v = i; return true;
            case short s: v = s; return true;
            case byte b: v = b; return true;
            case ulong ul: v = ul; return true;
            case uint ui: v = ui; return true;
        }

        // Only genuine text reaches the parser, and it is always invariant
        // because that is how it was written.
        return double.TryParse(
            Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);
    }
}
