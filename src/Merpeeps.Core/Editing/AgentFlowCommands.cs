using System.Text.Json.Nodes;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class AgentFlowEditCommand : IEditCommand
{
    private readonly Action<AgentFlowDocument> _edit; private AgentFlowDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal AgentFlowEditCommand(string description, string? id, Action<AgentFlowDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.AgentFlow ?? throw new ArgumentException("Choose Agentflow.");
        var next = before.Clone(); _edit(next); AgentFlowSemantics.Check(next); _ = AgentFlowDiagramAdapter.Write(next);
        _ = AgentFlowDiagramLayout.Create(new DiagramModel { Kind = "agentflow-beta", AgentFlow = next });
        _before = before; model.AgentFlow = next;
    }
    public void Undo(DiagramModel model) => model.AgentFlow = _before ?? throw new InvalidOperationException("Edit is not applied.");
}
public static class AgentFlowEditing
{
    public static AgentFlowEditCommand AddElement(AgentFlowElement element)
    { var copy = element.DeepCopy(); return new("Add agent-flow element", element.Id, d => d.Elements.Add(copy.DeepCopy())); }
    public static AgentFlowEditCommand UpdateElement(AgentFlowElement element)
    { var copy = element.DeepCopy(); return new("Edit agent-flow element", element.Id, d => d.Elements[Index(d.Elements, e => e.Id == element.Id)] = copy.DeepCopy()); }
    public static AgentFlowEditCommand RemoveElement(string id) => new("Remove agent-flow subtree", null, d =>
    {
        _ = Index(d.Elements, e => e.Id == id); var removed = new HashSet<string> { id };
        while (true)
        {
            var children = d.Elements.Where(e => e.ParentId is not null && removed.Contains(e.ParentId) && !removed.Contains(e.Id)).Select(e => e.Id).ToArray();
            if (children.Length == 0) break; removed.UnionWith(children);
        }
        d.Elements.RemoveAll(e => removed.Contains(e.Id)); d.Links.RemoveAll(l => removed.Contains(l.From) || removed.Contains(l.To));
    });
    public static AgentFlowEditCommand AddLink(AgentFlowLink link)
    { var copy = link.DeepCopy(); return new("Add agent-flow link", link.Id, d => d.Links.Add(copy.DeepCopy())); }
    public static AgentFlowEditCommand UpdateLink(AgentFlowLink link)
    { var copy = link.DeepCopy(); return new("Edit agent-flow link", link.Id, d => d.Links[Index(d.Links, l => l.Id == link.Id)] = copy.DeepCopy()); }
    public static AgentFlowEditCommand RemoveLink(string id) => new("Remove agent-flow link", null, d => d.Links.RemoveAt(Index(d.Links, l => l.Id == id)));
    public static AgentFlowEditCommand SetDirection(Direction direction) => new("Change workflow direction", null, d => d.Direction = direction);
    public static AgentFlowEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit agent-flow layout option", null, d => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Agentflow element no longer exists.");
}
