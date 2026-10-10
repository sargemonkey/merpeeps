using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class GitGraphAdapter : IDiagramAdapter
{
    public string Kind => "gitGraph";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source)
    {
        var document = new GitGraphDocument();
        var configuration = DataDiagramSupport.Configuration(source, "gitGraph");
        document.Configuration = configuration.Configuration; document.ThemeVariables = configuration.Theme; document.GlobalConfiguration = configuration.Global;
        var identity = new DataIdentity(source, "gitGraph");
        var header = false;
        foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimEnd(';');
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { document.Comments.Add(line); continue; }
            if (!header)
            {
                if (!line.StartsWith("gitGraph", StringComparison.Ordinal)) throw new FormatException("Expected gitGraph.");
                var direction = line[8..].Trim().TrimEnd(':');
                document.Direction = direction.Length == 0 ? GitDiagramDirection.LR : Enum.Parse<GitDiagramDirection>(direction, true);
                header = true; continue;
            }
            var command = Regex.Match(line, @"^(?<kind>commit|branch|checkout|switch|merge|cherry-pick)\b(?<tail>.*)$");
            if (!command.Success) throw new FormatException("Unknown GitGraph operation.");
            var kind = command.Groups["kind"].Value switch
            {
                "commit" => GitDiagramOperationKind.Commit, "branch" => GitDiagramOperationKind.Branch,
                "checkout" or "switch" => GitDiagramOperationKind.Checkout, "merge" => GitDiagramOperationKind.Merge, _ => GitDiagramOperationKind.CherryPick,
            };
            var tail = command.Groups["tail"].Value.Trim();
            string? name = null;
            if (kind is GitDiagramOperationKind.Branch or GitDiagramOperationKind.Checkout or GitDiagramOperationKind.Merge)
            {
                var first = Regex.Match(tail, "^(?:\"(?<quoted>[^\"]*)\"|(?<bare>\\S+))");
                if (!first.Success) throw new FormatException("GitGraph branch name is required.");
                name = first.Groups["quoted"].Success ? first.Groups["quoted"].Value : first.Groups["bare"].Value;
                tail = tail[first.Length..].Trim();
            }
            var properties = Regex.Matches(tail, "(?<key>id|tag|type|order|parent|msg)\\s*:\\s*(?:\"(?<quoted>[^\"]*)\"|(?<bare>[^\\s,]+))", RegexOptions.IgnoreCase);
            string? id = null, parent = null, message = null;
            int? order = null; var commitKind = GitDiagramCommitKind.Normal; var tags = new List<string>();
            foreach (Match property in properties)
            {
                var value = property.Groups["quoted"].Success ? property.Groups["quoted"].Value : property.Groups["bare"].Value;
                switch (property.Groups["key"].Value.ToLowerInvariant())
                {
                    case "id": id = value; break; case "parent": parent = value; break; case "msg": message = value; break;
                    case "tag": tags.Add(value); break; case "order": order = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "type": commitKind = Enum.Parse<GitDiagramCommitKind>(value, true); break;
                }
            }
            var leftover = Regex.Replace(tail, "(?<key>id|tag|type|order|parent|msg)\\s*:\\s*(?:\"(?<quoted>[^\"]*)\"|(?<bare>[^\\s,]+))", "", RegexOptions.IgnoreCase).Trim().Trim(',');
            if (leftover.Length > 0) throw new FormatException("Unrecognized GitGraph operation attributes: " + leftover);
            var operation = new GitDiagramOperation("", kind)
            {
                Name = kind == GitDiagramOperationKind.CherryPick ? id : name, CommitId = kind == GitDiagramOperationKind.CherryPick ? null : id,
                ParentCommitId = parent, Message = message, Order = order, CommitKind = commitKind, Tags = tags,
            };
            operation = operation with { Id = identity.Next(Signature(operation)) };
            document.Operations.Add(operation);
        }
        if (!header) throw new FormatException("GitGraph directive is missing.");
        _ = document.Replay();
        return new() { Kind = Kind, GitDiagram = document };
    }
    public string Write(DiagramModel model) => Write(model.GitDiagram ?? throw new ArgumentException("GitGraph content is missing."));
    public static string Write(GitGraphDocument document)
    {
        _ = document.Replay();
        var output = new StringBuilder("gitGraph ").Append(document.Direction).Append(":\n");
        foreach (var operation in document.Operations)
        {
            output.Append(operation.Kind switch { GitDiagramOperationKind.Commit => "commit", GitDiagramOperationKind.Branch => "branch", GitDiagramOperationKind.Checkout => "checkout", GitDiagramOperationKind.Merge => "merge", _ => "cherry-pick" });
            if (operation.Kind is GitDiagramOperationKind.Branch or GitDiagramOperationKind.Checkout or GitDiagramOperationKind.Merge) output.Append(' ').Append(Quote(operation.Name!));
            if (operation.Kind == GitDiagramOperationKind.CherryPick) output.Append(" id:").Append(Quote(operation.Name!));
            else if (operation.CommitId is { } id) output.Append(" id:").Append(Quote(id));
            if (operation.ParentCommitId is { } parent) output.Append(" parent:").Append(Quote(parent));
            if (operation.Order is { } order) output.Append(" order:").Append(order);
            if (operation.CommitKind != GitDiagramCommitKind.Normal) output.Append(" type:").Append(operation.CommitKind.ToString().ToUpperInvariant());
            foreach (var tag in operation.Tags) output.Append(" tag:").Append(Quote(tag));
            if (operation.Message is { } message) output.Append(" msg:").Append(Quote(message));
            output.Append('\n');
        }
        foreach (var comment in document.Comments) output.Append(comment).Append('\n');
        return output.ToString();
    }
    internal string Apply(string source, DiagramModel model)
    {
        var document = model.GitDiagram!; var before = Parse(source).GitDiagram!;
        var result = Write(before) == Write(document) ? source : MerpeepsDoc.ReplaceBody(source, Write(document));
        if (!JsonNode.DeepEquals(before.Configuration, document.Configuration) || !JsonNode.DeepEquals(before.ThemeVariables, document.ThemeVariables) || !JsonNode.DeepEquals(before.GlobalConfiguration, document.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "gitGraph", document.Configuration, document.ThemeVariables, document.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, GitGraphDocument document)
    {
        var identity = new DataIdentity(source, "gitGraph");
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = document.Operations.Select(o => identity.Entry(o.Id, Signature(o))).ToList() };
    }
    private static string Signature(GitDiagramOperation operation) => $"{operation.Kind}\0{operation.Name}\0{operation.CommitId}\0{operation.CommitKind}\0{operation.Order}\0{operation.ParentCommitId}\0{operation.Message}\0{string.Join("\0", operation.Tags)}";
    private static string Quote(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("GitGraph values cannot contain quotes or line breaks.");
        return "\"" + value + "\"";
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("GitGraph commits are derived from operations, not repository nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("GitGraph ancestry is derived from operation replay.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
}
