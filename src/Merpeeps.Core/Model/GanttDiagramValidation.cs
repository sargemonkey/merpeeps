using System.Text.RegularExpressions;
using Merpeeps.Core.Parsing;
using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public static class GanttDiagramValidation
{
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(GanttDiagramDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        var ids = new HashSet<string>(); var names = new HashSet<string>();
        if (document.DateFormat.Length == 0 || document.DateFormat.IndexOfAny(['\r', '\n']) >= 0)
            issues.Add(new("gantt-date-format", "", "Gantt dateFormat must be a single nonempty format.", true));
        if (document.WeekendStart is not (DayOfWeek.Friday or DayOfWeek.Saturday) || !Enum.IsDefined(document.Weekday))
            issues.Add(new("gantt-weekday", "", "Gantt weekend starts on Friday or Saturday; weekday must be valid.", true));
        if (document.TickInterval is { } interval && !Regex.IsMatch(interval, @"^[1-9]\d*(millisecond|second|minute|hour|day|week|month)$"))
            issues.Add(new("gantt-tick", "", "This tick interval is not supported; no silent automatic-interval fallback is used.", true));
        if (document.GlobalConfiguration["themeCSS"] is { } css && css.ToString().Trim().Length > 0)
            issues.Add(new("gantt-css", "", "Arbitrary Gantt CSS is preserved as source but cannot be rendered natively.", true));
        foreach (var section in document.Sections)
        {
            Identity(section.Id);
            if (section.Label.Length == 0 || section.Label.IndexOfAny(['\r', '\n']) >= 0) issues.Add(new("gantt-section", section.Id, "Gantt sections need a single-line label.", true));
        }
        var closedSections = new HashSet<string>(); string? currentSection = null;
        foreach (var task in document.Tasks)
        {
            if (task.SectionId != currentSection)
            {
                if (currentSection is not null) closedSections.Add(currentSection);
                if (task.SectionId is null || closedSections.Contains(task.SectionId))
                    issues.Add(new("gantt-section-order", task.Id, "Gantt section tasks must remain contiguous; move the task within its section or change its section explicitly.", true));
                currentSection = task.SectionId;
            }
            Identity(task.Id);
            if (task.Label.Length == 0 || task.Label.IndexOfAny(['\r', '\n', ':']) >= 0)
                issues.Add(new("gantt-label", task.Id, "Gantt task labels must be nonempty and cannot contain a colon or line break.", true));
            if (task.SourceId is { } sourceId && (!Regex.IsMatch(sourceId, @"^[^\s,:]+$") || !names.Add(sourceId)))
                issues.Add(new("gantt-source-id", task.Id, "Gantt task source IDs must be unique identifiers.", true));
            if (task.SectionId is { } sectionId && document.Sections.All(s => s.Id != sectionId))
                issues.Add(new("gantt-section", task.Id, "Gantt task section does not exist.", true));
            if (((int)task.Flags & ~31) != 0 || task.Flags.HasFlag(GanttTaskFlags.Milestone) && task.Flags.HasFlag(GanttTaskFlags.VerticalMarker))
                issues.Add(new("gantt-flags", task.Id, "Task status/marker flags are invalid.", true));
            if (task.SourceId is not null && task.Start is GanttStartSpec.Previous)
                issues.Add(new("gantt-start", task.Id, "A named Gantt task must have an explicit start or an after reference.", true));
            switch (task.Start)
            {
                case GanttStartSpec.Date date when string.IsNullOrWhiteSpace(date.Text):
                    issues.Add(new("gantt-date", task.Id, "Gantt start date is empty.", true)); break;
                case GanttStartSpec.After after: References(task.Id, after.TaskIds); break;
                case GanttStartSpec.Previous when document.Tasks.IndexOf(task) == 0:
                    issues.Add(new("gantt-first-start", task.Id, "The first Gantt task requires an explicit start; no current-date fallback is used.", true)); break;
            }
            switch (task.End)
            {
                case GanttEndSpec.Duration duration when !double.IsFinite(duration.Amount) || duration.Amount < 0 || !Enum.IsDefined(duration.Unit):
                    issues.Add(new("gantt-duration", task.Id, "Gantt duration must be finite, nonnegative and use a known unit.", true)); break;
                case GanttEndSpec.Date date when string.IsNullOrWhiteSpace(date.Text):
                    issues.Add(new("gantt-date", task.Id, "Gantt end date is empty.", true)); break;
                case GanttEndSpec.Until until: References(task.Id, until.TaskIds); break;
            }
        }
        foreach (var interaction in document.Interactions)
        {
            References(interaction.TaskId, [interaction.TaskId]);
            if (interaction.Source.IndexOfAny(['\r', '\n']) >= 0) issues.Add(new("gantt-interaction", interaction.TaskId, "Gantt interactions must be a single source statement.", true));
            issues.Add(new("gantt-inert-interaction", interaction.TaskId, "Gantt callback/link source is retained but never invoked automatically."));
        }
        foreach (var key in new[] { "useWidth", "leftPadding", "rightPadding", "topPadding", "barHeight", "barGap", "fontSize", "sectionFontSize" })
            if (document.Configuration[key] is { } value)
            {
                try { if (ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) < 0) throw new ArgumentException("must be nonnegative"); }
                catch (Exception error) when (error is ArgumentException or FormatException) { issues.Add(new("gantt-config", "", key + ": " + error.Message, true)); }
            }
        if (document.Configuration["topAxis"] is { } top && (top is not JsonValue scalar || !scalar.TryGetValue<bool>(out _)))
            issues.Add(new("gantt-config", "", "topAxis must be true or false.", true));
        foreach (var pair in document.ThemeVariables.Where(p => p.Key.EndsWith("Color", StringComparison.Ordinal)))
            if (pair.Value is not null && !SequenceColors.TryParse(pair.Value.ToString(), out _))
                issues.Add(new("gantt-color", "", "Invalid theme color: " + pair.Key, true));
        if (document.TodayMarker is { Length: > 0 } marker && marker != "off")
        {
            try
            {
                var style = ChartDiagramAdapter.ParseStyle(marker);
                foreach (var pair in style)
                {
                    if (pair.Key == "stroke")
                    {
                        if (pair.Value is null || !SequenceColors.TryParse(pair.Value.ToString(), out _)) throw new ArgumentException("Invalid today-marker stroke.");
                    }
                    else if (pair.Key is "stroke-width" or "opacity")
                    {
                        var value = pair.Value is null ? 0 : ChartConfiguration.ParseNumber(pair.Value.ToString());
                        if (value < 0 || pair.Key == "opacity" && value > 1) throw new ArgumentException("Invalid today-marker width/opacity.");
                    }
                    else throw new NotSupportedException("Unsupported today-marker style: " + pair.Key);
                }
            }
            catch (Exception error) when (error is ArgumentException or FormatException or NotSupportedException)
            { issues.Add(new("gantt-today-style", "", error.Message, true)); }
        }
        return issues;

        void Identity(string id)
        {
            if (string.IsNullOrEmpty(id) || !ids.Add(id)) issues.Add(new("gantt-identity", id, "Gantt task/section identities must be unique.", true));
        }
        void References(string owner, IReadOnlyList<string> targets)
        {
            if (targets.Count == 0) issues.Add(new("gantt-reference", owner, "Gantt references need at least one task.", true));
            foreach (var id in targets)
            {
                var task = document.Tasks.FirstOrDefault(t => t.Id == id);
                if (task is null || string.IsNullOrEmpty(task.SourceId))
                    issues.Add(new("gantt-reference", owner, "Referenced Gantt tasks must exist and have a source ID.", true));
            }
        }
    }
    public static void Check(GanttDiagramDocument document)
    {
        var issues = Validate(document);
        if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
    }
}
