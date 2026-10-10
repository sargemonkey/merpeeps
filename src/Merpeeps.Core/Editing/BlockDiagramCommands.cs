using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed record BlockLocation(string? ParentId, int Index, BlockItem Item);
public sealed class BlockEditCommand : IEditCommand
{
    private readonly Action<BlockDiagramDocument> _edit;
    private BlockDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal BlockEditCommand(string description, string? resultId, Action<BlockDiagramDocument> edit) =>
        (Description, ResultId, _edit) = (description, resultId, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Blocks ?? throw new ArgumentException("Choose a Block diagram.");
        var candidate = before.Clone(); _edit(candidate); BlockDiagramAdapter.Check(candidate); _ = BlockDiagramAdapter.Write(candidate);
        _before = before; model.Blocks = candidate;
    }
    public void Undo(DiagramModel model) => model.Blocks = _before ?? throw new InvalidOperationException("Block edit has not been applied.");
}
public static class BlockDiagramEditing
{
    public static BlockLocation Locate(BlockDiagramDocument document, string id)
    {
        return Find(document.Items, null) ?? throw new ArgumentException("Block no longer exists.");
        BlockLocation? Find(IReadOnlyList<BlockItem> items, string? parent)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Id == id) return new(parent, index, items[index]);
                if (Find(items[index].Children, items[index].Id) is { } child) return child;
            }
            return null;
        }
    }
    public static BlockEditCommand Add(BlockItem item, string? parentId = null, int? index = null)
    {
        var snapshot = item.DeepCopy();
        return new("Add block", item.Id, d => Change(d, parentId, items => items.Insert(index ?? items.Count, snapshot.DeepCopy())));
    }
    public static BlockEditCommand Update(BlockItem item)
    {
        var snapshot = item.DeepCopy();
        return new("Edit block", item.Id, d =>
        {
            var location = Locate(d, item.Id);
            Change(d, location.ParentId, items => items[location.Index] = snapshot with { Children = location.Item.Children });
        });
    }
    public static BlockEditCommand Move(string id, string? parentId, int index) => new("Move block in grid", id, d =>
    {
        var old = Locate(d, id);
        if (parentId is not null && BlockDiagramDocument.Descendants([old.Item]).Any(n => n.Id == parentId))
            throw new ArgumentException("A block cannot move inside its own subtree.");
        Change(d, old.ParentId, items => items.RemoveAt(old.Index));
        Change(d, parentId, items => items.Insert(index, old.Item));
    });
    public static BlockEditCommand Remove(string id) => new("Remove block and incident connections", null, d =>
    {
        var old = Locate(d, id); var removed = BlockDiagramDocument.Descendants([old.Item]).Select(n => n.Id).ToHashSet();
        Change(d, old.ParentId, items => items.RemoveAt(old.Index));
        d.Connections.RemoveAll(e => removed.Contains(e.From) || removed.Contains(e.To));
    });
    public static BlockEditCommand SetColumns(string? parentId, int? columns) => new("Change grid columns", parentId, d =>
    {
        if (parentId is null) d.Columns = columns;
        else
        {
            var location = Locate(d, parentId);
            if (location.Item.Kind != BlockItemKind.Composite) throw new ArgumentException("Only composite blocks own a grid.");
            Change(d, location.ParentId, items => items[location.Index] = location.Item with { Columns = columns });
        }
    });
    public static BlockEditCommand Connect(BlockConnection connection) => new("Connect blocks", connection.Id, d => d.Connections.Add(connection));
    public static BlockEditCommand UpdateConnection(BlockConnection connection) => new("Edit block connection", connection.Id, d =>
    {
        var index = d.Connections.FindIndex(e => e.Id == connection.Id);
        if (index < 0) throw new ArgumentException("Block connection no longer exists.");
        d.Connections[index] = connection;
    });
    public static BlockEditCommand RemoveConnection(string id) => new("Delete block connection", null, d =>
    {
        if (d.Connections.RemoveAll(e => e.Id == id) == 0) throw new ArgumentException("Block connection no longer exists.");
    });
    public static BlockEditCommand SetStyleClass(string name, JsonObject? style)
    {
        var copy = style?.DeepClone().AsObject();
        return new("Edit block style class", null, d => { if (copy is null) d.Classes.Remove(name); else d.Classes[name] = (JsonObject)copy.DeepClone(); });
    }
    private static void Change(BlockDiagramDocument document, string? parentId, Action<List<BlockItem>> change)
    {
        if (parentId is null) { change(document.Items); return; }
        var found = false; var roots = Transform(document.Items);
        if (!found) throw new ArgumentException("Parent composite no longer exists.");
        document.Items.Clear(); document.Items.AddRange(roots);
        List<BlockItem> Transform(IEnumerable<BlockItem> items) => items.Select(item =>
        {
            if (item.Id != parentId) return item with { Children = Transform(item.Children) };
            if (item.Kind != BlockItemKind.Composite) throw new ArgumentException("Blocks can only be moved into a composite.");
            var children = item.Children.ToList(); change(children); found = true;
            return item with { Children = children };
        }).ToList();
    }
}
