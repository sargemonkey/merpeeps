using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum GitDiagramOperationKind { Commit, Branch, Checkout, Merge, CherryPick }
public enum GitDiagramCommitKind { Normal, Reverse, Highlight }
public enum GitDiagramDirection { LR, TB, BT }
public sealed record GitDiagramOperation(string Id, GitDiagramOperationKind Kind)
{
    public string? Name { get; init; }
    public string? CommitId { get; init; }
    public GitDiagramCommitKind CommitKind { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public int? Order { get; init; }
    public string? ParentCommitId { get; init; }
    public string? Message { get; init; }
}
public sealed record GitDiagramCommit(string Id, string DisplayId, string Branch, IReadOnlyList<string> Parents,
    GitDiagramCommitKind Kind, IReadOnlyList<string> Tags, int Ordinal, string? CherrySource = null);
public sealed record GitDiagramBranch(string Name, string? HeadId, double Order, int Ordinal);
public sealed record GitDiagramState(IReadOnlyList<GitDiagramCommit> Commits, IReadOnlyList<GitDiagramBranch> Branches, string CurrentBranch);
public sealed class GitGraphDocument
{
    public GitDiagramDirection Direction { get; set; }
    public List<GitDiagramOperation> Operations { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public GitGraphDocument Clone()
    {
        var copy = new GitGraphDocument { Direction = Direction, Configuration = (JsonObject)Configuration.DeepClone(),
            ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        copy.Operations.AddRange(Operations.Select(o => o with { Tags = o.Tags.ToArray() })); copy.Comments.AddRange(Comments); return copy;
    }
    public GitDiagramState Replay()
    {
        foreach (var key in new[] { "showBranches", "showCommitLabel", "rotateCommitLabel", "parallelCommits" })
            if (Configuration[key] is { } value && (value is not JsonValue scalar || !scalar.TryGetValue<bool>(out _)))
                throw new ArgumentException(key + " must be true or false.");
        foreach (var pair in ThemeVariables)
        {
            if (pair.Value is null) continue;
            var text = ChartConfiguration.Raw(pair.Value);
            if (pair.Key is "commitLabelFontSize" or "tagLabelFontSize")
            {
                if (ChartConfiguration.ParseNumber(text) <= 0) throw new ArgumentException(pair.Key + " must be positive.");
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(pair.Key, @"^git(?:Inv|BranchLabel)?\d+$") ||
                pair.Key is "commitLabelColor" or "commitLabelBackground" or "tagLabelColor" or "tagLabelBackground" or "tagLabelBorder")
            {
                if (!Parsing.SequenceColors.TryParse(text, out _)) throw new ArgumentException(pair.Key + " must be a color.");
            }
        }
        var main = Configuration["mainBranchName"]?.GetValue<string>() ?? "main";
        var mainOrder = Configuration["mainBranchOrder"]?.GetValue<double>() ?? 0;
        var branches = new Dictionary<string, GitDiagramBranch>(StringComparer.Ordinal) { [main] = new(main, null, mainOrder, 0) };
        var commits = new List<GitDiagramCommit>();
        var bySourceId = new Dictionary<string, GitDiagramCommit>(StringComparer.Ordinal);
        var current = main;
        var operationIds = new HashSet<string>();
        foreach (var operation in Operations)
        {
            if (!operationIds.Add(operation.Id)) throw new ArgumentException("GitGraph operation identities must be unique.");
            switch (operation.Kind)
            {
                case GitDiagramOperationKind.Branch:
                    var name = operation.Name ?? throw new ArgumentException("Branch name is required.");
                    if (branches.ContainsKey(name)) throw new ArgumentException("Branch already exists.");
                    branches[name] = new(name, branches[current].HeadId, operation.Order ?? branches.Count / 10d, branches.Count);
                    current = name;
                    break;
                case GitDiagramOperationKind.Checkout:
                    if (operation.Name is null || !branches.ContainsKey(operation.Name)) throw new ArgumentException("Branch does not exist.");
                    current = operation.Name;
                    break;
                default:
                    var parents = new List<string>();
                    if (branches[current].HeadId is { } head) parents.Add(head);
                    string? cherrySource = null;
                    if (operation.Kind == GitDiagramOperationKind.Merge)
                    {
                        if (operation.Name == current || operation.Name is null || !branches.TryGetValue(operation.Name, out var branch) || branch.HeadId is null || parents.Count == 0)
                            throw new ArgumentException("Merge requires two different branches with commits.");
                        if (branch.HeadId == parents[0]) throw new ArgumentException("The branches already have the same head.");
                        parents.Add(branch.HeadId);
                    }
                    if (operation.Kind == GitDiagramOperationKind.CherryPick)
                    {
                        if (operation.Name is null || !bySourceId.TryGetValue(operation.Name, out var picked)) throw new ArgumentException("Cherry-pick source commit does not exist.");
                        if (picked.Branch == current || parents.Count == 0) throw new ArgumentException("Cherry-pick requires an existing current-branch head and a commit from another branch.");
                        if (picked.Parents.Count > 1 && operation.ParentCommitId is null) throw new ArgumentException("Specify an immediate parent when cherry-picking a merge.");
                        if (operation.ParentCommitId is { } parent &&
                            (!bySourceId.TryGetValue(parent, out var parentCommit) || !picked.Parents.Contains(parentCommit.Id)))
                            throw new ArgumentException("Cherry-pick parent must be an immediate source parent.");
                        parents.Add(picked.Id); cherrySource = picked.Id;
                    }
                    var displayId = operation.CommitId ?? "c-" + operation.Id[^Math.Min(8, operation.Id.Length)..];
                    if (operation.CommitId is not null && bySourceId.ContainsKey(displayId))
                        throw new DataDiagramNotEditableException([new("duplicate-commit-id", operation.Id, "Mermaid overwrites duplicate commit IDs. Native authoring requires distinct IDs and preserves this source without guessing.", true)]);
                    var commit = new GitDiagramCommit(operation.Id, displayId, current, parents, operation.CommitKind, operation.Tags.ToArray(), commits.Count, cherrySource);
                    commits.Add(commit);
                    if (operation.CommitId is not null) bySourceId[displayId] = commit;
                    branches[current] = branches[current] with { HeadId = commit.Id };
                    break;
            }
        }
        return new(commits, branches.Values.OrderBy(b => b.Order).ThenBy(b => b.Ordinal).ToArray(), current);
    }
}
