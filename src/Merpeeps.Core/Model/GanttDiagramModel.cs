using System.Globalization;
using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

[Flags]
public enum GanttTaskFlags { None = 0, Active = 1, Done = 2, Critical = 4, Milestone = 8, VerticalMarker = 16 }
public enum GanttDurationUnit { Millisecond, Second, Minute, Hour, Day, Week, Month, Year }
public abstract record GanttStartSpec
{
    public sealed record Previous : GanttStartSpec;
    public sealed record Date(string Text) : GanttStartSpec;
    public sealed record After(IReadOnlyList<string> TaskIds) : GanttStartSpec;
}
public abstract record GanttEndSpec
{
    public sealed record Date(string Text) : GanttEndSpec;
    public sealed record Duration(double Amount, GanttDurationUnit Unit) : GanttEndSpec;
    public sealed record Until(IReadOnlyList<string> TaskIds) : GanttEndSpec;
}
public sealed record GanttSection(string Id, string Label);
public sealed record GanttInteraction(string TaskId, string Source);
public sealed record GanttTask(string Id, string Label, GanttStartSpec Start, GanttEndSpec End)
{
    public string? SourceId { get; init; }
    public string? SectionId { get; init; }
    public GanttTaskFlags Flags { get; init; }
    public GanttTask DeepCopy() => this with
    {
        Start = Start is GanttStartSpec.After after ? new GanttStartSpec.After(after.TaskIds.ToArray()) : Start,
        End = End is GanttEndSpec.Until until ? new GanttEndSpec.Until(until.TaskIds.ToArray()) : End,
    };
}
/// <summary>Capture once in the host for an edit/render/export operation; the shared resolver never reads the system clock.</summary>
public sealed record GanttEvaluationContext(DateTimeOffset ReferenceInstant, TimeZoneInfo TimeZone, CultureInfo? Culture = null)
{
    public DateTime ReferenceLocal => TimeZoneInfo.ConvertTime(ReferenceInstant, TimeZone).DateTime;
    public CultureInfo DateCulture => Culture ?? CultureInfo.InvariantCulture;
}
public sealed record ResolvedGanttTask(string Id, DateTimeOffset Start, DateTimeOffset End, DateTimeOffset RenderEnd, DateTimeOffset Marker);
public sealed record GanttSchedule(IReadOnlyList<ResolvedGanttTask> Tasks, GanttEvaluationContext Context,
    IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public sealed class GanttDiagramDocument
{
    public string Title { get; set; } = "";
    public bool TitleInFrontmatter { get; set; }
    public string DateFormat { get; set; } = "YYYY-MM-DD";
    public string? AxisFormat { get; set; }
    public string? TickInterval { get; set; }
    public DayOfWeek Weekday { get; set; } = DayOfWeek.Sunday;
    public DayOfWeek WeekendStart { get; set; } = DayOfWeek.Saturday;
    public bool Compact { get; set; }
    public bool CompactInFrontmatter { get; set; }
    public string? TodayMarker { get; set; }
    public List<string> Excludes { get; } = new();
    public List<string> Includes { get; } = new();
    public List<GanttSection> Sections { get; } = new();
    public List<GanttTask> Tasks { get; } = new();
    public List<GanttInteraction> Interactions { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public GanttDiagramDocument Clone()
    {
        var copy = new GanttDiagramDocument
        {
            Title = Title, TitleInFrontmatter = TitleInFrontmatter, DateFormat = DateFormat, AxisFormat = AxisFormat,
            TickInterval = TickInterval, Weekday = Weekday, WeekendStart = WeekendStart, Compact = Compact, CompactInFrontmatter = CompactInFrontmatter, TodayMarker = TodayMarker,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Excludes.AddRange(Excludes); copy.Includes.AddRange(Includes);
        copy.Sections.AddRange(Sections); copy.Tasks.AddRange(Tasks.Select(t => t.DeepCopy()));
        copy.Interactions.AddRange(Interactions); copy.Comments.AddRange(Comments);
        return copy;
    }
}
