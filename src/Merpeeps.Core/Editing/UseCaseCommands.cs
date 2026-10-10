using System.Text.Json.Nodes;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class UseCaseEditCommand : IEditCommand
{
    private readonly Action<UseCaseDiagramDocument> _edit;
    private UseCaseDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal UseCaseEditCommand(string description, string? id, Action<UseCaseDiagramDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.UseCase ?? throw new ArgumentException("Choose a Use Case diagram.");
        var next = before.Clone(); _edit(next); UseCaseValidation.Check(next); _ = UseCaseDiagramAdapter.Write(next);
        _ = UseCaseDiagramLayout.Create(new DiagramModel { Kind = "usecase-beta", UseCase = next });
        _before = before; model.UseCase = next;
    }
    public void Undo(DiagramModel model) => model.UseCase = _before ?? throw new InvalidOperationException("Use-case edit is not applied.");
}
public static class UseCaseEditing
{
    public static UseCaseEditCommand AddNode(UseCaseNode node)
    { var copy = node.DeepCopy(); return new("Add actor / use case / table / note", node.Id, d => d.Nodes.Add(copy.DeepCopy())); }
    public static UseCaseEditCommand UpdateNode(UseCaseNode node)
    { var copy = node.DeepCopy(); return new("Edit use-case element", node.Id, d => d.Nodes[Index(d.Nodes, n => n.Id == node.Id)] = copy.DeepCopy()); }
    public static UseCaseEditCommand RemoveNode(string id) => new("Remove element, attached notes and relationships", null, d =>
    {
        var removed = d.Nodes.Where(n => n.Id == id || n.NoteTargetId == id).Select(n => n.Id).ToHashSet();
        if (removed.Count == 0) throw new ArgumentException("Use-case element no longer exists.");
        d.Nodes.RemoveAll(n => removed.Contains(n.Id)); d.Relationships.RemoveAll(r => removed.Contains(r.From) || removed.Contains(r.To));
    });
    public static UseCaseEditCommand AddBoundary(UseCaseBoundary boundary)
    { var copy = boundary.DeepCopy(); return new("Add system boundary", boundary.Id, d => d.Boundaries.Add(copy.DeepCopy())); }
    public static UseCaseEditCommand UpdateBoundary(UseCaseBoundary boundary)
    { var copy = boundary.DeepCopy(); return new("Edit system boundary", boundary.Id, d => d.Boundaries[Index(d.Boundaries, b => b.Id == boundary.Id)] = copy.DeepCopy()); }
    public static UseCaseEditCommand RemoveBoundary(string id) => new("Remove system boundary, retaining its elements", null, d =>
    {
        d.Boundaries.RemoveAt(Index(d.Boundaries, b => b.Id == id));
        for (var i = 0; i < d.Nodes.Count; i++) if (d.Nodes[i].BoundaryId == id) d.Nodes[i] = d.Nodes[i] with { BoundaryId = null };
    });
    public static UseCaseEditCommand AddRelationship(UseCaseRelationship relationship)
    { var copy = relationship.DeepCopy(); return new("Add UML relationship", relationship.Id, d => d.Relationships.Add(copy.DeepCopy())); }
    public static UseCaseEditCommand UpdateRelationship(UseCaseRelationship relationship)
    { var copy = relationship.DeepCopy(); return new("Edit UML relationship", relationship.Id, d => d.Relationships[Index(d.Relationships, r => r.Id == relationship.Id)] = copy.DeepCopy()); }
    public static UseCaseEditCommand RemoveRelationship(string id) => new("Remove UML relationship", null, d => d.Relationships.RemoveAt(Index(d.Relationships, r => r.Id == id)));
    public static UseCaseEditCommand SetDirection(Direction direction) => new("Change use-case direction", null, d => d.Direction = direction);
    public static UseCaseEditCommand SetStyleClass(string name, JsonObject? style)
    {
        var copy = style?.DeepClone().AsObject();
        return new("Edit use-case style class", null, d => { if (copy is null) d.Classes.Remove(name); else d.Classes[name] = (JsonObject)copy.DeepClone(); });
    }
    public static UseCaseEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit use-case display option", null, d =>
        {
            if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone();
        });
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Use-case element no longer exists.");
}
