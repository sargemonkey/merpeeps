using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class TemporalDiagramAdapter(TemporalFamily family) : IDiagramAdapter
{
    public TemporalFamily Family => family;
    public string Kind => family == TemporalFamily.Timeline ? "timeline" : "journey";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new TemporalDocument { Family = family };
        var settings = DataDiagramSupport.Configuration(source, Kind);
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        if (MerpeepsDoc.Split(source).Frontmatter.GetValueOrDefault("title") is string title) { document.Title = title; document.TitleInFrontmatter = true; }
        var identities = new DataIdentity(source, Kind);
        string? section = null;
        var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header)
            {
                if (!CanParse(line)) throw new FormatException("Unexpected temporal diagram directive.");
                var tail = line[Kind.Length..].Trim();
                if (family == TemporalFamily.Timeline && tail is "TD" or "LR") document.TopDown = tail == "TD";
                else if (tail.Length > 0) throw new FormatException("Unsupported temporal direction.");
                header = true;
                continue;
            }
            if (line.StartsWith("title ", StringComparison.Ordinal))
            {
                document.Title = SequenceText.Decode(line[6..]); document.TitleInFrontmatter = false; continue;
            }
            if (line.StartsWith("section ", StringComparison.Ordinal))
            {
                var label = SequenceText.Decode(line[8..]);
                section = identities.Next("section:" + label);
                document.Sections.Add(new(section, label)); continue;
            }
            if (family == TemporalFamily.Timeline)
            {
                var values = line.Split(':').Select(v => SequenceText.Decode(v.Trim())).ToArray();
                if (values[0].Length == 0)
                {
                    if (document.Periods.Count == 0) throw new FormatException("A continuation event requires a preceding time period.");
                    var prior = document.Periods[^1];
                    var events = prior.Events.ToList();
                    foreach (var text in values.Skip(1)) events.Add(new(identities.Next("event:" + prior.Id + "\0" + text), text));
                    document.Periods[^1] = prior with { Events = events };
                }
                else
                {
                    var id = identities.Next("period:" + values[0]);
                    var events = values.Skip(1).Select(text => new TimelineEvent(identities.Next("event:" + id + "\0" + text), text)).ToArray();
                    document.Periods.Add(new(id, values[0], section, events));
                }
            }
            else
            {
                var values = line.Split(':', 3);
                if (values.Length < 2 || !int.TryParse(values[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
                    throw new FormatException("Journey tasks require a score and optional comma-separated actors.");
                var label = SequenceText.Decode(values[0].Trim());
                var actors = values.Length > 2 ? values[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
                document.Tasks.Add(new(identities.Next("task:" + label), label, section, score, actors));
            }
        }
        if (!header) throw new FormatException("Temporal diagram directive is missing.");
        var errors = Validate(document);
        if (errors.Any(e => e.Blocking)) throw new DataDiagramNotEditableException(errors);
        return new() { Kind = Kind, Temporal = document };
    }
    public string Write(DiagramModel model) => Write(model.Temporal ?? throw new ArgumentException("Temporal content is missing."));
    public static string Write(TemporalDocument document)
    {
        var errors = Validate(document);
        if (errors.Any(e => e.Blocking)) throw new DataDiagramNotEditableException(errors);
        var builder = new StringBuilder(document.Family == TemporalFamily.Timeline ? "timeline" + (document.TopDown ? " TD" : "") : "journey").Append('\n');
        if (!document.TitleInFrontmatter && document.Title.Length > 0) builder.Append("title ").Append(DataDiagramSupport.Encode(document.Title)).Append('\n');
        string? active = null;
        if (document.Family == TemporalFamily.Timeline)
            foreach (var period in document.Periods)
            {
                Section(period.SectionId);
                builder.Append(DataDiagramSupport.Encode(period.Label));
                foreach (var item in period.Events) builder.Append(" : ").Append(DataDiagramSupport.Encode(item.Text));
                builder.Append('\n');
            }
        else
            foreach (var task in document.Tasks)
            {
                Section(task.SectionId);
                builder.Append(DataDiagramSupport.Encode(task.Label)).Append(": ").Append(task.Score).Append(": ")
                    .Append(string.Join(", ", task.Actors)).Append('\n');
            }
        var referenced = document.Family == TemporalFamily.Timeline ? document.Periods.Select(p => p.SectionId) : document.Tasks.Select(t => t.SectionId);
        foreach (var section in document.Sections.Where(s => !referenced.Contains(s.Id))) builder.Append("section ").Append(DataDiagramSupport.Encode(section.Label)).Append('\n');
        foreach (var comment in document.Comments) builder.Append(comment).Append('\n');
        return builder.ToString();
        void Section(string? id)
        {
            if (id == active) return;
            if (id is null) throw new ArgumentException("Unsectioned items must precede named sections.");
            builder.Append("section ").Append(DataDiagramSupport.Encode(document.Sections.Single(s => s.Id == id).Label)).Append('\n');
            active = id;
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Temporal ?? throw new ArgumentException("Temporal content is missing.");
        var before = Parse(source).Temporal!;
        var result = source;
        if (Write(before) != Write(document))
        {
            var body = Write(document);
            if (MerpeepsDoc.BodyOf(source).Contains("\r\n", StringComparison.Ordinal)) body = body.Replace("\n", "\r\n");
            result = MerpeepsDoc.ReplaceBody(result, body);
        }
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) ||
            !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration) || document.TitleInFrontmatter && before.Title != document.Title)
            result = DataDiagramSupport.ApplyConfiguration(result, Kind, document.Configuration, document.ThemeVariables, document.GlobalConfiguration, document.TitleInFrontmatter, document.Title);
        return result;
    }
    internal static object Identity(string source, TemporalDocument document)
    {
        var key = document.Family == TemporalFamily.Timeline ? "timeline" : "journey";
        var identity = new DataIdentity(source, key);
        var entries = new List<object?>();
        entries.AddRange(document.Sections.Select(s => identity.Entry(s.Id, "section:" + s.Label)));
        foreach (var period in document.Periods)
        {
            entries.Add(identity.Entry(period.Id, "period:" + period.Label));
            entries.AddRange(period.Events.Select(e => identity.Entry(e.Id, "event:" + period.Id + "\0" + e.Text)));
        }
        entries.AddRange(document.Tasks.Select(t => identity.Entry(t.Id, "task:" + t.Label)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(TemporalDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) { if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) issues.Add(new("identity", id, "Temporal identities must be unique.", true)); }
        void Text(string text, string id)
        {
            if (text.Contains(':')) issues.Add(new("text-delimiter", id, "Temporal labels cannot contain a literal colon; it separates events or task fields.", true));
        }
        foreach (var section in document.Sections) { Id(section.Id); Text(section.Label, section.Id); }
        var sectionIds = document.Sections.Select(s => s.Id).ToHashSet();
        string? prior = null;
        var closed = new HashSet<string>();
        var groups = document.Family == TemporalFamily.Timeline ? document.Periods.Select(p => p.SectionId) : document.Tasks.Select(t => t.SectionId);
        foreach (var group in groups)
        {
            if (group != prior)
            {
                if (prior is not null) closed.Add(prior);
                if (group is null && closed.Count > 0 || group is not null && closed.Contains(group))
                    issues.Add(new("section-order", group ?? "", "Keep each section's items together and unsectioned items first.", true));
                prior = group;
            }
        }
        foreach (var period in document.Periods)
        {
            Id(period.Id); Text(period.Label, period.Id);
            if (period.SectionId is { } section && !sectionIds.Contains(section)) issues.Add(new("section", period.Id, "Time-period section does not exist.", true));
            foreach (var item in period.Events) { Id(item.Id); Text(item.Text, item.Id); }
        }
        foreach (var task in document.Tasks)
        {
            Id(task.Id); Text(task.Label, task.Id);
            if (task.Score is < 1 or > 5) issues.Add(new("score", task.Id, "Journey scores range from 1 to 5.", true));
            if (task.Actors.Any(a => string.IsNullOrWhiteSpace(a) || a.IndexOfAny([',', ':', '\r', '\n']) >= 0))
                issues.Add(new("actors", task.Id, "Actor names cannot contain task delimiters or line breaks.", true));
            if (task.SectionId is { } section && !sectionIds.Contains(section)) issues.Add(new("section", task.Id, "Task section does not exist.", true));
        }
        issues.AddRange(DataDiagramSupport.ValidateOptions(document.Configuration, document.ThemeVariables, document.GlobalConfiguration,
            document.Family == TemporalFamily.Timeline ? "timeline" : "journey", [], ["disableMulticolor"]));
        return issues;
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Temporal items are not graph nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Temporal diagrams do not use graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
