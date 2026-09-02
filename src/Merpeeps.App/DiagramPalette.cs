using System;
using Avalonia.Media;

namespace Merpeeps.App;

/// <summary>
/// The colours mermaid gives an unstyled node under each of its themes, so the
/// editor's canvas can draw the same diagram the renderer will.
/// </summary>
/// <remarks>
/// <para>Every value here was measured, not copied from documentation: each
/// theme was rendered by the exact mermaid build this project ships and the
/// computed fill, stroke and text colour read back off the SVG
/// (<c>oracle/theme-colour-probe.mjs</c>, which is also the gate that keeps
/// them honest when mermaid is upgraded).</para>
///
/// <para>Guessing would have been worse than useless. The canvas exists to
/// predict the render, so a palette that merely looked plausible would make the
/// editor confidently wrong — the single failure the reference pane was built
/// to rule out.</para>
/// </remarks>
public static class DiagramPalette
{
    /// <param name="Name">The value written to <c>config: theme:</c>.</param>
    /// <param name="Label">What the menu shows.</param>
    public readonly record struct Entry(
        string Name, string Label, Color Fill, Color Stroke, Color Text);

    private static Color C(string hex) => Color.Parse(hex);

    /// <summary>
    /// Every theme mermaid ships. <c>base</c> is included because it is the one
    /// intended for customising, and seeing its starting point is the only way
    /// to judge a change to it.
    /// </summary>
    /// <remarks>
    /// <para>The list started as the five long-standing themes and was wrong:
    /// mermaid also ships <c>neo</c> and <c>redux</c> and their variants. They
    /// were missed because an unknown theme name is not an error — mermaid
    /// silently renders with <c>default</c> — so nothing complains either way.
    /// The probe now checks each name's colours actually differ from
    /// <c>default</c> before believing it exists.</para>
    ///
    /// <para><c>redux-color</c> and <c>redux-dark-color</c> give flowchart nodes
    /// the same colours as their plain counterparts; they differ on diagram
    /// types that assign a colour sequence. Identical entries here are measured,
    /// not copy-paste.</para>
    ///
    /// <para>Not included: <c>mermaid-chart</c>. It appears in Mermaid Chart's
    /// own editor but is not in the open-source build — asked for it, mermaid
    /// falls back to <c>default</c>.</para>
    /// </remarks>
    public static readonly Entry[] All =
    [
        new("default", "Default", C("#ECECFF"), C("#9370DB"), C("#333333")),
        new("neutral", "Neutral", C("#EEEEEE"), C("#999999"), C("#000000")),
        new("forest",  "Forest",  C("#CDE498"), C("#13540C"), C("#000000")),
        new("dark",    "Dark",    C("#1F2020"), C("#CCCCCC"), C("#CCCCCC")),
        new("base",    "Base",    C("#FFF4DD"), C("#EEDEBB"), C("#333333")),

        new("neo",      "Neo",      C("#FFFFFF"), C("#000000"), C("#333333")),
        new("neo-dark", "Neo dark", C("#2A2020"), C("#CCCCCC"), C("#E0DFDF")),

        new("redux",            "Redux",            C("#FFFFFF"), C("#28253D"), C("#28253D")),
        new("redux-color",      "Redux colour",     C("#FFFFFF"), C("#28253D"), C("#28253D")),
        new("redux-dark",       "Redux dark",       C("#111113"), C("#FFFFFF"), C("#E0DFDF")),
        new("redux-dark-color", "Redux dark colour", C("#111113"), C("#FFFFFF"), C("#E0DFDF")),
    ];

    /// <summary>
    /// The entry for a theme name, or null when the document names no theme or
    /// one we do not know.
    /// </summary>
    /// <remarks>
    /// An unknown name returns null rather than falling back to
    /// <c>default</c>. A future mermaid theme should leave the canvas on its own
    /// neutral colours — which look deliberate — instead of asserting a specific
    /// palette that would probably be wrong.
    /// </remarks>
    public static Entry? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var e in All)
            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>Index into <see cref="All"/>, or -1.</summary>
    public static int IndexOf(string? name)
    {
        for (var i = 0; i < All.Length; i++)
            if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
