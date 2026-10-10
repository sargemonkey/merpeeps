using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class GanttDiagramAdapter : IDiagramAdapter
{
    public string Kind => "gantt";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new GanttDiagramDocument();
        var settings = DataDiagramSupport.Configuration(source, Kind);
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var frontmatter = MerpeepsDoc.Split(source).Frontmatter;
        if (frontmatter.GetValueOrDefault("title") is string title) { document.Title = title; document.TitleInFrontmatter = true; }
        document.CompactInFrontmatter = frontmatter.ContainsKey("displayMode");
        document.Compact = (frontmatter.GetValueOrDefault("displayMode") as string ?? document.Configuration["displayMode"]?.ToString()) == "compact";
        var identity = new DataIdentity(source, "gantt");
        var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = new List<(int Index, bool Start, string[] Names)>();
        var interactions = new List<(string Alias, string Source)>();
        string? section = null; var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected gantt."); header = true; continue; }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { document.Title = line[6..].Trim(); document.TitleInFrontmatter = false; continue; }
            if (line.StartsWith("dateFormat ", StringComparison.Ordinal)) { document.DateFormat = line[11..].Trim(); continue; }
            if (line.StartsWith("axisFormat ", StringComparison.Ordinal)) { document.AxisFormat = line[11..].Trim(); continue; }
            if (line.StartsWith("tickInterval ", StringComparison.Ordinal)) { document.TickInterval = line[13..].Trim(); continue; }
            if (line.StartsWith("weekday ", StringComparison.Ordinal)) { document.Weekday = Day(line[8..]); continue; }
            if (line.StartsWith("weekend ", StringComparison.Ordinal)) { document.WeekendStart = Day(line[8..]); continue; }
            if (line.StartsWith("todayMarker ", StringComparison.Ordinal)) { document.TodayMarker = line[12..].Trim(); continue; }
            if (line.StartsWith("excludes ", StringComparison.Ordinal)) { document.Excludes.AddRange(Words(line[9..])); continue; }
            if (line.StartsWith("includes ", StringComparison.Ordinal)) { document.Includes.AddRange(Words(line[9..])); continue; }
            if (line.StartsWith("section ", StringComparison.Ordinal))
            {
                var name = line[8..].Trim(); if (name.Length == 0) throw new FormatException("Gantt sections need a label.");
                section = identity.Next("section:" + name); document.Sections.Add(new(section, name)); continue;
            }
            if (line.StartsWith("click ", StringComparison.Ordinal))
            {
                var space = line.IndexOf(' ', 6);
                if (space < 0) throw new FormatException("Gantt interaction requires an action.");
                interactions.Add((line[6..space], line[(space + 1)..])); continue;
            }
            var comment = line.IndexOf("%%", StringComparison.Ordinal);
            if (comment >= 0) { document.Comments.Add(line[comment..]); line = line[..comment].Trim(); }
            var separator = line.IndexOf(':');
            if (separator < 0) throw new FormatException("Gantt tasks use label : metadata.");
            var label = line[..separator].Trim();
            var metadata = line[(separator + 1)..].Split(',', StringSplitOptions.TrimEntries).ToList();
            var flags = GanttTaskFlags.None;
            while (metadata.Count > 0 && Flag(metadata[0]) is { } flag) { flags |= flag; metadata.RemoveAt(0); }
            if (metadata.Count is < 1 or > 3) throw new FormatException("Gantt task metadata needs one, two or three values after flags.");
            string? sourceId = metadata.Count == 3 ? metadata[0] : null;
            if (sourceId is not null) metadata.RemoveAt(0);
            var id = identity.Next(sourceId is null ? "task:" + section + "\0" + label : "task-id:" + sourceId);
            if (sourceId is not null && !sourceIds.TryAdd(sourceId, id)) throw new FormatException("Duplicate Gantt task source identity: " + sourceId);
            GanttStartSpec start = new GanttStartSpec.Previous();
            if (metadata.Count == 2)
            {
                if (metadata[0].StartsWith("after ", StringComparison.Ordinal))
                {
                    var names = Words(metadata[0][6..]);
                    references.Add((document.Tasks.Count, true, names)); start = new GanttStartSpec.After(names);
                }
                else start = new GanttStartSpec.Date(metadata[0]);
            }
            GanttEndSpec end;
            var endToken = metadata[^1];
            if (endToken.StartsWith("until ", StringComparison.Ordinal))
            {
                var names = Words(endToken[6..]); references.Add((document.Tasks.Count, false, names)); end = new GanttEndSpec.Until(names);
            }
            else
            {
                var duration = Regex.Match(endToken, @"^(?<value>(?:\d+(?:\.\d*)?|\.\d+))(?<unit>ms|s|m|h|d|w|M|y)$");
                end = duration.Success ? new GanttEndSpec.Duration(double.Parse(duration.Groups["value"].Value, CultureInfo.InvariantCulture), Unit(duration.Groups["unit"].Value))
                    : new GanttEndSpec.Date(endToken);
            }
            document.Tasks.Add(new(id, label, start, end) { SourceId = sourceId, SectionId = section, Flags = flags });
        }
        if (!header) throw new FormatException("Gantt directive is missing.");
        foreach (var reference in references)
        {
            var ids = reference.Names.Select(name => sourceIds.GetValueOrDefault(name) ?? throw new DataDiagramNotEditableException([new("gantt-reference", name, "Gantt references must name a declared task; no current-date fallback is used.", true)])).ToArray();
            var task = document.Tasks[reference.Index];
            document.Tasks[reference.Index] = reference.Start ? task with { Start = new GanttStartSpec.After(ids) } : task with { End = new GanttEndSpec.Until(ids) };
        }
        foreach (var interaction in interactions)
            document.Interactions.Add(new(sourceIds.GetValueOrDefault(interaction.Alias) ?? throw new FormatException("Unknown Gantt interaction target."), interaction.Source));
        GanttDiagramValidation.Check(document);
        return new() { Kind = Kind, Gantt = document };
    }
    public string Write(DiagramModel model) => Write(model.Gantt ?? throw new ArgumentException("Gantt content is missing."));
    public static string Write(GanttDiagramDocument document)
    {
        GanttDiagramValidation.Check(document);
        var output = new StringBuilder("gantt\n");
        output.Append("dateFormat ").Append(document.DateFormat).Append('\n');
        if (!document.TitleInFrontmatter && document.Title.Length > 0) output.Append("title ").Append(document.Title).Append('\n');
        if (document.AxisFormat is { } axis) output.Append("axisFormat ").Append(axis).Append('\n');
        if (document.TickInterval is { } tick) output.Append("tickInterval ").Append(tick).Append('\n');
        if (document.Weekday != DayOfWeek.Sunday) output.Append("weekday ").Append(document.Weekday.ToString().ToLowerInvariant()).Append('\n');
        if (document.WeekendStart != DayOfWeek.Saturday) output.Append("weekend ").Append(document.WeekendStart.ToString().ToLowerInvariant()).Append('\n');
        if (document.TodayMarker is { } marker) output.Append("todayMarker ").Append(marker).Append('\n');
        if (document.Excludes.Count > 0) output.Append("excludes ").Append(string.Join(" ", document.Excludes)).Append('\n');
        if (document.Includes.Count > 0) output.Append("includes ").Append(string.Join(" ", document.Includes)).Append('\n');
        string? currentSection = null;
        foreach (var task in document.Tasks)
        {
            if (task.SectionId != currentSection)
            {
                if (task.SectionId is null && currentSection is not null) throw new NotSupportedException("A sectionless Gantt task cannot follow sectioned tasks.");
                if (task.SectionId is { } id) output.Append("section ").Append(document.Sections.Single(s => s.Id == id).Label).Append('\n');
                currentSection = task.SectionId;
            }
            var values = new List<string>();
            foreach (var flag in new[] { GanttTaskFlags.Active, GanttTaskFlags.Done, GanttTaskFlags.Critical, GanttTaskFlags.Milestone, GanttTaskFlags.VerticalMarker })
                if (task.Flags.HasFlag(flag)) values.Add(flag switch { GanttTaskFlags.Critical => "crit", GanttTaskFlags.VerticalMarker => "vert", _ => flag.ToString().ToLowerInvariant() });
            if (task.SourceId is { } sourceId)
            {
                if (task.Start is GanttStartSpec.Previous) throw new ArgumentException("A source-identified Gantt task needs an explicit date or after reference.");
                values.Add(sourceId);
            }
            if (task.Start is GanttStartSpec.Date date) values.Add(date.Text);
            else if (task.Start is GanttStartSpec.After after) values.Add("after " + string.Join(" ", after.TaskIds.Select(SourceId)));
            values.Add(task.End switch
            {
                GanttEndSpec.Date endDate => endDate.Text,
                GanttEndSpec.Duration duration => ChartDiagramAdapter.Number(duration.Amount) + Unit(duration.Unit),
                GanttEndSpec.Until until => "until " + string.Join(" ", until.TaskIds.Select(SourceId)),
                _ => throw new ArgumentException("Unknown Gantt end specification."),
            });
            output.Append(task.Label).Append(" : ").Append(string.Join(", ", values)).Append('\n');
        }
        foreach (var section in document.Sections.Where(s => document.Tasks.All(t => t.SectionId != s.Id))) output.Append("section ").Append(section.Label).Append('\n');
        foreach (var interaction in document.Interactions) output.Append("click ").Append(SourceId(interaction.TaskId)).Append(' ').Append(interaction.Source).Append('\n');
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        string SourceId(string id) => document.Tasks.Single(t => t.Id == id).SourceId ?? throw new ArgumentException("Referenced Gantt tasks need an explicit source ID.");
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Gantt!; var before = Parse(source).Gantt!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) ||
            !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration) || before.Compact != document.Compact || document.TitleInFrontmatter && before.Title != document.Title)
        {
            var config = (JsonObject)document.GlobalConfiguration.DeepClone();
            var local = (JsonObject)document.Configuration.DeepClone();
            local["displayMode"] = document.Compact ? "compact" : "";
            config["gantt"] = local; config["themeVariables"] = document.ThemeVariables.DeepClone();
            using var json = System.Text.Json.JsonDocument.Parse(config.ToJsonString());
            result = MerpeepsDoc.WithChartConfiguration(result, MerpeepsDoc.Normalise(json.RootElement)!, document.TitleInFrontmatter, document.Title,
                document.CompactInFrontmatter, document.Compact ? "compact" : null);
        }
        return result;
    }
    internal static object Identity(string source, GanttDiagramDocument document)
    {
        var map = new DataIdentity(source, "gantt");
        var entries = document.Sections.Select(s => map.Entry(s.Id, "section:" + s.Label)).ToList();
        entries.AddRange(document.Tasks.Select(t => map.Entry(t.Id, t.SourceId is null ? "task:" + t.SectionId + "\0" + t.Label : "task-id:" + t.SourceId)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string[] Words(string input) => input.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static DayOfWeek Day(string input) => Enum.TryParse<DayOfWeek>(input.Trim(), true, out var day) && Enum.IsDefined(day) ? day : throw new FormatException("Unknown Gantt weekday.");
    private static GanttTaskFlags? Flag(string input) => input switch
    { "active" => GanttTaskFlags.Active, "done" => GanttTaskFlags.Done, "crit" => GanttTaskFlags.Critical, "milestone" => GanttTaskFlags.Milestone, "vert" => GanttTaskFlags.VerticalMarker, _ => null };
    private static GanttDurationUnit Unit(string input) => input switch
    { "ms" => GanttDurationUnit.Millisecond, "s" => GanttDurationUnit.Second, "m" => GanttDurationUnit.Minute, "h" => GanttDurationUnit.Hour, "d" => GanttDurationUnit.Day, "w" => GanttDurationUnit.Week, "M" => GanttDurationUnit.Month, "y" => GanttDurationUnit.Year, _ => throw new FormatException("Unknown Gantt duration unit.") };
    private static string Unit(GanttDurationUnit unit) => unit switch
    { GanttDurationUnit.Millisecond => "ms", GanttDurationUnit.Second => "s", GanttDurationUnit.Minute => "m", GanttDurationUnit.Hour => "h", GanttDurationUnit.Day => "d", GanttDurationUnit.Week => "w", GanttDurationUnit.Month => "M", GanttDurationUnit.Year => "y", _ => throw new ArgumentException("Unknown Gantt duration unit.") };
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Gantt tasks retain temporal start/end specifications.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Gantt dependencies are temporal references.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
