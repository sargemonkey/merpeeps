using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record GanttCoordinates(DiagramRect Plot, DateTimeOffset Minimum, DateTimeOffset Maximum)
{
    public double X(DateTimeOffset value) => Plot.X + (value - Minimum).TotalMilliseconds / (Maximum - Minimum).TotalMilliseconds * Plot.Width;
    public DateTimeOffset TimeAt(double x) => Minimum.AddMilliseconds(Math.Clamp((x - Plot.X) / Plot.Width, 0, 1) * (Maximum - Minimum).TotalMilliseconds);
}
public sealed record GanttLayoutResult(DrawingScene Scene, GanttSchedule Schedule, GanttCoordinates Coordinates,
    IReadOnlyDictionary<string, DiagramRect> TaskBounds, IReadOnlyDictionary<string, int> Rows, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class GanttDiagramLayout
{
    public static GanttLayoutResult Create(DiagramModel model, GanttEvaluationContext context, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.Gantt ?? throw new ArgumentException("Gantt content is missing.");
        var schedule = GanttScheduleResolver.Resolve(document, context);
        var builder = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var byId = schedule.Tasks.ToDictionary(t => t.Id);
        var minimum = schedule.Tasks.Select(t => t.Start).DefaultIfEmpty(context.ReferenceInstant).Min();
        var maximum = schedule.Tasks.Select(t => t.End).DefaultIfEmpty(minimum.AddDays(1)).Max();
        if (maximum <= minimum) maximum = minimum.AddDays(1);
        var left = Number(document, "leftPadding", 150); var right = Number(document, "rightPadding", 75);
        var width = Number(document, "useWidth", 900); var top = Number(document, "topPadding", 75);
        var barHeight = Number(document, "barHeight", 24); var gap = Number(document, "barGap", 6);
        var fontSize = Number(document, "fontSize", 12);
        if (width <= 0 || left < 0 || right < 0 || top < 0 || barHeight <= 0 || gap < 0 || fontSize <= 0) throw new ArgumentException("Invalid Gantt drawing dimensions.");
        var rowMap = new Dictionary<string, int>();
        var sections = new Dictionary<string, (int Start, int End)>();
        var lastSection = ""; var rowCount = 0; var sectionRows = new List<DateTimeOffset>();
        foreach (var task in document.Tasks)
        {
            if (task.Flags.HasFlag(GanttTaskFlags.VerticalMarker)) continue;
            var section = task.SectionId ?? "";
            if (section != lastSection) { sectionRows.Clear(); lastSection = section; }
            int row;
            if (document.Compact)
            {
                var candidate = sectionRows.FindIndex(end => end <= byId[task.Id].Start);
                if (candidate >= 0)
                {
                    var startRow = sections.GetValueOrDefault(section, (rowCount, rowCount)).Item1;
                    row = startRow + candidate; sectionRows[candidate] = byId[task.Id].End;
                }
                else { row = rowCount++; sectionRows.Add(byId[task.Id].End); }
            }
            else row = rowCount++;
            rowMap[task.Id] = row;
            var range = sections.GetValueOrDefault(section, (row, row));
            sections[section] = (Math.Min(range.Item1, row), Math.Max(range.Item2, row));
        }
        var chartHeight = Math.Max(1, rowCount) * (barHeight + gap);
        var plot = new DiagramRect(left, top, width, chartHeight);
        var coordinates = new GanttCoordinates(plot, minimum, maximum);
        if (document.Title.Length > 0) builder.Text("title", "GanttDiagram", "", document.Title, 20, left, 15, width, true);
        foreach (var (sectionId, range) in sections)
        {
            var index = sectionId.Length == 0 ? 0 : Math.Max(0, document.Sections.FindIndex(s => s.Id == sectionId));
            var rectangle = new DiagramRect(0, top + range.Start * (barHeight + gap), left + width + right, (range.End - range.Start + 1) * (barHeight + gap));
            builder.Box("section-" + sectionId, "GanttSection", sectionId, rectangle, builder.Tint(builder.Scale(index)), null);
            if (sectionId.Length > 0)
            {
                var label = document.Sections.Single(s => s.Id == sectionId).Label;
                builder.Text("section-label-" + sectionId, "GanttSection", sectionId, label, fontSize, 5, rectangle.Y + 4, Math.Max(1, left - 16), true);
                builder.Hit("GanttSection", sectionId, rectangle);
            }
        }
        var axisFormat = document.AxisFormat ?? document.Configuration["axisFormat"]?.ToString() ?? "%Y-%m-%d";
        foreach (var tick in Ticks(document, minimum, maximum, context))
        {
            var x = coordinates.X(tick);
            builder.Line("tick-" + tick.UtcTicks, "GanttAxis", "", [new(x, top), new(x, top + chartHeight)], builder.Ink with { Alpha = 55 }, 1);
            var text = Format(tick, axisFormat, context);
            builder.Text("tick-label-" + tick.UtcTicks, "GanttAxis", "", text, 10, x - 20, top + chartHeight + 8, 110);
            if (document.Configuration["topAxis"]?.GetValue<bool>() == true) builder.Text("top-tick-" + tick.UtcTicks, "GanttAxis", "", text, 10, x - 20, top - 22, 110);
        }
        var bounds = new Dictionary<string, DiagramRect>();
        foreach (var task in document.Tasks)
        {
            var resolved = byId[task.Id];
            var color = task.Flags.HasFlag(GanttTaskFlags.Critical) ? Paint(document, "critBkgColor", new(194, 58, 70))
                : task.Flags.HasFlag(GanttTaskFlags.Done) ? Paint(document, "doneTaskBkgColor", new(139, 164, 145))
                : task.Flags.HasFlag(GanttTaskFlags.Active) ? Paint(document, "activeTaskBkgColor", new(73, 132, 197))
                : Paint(document, "taskBkgColor", new(121, 146, 184));
            var x = coordinates.X(resolved.Start); var end = coordinates.X(resolved.RenderEnd);
            if (task.Flags.HasFlag(GanttTaskFlags.VerticalMarker))
            {
                x = coordinates.X(resolved.Marker);
                builder.Line(task.Id, "GanttTask", task.Id, [new(x, top), new(x, top + chartHeight)], color, 2);
                builder.Text(task.Id + "-label", "GanttTask", task.Id, task.Label, fontSize, x + 4, top - 20, 160, true);
                bounds[task.Id] = new(x - 4, top, 8, chartHeight);
            }
            else
            {
                var y = top + rowMap[task.Id] * (barHeight + gap);
                if (task.Flags.HasFlag(GanttTaskFlags.Milestone))
                {
                    x = coordinates.X(resolved.Marker); var radius = barHeight / 2;
                    builder.Polygon(task.Id, "GanttTask", task.Id, [new(x, y), new(x + radius, y + radius), new(x, y + barHeight), new(x - radius, y + radius)], color, builder.Ink);
                    bounds[task.Id] = new(x - radius, y, barHeight, barHeight);
                }
                else
                {
                    bounds[task.Id] = new(x, y, Math.Max(0, end - x), barHeight);
                    builder.Box(task.Id, "GanttTask", task.Id, bounds[task.Id], color, Paint(document, "taskBorderColor", builder.Ink));
                }
                var label = builder.MeasureWrapped(task.Label, fontSize, double.MaxValue);
                var inside = !task.Flags.HasFlag(GanttTaskFlags.Milestone) && label.Width + 12 <= end - x;
                var labelX = inside ? x + (end - x - label.Width) / 2 : end + 6;
                if (!inside && labelX + label.Width > plot.X + plot.Width && x - label.Width - 6 >= plot.X) labelX = x - label.Width - 6;
                builder.Text(task.Id + "-label", "GanttTask", task.Id, task.Label, fontSize, labelX, y + (barHeight - label.Height) / 2,
                    double.MaxValue, color: inside ? Paint(document, "taskTextColor", new(255, 255, 255)) : builder.Ink);
            }
            builder.Hit("GanttTask", task.Id, bounds[task.Id], task.SectionId);
        }
        if (document.TodayMarker != "off" && context.ReferenceInstant >= minimum && context.ReferenceInstant <= maximum)
        {
            var style = document.TodayMarker is { Length: > 0 } marker ? ChartDiagramAdapter.ParseStyle(marker) : new System.Text.Json.Nodes.JsonObject();
            var color = style["stroke"] is { } stroke && SequenceColors.TryParse(stroke.ToString(), out var parsed) ? parsed : new ClassColor(220, 65, 55);
            var opacity = style["opacity"] is { } rawOpacity ? ChartConfiguration.ParseNumber(rawOpacity.ToString()) : 1;
            var lineWidth = style["stroke-width"] is { } line ? ChartConfiguration.ParseNumber(line.ToString()) : 2;
            if (opacity is < 0 or > 1 || lineWidth < 0) throw new ArgumentException("Invalid Gantt today-marker style.");
            var x = coordinates.X(context.ReferenceInstant);
            builder.Line("today", "GanttToday", "", [new(x, top), new(x, top + chartHeight)], color with { Alpha = (byte)Math.Round(color.Alpha * opacity) }, lineWidth);
        }
        var scene = builder.Finish(); var offset = builder.OriginOffset;
        return new(scene, schedule, coordinates with { Plot = plot with { X = plot.X + offset.X, Y = plot.Y + offset.Y } },
            bounds.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + offset.X, Y = p.Value.Y + offset.Y }), rowMap,
            GanttDiagramValidation.Validate(document));
    }
    private static IEnumerable<DateTimeOffset> Ticks(GanttDiagramDocument document, DateTimeOffset minimum, DateTimeOffset maximum, GanttEvaluationContext context)
    {
        var interval = document.TickInterval ?? document.Configuration["tickInterval"]?.ToString();
        if (interval is null)
        {
            for (var i = 0; i <= 5; i++) yield return minimum.AddTicks((maximum - minimum).Ticks / 5 * i);
            yield break;
        }
        var match = Regex.Match(interval, @"^(?<n>[1-9]\d*)(?<unit>millisecond|second|minute|hour|day|week|month)$");
        if (!match.Success) throw new NotSupportedException("Unsupported Gantt tick interval.");
        var step = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture); var unit = match.Groups["unit"].Value;
        var local = TimeZoneInfo.ConvertTime(minimum, context.TimeZone).DateTime;
        if (unit is "day" or "week") local = local.Date;
        if (unit == "month") local = new(local.Year, local.Month, 1);
        if (unit == "week") while (local.DayOfWeek != document.Weekday) local = local.AddDays(-1);
        var current = GanttDateCodec.InZone(local, context); var count = 0;
        while (current <= maximum)
        {
            if (++count > 2000) throw new ArgumentException("Gantt axis would need more than 2,000 ticks.");
            if (current >= minimum) yield return current;
            current = unit switch
            {
                "millisecond" => current.AddMilliseconds(step), "second" => current.AddSeconds(step), "minute" => current.AddMinutes(step),
                "hour" => current.AddHours(step), "day" => GanttDateCodec.AddDuration(current, new(step, GanttDurationUnit.Day), context),
                "week" => GanttDateCodec.AddDuration(current, new(step, GanttDurationUnit.Week), context),
                "month" => GanttDateCodec.AddDuration(current, new(step, GanttDurationUnit.Month), context), _ => throw new ArgumentException(),
            };
        }
    }
    public static string Format(DateTimeOffset instant, string format, GanttEvaluationContext context)
    {
        var date = TimeZoneInfo.ConvertTime(instant, context.TimeZone);
        var output = new StringBuilder();
        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '%') { output.Append(format[index]); continue; }
            if (++index >= format.Length) throw new FormatException("Incomplete Gantt axis format.");
            output.Append(format[index] switch
            {
                '%' => "%", 'Y' => date.ToString("yyyy", context.DateCulture), 'y' => date.ToString("yy", context.DateCulture),
                'm' => date.ToString("MM", context.DateCulture), 'd' => date.ToString("dd", context.DateCulture), 'e' => date.Day.ToString().PadLeft(2),
                'H' => date.ToString("HH", context.DateCulture), 'I' => date.ToString("hh", context.DateCulture), 'M' => date.ToString("mm", context.DateCulture),
                'S' => date.ToString("ss", context.DateCulture), 'L' => date.ToString("fff", context.DateCulture), 'p' => date.ToString("tt", context.DateCulture),
                'a' => date.ToString("ddd", context.DateCulture), 'A' => date.ToString("dddd", context.DateCulture),
                'b' => date.ToString("MMM", context.DateCulture), 'B' => date.ToString("MMMM", context.DateCulture),
                'j' => date.DayOfYear.ToString("000", CultureInfo.InvariantCulture), 'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'x' => date.ToString("MM/dd/yyyy", context.DateCulture), 'X' => date.ToString("HH:mm:ss", context.DateCulture),
                'Z' => date.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", ""), 's' => instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                'c' => date.ToString("ddd MMM d HH:mm:ss yyyy", context.DateCulture),
                'U' => Week(date, DayOfWeek.Sunday), 'W' => Week(date, DayOfWeek.Monday),
                _ => throw new NotSupportedException("Unsupported Gantt axis format token: %" + format[index]),
            });
        }
        return output.ToString();
        static string Week(DateTimeOffset date, DayOfWeek first)
        {
            var start = new DateTime(date.Year, 1, 1);
            var firstStart = start.AddDays(((int)first - (int)start.DayOfWeek + 7) % 7);
            return (date.Date < firstStart ? 0 : (int)(date.Date - firstStart).TotalDays / 7 + 1).ToString("00", CultureInfo.InvariantCulture);
        }
    }
    private static double Number(GanttDiagramDocument d, string key, double fallback) => d.Configuration[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
    private static ClassColor Paint(GanttDiagramDocument d, string key, ClassColor fallback) => d.ThemeVariables[key] is { } value && SequenceColors.TryParse(ChartConfiguration.Raw(value), out var color) ? color : fallback;
}
