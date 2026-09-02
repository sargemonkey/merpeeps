namespace Merpeeps.Core.Model;

/// <summary>
/// Layout expressed as relationships rather than coordinates.
///
/// <para>Storing <c>x</c> and <c>y</c> says where a node sits in pixels, which
/// is a fact about the picture that has to be kept in step with a separate fact
/// about the graph. When the two drift, a coordinate points at nothing and the
/// node lands somewhere wrong — while the diagram still looks perfectly
/// plausible. A constraint cannot fail that way: if it names a node that no
/// longer exists it simply stops applying, and the engine lays the diagram out
/// normally. Correct-but-ordinary beats wrong-but-plausible.</para>
///
/// <para>Constraints are also sparse by nature. You say something about the two
/// or three nodes you care about, where coordinates pull towards an entry per
/// node — so there is far less to keep in sync in the first place.</para>
///
/// <para>This is the arrangement every durable text diagram language converged
/// on independently: Graphviz, D2, TikZ and PlantUML all make relationship
/// placement first-class and quarantine absolute coordinates into a separate
/// opt-in mode. Mermaid's own most-upvoted layout request (#3723, "specify that
/// two nodes should be at the same level") is asking for a rank constraint, not
/// a coordinate map.</para>
/// </summary>
public sealed class LayoutConstraints
{
    /// <summary>
    /// Groups of node ids that should share a layer. Mermaid issue #3723
    /// verbatim, and the single most requested layout control.
    /// </summary>
    public List<List<string>> SameRank { get; } = new();

    /// <summary>
    /// Groups of node ids to place in the given left-to-right (or top-to-bottom)
    /// sequence within their layer. Purely ordinal — no coordinates, so it
    /// survives every edit that does not delete the nodes themselves.
    /// </summary>
    public List<List<string>> Order { get; } = new();

    /// <summary>
    /// Constraint entries this build does not model, kept verbatim.
    ///
    /// <para>Teaching the reader about <c>same-rank</c> and <c>order</c> made it
    /// <em>lossy</em> for every other constraint kind: it would parse the list,
    /// re-emit only what it understood, and silently drop the rest. Before it
    /// understood constraints at all it preserved them perfectly, so partial
    /// knowledge was worse than none — a newer tool's work destroyed by an older
    /// one merely opening the file.</para>
    ///
    /// <para>Same rule as unresolvable node ids and unknown icon names: retain
    /// what you cannot interpret.</para>
    /// </summary>
    public List<object?> Unrecognised { get; } = new();

    /// <summary>True when nothing has been constrained.</summary>
    public bool IsEmpty =>
        SameRank.Count == 0 && Order.Count == 0 && Unrecognised.Count == 0;

    /// <summary>
    /// Ids named by any constraint that the model does not contain.
    ///
    /// <para>Reported rather than removed. A constraint mentioning a missing
    /// node is inert, not harmful, and deleting it would destroy intent the
    /// moment someone renamed a node in a branch — the same reasoning that keeps
    /// stale positions and unknown icon names in the file.</para>
    /// </summary>
    public IReadOnlyList<string> UnresolvedIds(DiagramModel model)
    {
        var known = model.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var id in SameRank.Concat(Order).SelectMany(g => g))
        {
            if (!known.Contains(id) && !missing.Contains(id, StringComparer.Ordinal))
                missing.Add(id);
        }

        return missing;
    }

    /// <summary>
    /// The constraint groups that can actually be applied, with unknown ids
    /// dropped and groups too small to mean anything discarded.
    /// </summary>
    internal static IEnumerable<List<string>> Applicable(
        IEnumerable<List<string>> groups, HashSet<string> known) =>
        groups
            .Select(g => g.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList())
            .Where(g => g.Count >= 2);
}
