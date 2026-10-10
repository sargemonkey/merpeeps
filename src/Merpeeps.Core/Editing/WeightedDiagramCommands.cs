using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class WeightedEditCommand : IEditCommand
{
    private readonly Action<SankeyDocument>? _flow;
    private readonly Action<TreemapDocument>? _tree;
    private SankeyDocument? _oldFlow;
    private TreemapDocument? _oldTree;
    public string Description { get; }
    public string? ResultId { get; }
    internal WeightedEditCommand(string name, string? result, Action<SankeyDocument> edit) => (Description, ResultId, _flow) = (name, result, edit);
    internal WeightedEditCommand(string name, string? result, Action<TreemapDocument> edit) => (Description, ResultId, _tree) = (name, result, edit);
    public void Do(DiagramModel model)
    {
        if (_flow is not null)
        {
            var old = model.Sankey ?? throw new ArgumentException("Choose a Sankey.");
            var next = old.Clone(); _flow(next); SankeyDiagramAdapter.Validate(next); _ = SankeyDiagramAdapter.Write(next);
            _oldFlow = old; model.Sankey = next;
        }
        else
        {
            var old = model.Treemap ?? throw new ArgumentException("Choose a treemap.");
            var next = old.Clone(); _tree!(next); TreemapDiagramAdapter.Validate(next); _ = TreemapDiagramAdapter.Write(next);
            _oldTree = old; model.Treemap = next;
        }
    }
    public void Undo(DiagramModel model)
    {
        if (_flow is not null) model.Sankey = _oldFlow ?? throw new InvalidOperationException("Not applied.");
        else model.Treemap = _oldTree ?? throw new InvalidOperationException("Not applied.");
    }
}
public static class SankeyEditing
{
    public static WeightedEditCommand AddFlow(string from, string to, double value)
    {
        var linkId = "f" + Guid.NewGuid().ToString("N");
        var fromId = "n" + Guid.NewGuid().ToString("N"); var toId = "n" + Guid.NewGuid().ToString("N");
        return new("Add weighted flow", linkId, (SankeyDocument d) =>
        {
            var a = d.Nodes.FirstOrDefault(n => n.Label == from);
            if (a is null) { a = new(fromId, from); d.Nodes.Add(a); }
            var b = d.Nodes.FirstOrDefault(n => n.Label == to);
            if (b is null) { b = new(toId, to); d.Nodes.Add(b); }
            d.Links.Add(new(linkId, a.Id, b.Id, value));
        });
    }
    public static WeightedEditCommand UpdateFlow(SankeyLink link) => new("Edit weighted flow", link.Id, (SankeyDocument d) =>
    {
        var index = d.Links.FindIndex(l => l.Id == link.Id);
        if (index < 0) throw new ArgumentException("Flow no longer exists.");
        d.Links[index] = link;
    });
    public static WeightedEditCommand RenameNode(string id, string label) => new("Rename flow node", id, (SankeyDocument d) =>
    {
        var index = d.Nodes.FindIndex(n => n.Id == id); if (index < 0) throw new ArgumentException("Flow node no longer exists.");
        var oldLabel = d.Nodes[index].Label;
        if (oldLabel != label && d.Configuration["nodeColors"] is JsonObject colors && colors.TryGetPropertyValue(oldLabel, out var color))
        {
            if (colors.TryGetPropertyValue(label, out var existing) && !JsonNode.DeepEquals(color, existing))
                throw new ArgumentException("The new label already has a different configured node color. Resolve that color entry before renaming.");
            colors[label] = color?.DeepClone();
            colors.Remove(oldLabel);
        }
        d.Nodes[index] = d.Nodes[index] with { Label = label };
    });
    public static WeightedEditCommand RemoveFlow(string id) => new("Delete weighted flow", null, (SankeyDocument d) =>
    {
        if (d.Links.RemoveAll(l => l.Id == id) == 0) throw new ArgumentException("Flow no longer exists.");
        d.Nodes.RemoveAll(n => d.Links.All(l => l.From != n.Id && l.To != n.Id));
    });
    public static WeightedEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit Sankey option", null, (SankeyDocument d) => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
}
public sealed record TreemapLocation(string? ParentId, int Index, TreemapNode Node);
public static class TreemapEditing
{
    public static TreemapLocation Locate(TreemapDocument document, string id)
    {
        return Find(document.Roots, null) ?? throw new ArgumentException("Treemap node no longer exists.");
        TreemapLocation? Find(IReadOnlyList<TreemapNode> nodes, string? parent)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Id == id) return new(parent, i, nodes[i]);
                if (Find(nodes[i].Children, nodes[i].Id) is { } child) return child;
            }
            return null;
        }
    }
    public static WeightedEditCommand Add(TreemapNode node, string? parentId = null)
    {
        var copy = node.DeepCopy();
        return new("Add treemap node", node.Id, (TreemapDocument d) => Change(d, parentId, nodes => nodes.Add(copy.DeepCopy())));
    }
    public static WeightedEditCommand Update(TreemapNode node)
    {
        var copy = node.DeepCopy();
        return new("Edit treemap node", node.Id, (TreemapDocument d) =>
        {
            var location = Locate(d, node.Id);
            Change(d, location.ParentId, nodes => nodes[location.Index] = copy with { Children = location.Node.Children });
        });
    }
    public static WeightedEditCommand Remove(string id) => new("Delete weighted subtree", null, (TreemapDocument d) =>
    {
        var location = Locate(d, id); Change(d, location.ParentId, nodes => nodes.RemoveAt(location.Index));
    });
    public static WeightedEditCommand Move(string id, string? parentId, int index) => new("Move weighted subtree", id, (TreemapDocument d) =>
    {
        var location = Locate(d, id);
        if (parentId is not null && TreemapDocument.Walk([location.Node]).Any(n => n.Id == parentId)) throw new ArgumentException("A subtree cannot contain itself.");
        Change(d, location.ParentId, nodes => nodes.RemoveAt(location.Index));
        Change(d, parentId, nodes => nodes.Insert(index, location.Node));
    });
    public static WeightedEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit treemap option", null, (TreemapDocument d) => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    public static WeightedEditCommand SetClass(string name, JsonObject? style)
    {
        var copy = style?.DeepClone().AsObject();
        return new("Edit treemap style class", null, (TreemapDocument d) => { if (copy is null) d.Classes.Remove(name); else d.Classes[name] = (JsonObject)copy.DeepClone(); });
    }
    private static void Change(TreemapDocument d, string? parentId, Action<List<TreemapNode>> change)
    {
        if (parentId is null) { change(d.Roots); return; }
        var found = false;
        var roots = Transform(d.Roots);
        if (!found) throw new ArgumentException("Parent node no longer exists.");
        d.Roots.Clear(); d.Roots.AddRange(roots);
        List<TreemapNode> Transform(IEnumerable<TreemapNode> nodes) => nodes.Select(n =>
        {
            var children = n.Children.ToList();
            if (n.Id == parentId)
            {
                if (n.Value is not null) throw new ArgumentException("A leaf with a value cannot receive children.");
                change(children); found = true;
            }
            else children = Transform(children);
            return n with { Children = children };
        }).ToList();
    }
}
