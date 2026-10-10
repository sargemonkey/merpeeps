using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed record ZenLocation(string? ParentId, string? BranchId, int Index, ZenStatement Statement);
public sealed class ZenEditCommand : IEditCommand
{
    private readonly Action<ZenDiagramDocument> _edit; private ZenDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal ZenEditCommand(string description, string? id, Action<ZenDiagramDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Zen ?? throw new ArgumentException("Choose ZenUML.");
        var next = before.Clone(); _edit(next); ZenSemantics.RebindContext(next); ZenSemantics.Check(next); _ = ZenDiagramAdapter.Write(next);
        _ = ZenDiagramLayout.Create(new DiagramModel { Kind = "zenuml", Zen = next }); _before = before; model.Zen = next;
    }
    public void Undo(DiagramModel model) => model.Zen = _before ?? throw new InvalidOperationException("ZenUML edit is not applied.");
}
public static class ZenEditing
{
    public static ZenEditCommand AddParticipant(ZenParticipant participant) => new("Add ZenUML participant", participant.Id, d => d.Participants.Add(participant with { Declared = true }));
    public static ZenEditCommand UpdateParticipant(ZenParticipant participant) => new("Edit ZenUML participant", participant.Id, d => d.Participants[Index(d.Participants, p => p.Id == participant.Id)] = participant);
    public static ZenEditCommand MoveParticipant(string id, int index) => new("Reorder ZenUML participant", id, d =>
    {
        var old = Index(d.Participants, p => p.Id == id); var participant = d.Participants[old]; d.Participants.RemoveAt(old); d.Participants.Insert(index, participant);
        for (var i = 0; i < d.Participants.Count; i++) d.Participants[i] = d.Participants[i] with { Declared = true };
    });
    public static ZenEditCommand RemoveParticipant(string id) => new("Remove unused ZenUML participant", null, d =>
    {
        if (ZenSemantics.Walk(d.Statements).Any(s => s.From == id || s.To == id)) throw new ArgumentException("Remove or reconnect participant messages first.");
        d.Participants.RemoveAt(Index(d.Participants, p => p.Id == id));
    });
    public static ZenEditCommand AddStatement(ZenStatement statement, string? parentId = null, string? branchId = null, int? position = null)
    {
        var copy = statement.DeepCopy();
        return new("Add ZenUML statement", statement.Id, d => Change(d, parentId, branchId, list => list.Insert(position ?? list.Count, copy.DeepCopy())));
    }
    public static ZenEditCommand UpdateStatement(ZenStatement statement)
    {
        var copy = statement.DeepCopy();
        return new("Edit ZenUML statement", statement.Id, d =>
        { var location = Locate(d, statement.Id); Change(d, location.ParentId, location.BranchId, list => list[location.Index] = copy.DeepCopy()); });
    }
    public static ZenEditCommand RemoveStatement(string id) => new("Remove ZenUML statement subtree", null, d =>
    { var location = Locate(d, id); Change(d, location.ParentId, location.BranchId, list => list.RemoveAt(location.Index)); });
    public static ZenEditCommand MoveStatement(string id, string? parent, string? branch, int position) => new("Reorder ZenUML statement", id, d =>
    {
        var location = Locate(d, id);
        if (parent is not null && ZenSemantics.Walk([location.Statement]).Any(s => s.Id == parent)) throw new ArgumentException("A statement cannot contain itself.");
        Change(d, location.ParentId, location.BranchId, list => list.RemoveAt(location.Index));
        Change(d, parent, branch, list => list.Insert(position, location.Statement));
    });
    public static ZenEditCommand SetTitle(string title) => new("Edit ZenUML title", null, d => d.Title = title);
    public static ZenLocation Locate(ZenDiagramDocument d, string id)
    {
        return Find(d.Statements, null, null) ?? throw new ArgumentException("ZenUML statement no longer exists.");
        ZenLocation? Find(IReadOnlyList<ZenStatement> statements, string? parent, string? branch)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var s = statements[i]; if (s.Id == id) return new(parent, branch, i, s);
                if (Find(s.Children, s.Id, null) is { } nested) return nested;
                foreach (var b in s.Branches) if (Find(b.Statements, s.Id, b.Id) is { } found) return found;
            }
            return null;
        }
    }
    private static void Change(ZenDiagramDocument d, string? parent, string? branch, Action<List<ZenStatement>> change)
    {
        if (parent is null) { change(d.Statements); return; }
        var found = false;
        var result = Transform(d.Statements);
        if (!found) throw new ArgumentException("ZenUML parent or branch no longer exists.");
        d.Statements.Clear(); d.Statements.AddRange(result);
        IReadOnlyList<ZenStatement> Transform(IEnumerable<ZenStatement> statements) => statements.Select(s =>
        {
            if (s.Id == parent && branch is null)
            {
                var children = s.Children.ToList(); change(children); found = true; return s with { Children = children };
            }
            var branches = s.Branches.Select(b =>
            {
                var children = b.Statements.ToList();
                if (s.Id == parent && b.Id == branch) { change(children); found = true; } else children = Transform(children).ToList();
                return b with { Statements = children };
            }).ToArray();
            return s with { Children = Transform(s.Children), Branches = branches };
        }).ToArray();
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("ZenUML participant no longer exists.");
}
