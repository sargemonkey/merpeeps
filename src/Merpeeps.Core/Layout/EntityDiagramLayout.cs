using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public static class EntityDiagramLayout
{
    /// <summary>Respect manually positioned entities; otherwise arrange groups recursively in their declared directions.</summary>
    public static void Apply(DiagramModel model)
    {
        if (model.EntityOptions is null) throw new ArgumentException("Expected an ER model.");
        if (model.LayoutEngine is not (null or "manual" or "layered"))
        {
            LayoutEngines.Apply(model, model.LayoutEngine);
            return;
        }
        if (model.AnyExplicitPositions)
        {
            LayeredLayout.Apply(model);
            return;
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        model.EntityOptions.EmptyGroupLayouts.Clear();
        Arrange(null, model.Direction);

        (double Width, double Height) Arrange(string? groupId, Direction inherited)
        {
            if (groupId is not null && !paths.Add(groupId)) throw new ArgumentException("Subgraph cycle.");
            var group = groupId is null ? null : model.Subgraphs.Single(g => g.Id == groupId);
            var direction = group?.Direction ?? inherited;
            var children = model.Subgraphs.Where(g => g.ParentId == groupId).ToArray();
            var immediate = model.Nodes.Where(n => n.SubgraphId == groupId).ToArray();
            var cells = new DiagramModel { Direction = direction };
            cells.Nodes.AddRange(immediate);
            var proxies = new Dictionary<string, DiagramNode>();
            foreach (var child in children)
            {
                var size = Arrange(child.Id, direction);
                var proxy = new DiagramNode { Id = child.Id, Width = size.Width + 40, Height = size.Height + 56 };
                proxies.Add(child.Id, proxy);
                cells.Nodes.Add(proxy);
            }
            foreach (var edge in model.Edges)
            {
                var from = CellFor(edge.From);
                var to = CellFor(edge.To);
                if (from is not null && to is not null && from != to)
                    cells.Edges.Add(new() { From = from, To = to });
            }
            LayeredLayout.Apply(cells);
            foreach (var child in children)
            {
                var proxy = proxies[child.Id];
                foreach (var node in Descendants(model, child.Id))
                {
                    node.X += proxy.X + 20;
                    node.Y += proxy.Y + 36;
                }
                foreach (var id in model.EntityOptions.EmptyGroupLayouts.Keys.ToArray())
                {
                    var parent = id;
                    while (parent != child.Id && model.Subgraphs.Single(g => g.Id == parent).ParentId is { } next)
                        parent = next;
                    if (parent != child.Id) continue;
                    var frame = model.EntityOptions.EmptyGroupLayouts[id];
                    model.EntityOptions.EmptyGroupLayouts[id] = frame with
                    {
                        X = frame.X + proxy.X + (id == child.Id ? 0 : 20),
                        Y = frame.Y + proxy.Y + (id == child.Id ? 0 : 36),
                    };
                }
            }
            if (groupId is not null) paths.Remove(groupId);
            if (groupId is not null && cells.Nodes.Count == 0)
                model.EntityOptions.EmptyGroupLayouts[groupId] = new(0, 0, 240, 120);
            return cells.Nodes.Count == 0 ? (200, 64)
                : (cells.Nodes.Max(n => n.X + n.Width), cells.Nodes.Max(n => n.Y + n.Height));

            string? CellFor(string endpoint)
            {
                if (cells.Nodes.Any(n => n.Id == endpoint)) return endpoint;
                var scope = model.Node(endpoint)?.SubgraphId ?? model.Subgraphs.FirstOrDefault(g => g.Id == endpoint)?.ParentId;
                var seen = new HashSet<string>();
                while (scope is not null && seen.Add(scope))
                {
                    if (proxies.ContainsKey(scope)) return scope;
                    scope = model.Subgraphs.FirstOrDefault(g => g.Id == scope)?.ParentId;
                }
                return null;
            }
        }
    }

    public static IReadOnlyList<DiagramNode> Descendants(DiagramModel model, string groupId)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal) { groupId };
        var pending = new Queue<string>();
        pending.Enqueue(groupId);
        while (pending.TryDequeue(out var current))
            foreach (var child in model.Subgraphs.Where(g => g.ParentId == current))
            {
                if (!groups.Add(child.Id)) throw new ArgumentException("Subgraph cycle.");
                pending.Enqueue(child.Id);
            }
        return model.Nodes.Where(n => n.SubgraphId is { } scope && groups.Contains(scope)).ToArray();
    }
}
