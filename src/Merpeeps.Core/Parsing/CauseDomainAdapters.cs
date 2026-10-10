using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class IshikawaDiagramAdapter : IDiagramAdapter
{
    public string Kind => "ishikawa-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new IshikawaDocument(); var settings = DataDiagramSupport.Configuration(source, "ishikawa");
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var identities = new DataIdentity(source, "ishikawa"); var header = false; var problem = false;
        var roots = new List<Builder>(); var stack = new Stack<(int Indent, Builder Node)>();
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected ishikawa-beta."); header = true; continue; }
            if (!problem) { document.Problem = line; problem = true; continue; }
            var expanded = raw.Replace("\t", "    "); var indent = expanded.Length - expanded.TrimStart().Length;
            var node = new Builder(new(identities.Next("cause:" + line), line, []));
            while (stack.Count > 0 && stack.Peek().Indent >= indent) stack.Pop();
            if (stack.TryPeek(out var parent)) parent.Node.Children.Add(node); else roots.Add(node);
            stack.Push((indent, node));
        }
        if (!header) throw new FormatException("Ishikawa directive is missing.");
        document.Causes.AddRange(roots.Select(c => c.Finish())); Validate(document);
        return new() { Kind = Kind, Ishikawa = document };
    }
    public static void Validate(IshikawaDocument document)
    {
        if (document.Problem.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("The Ishikawa problem must be one line.");
        if (document.Problem.Length == 0 && document.Causes.Count > 0) throw new ArgumentException("Causes need a problem statement.");
        var ids = new HashSet<string>(); var pending = new Stack<IshikawaCause>(document.Causes);
        while (pending.TryPop(out var cause))
        {
            if (cause.Id.Length == 0 || !ids.Add(cause.Id)) throw new ArgumentException("Cause identities must be unique and the hierarchy acyclic.");
            if (string.IsNullOrWhiteSpace(cause.Label) || cause.Label.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Cause labels must be nonempty single lines.");
            foreach (var child in cause.Children) pending.Push(child);
        }
    }
    public string Write(DiagramModel model) => Write(model.Ishikawa ?? throw new ArgumentException("Ishikawa content is missing."));
    public static string Write(IshikawaDocument document)
    {
        Validate(document); var output = new StringBuilder("ishikawa-beta\n");
        if (document.Problem.Length > 0) output.Append(document.Problem).Append('\n');
        Causes(document.Causes, 0); foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
        void Causes(IEnumerable<IshikawaCause> causes, int depth)
        {
            foreach (var cause in causes) { output.Append(' ', depth * 4).Append(cause.Label).Append('\n'); Causes(cause.Children, depth + 1); }
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Ishikawa!; var before = Parse(source).Ishikawa!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "ishikawa", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, IshikawaDocument document)
    {
        var identity = new DataIdentity(source, "ishikawa");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = document.AllCauses.Select(c => identity.Entry(c.Id, "cause:" + c.Label)).ToList() };
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Ishikawa causes belong to a hierarchy.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Ishikawa branches are derived from causes.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
    private sealed class Builder(IshikawaCause value)
    {
        public List<Builder> Children { get; } = new();
        public IshikawaCause Finish() => value with { Children = Children.Select(c => c.Finish()).ToArray() };
    }
}

public sealed class CynefinDiagramAdapter : IDiagramAdapter
{
    public string Kind => "cynefin-beta";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new CynefinDocument(); var settings = DataDiagramSupport.Configuration(source, "cynefin");
        document.Configuration = settings.Configuration; document.ThemeVariables = settings.Theme; document.GlobalConfiguration = settings.Global;
        var identity = new DataIdentity(source, "cynefin"); CynefinDomain? domain = null; var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header) { if (line != Kind) throw new FormatException("Expected cynefin-beta."); header = true; continue; }
            if (line.StartsWith("title ", StringComparison.Ordinal)) { document.Title = line[6..]; continue; }
            if (line.StartsWith("accTitle:", StringComparison.Ordinal)) { document.AccessibilityTitle = line[9..].Trim(); continue; }
            if (line.StartsWith("accDescr:", StringComparison.Ordinal)) { document.AccessibilityDescription = line[9..].Trim(); continue; }
            if (Enum.TryParse<CynefinDomain>(line, true, out var parsed) && Enum.IsDefined(parsed))
            { domain = parsed; if (!document.DeclaredDomains.Contains(parsed)) document.DeclaredDomains.Add(parsed); continue; }
            var transition = Regex.Match(line, "^(?<from>complex|complicated|clear|chaotic|confusion)\\s*-->\\s*(?<to>complex|complicated|clear|chaotic|confusion)(?:\\s*:\\s*\"(?<label>[^\"]*)\")?$");
            if (transition.Success)
            {
                var from = Enum.Parse<CynefinDomain>(transition.Groups["from"].Value, true);
                var to = Enum.Parse<CynefinDomain>(transition.Groups["to"].Value, true);
                var label = System.Net.WebUtility.HtmlDecode(transition.Groups["label"].Value);
                document.Transitions.Add(new(identity.Next("transition:" + from + "\0" + to + "\0" + label), from, to, label)); continue;
            }
            if (domain is null || line.Length < 2 || line[0] != '"' || line[^1] != '"') throw new FormatException("Cynefin items need a declared domain and a quoted label.");
            var text = System.Net.WebUtility.HtmlDecode(line[1..^1]);
            document.Items.Add(new(identity.Next("item:" + domain + "\0" + text), domain.Value, text));
        }
        if (!header) throw new FormatException("Cynefin directive is missing.");
        Validate(document); return new() { Kind = Kind, Cynefin = document };
    }
    public static void Validate(CynefinDocument document)
    {
        var ids = new HashSet<string>();
        foreach (var item in document.Items)
        {
            if (!ids.Add(item.Id) || !Enum.IsDefined(item.Domain) || item.Label.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Invalid Cynefin item identity/domain/label.");
        }
        foreach (var transition in document.Transitions)
        {
            if (!ids.Add(transition.Id) || !Enum.IsDefined(transition.From) || !Enum.IsDefined(transition.To) || transition.Label.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Invalid Cynefin transition.");
            if (transition.From == transition.To) throw new DataDiagramNotEditableException([new("cynefin-self-transition", transition.Id, "Upstream ignores self transitions; native authoring preserves this source instead of pretending to draw one.", true)]);
        }
        var issues = DataDiagramSupport.ValidateOptions(document.Configuration, document.ThemeVariables, document.GlobalConfiguration, "cynefin",
            ["width", "height", "padding", "boundaryAmplitude", "seed"], ["showDomainDescriptions"]);
        if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
        var theme = document.ThemeVariables["cynefin"] as JsonObject;
        if (theme is not null)
            foreach (var pair in theme)
            {
                if (pair.Value is null) continue;
                if (pair.Key.EndsWith("Bg", StringComparison.Ordinal) || pair.Key.EndsWith("Color", StringComparison.Ordinal))
                {
                    if (!SequenceColors.TryParse(pair.Value.ToString(), out _)) throw new ArgumentException("Invalid Cynefin color: " + pair.Key);
                }
                if ((pair.Key.EndsWith("Width", StringComparison.Ordinal) || pair.Key.EndsWith("Size", StringComparison.Ordinal)) &&
                    ChartConfiguration.ParseNumber(pair.Value.ToString()) <= 0) throw new ArgumentException("Cynefin text sizes and line widths must be positive.");
            }
        if (document.Title.IndexOfAny(['\r', '\n']) >= 0 || document.AccessibilityTitle.IndexOfAny(['\r', '\n']) >= 0 ||
            document.AccessibilityDescription.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Cynefin text directives must be single lines.");
        if (document.GlobalConfiguration["look"]?.ToString() == "handDrawn") throw new NotSupportedException("Cynefin does not support hand-drawn rendering.");
    }
    public string Write(DiagramModel model) => Write(model.Cynefin ?? throw new ArgumentException("Cynefin content is missing."));
    public static string Write(CynefinDocument document)
    {
        Validate(document); var output = new StringBuilder("cynefin-beta\n");
        if (document.Title.Length > 0) output.Append("title ").Append(document.Title).Append('\n');
        if (document.AccessibilityTitle.Length > 0) output.Append("accTitle: ").Append(document.AccessibilityTitle).Append('\n');
        if (document.AccessibilityDescription.Length > 0) output.Append("accDescr: ").Append(document.AccessibilityDescription).Append('\n');
        foreach (var domain in document.DeclaredDomains.Concat(document.Items.Select(i => i.Domain)).Distinct())
        {
            output.Append(domain.ToString().ToLowerInvariant()).Append('\n');
            foreach (var item in document.Items.Where(i => i.Domain == domain)) output.Append("  ").Append(Quote(item.Label)).Append('\n');
        }
        foreach (var transition in document.Transitions)
            output.Append(transition.From.ToString().ToLowerInvariant()).Append(" --> ").Append(transition.To.ToString().ToLowerInvariant())
                .Append(transition.Label.Length == 0 ? "" : " : " + Quote(transition.Label)).Append('\n');
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.Cynefin!; var before = Parse(source).Cynefin!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "cynefin", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, CynefinDocument document)
    {
        var identity = new DataIdentity(source, "cynefin");
        var entries = document.Items.Select(i => identity.Entry(i.Id, "item:" + i.Domain + "\0" + i.Label)).ToList();
        entries.AddRange(document.Transitions.Select(t => identity.Entry(t.Id, "transition:" + t.From + "\0" + t.To + "\0" + t.Label)));
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }
    private static string Quote(string value) => "\"" + value.Replace("&", "&amp;").Replace("\"", "&quot;") + "\"";
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Cynefin items belong to fixed domains.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Cynefin transitions connect domains.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
