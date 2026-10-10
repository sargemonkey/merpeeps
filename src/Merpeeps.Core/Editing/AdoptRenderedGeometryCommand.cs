using Merpeeps.Core.Model;

namespace Merpeeps.Core.Editing;

public sealed class AdoptRenderedGeometryCommand : IEditCommand
{
    private readonly RenderedGraphGeometry _geometry;
    private readonly Dictionary<DiagramNode, (double X, double Y, double Width, double Height, bool Positioned, bool Sized)> _before;
    private readonly RenderedGraphGeometry? _oldGeometry;
    private readonly string? _oldEngine;
    private readonly Dictionary<string, IReadOnlyList<DiagramPoint>> _oldWaypoints;
    public string Description => "Adopt rendered geometry";
    public int NodeCount => _before.Count;
    public IReadOnlyList<string> UnmatchedIds { get; }

    public AdoptRenderedGeometryCommand(DiagramModel model, RenderedGraphGeometry geometry)
    {
        geometry = RenderedGraphGeometry.Parse(MerpeepsDoc.ToJson(geometry.ToMetadata()));
        if (model.Kind is not ("flowchart" or "graph" or "flowchart-elk"))
            throw new ArgumentException("Rendered geometry adoption currently supports Flowchart diagrams.");
        var all = geometry.Nodes.Keys.Concat(geometry.Groups.Keys).ToArray();
        UnmatchedIds = all.Where(id => model.Node(id) is null && !model.Subgraphs.Any(g => g.Id == id)).ToArray();
        if (UnmatchedIds.Count > 0) throw new ArgumentException("Rendered identities do not match the diagram: " + string.Join(", ", UnmatchedIds));
        if (model.Nodes.Any(n => !geometry.Nodes.ContainsKey(n.Id))) throw new ArgumentException("Rendered geometry must include every node, including legend items.");
        if (model.Nodes.Any(n => geometry.Nodes[n.Id].Text != n.Display))
            throw new ArgumentException("Rendered node content does not match this source.");
        foreach (var (id, group) in geometry.Groups)
        {
            var actual = model.Subgraphs.Single(g => g.Id == id);
            if (group.Text != (actual.Title ?? actual.Id)) throw new ArgumentException($"Rendered group '{id}' has different content.");
            if (!GraphGroupGeometry.Members(model, actual).Select(n => n.Id).Order().SequenceEqual(group.Members.Order()))
                throw new ArgumentException($"Rendered group '{id}' has different ownership members.");
        }
        if (geometry.Edges.Keys.Any(key => !model.Edges.Any(e => e.Key == key)))
            throw new ArgumentException("A rendered connector does not match a diagram relationship.");
        if (geometry.Edges.Any(p => (model.Edges.Single(e => e.Key == p.Key).Label ?? "") != p.Value.Text))
            throw new ArgumentException("Rendered connector labels do not match this source.");
        _geometry = geometry; _oldGeometry = model.RenderedGeometry; _oldEngine = model.LayoutEngine;
        _oldWaypoints = new(model.EdgeWaypoints, StringComparer.Ordinal);
        _before = model.Nodes.ToDictionary(n => n, n => (n.X, n.Y, n.Width, n.Height, n.HasExplicitPosition, n.HasExplicitSize));
    }
    public void Do(DiagramModel model)
    {
        foreach (var node in _before.Keys)
        {
            var bounds = _geometry.Nodes[node.Id].Bounds;
            node.X = bounds.X; node.Y = bounds.Y; node.Width = bounds.Width; node.Height = bounds.Height;
            node.HasExplicitPosition = true; node.HasExplicitSize = true;
        }
        model.RenderedGeometry = _geometry; model.LayoutEngine = "manual";
        foreach (var key in _geometry.Edges.Keys) model.EdgeWaypoints.Remove(key);
    }
    public void Undo(DiagramModel model)
    {
        foreach (var (node, before) in _before)
        {
            node.X = before.X; node.Y = before.Y; node.Width = before.Width; node.Height = before.Height;
            node.HasExplicitPosition = before.Positioned; node.HasExplicitSize = before.Sized;
        }
        model.RenderedGeometry = _oldGeometry; model.LayoutEngine = _oldEngine;
        model.EdgeWaypoints.Clear();
        foreach (var (key, route) in _oldWaypoints) model.EdgeWaypoints[key] = route;
    }
}
