using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class DataDiagramEditCommand : IEditCommand
{
    private readonly Action<OutlineDocument>? _outline;
    private readonly Action<TemporalDocument>? _temporal;
    private OutlineDocument? _beforeOutline;
    private TemporalDocument? _beforeTemporal;
    public string Description { get; }
    public DataDiagramTarget? Result { get; }
    internal DataDiagramEditCommand(string description, DataDiagramTarget? result, Action<OutlineDocument> edit) =>
        (Description, Result, _outline) = (description, result, edit);
    internal DataDiagramEditCommand(string description, DataDiagramTarget? result, Action<TemporalDocument> edit) =>
        (Description, Result, _temporal) = (description, result, edit);
    public void Do(DiagramModel model)
    {
        if (_outline is not null)
        {
            var before = model.Outline ?? throw new ArgumentException("An outline diagram is required.");
            var next = before.Clone(); _outline(next);
            var issues = OutlineDiagramAdapter.Validate(next);
            if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
            _ = OutlineDiagramAdapter.Write(next);
            _beforeOutline = before; model.Outline = next;
        }
        else
        {
            var before = model.Temporal ?? throw new ArgumentException("A temporal diagram is required.");
            var next = before.Clone(); _temporal!(next);
            var issues = TemporalDiagramAdapter.Validate(next);
            if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
            _ = TemporalDiagramAdapter.Write(next);
            _beforeTemporal = before; model.Temporal = next;
        }
    }
    public void Undo(DiagramModel model)
    {
        if (_outline is not null) model.Outline = _beforeOutline ?? throw new InvalidOperationException("Edit was not applied.");
        else model.Temporal = _beforeTemporal ?? throw new InvalidOperationException("Edit was not applied.");
    }
}

public sealed record OutlineLocation(string? ParentId, int Index, OutlineNode Node);
public static class OutlineEditing
{
    public static string NewId() => "h" + Guid.NewGuid().ToString("N");
    public static OutlineLocation Locate(OutlineDocument document, string id)
    {
        return Find(document.Roots, null) ?? throw new ArgumentException("Outline node no longer exists.");
        OutlineLocation? Find(IReadOnlyList<OutlineNode> nodes, string? parent)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                if (nodes[index].Id == id) return new(parent, index, nodes[index]);
                if (Find(nodes[index].Children, nodes[index].Id) is { } found) return found;
            }
            return null;
        }
    }
    public static DataDiagramEditCommand Add(OutlineNode node, string? parentId = null, int? index = null)
    {
        var copy = node.DeepCopy();
        return new("Add outline node", new(DataDiagramRole.Node, node.Id), (OutlineDocument document) =>
            Change(document, parentId, nodes => nodes.Insert(index ?? nodes.Count, copy.DeepCopy())));
    }
    public static DataDiagramEditCommand Update(string id, OutlineNode node)
    {
        if (id != node.Id) throw new ArgumentException("Changing an outline label must retain its stable identity.");
        var copy = node.DeepCopy();
        return new("Edit outline node", new(DataDiagramRole.Node, id), (OutlineDocument document) =>
        {
            var location = Locate(document, id);
            Change(document, location.ParentId, nodes => nodes[location.Index] = copy with { Children = location.Node.Children });
        });
    }
    public static DataDiagramEditCommand Remove(string id) => new("Delete outline subtree", null, (OutlineDocument document) =>
    {
        var location = Locate(document, id);
        foreach (var node in OutlineDocument.Walk([location.Node])) document.Positions.Remove(node.Id);
        Change(document, location.ParentId, nodes => nodes.RemoveAt(location.Index));
    });
    public static DataDiagramEditCommand Move(string id, string? parentId, int index) =>
        new("Move outline subtree", new(DataDiagramRole.Node, id), (OutlineDocument document) =>
        {
            var location = Locate(document, id);
            if (parentId is not null && OutlineDocument.Walk([location.Node]).Any(n => n.Id == parentId))
                throw new ArgumentException("A node cannot be moved into its own subtree.");
            Change(document, location.ParentId, nodes => nodes.RemoveAt(location.Index));
            Change(document, parentId, nodes =>
            {
                if (index < 0 || index > nodes.Count) throw new ArgumentOutOfRangeException(nameof(index));
                nodes.Insert(index, location.Node);
            });
        });
    public static DataDiagramEditCommand SetPosition(string id, DiagramPoint? position) =>
        new("Move mindmap node", new(DataDiagramRole.Node, id), (OutlineDocument document) =>
        {
            if (document.Family != OutlineFamily.Mindmap) throw new ArgumentException("Tree View rows follow hierarchy order.");
            _ = document.Node(id);
            if (position is { } point) document.Positions[id] = point; else document.Positions.Remove(id);
        });
    public static DataDiagramEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit outline option", new(DataDiagramRole.Diagram, ""), (OutlineDocument document) =>
        {
            if (copy is null) document.Configuration.Remove(key); else document.Configuration[key] = copy.DeepClone();
        });
    }
    public static DataDiagramEditCommand SetMindmapLayout(bool tidy) => new("Change mindmap layout", new(DataDiagramRole.Diagram, ""), (OutlineDocument document) =>
    {
        if (document.Family != OutlineFamily.Mindmap) throw new ArgumentException("Choose a mindmap.");
        if (tidy) document.GlobalConfiguration["layout"] = "tidy-tree"; else document.GlobalConfiguration.Remove("layout");
    });
    public static DataDiagramEditCommand SetThemeVariable(string path, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit outline theme variable", new(DataDiagramRole.Diagram, ""),
            (OutlineDocument document) => SetJsonPath(document.ThemeVariables, path, copy));
    }
    internal static void SetJsonPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Use a nonempty property path.");
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (root[parts[i]] is not JsonObject child) { child = new(); root[parts[i]] = child; }
            root = child;
        }
        if (value is null) root.Remove(parts[^1]); else root[parts[^1]] = value.DeepClone();
    }
    private static void Change(OutlineDocument document, string? parent, Action<List<OutlineNode>> edit)
    {
        if (parent is null) { edit(document.Roots); return; }
        var found = false;
        var roots = Walk(document.Roots);
        if (!found) throw new ArgumentException("Parent node no longer exists.");
        document.Roots.Clear(); document.Roots.AddRange(roots);
        List<OutlineNode> Walk(IEnumerable<OutlineNode> nodes) => nodes.Select(node =>
        {
            var children = node.Children.ToList();
            if (node.Id == parent) { edit(children); found = true; }
            else children = Walk(children);
            return node with { Children = children };
        }).ToList();
    }
}

public static class TemporalEditing
{
    public static string NewId() => "t" + Guid.NewGuid().ToString("N");
    public static DataDiagramEditCommand SetTitle(string title) => new("Rename temporal diagram", new(DataDiagramRole.Diagram, ""), (TemporalDocument document) => document.Title = title);
    public static DataDiagramEditCommand AddSection(TemporalSection section) => new("Add section", new(DataDiagramRole.Section, section.Id), (TemporalDocument document) => document.Sections.Add(section));
    public static DataDiagramEditCommand UpdateSection(TemporalSection section) => new("Rename section", new(DataDiagramRole.Section, section.Id), (TemporalDocument document) =>
    {
        document.Sections[Index(document.Sections, s => s.Id == section.Id)] = section;
    });
    public static DataDiagramEditCommand RemoveSection(string id) => new("Remove section, keep its items", null, (TemporalDocument document) =>
    {
        var index = Index(document.Sections, s => s.Id == id);
        if (document.Family == TemporalFamily.Timeline)
        {
            var first = document.Periods.FindIndex(p => p.SectionId == id);
            var destination = first > 0 ? document.Periods[first - 1].SectionId : null;
            for (var i = 0; i < document.Periods.Count; i++)
                if (document.Periods[i].SectionId == id) document.Periods[i] = document.Periods[i] with { SectionId = destination };
        }
        else
        {
            var first = document.Tasks.FindIndex(t => t.SectionId == id);
            var destination = first > 0 ? document.Tasks[first - 1].SectionId : null;
            for (var i = 0; i < document.Tasks.Count; i++)
                if (document.Tasks[i].SectionId == id) document.Tasks[i] = document.Tasks[i] with { SectionId = destination };
        }
        document.Sections.RemoveAt(index);
    });
    public static DataDiagramEditCommand MoveSection(string id, int index) => new("Reorder sections and their items", new(DataDiagramRole.Section, id), (TemporalDocument document) =>
    {
        Move(document.Sections, s => s.Id == id, index);
        if (document.Family == TemporalFamily.Timeline)
        {
            var items = document.Periods.ToArray();
            document.Periods.Clear();
            document.Periods.AddRange(items.Where(p => p.SectionId is null));
            foreach (var section in document.Sections) document.Periods.AddRange(items.Where(p => p.SectionId == section.Id));
        }
        else
        {
            var items = document.Tasks.ToArray();
            document.Tasks.Clear();
            document.Tasks.AddRange(items.Where(t => t.SectionId is null));
            foreach (var section in document.Sections) document.Tasks.AddRange(items.Where(t => t.SectionId == section.Id));
        }
    });
    public static DataDiagramEditCommand AddPeriod(TimelinePeriod period)
    {
        var copy = period with { Events = period.Events.ToArray() };
        return new("Add time period", new(DataDiagramRole.Period, period.Id), (TemporalDocument document) =>
        {
            if (document.Family != TemporalFamily.Timeline) throw new ArgumentException("Choose a timeline.");
            document.Periods.Insert(InsertIndex(document.Periods.Select(p => p.SectionId).ToArray(), copy.SectionId), copy);
        });
    }
    public static DataDiagramEditCommand UpdatePeriod(TimelinePeriod period) => new("Edit time period", new(DataDiagramRole.Period, period.Id), (TemporalDocument document) =>
    {
        var index = Index(document.Periods, p => p.Id == period.Id);
        var prior = document.Periods[index];
        document.Periods.RemoveAt(index);
        document.Periods.Insert(prior.SectionId == period.SectionId ? index : InsertIndex(document.Periods.Select(p => p.SectionId).ToArray(), period.SectionId),
            period with { Events = prior.Events });
    });
    public static DataDiagramEditCommand AddEvent(string periodId, TimelineEvent item) => Events("Add timeline event", periodId, item.Id, events => events.Add(item));
    public static DataDiagramEditCommand UpdateEvent(string periodId, TimelineEvent item) => Events("Edit timeline event", periodId, item.Id, events => events[Index(events, e => e.Id == item.Id)] = item);
    public static DataDiagramEditCommand RemoveEvent(string periodId, string id) => Events("Delete timeline event", periodId, null, events => events.RemoveAt(Index(events, e => e.Id == id)));
    public static DataDiagramEditCommand MoveEvent(string periodId, string id, int index) => Events("Reorder timeline events", periodId, id, events => Move(events, e => e.Id == id, index));
    public static DataDiagramEditCommand TransferEvent(string fromPeriodId, string toPeriodId, string eventId, int index) =>
        new("Move event to time period", new(DataDiagramRole.Event, eventId, toPeriodId), (TemporalDocument document) =>
        {
            var from = Index(document.Periods, p => p.Id == fromPeriodId);
            var to = Index(document.Periods, p => p.Id == toPeriodId);
            var events = document.Periods[from].Events.ToList();
            var old = Index(events, e => e.Id == eventId);
            var item = events[old]; events.RemoveAt(old);
            document.Periods[from] = document.Periods[from] with { Events = events };
            var target = from == to ? events : document.Periods[to].Events.ToList();
            if (index < 0 || index > target.Count) throw new ArgumentOutOfRangeException(nameof(index));
            target.Insert(index, item);
            document.Periods[to] = document.Periods[to] with { Events = target };
        });
    public static DataDiagramEditCommand MovePeriod(string id, int index) => new("Reorder time periods", new(DataDiagramRole.Period, id), (TemporalDocument document) => Move(document.Periods, p => p.Id == id, index));
    public static DataDiagramEditCommand RemovePeriod(string id) => new("Delete time period and events", null, (TemporalDocument document) => document.Periods.RemoveAt(Index(document.Periods, p => p.Id == id)));
    public static DataDiagramEditCommand AddTask(JourneyTask task)
    {
        var copy = task with { Actors = task.Actors.ToArray() };
        return new("Add journey task", new(DataDiagramRole.Task, task.Id), (TemporalDocument document) =>
        {
            if (document.Family != TemporalFamily.Journey) throw new ArgumentException("Choose a journey.");
            document.Tasks.Insert(InsertIndex(document.Tasks.Select(t => t.SectionId).ToArray(), task.SectionId), copy);
        });
    }
    public static DataDiagramEditCommand UpdateTask(JourneyTask task)
    {
        var copy = task with { Actors = task.Actors.ToArray() };
        return new("Edit journey task", new(DataDiagramRole.Task, task.Id), (TemporalDocument document) =>
        {
            var index = Index(document.Tasks, t => t.Id == task.Id);
            var old = document.Tasks[index]; document.Tasks.RemoveAt(index);
            document.Tasks.Insert(old.SectionId == copy.SectionId ? index : InsertIndex(document.Tasks.Select(t => t.SectionId).ToArray(), copy.SectionId), copy);
        });
    }
    public static DataDiagramEditCommand MoveTask(string id, int index) => new("Reorder journey tasks", new(DataDiagramRole.Task, id), (TemporalDocument document) => Move(document.Tasks, t => t.Id == id, index));
    public static DataDiagramEditCommand RemoveTask(string id) => new("Delete journey task", null, (TemporalDocument document) => document.Tasks.RemoveAt(Index(document.Tasks, t => t.Id == id)));
    public static DataDiagramEditCommand SetDirection(bool topDown) => new("Change timeline direction", new(DataDiagramRole.Diagram, ""), (TemporalDocument document) =>
    {
        if (document.Family != TemporalFamily.Timeline) throw new ArgumentException("Choose a timeline.");
        document.TopDown = topDown;
    });
    public static DataDiagramEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Change temporal option", new(DataDiagramRole.Diagram, ""), (TemporalDocument document) =>
        {
            if (copy is null) document.Configuration.Remove(key); else document.Configuration[key] = copy.DeepClone();
        });
    }
    public static DataDiagramEditCommand SetThemeVariable(string path, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit temporal theme variable", new(DataDiagramRole.Diagram, ""),
            (TemporalDocument document) => OutlineEditing.SetJsonPath(document.ThemeVariables, path, copy));
    }
    private static DataDiagramEditCommand Events(string description, string periodId, string? result, Action<List<TimelineEvent>> edit) =>
        new(description, result is null ? new(DataDiagramRole.Period, periodId) : new(DataDiagramRole.Event, result, periodId), (TemporalDocument document) =>
        {
            var index = Index(document.Periods, p => p.Id == periodId);
            var events = document.Periods[index].Events.ToList(); edit(events);
            document.Periods[index] = document.Periods[index] with { Events = events };
        });
    private static int InsertIndex(string?[] sections, string? target)
    {
        var last = Array.FindLastIndex(sections, s => s == target);
        return last >= 0 ? last + 1 : target is null ? 0 : sections.Length;
    }
    private static int Index<T>(IReadOnlyList<T> list, Func<T, bool> match)
    {
        for (var i = 0; i < list.Count; i++) if (match(list[i])) return i;
        throw new ArgumentException("Temporal item no longer exists.");
    }
    private static void Move<T>(List<T> list, Func<T, bool> match, int index)
    {
        if (index < 0 || index >= list.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var previous = Index(list, match); var item = list[previous]; list.RemoveAt(previous); list.Insert(index, item);
    }
}
