using System.Text.Json.Nodes;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class StrategyEditCommand : IEditCommand
{
    private readonly Action<WardleyDiagramDocument>? _wardley;
    private readonly Action<IshikawaDocument>? _ishikawa;
    private readonly Action<CynefinDocument>? _cynefin;
    private WardleyDiagramDocument? _oldWardley;
    private IshikawaDocument? _oldIshikawa;
    private CynefinDocument? _oldCynefin;
    public string Description { get; }
    public string? ResultId { get; }
    internal StrategyEditCommand(string description, string? result, Action<WardleyDiagramDocument> edit) => (Description, ResultId, _wardley) = (description, result, edit);
    internal StrategyEditCommand(string description, string? result, Action<IshikawaDocument> edit) => (Description, ResultId, _ishikawa) = (description, result, edit);
    internal StrategyEditCommand(string description, string? result, Action<CynefinDocument> edit) => (Description, ResultId, _cynefin) = (description, result, edit);
    public void Do(DiagramModel model)
    {
        if (_wardley is not null)
        {
            var before = model.Wardley ?? throw new ArgumentException("Choose a Wardley map.");
            var next = before.Clone(); _wardley(next); _ = WardleyDiagramAdapter.Write(next);
            _ = StrategyDiagramLayouts.Wardley(new DiagramModel { Kind = "wardley-beta", Wardley = next });
            _oldWardley = before; model.Wardley = next;
        }
        else if (_ishikawa is not null)
        {
            var before = model.Ishikawa ?? throw new ArgumentException("Choose an Ishikawa diagram.");
            var next = before.Clone(); _ishikawa(next); _ = IshikawaDiagramAdapter.Write(next);
            _ = StrategyDiagramLayouts.Ishikawa(new DiagramModel { Kind = "ishikawa-beta", Ishikawa = next });
            _oldIshikawa = before; model.Ishikawa = next;
        }
        else
        {
            var before = model.Cynefin ?? throw new ArgumentException("Choose a Cynefin framework.");
            var next = before.Clone(); _cynefin!(next); _ = CynefinDiagramAdapter.Write(next);
            _ = StrategyDiagramLayouts.Cynefin(new DiagramModel { Kind = "cynefin-beta", Cynefin = next });
            _oldCynefin = before; model.Cynefin = next;
        }
    }
    public void Undo(DiagramModel model)
    {
        if (_wardley is not null) model.Wardley = _oldWardley ?? throw new InvalidOperationException("Not applied.");
        else if (_ishikawa is not null) model.Ishikawa = _oldIshikawa ?? throw new InvalidOperationException("Not applied.");
        else model.Cynefin = _oldCynefin ?? throw new InvalidOperationException("Not applied.");
    }
}
public static class WardleyEditing
{
    public static StrategyEditCommand AddComponent(WardleyComponent component) => new("Add Wardley component", component.Id, (WardleyDiagramDocument d) => d.Components.Add(component));
    public static StrategyEditCommand UpdateComponent(WardleyComponent component) => new("Edit Wardley component", component.Id, (WardleyDiagramDocument d) =>
        d.Components[Index(d.Components, c => c.Id == component.Id)] = component);
    public static StrategyEditCommand MoveComponent(string id, double visibility, double evolution) => new("Move Wardley component", id, (WardleyDiagramDocument d) =>
    {
        var index = Index(d.Components, c => c.Id == id); var component = d.Components[index];
        d.Components[index] = component with { Visibility = component.PipelineParentId is null ? visibility : null, Evolution = evolution };
    });
    public static StrategyEditCommand RemoveComponent(string id) => new("Remove Wardley component and variants", null, (WardleyDiagramDocument d) =>
    {
        _ = Index(d.Components, c => c.Id == id);
        var ids = d.Components.Where(c => c.Id == id || c.PipelineParentId == id).Select(c => c.Id).ToHashSet();
        d.Components.RemoveAll(c => ids.Contains(c.Id)); d.Links.RemoveAll(l => ids.Contains(l.From) || ids.Contains(l.To));
    });
    public static StrategyEditCommand AddLink(WardleyLink link) => new("Add Wardley dependency", link.Id, (WardleyDiagramDocument d) => d.Links.Add(link));
    public static StrategyEditCommand UpdateLink(WardleyLink link) => new("Edit Wardley dependency", link.Id, (WardleyDiagramDocument d) => d.Links[Index(d.Links, l => l.Id == link.Id)] = link);
    public static StrategyEditCommand RemoveLink(string id) => new("Remove Wardley dependency", null, (WardleyDiagramDocument d) => d.Links.RemoveAt(Index(d.Links, l => l.Id == id)));
    public static StrategyEditCommand SetAnnotation(WardleyAnnotation annotation) => new("Edit Wardley annotation", annotation.Id, (WardleyDiagramDocument d) =>
    {
        var index = d.Annotations.FindIndex(a => a.Id == annotation.Id); if (index < 0) d.Annotations.Add(annotation); else d.Annotations[index] = annotation;
    });
    public static StrategyEditCommand RemoveAnnotation(string id) => new("Remove Wardley annotation", null, (WardleyDiagramDocument d) => d.Annotations.RemoveAt(Index(d.Annotations, a => a.Id == id)));
    public static StrategyEditCommand SetMap(string title, double width, double height, IReadOnlyList<WardleyStage> stages, DiagramPoint? annotationBox)
    {
        var copy = stages.ToArray();
        return new("Edit Wardley map options", null, (WardleyDiagramDocument d) =>
        { d.Title = title; d.Width = width; d.Height = height; d.HasExplicitSize = true; d.Stages.Clear(); d.Stages.AddRange(copy); d.AnnotationBox = annotationBox; });
    }
    private static int Index<T>(List<T> list, Predicate<T> predicate) => list.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Wardley element no longer exists.");
}
public sealed record IshikawaLocation(string? ParentId, int Index, IshikawaCause Cause);
public static class IshikawaEditing
{
    public static IshikawaLocation Locate(IshikawaDocument d, string id)
    {
        return Find(d.Causes, null) ?? throw new ArgumentException("Cause no longer exists.");
        IshikawaLocation? Find(IReadOnlyList<IshikawaCause> causes, string? parent)
        {
            for (var i = 0; i < causes.Count; i++)
            {
                if (causes[i].Id == id) return new(parent, i, causes[i]);
                if (Find(causes[i].Children, causes[i].Id) is { } result) return result;
            }
            return null;
        }
    }
    public static StrategyEditCommand SetProblem(string problem) => new("Edit problem statement", null, (IshikawaDocument d) => d.Problem = problem);
    public static StrategyEditCommand AddCause(IshikawaCause cause, string? parentId = null)
    {
        var copy = cause.DeepCopy();
        return new("Add cause", cause.Id, (IshikawaDocument d) => Change(d, parentId, list => list.Add(copy.DeepCopy())));
    }
    public static StrategyEditCommand RenameCause(string id, string label) => new("Edit cause", id, (IshikawaDocument d) =>
    {
        var location = Locate(d, id); Change(d, location.ParentId, list => list[location.Index] = location.Cause with { Label = label });
    });
    public static StrategyEditCommand MoveCause(string id, string? parentId, int position) => new("Move cause subtree", id, (IshikawaDocument d) =>
    {
        var location = Locate(d, id);
        if (parentId is not null && IshikawaDocument.Walk([location.Cause]).Any(c => c.Id == parentId)) throw new ArgumentException("A cause cannot be moved into itself.");
        Change(d, location.ParentId, list => list.RemoveAt(location.Index));
        Change(d, parentId, list => list.Insert(position, location.Cause));
    });
    public static StrategyEditCommand RemoveCause(string id) => new("Remove cause subtree", null, (IshikawaDocument d) =>
    {
        var location = Locate(d, id); Change(d, location.ParentId, list => list.RemoveAt(location.Index));
    });
    private static void Change(IshikawaDocument d, string? parentId, Action<List<IshikawaCause>> change)
    {
        if (parentId is null) { change(d.Causes); return; }
        var found = false; var roots = Transform(d.Causes);
        if (!found) throw new ArgumentException("Parent cause no longer exists.");
        d.Causes.Clear(); d.Causes.AddRange(roots);
        List<IshikawaCause> Transform(IEnumerable<IshikawaCause> causes) => causes.Select(c =>
        {
            var children = c.Children.ToList();
            if (c.Id == parentId) { change(children); found = true; } else children = Transform(children);
            return c with { Children = children };
        }).ToList();
    }
}
public static class CynefinEditing
{
    public static StrategyEditCommand AddItem(CynefinItem item) => new("Add domain item", item.Id, (CynefinDocument d) =>
    { if (!d.DeclaredDomains.Contains(item.Domain)) d.DeclaredDomains.Add(item.Domain); d.Items.Add(item); });
    public static StrategyEditCommand UpdateItem(CynefinItem item) => new("Edit domain item", item.Id, (CynefinDocument d) =>
    { if (!d.DeclaredDomains.Contains(item.Domain)) d.DeclaredDomains.Add(item.Domain); d.Items[Index(d.Items, i => i.Id == item.Id)] = item; });
    public static StrategyEditCommand MoveItem(string id, CynefinDomain domain, int position) => new("Move item between domains", id, (CynefinDocument d) =>
    {
        var index = Index(d.Items, i => i.Id == id); var item = d.Items[index]; d.Items.RemoveAt(index);
        var targets = d.Items.Where(i => i.Domain == domain).ToArray();
        if (position < 0 || position > targets.Length) throw new ArgumentOutOfRangeException(nameof(position));
        var target = position < targets.Length ? d.Items.IndexOf(targets[position]) : targets.Length == 0 ? d.Items.Count : d.Items.IndexOf(targets[^1]) + 1;
        if (!d.DeclaredDomains.Contains(domain)) d.DeclaredDomains.Add(domain);
        d.Items.Insert(target, item with { Domain = domain });
    });
    public static StrategyEditCommand RemoveItem(string id) => new("Remove domain item", null, (CynefinDocument d) => d.Items.RemoveAt(Index(d.Items, i => i.Id == id)));
    public static StrategyEditCommand SetTransition(CynefinTransition transition) => new("Edit domain transition", transition.Id, (CynefinDocument d) =>
    { var index = d.Transitions.FindIndex(t => t.Id == transition.Id); if (index < 0) d.Transitions.Add(transition); else d.Transitions[index] = transition; });
    public static StrategyEditCommand RemoveTransition(string id) => new("Remove domain transition", null, (CynefinDocument d) => d.Transitions.RemoveAt(Index(d.Transitions, t => t.Id == id)));
    public static StrategyEditCommand SetTitle(string title) => new("Edit framework title", null, (CynefinDocument d) => d.Title = title);
    public static StrategyEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone(); return new("Edit Cynefin option", null, (CynefinDocument d) =>
        { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    public static StrategyEditCommand SetThemeVariable(string key, JsonNode? value)
    {
        var copy = value?.DeepClone(); return new("Edit Cynefin theme", null, (CynefinDocument d) =>
        { var theme = d.ThemeVariables["cynefin"] as JsonObject ?? new(); if (copy is null) theme.Remove(key); else theme[key] = copy.DeepClone(); if (theme.Parent is null) d.ThemeVariables["cynefin"] = theme; });
    }
    private static int Index<T>(List<T> list, Predicate<T> predicate) => list.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Cynefin item no longer exists.");
}
