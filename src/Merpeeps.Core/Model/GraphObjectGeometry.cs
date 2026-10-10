namespace Merpeeps.Core.Model;

public enum GraphObjectKind { Node, Group }
public readonly record struct GraphObject(GraphObjectKind Kind, string Id);

public static class GraphObjectGeometry
{
    public static bool SupportsArrangement(DiagramModel model) =>
        model.Kind is "flowchart" or "graph" or "flowchart-elk" or "stateDiagram" or "stateDiagram-v2"
            or "classDiagram" or "classDiagram-v2" or "erDiagram";

    public static IReadOnlyList<GraphObject> Normalize(DiagramModel model, IEnumerable<GraphObject> objects)
    {
        if (!SupportsArrangement(model)) throw new ArgumentException("This diagram uses semantic layout rather than free object positioning.");
        var items = objects.Distinct().ToArray();
        foreach (var item in items) _ = Bounds(model, item);
        var groups = items.Where(i => i.Kind == GraphObjectKind.Group)
            .ToDictionary(i => i, i => model.Subgraphs.Single(g => g.Id == i.Id));
        var descendants = groups.ToDictionary(p => p.Key, p => GraphGroupGeometry.Descendants(model, p.Value).Select(g => g.Id).ToHashSet());
        var members = groups.ToDictionary(p => p.Key, p => GraphGroupGeometry.Members(model, p.Value).Select(n => n.Id).ToHashSet());
        return items.Where(item => !groups.Keys.Any(parent => parent != item &&
            (item.Kind == GraphObjectKind.Group ? descendants[parent].Contains(item.Id) : members[parent].Contains(item.Id)))).ToArray();
    }

    public static DiagramRect Bounds(DiagramModel model, GraphObject item)
    {
        if (item.Kind == GraphObjectKind.Group)
        {
            var group = model.Subgraphs.SingleOrDefault(g => g.Id == item.Id)
                ?? throw new ArgumentException($"Group '{item.Id}' no longer exists.");
            return GraphGroupGeometry.Bounds(model, group);
        }
        if (item.Kind != GraphObjectKind.Node) throw new ArgumentException("Unknown graph object kind.");
        var node = model.Node(item.Id) ?? throw new ArgumentException($"Node '{item.Id}' no longer exists.");
        return new(node.X, node.Y, node.Width, node.Height);
    }
}
