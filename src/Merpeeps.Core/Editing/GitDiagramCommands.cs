using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class GitDiagramEditCommand(string description, string? resultId, Action<GitGraphDocument> edit) : IEditCommand
{
    private GitGraphDocument? _before;
    public string Description => description;
    public string? ResultId => resultId;
    public void Do(DiagramModel model)
    {
        var before = model.GitDiagram ?? throw new ArgumentException("Choose a GitGraph diagram.");
        var next = before.Clone(); edit(next); _ = next.Replay(); _ = GitGraphAdapter.Write(next);
        _before = before; model.GitDiagram = next;
    }
    public void Undo(DiagramModel model) => model.GitDiagram = _before ?? throw new InvalidOperationException("Not applied.");
}
public static class GitDiagramEditing
{
    public static GitDiagramEditCommand Insert(GitDiagramOperation operation, int? index = null)
    {
        var copy = operation with { Tags = operation.Tags.ToArray() };
        return new("Add GitGraph operation", operation.Id, d => d.Operations.Insert(index ?? d.Operations.Count, copy with { Tags = copy.Tags.ToArray() }));
    }
    public static GitDiagramEditCommand Update(GitDiagramOperation operation)
    {
        var copy = operation with { Tags = operation.Tags.ToArray() };
        return new("Edit GitGraph operation", operation.Id, d => d.Operations[Index(d, operation.Id)] = copy with { Tags = copy.Tags.ToArray() });
    }
    public static GitDiagramEditCommand Move(string id, int target) => new("Reorder GitGraph operations", id, d =>
    {
        if (target < 0 || target >= d.Operations.Count) throw new ArgumentOutOfRangeException(nameof(target));
        var index = Index(d, id); var operation = d.Operations[index]; d.Operations.RemoveAt(index); d.Operations.Insert(target, operation);
    });
    public static GitDiagramEditCommand Remove(string id) => new("Delete GitGraph operation", null, d => d.Operations.RemoveAt(Index(d, id)));
    public static GitDiagramEditCommand SetDirection(GitDiagramDirection direction) => new("Change GitGraph direction", null, d => d.Direction = direction);
    public static GitDiagramEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit GitGraph option", null, d => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    public static GitDiagramEditCommand SetThemeVariable(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit GitGraph theme", null, d => OutlineEditing.SetJsonPath(d.ThemeVariables, key, copy));
    }
    private static int Index(GitGraphDocument d, string id) => d.Operations.FindIndex(o => o.Id == id) is var index && index >= 0 ? index : throw new ArgumentException("Operation no longer exists.");
}
