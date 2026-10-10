using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class C4EditCommand : IEditCommand
{
    private readonly Action<C4DiagramDocument> _change;
    private C4DiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal C4EditCommand(string description, string? result, Action<C4DiagramDocument> change) => (Description, ResultId, _change) = (description, result, change);
    public void Do(DiagramModel model)
    {
        var before = model.C4 ?? throw new ArgumentException("Choose a C4 diagram.");
        var next = before.Clone(); _change(next); C4DiagramValidation.Check(next); _ = C4DiagramAdapter.Write(next);
        _before = before; model.C4 = next;
    }
    public void Undo(DiagramModel model) => model.C4 = _before ?? throw new InvalidOperationException("C4 edit has not been applied.");
}
public static class C4DiagramEditing
{
    public static C4EditCommand AddElement(C4Element element)
    {
        var copy = element.DeepCopy();
        return new("Add C4 element", element.Id, d => d.Elements.Add(copy.DeepCopy()));
    }
    public static C4EditCommand UpdateElement(C4Element element)
    {
        var copy = element.DeepCopy();
        return new("Edit C4 element", element.Id, d => d.Elements[Index(d.Elements, e => e.Id == element.Id)] = copy.DeepCopy());
    }
    public static C4EditCommand MoveElement(string id, string? parentId, int position) => new("Move C4 element", id, d =>
    {
        var index = Index(d.Elements, e => e.Id == id); var element = d.Elements[index]; d.Elements.RemoveAt(index);
        var siblings = d.Elements.Where(e => e.ParentId == parentId).ToArray();
        if (position < 0 || position > siblings.Length) throw new ArgumentOutOfRangeException(nameof(position));
        var targetIndex = position < siblings.Length ? d.Elements.IndexOf(siblings[position]) : siblings.Length > 0 ? d.Elements.IndexOf(siblings[^1]) + 1 : d.Elements.Count;
        d.Elements.Insert(targetIndex, element with { ParentId = parentId });
    });
    public static C4EditCommand RemoveElement(string id) => new("Remove C4 subtree and relationships", null, d =>
    {
        _ = Index(d.Elements, e => e.Id == id);
        var removed = new HashSet<string> { id };
        while (true)
        {
            var children = d.Elements.Where(e => e.ParentId is { } parent && removed.Contains(parent) && !removed.Contains(e.Id)).Select(e => e.Id).ToArray();
            if (children.Length == 0) break;
            removed.UnionWith(children);
        }
        d.Elements.RemoveAll(e => removed.Contains(e.Id));
        d.Relationships.RemoveAll(r => removed.Contains(r.From) || removed.Contains(r.To));
    });
    public static C4EditCommand AddRelationship(C4Relationship relation)
    {
        var copy = relation.DeepCopy(); return new("Add C4 relationship", relation.Id, d => d.Relationships.Add(copy.DeepCopy()));
    }
    public static C4EditCommand UpdateRelationship(C4Relationship relation)
    {
        var copy = relation.DeepCopy(); return new("Edit C4 relationship", relation.Id, d => d.Relationships[Index(d.Relationships, r => r.Id == relation.Id)] = copy.DeepCopy());
    }
    public static C4EditCommand RemoveRelationship(string id) => new("Remove C4 relationship", null, d => d.Relationships.RemoveAt(Index(d.Relationships, r => r.Id == id)));
    public static C4EditCommand MoveRelationship(string id, int position) => new("Reorder C4 relationship", id, d =>
    {
        var index = Index(d.Relationships, r => r.Id == id); var relation = d.Relationships[index]; d.Relationships.RemoveAt(index); d.Relationships.Insert(position, relation);
    });
    public static C4EditCommand SetLayout(int shapesPerRow, int boundariesPerRow) => new("Edit C4 row layout", null, d =>
    {
        d.LayoutOptions["c4ShapeInRow"] = shapesPerRow; d.LayoutOptions["c4BoundaryInRow"] = boundariesPerRow;
    });
    public static C4EditCommand SetTitle(string title) => new("Edit C4 title", null, d => d.Title = title);
    public static C4EditCommand SetWrap(bool wrap) => new("Edit C4 wrapping", null, d => d.Configuration["wrap"] = wrap);
    private static int Index<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var i = 0; i < items.Count; i++) if (predicate(items[i])) return i;
        throw new ArgumentException("C4 element or relationship no longer exists.");
    }
}
