using System.Globalization;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public static class GanttScheduleResolver
{
    public static GanttSchedule Resolve(GanttDiagramDocument document, GanttEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GanttDiagramValidation.Check(document);
        var tasks = document.Tasks.ToDictionary(t => t.Id);
        var starts = new Dictionary<string, DateTimeOffset>();
        var ends = new Dictionary<string, (DateTimeOffset End, DateTimeOffset RenderEnd)>();
        var visiting = new HashSet<(string Id, bool End)>();
        var diagnostics = new List<DataDiagramDiagnostic>();
        var includeDates = Dates(document.Includes, false);
        var excludeDates = Dates(document.Excludes, true);
        var result = document.Tasks.Select(task =>
        {
            var start = Start(task.Id); var end = End(task.Id);
            return new ResolvedGanttTask(task.Id, start, end.End, end.RenderEnd, start + TimeSpan.FromTicks((end.End - start).Ticks / 2));
        }).ToArray();
        return new(result, context, diagnostics);

        GanttTask Task(string id) => tasks.GetValueOrDefault(id) ?? throw new DataDiagramNotEditableException([new("gantt-reference", id, "Referenced Gantt task does not exist; no current-date fallback is used.", true)]);
        DateTimeOffset Start(string id)
        {
            if (starts.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add((id, false))) throw Cycle(id);
            try
            {
                var task = Task(id);
                var value = task.Start switch
                {
                    GanttStartSpec.Date date => GanttDateCodec.Parse(date.Text, document.DateFormat, context, true),
                    GanttStartSpec.After after when after.TaskIds.Count > 0 => after.TaskIds.Select(other => End(other).End).Max(),
                    GanttStartSpec.Previous => Previous(id),
                    _ => throw new ArgumentException("A Gantt after-reference needs at least one task."),
                };
                return starts[id] = value;
            }
            finally { visiting.Remove((id, false)); }
        }
        DateTimeOffset Previous(string id)
        {
            var index = document.Tasks.FindIndex(t => t.Id == id);
            if (index <= 0) throw new DataDiagramNotEditableException([new("gantt-first-start", id, "The first Gantt task needs an explicit start; no current-date fallback is used.", true)]);
            return End(document.Tasks[index - 1].Id).End;
        }
        (DateTimeOffset End, DateTimeOffset RenderEnd) End(string id)
        {
            if (ends.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add((id, true))) throw Cycle(id);
            try
            {
                var task = Task(id); var start = Start(id);
                var end = task.End switch
                {
                    GanttEndSpec.Date date => GanttDateCodec.Parse(date.Text, document.DateFormat, context),
                    GanttEndSpec.Duration duration => GanttDateCodec.AddDuration(start, duration, context),
                    GanttEndSpec.Until until when until.TaskIds.Count > 0 => until.TaskIds.Select(Start).Min(),
                    _ => throw new ArgumentException("A Gantt until-reference needs at least one task."),
                };
                if (end < start) throw new ArgumentException("A Gantt task cannot end before it starts.");
                var renderEnd = end;
                var manual = task.End is GanttEndSpec.Date raw &&
                    DateTime.TryParseExact(raw.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                if (!manual && document.Excludes.Count > 0)
                {
                    var cursor = AddDay(start); var invalid = false; var iterations = 0;
                    while (cursor <= end)
                    {
                        if (++iterations > 10000) throw new ArgumentException("Gantt exclusion expansion exceeds 10,000 days.");
                        if (!invalid) renderEnd = end;
                        invalid = Excluded(cursor);
                        if (invalid) end = AddDay(end);
                        cursor = AddDay(cursor);
                    }
                }
                return ends[id] = (end, renderEnd);
            }
            finally { visiting.Remove((id, true)); }
        }
        DateTimeOffset AddDay(DateTimeOffset value) => GanttDateCodec.InZone(TimeZoneInfo.ConvertTime(value, context.TimeZone).DateTime.AddDays(1), context);
        bool Excluded(DateTimeOffset value)
        {
            var local = TimeZoneInfo.ConvertTime(value, context.TimeZone).Date;
            if (includeDates.Contains(local)) return false;
            foreach (var token in document.Excludes)
            {
                if (token.Equals("weekends", StringComparison.OrdinalIgnoreCase) &&
                    (local.DayOfWeek == document.WeekendStart || local.DayOfWeek == (DayOfWeek)(((int)document.WeekendStart + 1) % 7))) return true;
                if (Enum.TryParse<DayOfWeek>(token, true, out var weekday) && local.DayOfWeek == weekday) return true;
            }
            return excludeDates.Contains(local);
        }
        HashSet<DateTime> Dates(IEnumerable<string> tokens, bool exclusions)
        {
            var dates = new HashSet<DateTime>();
            foreach (var token in tokens)
            {
                if (exclusions && (token.Equals("weekends", StringComparison.OrdinalIgnoreCase) || Enum.TryParse<DayOfWeek>(token, true, out _))) continue;
                if (DateTime.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) dates.Add(iso.Date);
                else dates.Add(TimeZoneInfo.ConvertTime(GanttDateCodec.Parse(token, document.DateFormat, context), context.TimeZone).Date);
            }
            return dates;
        }
        DataDiagramNotEditableException Cycle(string id) => new([new("gantt-cycle", id, "Gantt start/end references form a cycle.", true)]);
    }
}
