namespace Merpeeps.Core.Model;

/// <summary>
/// What a node should look like, decided separately from drawing it.
///
/// <para>The rules are small but easy to get subtly wrong: which parts are
/// Merpeeps styling that must vanish in plain mode, which are mermaid's own
/// (shape) and must not, and how sizing interacts with an icon that may or may
/// not be drawn. Buried inside a render method those rules can only be checked
/// by looking at pixels; here they can be asserted.</para>
///
/// <para>Deliberately free of any UI type so it lives in Core and can be
/// tested without a display or a toolkit.</para>
/// </summary>
public sealed record NodeAppearance
{
    /// <summary>The node's stored fill, or null to use the renderer's default.</summary>
    public string? Fill { get; init; }

    /// <summary>The node's stored outline colour, or null for the default.</summary>
    public string? Stroke { get; init; }

    /// <summary>Outline width, or null for the default.</summary>
    public double? StrokeWidth { get; init; }

    /// <summary>Whether the outline is dashed.</summary>
    public bool Dashed { get; init; }

    /// <summary>Label colour, or null to pick one that contrasts with the fill.</summary>
    public string? TextColor { get; init; }

    /// <summary>The icon to draw, or null for none.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Geometry. Always the node's own shape: shapes are mermaid syntax, not
    /// Merpeeps styling, so a plain render still shows a cylinder as a cylinder.
    /// </summary>
    public required NodeShape Shape { get; init; }

    /// <summary>True when an icon will be drawn, and space must be left for it.</summary>
    public bool HasIcon => Icon is not null;

    /// <summary>True when nothing beyond the shape is customised.</summary>
    public bool IsPlain =>
        Fill is null && Stroke is null && StrokeWidth is null
        && !Dashed && TextColor is null && Icon is null;

    /// <summary>
    /// Work out how <paramref name="node"/> should be drawn.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="showMerpeepsStyling">
    /// False renders as a stock mermaid renderer would: shape only, with every
    /// piece of Merpeeps presentation — fill, outline, text colour and icon —
    /// suppressed together. Suppressing some but not others produces a view
    /// that matches neither what mermaid draws nor what Merpeeps draws.
    /// </param>
    public static NodeAppearance For(DiagramNode node, bool showMerpeepsStyling)
    {
        if (!showMerpeepsStyling) return new NodeAppearance { Shape = node.Shape };

        return new NodeAppearance
        {
            Shape = node.Shape,
            Fill = Blank(node.Fill),
            Stroke = Blank(node.Stroke),
            StrokeWidth = node.StrokeWidth,
            Dashed = node.Dashed,
            TextColor = Blank(node.TextColor),
            // An unrecognised icon name draws nothing, so treat it as absent
            // rather than reserving space for a glyph that never appears.
            Icon = IconVocabulary.IsKnown(node.Icon) ? node.Icon : null,
        };
    }

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
