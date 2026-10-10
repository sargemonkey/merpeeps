using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class RequirementEditCommand : IEditCommand
{
    private readonly Action<RequirementDocument> _change;
    private RequirementDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal RequirementEditCommand(string description, string? resultId, Action<RequirementDocument> change) => (Description, ResultId, _change) = (description, resultId, change);
    public void Do(DiagramModel model)
    {
        var original = model.Requirements ?? throw new ArgumentException("Choose a requirement diagram.");
        var candidate = original.Clone(); _change(candidate); RequirementDiagramAdapter.Validate(candidate);
        _ = RequirementDiagramAdapter.Write(candidate); _before = original; model.Requirements = candidate;
    }
    public void Undo(DiagramModel model) => model.Requirements = _before ?? throw new InvalidOperationException("Not applied.");
}
public static class RequirementEditing
{
    public static RequirementEditCommand Add(RequirementItem item)
    {
        var copy = item.DeepCopy();
        return new("Add requirement element", item.Id, d => d.Items.Add(copy.DeepCopy()));
    }
    public static RequirementEditCommand Update(RequirementItem item)
    {
        var copy = item.DeepCopy();
        return new("Edit requirement element", item.Id, d => d.Items[Index(d.Items, n => n.Id == item.Id)] = copy.DeepCopy());
    }
    public static RequirementEditCommand Remove(string id) => new("Delete requirement element and relationships", null, d =>
    {
        d.Items.RemoveAt(Index(d.Items, n => n.Id == id)); d.Relations.RemoveAll(r => r.From == id || r.To == id);
    });
    public static RequirementEditCommand Connect(RequirementRelation relation) => new("Add requirement relationship", relation.Id, d => d.Relations.Add(relation));
    public static RequirementEditCommand UpdateRelation(RequirementRelation relation) => new("Edit requirement relationship", relation.Id, d => d.Relations[Index(d.Relations, r => r.Id == relation.Id)] = relation);
    public static RequirementEditCommand RemoveRelation(string id) => new("Delete requirement relationship", null, d => d.Relations.RemoveAt(Index(d.Relations, r => r.Id == id)));
    public static RequirementEditCommand SetDirection(Direction direction) => new("Change requirement direction", null, d => d.Direction = direction);
    public static RequirementEditCommand SetClass(string name, JsonObject? style)
    {
        var copy = style?.DeepClone().AsObject();
        return new("Edit requirement style class", null, d => { if (copy is null) d.Classes.Remove(name); else d.Classes[name] = (JsonObject)copy.DeepClone(); });
    }
    private static int Index<T>(IReadOnlyList<T> values, Func<T, bool> match)
    {
        for (var i = 0; i < values.Count; i++) if (match(values[i])) return i;
        throw new ArgumentException("Requirement item no longer exists.");
    }
}
