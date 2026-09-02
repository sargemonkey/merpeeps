namespace Merpeeps.Core.Parsing;

/// <summary>
/// Words mermaid's flowchart grammar reserves, and how to keep clear of them.
///
/// <para>Mermaid cannot use these as node ids in <em>any</em> form. Not bare,
/// not labelled, not quoted, not inside a subgraph, not as an edge source — all
/// of them are parse errors. Verified against 11.16.1 by
/// <c>oracle/reserved-word-probe2.mjs</c>, which also confirms what does work: a
/// trailing underscore, or a change of case.</para>
///
/// <para>This exists because Merpeeps <em>writes</em> mermaid. An agent asking for
/// a node called <c>end</c> is being entirely reasonable — it is the obvious id
/// for a terminal state — and writing it verbatim would produce a body that
/// renders nowhere. The format's central promise is that the body stays valid
/// for a stock renderer, so emitting an unrenderable one is the worst bug on
/// offer.</para>
///
/// <para>Found by a cross-language generation benchmark, which measured this as
/// mermaid's one reproducible weakness against DOT and D2: both of those simply
/// quote the word and keep the id, while mermaid forces a rename. We cannot fix
/// mermaid, but we can make sure the rename is minimal, predictable and never
/// silently collides.</para>
/// </summary>
public static class MermaidKeywords
{
    /// <summary>
    /// The reserved set. Deliberately conservative: only words confirmed to
    /// break, because over-reserving would rename ids that were never a problem
    /// and make diffs noisy for no benefit.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "end", "class", "style", "graph", "subgraph", "click", "linkStyle",
        "flowchart", "classDef", "direction", "callback", "href", "call",
    };

    /// <summary>
    /// True when mermaid would reject <paramref name="id"/> as a node id.
    /// </summary>
    /// <remarks>
    /// Case-sensitive on purpose. Mermaid's keywords are lowercase and <c>End</c>
    /// parses perfectly well, so treating it as reserved would rename ids that
    /// work.
    /// </remarks>
    public static bool IsReserved(string? id) =>
        id is not null && Reserved.Contains(id);

    /// <summary>
    /// An id mermaid will accept, as close to the requested one as possible.
    /// </summary>
    /// <param name="id">The requested id.</param>
    /// <param name="taken">
    /// Optional test for ids already in use, consulted <em>only</em> when the
    /// requested id had to be escaped, so escaping cannot silently merge two
    /// distinct nodes by colliding with an existing <c>end_</c>.
    /// </param>
    /// <remarks>
    /// <para>A trailing underscore rather than a rename: the id stays readable
    /// and obviously derived from what was asked for. Renaming <c>end</c> to
    /// <c>finish</c> would parse but quietly change what the document says.</para>
    /// <para>An id that needs no escaping is returned untouched even when it is
    /// already taken. Consulting <paramref name="taken"/> unconditionally turned
    /// a genuine duplicate into a silent rename, which broke duplicate detection
    /// — the caller owns that decision, not this method.</para>
    /// </remarks>
    public static string MakeSafe(string id, Func<string, bool>? taken = null)
    {
        if (!IsReserved(id)) return id;

        var safe = id + "_";
        if (taken is not null) while (taken(safe)) safe += "_";
        return safe;
    }

    /// <summary>
    /// The id a caller's reference resolves to.
    ///
    /// <para>Because a reserved word can never be a real id, a later operation
    /// naming <c>end</c> can only mean the node that was escaped to <c>end_</c>.
    /// Without this, a batch that adds <c>end</c> and then connects to it would
    /// half-apply: the node appears and the edge silently does not.</para>
    /// </summary>
    public static string Resolve(string id, Func<string, bool> exists) =>
        exists(id) || !IsReserved(id) ? id : MakeSafe(id);
}
