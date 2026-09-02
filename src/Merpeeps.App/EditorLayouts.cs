using System;
using System.Collections.Generic;
using System.Linq;
using Merpeeps.Core.Layout;

namespace Merpeeps.App;

/// <summary>
/// Every layout the editor can offer.
/// </summary>
/// <remarks>
/// <para>All of them are written in C# and run in-process. That is a deliberate
/// narrowing: the editor used to also offer layouts borrowed from the reference
/// renderer — ELK's four algorithms and mermaid's own dagre — computed in a
/// browser and harvested back as coordinates. They worked, but choosing one
/// opened a browser pane mid-edit, which is a strange thing for a dropdown to do
/// and read as a bug rather than a feature.</para>
///
/// <para>Nothing is lost from files already carrying those layouts. A layout is
/// stored as coordinates, not as an instruction to re-run an engine, so a
/// document recording <c>engine: elk</c> keeps its arrangement exactly and
/// renders as it always did. The dropdown simply will not offer to compute a new
/// one. See <see cref="IndexOf"/> for what the UI does with an engine name it no
/// longer recognises.</para>
///
/// <para>This lives in the app, not in <see cref="LayoutEngines"/>, because the
/// list is a UI concern. Core, the CLI and the MCP server all reach for
/// <see cref="LayoutEngines"/> directly.</para>
/// </remarks>
public static class EditorLayouts
{
    /// <param name="Label">What the dropdown shows.</param>
    /// <param name="Id">Recorded as <c>layout.engine</c> in the document.</param>
    /// <param name="Description">Tooltip text.</param>
    public readonly record struct Choice(string Label, string Id, string Description);

    /// <summary>
    /// The engines, in the order the dropdown offers them.
    /// </summary>
    /// <remarks>
    /// Labels keep the <c>Merpeeps:</c> prefix even though nothing else appears in
    /// the list any more. It says whose arrangement this is, which matters
    /// precisely because a stock renderer without the plugin will draw the
    /// diagram its own way.
    /// </remarks>
    public static readonly IReadOnlyList<Choice> All =
    [
        .. LayoutEngines.Available.Select(e =>
            new Choice($"Merpeeps: {e.Name.ToLowerInvariant()}", e.Id, e.Description)),
    ];

    /// <summary>
    /// The index to select for a recorded engine id, or -1 when the document
    /// names something we do not offer.
    /// </summary>
    /// <remarks>
    /// -1 is a real answer, not a failure. It covers <c>manual</c>, which is not
    /// an engine but a statement that a person placed the nodes, and it covers
    /// the retired borrowed layouts. In both cases the caller leaves the dropdown
    /// alone rather than snapping to a default, because changing the selection
    /// would misreport what produced the arrangement on screen.
    /// </remarks>
    public static int IndexOf(string? id) =>
        id is null ? -1 : All.ToList().FindIndex(
            c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The choice at an index, or the default when out of range.</summary>
    public static Choice At(int index) =>
        index >= 0 && index < All.Count ? All[index] : All[0];
}
