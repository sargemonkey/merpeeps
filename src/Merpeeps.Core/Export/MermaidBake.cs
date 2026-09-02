using System.Globalization;
using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Export;

/// <summary>
/// Rewrites a Merpeeps document as ordinary mermaid, so its styling survives
/// somewhere no plugin is installed.
///
/// <para>Merpeeps keeps styling out of the diagram body on purpose: the body stays
/// byte-identical, and a stock renderer draws a correct plain picture. The cost
/// is that the styling only appears where <c>merpeeps-mermaid-layout</c> is
/// loaded — which is almost nowhere. GitHub, GitLab, Notion and every other host
/// embeds mermaid as-is.</para>
///
/// <para>Baking trades that invariant away deliberately and once: it emits real
/// <c>style</c> statements into the body, producing a file that renders
/// correctly unaided. That is why it is an explicit export and never something
/// saving does — the whole design rests on the body being untouched, so mutating
/// it has to be something you ask for.</para>
///
/// <para>What can be carried across is a measured fact rather than a guess; see
/// <c>oracle/style-support-probe.mjs</c> and
/// <c>oracle/bake-capability-probe.mjs</c>, which derive it from a real
/// renderer.</para>
/// </summary>
public static class MermaidBake
{
    /// <summary>
    /// Diagram families whose grammar accepts <c>style</c>. Everything else
    /// rejects it with a parse error, so baking into them would produce a file
    /// that does not render at all — strictly worse than the original.
    /// </summary>
    /// <remarks>
    /// Verified against mermaid 11.16.1. Accepted: flowchart/graph, stateDiagram,
    /// classDiagram, erDiagram, block. Rejected: sequenceDiagram, C4*,
    /// architecture, journey, mindmap.
    /// </remarks>
    private static readonly string[] SupportsStyle =
    {
        "flowchart", "graph", "statediagram", "classdiagram", "erdiagram", "block",
    };

    /// <summary>What a bake could and could not carry into plain mermaid.</summary>
    /// <param name="Output">The baked document.</param>
    /// <param name="StyledNodes">Nodes that received a <c>style</c> statement.</param>
    /// <param name="DroppedIcons">
    /// Node ids whose icon could not survive. Mermaid's <c>@{ icon: }</c> shape
    /// needs an icon pack registered by the host through
    /// <c>registerIconPacks()</c> — a runtime call, not syntax — so no icon can
    /// be expressed in a file that must render unaided. Verified with a control
    /// render in <c>oracle/icon-control-probe.mjs</c>.
    /// </param>
    /// <param name="DroppedPositions">
    /// Node ids whose stored coordinates were lost. Mermaid has no syntax for
    /// placing a node, so a baked file is laid out by mermaid's own algorithm.
    /// </param>
    /// <param name="UnsupportedKind">
    /// Set when the diagram type rejects <c>style</c> entirely, in which case
    /// nothing was baked and the body is unchanged.
    /// </param>
    public readonly record struct Result(
        string Output,
        IReadOnlyList<string> StyledNodes,
        IReadOnlyList<string> DroppedIcons,
        IReadOnlyList<string> DroppedPositions,
        string? UnsupportedKind)
    {
        /// <summary>True when something the document carried could not be expressed.</summary>
        public bool Lossy =>
            DroppedIcons.Count > 0 || DroppedPositions.Count > 0 || UnsupportedKind is not null;
    }

    /// <summary>
    /// Produce plain mermaid carrying as much of the styling as the grammar
    /// allows, and report precisely what it could not carry.
    /// </summary>
    public static Result Bake(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var body = MerpeepsDoc.BodyOf(source);

        var icons = model.Nodes
            .Where(n => !string.IsNullOrEmpty(n.Icon))
            .Select(n => n.Id)
            .ToArray();

        var positions = model.Nodes
            .Where(n => n.HasExplicitPosition)
            .Select(n => n.Id)
            .ToArray();

        // Read the kind from the source rather than from the model. The adapter
        // registry falls back to flowchart for anything it does not recognise,
        // so model.Kind reports "flowchart" for a sequence diagram — and trusting
        // it would inject `style` into a grammar that rejects it, turning a
        // working file into a parse error. That is the precise failure this
        // guard exists to prevent, so it must not consult a guessing party.
        var kind = OpeningKeyword(body);

        if (!AcceptsStyle(kind))
        {
            return new Result(Ensure(body), [], icons, positions, kind);
        }

        var styled = new List<string>();
        var sb = new StringBuilder(Ensure(body));

        foreach (var node in model.Nodes)
        {
            var decl = NodeStyle(node);
            if (decl is null) continue;
            sb.Append("    style ").Append(node.Id).Append(' ').Append(decl).Append('\n');
            styled.Add(node.Id);
        }

        return new Result(sb.ToString(), styled, icons, positions, null);
    }

    /// <summary>Does this diagram family's grammar accept a <c>style</c> statement?</summary>
    public static bool AcceptsStyle(string kind)
    {
        var k = kind.Replace("-", "").ToLowerInvariant();
        return SupportsStyle.Any(s => k.StartsWith(s, StringComparison.Ordinal));
    }

    /// <summary>
    /// The diagram's opening keyword, taken straight from the source.
    ///
    /// <para>Deliberately independent of the parser: what matters here is what
    /// mermaid's grammar will see, not what our adapters managed to make of
    /// it.</para>
    /// </summary>
    public static string OpeningKeyword(string body)
    {
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;

            // "flowchart TD" -> "flowchart"; "architecture-beta" stays whole.
            var space = line.IndexOfAny([' ', '\t']);
            return space > 0 ? line[..space] : line;
        }
        return "";
    }

    /// <summary>
    /// The declaration body for one node, or null when it has nothing paintable.
    /// </summary>
    private static string? NodeStyle(DiagramNode n)
    {
        var parts = new List<string>(5);

        if (!string.IsNullOrWhiteSpace(n.Fill)) parts.Add($"fill:{n.Fill}");
        if (!string.IsNullOrWhiteSpace(n.Stroke)) parts.Add($"stroke:{n.Stroke}");
        if (n.StrokeWidth is { } w)
            parts.Add($"stroke-width:{w.ToString("0.##", CultureInfo.InvariantCulture)}px");

        // 5 3 matches what the editor draws, so the baked file looks like the
        // preview rather than merely being dashed.
        if (n.Dashed) parts.Add("stroke-dasharray:5 3");
        if (!string.IsNullOrWhiteSpace(n.TextColor)) parts.Add($"color:{n.TextColor}");

        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    /// <summary>Guarantee a trailing newline so appended statements start cleanly.</summary>
    private static string Ensure(string body) =>
        body.Length == 0 || body.EndsWith('\n') ? body : body + "\n";
}
