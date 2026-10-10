using System.Text.Json.Nodes;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class SwimlaneEditCommand : IEditCommand
{
    private readonly Action<SwimlaneDiagramDocument> _edit;
    private SwimlaneDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal SwimlaneEditCommand(string description, string? id, Action<SwimlaneDiagramDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Swimlane ?? throw new ArgumentException("Choose a Swimlane diagram.");
        var next = before.Clone(); _edit(next); SwimlaneValidation.Check(next); _ = SwimlaneDiagramAdapter.Write(next);
        _ = SwimlaneDiagramLayout.Create(new DiagramModel { Kind = "swimlane-beta", Swimlane = next });
        _before = before; model.Swimlane = next;
    }
    public void Undo(DiagramModel model) => model.Swimlane = _before ?? throw new InvalidOperationException("Swimlane edit is not applied.");
}
public static class SwimlaneEditing
{
    public static SwimlaneEditCommand AddGroup(SwimlaneGroup group)
    { var copy = group.DeepCopy(); return new("Add responsibility lane / group", group.Id, d => d.Groups.Add(copy.DeepCopy())); }
    public static SwimlaneEditCommand UpdateGroup(SwimlaneGroup group)
    { var copy = group.DeepCopy(); return new("Edit responsibility lane / group", group.Id, d => d.Groups[Index(d.Groups, g => g.Id == group.Id)] = copy.DeepCopy()); }
    public static SwimlaneEditCommand MoveGroup(string id, int position) => new("Reorder lanes / groups", id, d =>
    { var index = Index(d.Groups, g => g.Id == id); var group = d.Groups[index]; d.Groups.RemoveAt(index); d.Groups.Insert(position, group); });
    public static SwimlaneEditCommand RemoveGroup(string id) => new("Remove lane / group and reparent its contents", null, d =>
    {
        var index = Index(d.Groups, g => g.Id == id); var parent = d.Groups[index].ParentId; d.Groups.RemoveAt(index);
        for (var i = 0; i < d.Nodes.Count; i++) if (d.Nodes[i].GroupId == id) d.Nodes[i] = d.Nodes[i] with { GroupId = parent };
        for (var i = 0; i < d.Groups.Count; i++) if (d.Groups[i].ParentId == id) d.Groups[i] = d.Groups[i] with { ParentId = parent };
    });
    public static SwimlaneEditCommand AddNode(SwimlaneNode node)
    { var copy = node.DeepCopy(); return new("Add lane step", node.Id, d => d.Nodes.Add(copy.DeepCopy())); }
    public static SwimlaneEditCommand UpdateNode(SwimlaneNode node)
    { var copy = node.DeepCopy(); return new("Edit lane step", node.Id, d => d.Nodes[Index(d.Nodes, n => n.Id == node.Id)] = copy.DeepCopy()); }
    public static SwimlaneEditCommand RemoveNode(string id) => new("Remove lane step and handoffs", null, d =>
    { d.Nodes.RemoveAt(Index(d.Nodes, n => n.Id == id)); d.Links.RemoveAll(l => l.From == id || l.To == id); });
    public static SwimlaneEditCommand AddLink(SwimlaneLink link) => new("Add handoff", link.Id, d => d.Links.Add(link));
    public static SwimlaneEditCommand UpdateLink(SwimlaneLink link) => new("Edit handoff", link.Id, d => d.Links[Index(d.Links, l => l.Id == link.Id)] = link);
    public static SwimlaneEditCommand RemoveLink(string id) => new("Remove handoff", null, d => d.Links.RemoveAt(Index(d.Links, l => l.Id == id)));
    public static SwimlaneEditCommand SetDirection(Direction direction) => new("Edit process direction", null, d => d.Direction = direction);
    public static SwimlaneEditCommand SetStyleClass(string name, JsonObject? values)
    {
        var copy = values?.DeepClone().AsObject();
        return new("Edit lane style class", null, d => { if (copy is null) d.Classes.Remove(name); else d.Classes[name] = (JsonObject)copy.DeepClone(); });
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Lane element no longer exists.");
}
