using System.Text.Json.Nodes;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class GanttEditCommand : IEditCommand
{
    private readonly Action<GanttDiagramDocument> _edit;
    private readonly GanttEvaluationContext _context;
    private GanttDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal GanttEditCommand(string description, string? result, GanttEvaluationContext context, Action<GanttDiagramDocument> edit)
    {
        Description = description; ResultId = result; _edit = edit;
        _context = context with { Culture = System.Globalization.CultureInfo.ReadOnly((System.Globalization.CultureInfo)context.DateCulture.Clone()) };
    }
    public void Do(DiagramModel model)
    {
        var before = model.Gantt ?? throw new ArgumentException("Choose a Gantt diagram.");
        var candidate = before.Clone(); _edit(candidate); GanttDiagramValidation.Check(candidate);
        _ = GanttDiagramLayout.Create(new DiagramModel { Kind = "gantt", Gantt = candidate }, _context); _ = GanttDiagramAdapter.Write(candidate);
        _before = before; model.Gantt = candidate;
    }
    public void Undo(DiagramModel model) => model.Gantt = _before ?? throw new InvalidOperationException("Gantt edit has not been applied.");
}
public static class GanttDiagramEditing
{
    public static GanttEditCommand AddTask(GanttTask task, GanttEvaluationContext context, int? position = null)
    {
        var copy = task.DeepCopy();
        return new("Add Gantt task", task.Id, context, d => d.Tasks.Insert(position ?? SectionEnd(d, copy.SectionId), copy.DeepCopy()));
    }
    public static GanttEditCommand UpdateTask(GanttTask task, GanttEvaluationContext context)
    {
        var copy = task.DeepCopy(); return new("Edit Gantt task", task.Id, context, d =>
        {
            var index = Index(d.Tasks, t => t.Id == task.Id);
            if (d.Tasks[index].SectionId == copy.SectionId) d.Tasks[index] = copy.DeepCopy();
            else { d.Tasks.RemoveAt(index); d.Tasks.Insert(SectionEnd(d, copy.SectionId), copy.DeepCopy()); }
        });
    }
    public static GanttEditCommand MoveTask(string id, int position, GanttEvaluationContext context) => new("Reorder Gantt task", id, context, d =>
    {
        var index = Index(d.Tasks, t => t.Id == id); var task = d.Tasks[index]; d.Tasks.RemoveAt(index); d.Tasks.Insert(position, task);
    });
    public static GanttEditCommand RemoveTask(string id, GanttEvaluationContext context) => new("Remove Gantt task", null, context, d =>
    {
        d.Tasks.RemoveAt(Index(d.Tasks, t => t.Id == id)); d.Interactions.RemoveAll(i => i.TaskId == id);
    });
    public static GanttEditCommand AddSection(GanttSection section, GanttEvaluationContext context) => new("Add Gantt section", section.Id, context, d => d.Sections.Add(section));
    public static GanttEditCommand RenameSection(string id, string label, GanttEvaluationContext context) => new("Rename Gantt section", id, context, d =>
        d.Sections[Index(d.Sections, s => s.Id == id)] = new(id, label));
    /// <summary>Remove the section and move its tasks to the end of the destination section (or the unsectioned prefix).</summary>
    public static GanttEditCommand RemoveSection(string id, string? destinationSectionId, GanttEvaluationContext context) =>
        new("Remove Gantt section and reparent tasks", destinationSectionId, context, d =>
        {
            if (destinationSectionId == id) throw new ArgumentException("Choose another destination section.");
            if (destinationSectionId is not null) _ = Index(d.Sections, s => s.Id == destinationSectionId);
            var index = Index(d.Sections, s => s.Id == id);
            var moved = d.Tasks.Where(t => t.SectionId == id).Select(t => t with { SectionId = destinationSectionId }).ToArray();
            d.Tasks.RemoveAll(t => t.SectionId == id); d.Sections.RemoveAt(index);
            var destination = SectionEnd(d, destinationSectionId);
            d.Tasks.InsertRange(destination, moved);
        });
    public static GanttEditCommand SetTitle(string title, GanttEvaluationContext context) => new("Edit Gantt title", null, context, d => d.Title = title);
    public static GanttEditCommand SetDateFormat(string format, GanttEvaluationContext context) => new("Edit Gantt date format", null, context, d => d.DateFormat = format);
    public static GanttEditCommand SetAxis(string? format, string? interval, DayOfWeek weekday, GanttEvaluationContext context) =>
        new("Edit Gantt axis", null, context, d => { d.AxisFormat = format; d.TickInterval = interval; d.Weekday = weekday; });
    public static GanttEditCommand SetCalendar(IReadOnlyList<string> excludes, IReadOnlyList<string> includes, DayOfWeek weekend, GanttEvaluationContext context)
    {
        var excluded = excludes.ToArray(); var included = includes.ToArray();
        return new("Edit Gantt calendar", null, context, d =>
        {
            d.Excludes.Clear(); d.Excludes.AddRange(excluded);
            d.Includes.Clear(); d.Includes.AddRange(included); d.WeekendStart = weekend;
        });
    }
    public static GanttEditCommand SetCompact(bool compact, GanttEvaluationContext context) => new("Edit Gantt row mode", null, context, d => d.Compact = compact);
    public static GanttEditCommand SetTodayMarker(string? marker, GanttEvaluationContext context) => new("Edit Gantt today marker", null, context, d => d.TodayMarker = marker);
    public static GanttEditCommand SetOption(string key, JsonNode? value, GanttEvaluationContext context)
    {
        var copy = value?.DeepClone();
        return new("Edit Gantt option", null, context, d => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    private static int Index<T>(IReadOnlyList<T> list, Func<T, bool> predicate)
    {
        for (var i = 0; i < list.Count; i++) if (predicate(list[i])) return i;
        throw new ArgumentException("Gantt task/section no longer exists.");
    }
    private static int SectionEnd(GanttDiagramDocument document, string? sectionId)
    {
        if (sectionId is null) return document.Tasks.TakeWhile(t => t.SectionId is null).Count();
        var sectionIndex = Index(document.Sections, s => s.Id == sectionId);
        var last = document.Tasks.FindLastIndex(t => t.SectionId == sectionId);
        if (last >= 0) return last + 1;
        var next = document.Tasks.FindIndex(t => t.SectionId is { } id && document.Sections.FindIndex(s => s.Id == id) > sectionIndex);
        return next < 0 ? document.Tasks.Count : next;
    }
}
