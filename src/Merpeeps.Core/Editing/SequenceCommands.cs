using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed record SequenceLocation(string? ParentBranchId, int Index, SequenceStatement Statement);
public sealed record SequenceBranchLocation(string BlockId, int Index, SequenceBranch Branch);

/// <summary>One validated snapshot edit in the existing diagram history; failures never mutate the live sequence.</summary>
public sealed class SequenceEditCommand : IEditCommand
{
    private readonly Action<SequenceDiagramModel> _edit;
    private SequenceDiagramModel? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal SequenceEditCommand(string description, string? resultId, Action<SequenceDiagramModel> edit) =>
        (Description, ResultId, _edit) = (description, resultId, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Sequence ?? throw new InvalidOperationException("This edit requires a sequence diagram.");
        var candidate = before.Clone();
        _edit(candidate);
        var diagnostics = SequenceValidation.Validate(candidate);
        if (diagnostics.Count > 0) throw new SequenceNotEditableException(new(candidate, diagnostics));
        var body = SequenceDiagramAdapter.Write(candidate);
        var read = new SequenceDiagramAdapter().ReadSource(body);
        if (!read.CanEdit) throw new SequenceNotEditableException(read);
        if (!candidate.Participants.Select(p => p.Id).SequenceEqual(read.Model.Participants.Select(p => p.Id)))
            throw new ArgumentException("That lane order conflicts with participant creation or box declarations. Move the associated creation message instead.");
        _before = before;
        model.Sequence = candidate;
    }
    public void Undo(DiagramModel model) =>
        model.Sequence = _before ?? throw new InvalidOperationException("The sequence edit has not been applied.");
}

public static class SequenceEditing
{
    public static SequenceLocation Locate(SequenceDiagramModel model, string id)
    {
        var found = Find(model.Statements, null);
        return found ?? throw new ArgumentException($"Sequence statement '{id}' no longer exists.");
        SequenceLocation? Find(IReadOnlyList<SequenceStatement> items, string? branchId)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Id == id) return new(branchId, index, items[index]);
                if (items[index] is not SequenceBlock block) continue;
                foreach (var branch in block.Branches)
                    if (Find(branch.Statements, branch.Id) is { } nested) return nested;
            }
            return null;
        }
    }

    public static SequenceBranchLocation LocateBranch(SequenceDiagramModel model, string id)
    {
        foreach (var block in SequenceDiagramModel.Walk(model.Statements).OfType<SequenceBlock>())
            for (var index = 0; index < block.Branches.Count; index++)
                if (block.Branches[index].Id == id) return new(block.Id, index, block.Branches[index]);
        throw new ArgumentException($"Sequence branch '{id}' no longer exists.");
    }

    public static SequenceEditCommand AddParticipant(SequenceParticipant participant)
    {
        var copy = participant.DeepCopy();
        var declarationId = NewId();
        return new("Add participant " + participant.Display, participant.Id, model =>
        {
            if (model.Participants.Any(p => p.Id == copy.Id)) throw new ArgumentException("Participant identity already exists.");
            model.Participants.Add(copy.DeepCopy());
            model.Statements.Add(new SequenceDeclaration(declarationId, copy.Id));
        });
    }

    public static SequenceEditCommand UpdateParticipant(string id, SequenceParticipant participant)
    {
        var copy = participant.DeepCopy();
        return new("Edit participant " + participant.Display, participant.Id, model =>
        {
            var index = model.Participants.FindIndex(p => p.Id == id);
            if (index < 0) throw new ArgumentException("Participant no longer exists.");
            if (copy.Id != id && model.Participants.Any(p => p.Id == copy.Id)) throw new ArgumentException("Participant identity already exists.");
            var oldBox = model.Participants[index].BoxId;
            model.Participants[index] = copy.DeepCopy();
            if (copy.Id != id)
                MapStatements(model, statement => statement switch
                {
                    SequenceDeclaration d => d with { ParticipantId = Replace(d.ParticipantId) },
                    SequenceMessage m => m with
                    {
                        From = Replace(m.From), To = Replace(m.To),
                        CreateParticipantId = Optional(m.CreateParticipantId), DestroyParticipantId = Optional(m.DestroyParticipantId),
                    },
                    SequenceActivation a => a with { ParticipantId = Replace(a.ParticipantId) },
                    SequenceNote n => n with { ParticipantId = Replace(n.ParticipantId), SecondParticipantId = Optional(n.SecondParticipantId) },
                    SequenceParticipantMetadata m => m with { ParticipantId = Replace(m.ParticipantId) },
                    _ => statement,
                });
            if (copy.BoxId != oldBox) NormalizeDeclarations(model);
            string Replace(string value) => value == id ? copy.Id : value;
            string? Optional(string? value) => value is null ? null : Replace(value);
        });
    }

    public static SequenceEditCommand SetParticipantAlias(string id, string? alias) =>
        new("Rename participant " + id, id, model =>
        {
            var index = model.Participants.FindIndex(p => p.Id == id);
            if (index < 0) throw new ArgumentException("Participant no longer exists.");
            var participant = model.Participants[index].DeepCopy();
            participant.Configuration.Remove("alias");
            model.Participants[index] = participant with { Alias = alias, AliasIsExternal = true };
        });

    public static SequenceEditCommand MoveParticipant(string id, int targetIndex) =>
        new("Reorder participant lanes", id, model =>
        {
            var index = model.Participants.FindIndex(p => p.Id == id);
            if (index < 0 || targetIndex < 0 || targetIndex >= model.Participants.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
            var participant = model.Participants[index];
            model.Participants.RemoveAt(index);
            model.Participants.Insert(targetIndex, participant);
            NormalizeDeclarations(model);
        });

    public static SequenceEditCommand RemoveParticipant(string id) =>
        new("Delete participant and its interactions", null, model =>
        {
            _ = model.Participant(id);
            if (SequenceDiagramModel.Walk(model.Statements).OfType<SequenceMessage>().Any(m =>
                (m.From == id || m.To == id) && m.CreateParticipantId is { } created && created != id))
                throw new ArgumentException("This participant creates another participant. Remove or change that creation first.");
            model.Participants.RemoveAll(p => p.Id == id);
            FilterStatements(model, statement => !SequenceValidation.References(statement, false).Contains(id));
        });

    public static SequenceEditCommand Insert(SequenceStatement statement, string? branchId = null, int? index = null)
    {
        var copy = SequenceDiagramModel.CloneStatements([statement]).Single();
        return new("Add sequence " + StatementName(statement), statement.Id,
            model => ChangeList(model, branchId, items =>
            {
                var at = index ?? items.Count;
                if (at < 0 || at > items.Count) throw new ArgumentOutOfRangeException(nameof(index));
                items.Insert(at, SequenceDiagramModel.CloneStatements([copy]).Single());
            }));
    }

    public static SequenceEditCommand CreateParticipantOnMessage(
        SequenceParticipant participant, SequenceMessage message, string? branchId = null, int? index = null)
    {
        var copy = participant.DeepCopy();
        var paired = message with { CreateParticipantId = participant.Id };
        return new("Create participant by message", message.Id, model =>
        {
            if (model.Participants.Any(p => p.Id == copy.Id)) throw new ArgumentException("Created participant must have a new identity.");
            model.Participants.Add(copy.DeepCopy());
            ChangeList(model, branchId, items => items.Insert(index ?? items.Count, paired));
            ReconcileCreationOrder(model);
        });
    }

    public static SequenceEditCommand Update(string id, SequenceStatement replacement)
    {
        if (id != replacement.Id) throw new ArgumentException("Editing a statement must retain its identity.");
        var copy = SequenceDiagramModel.CloneStatements([replacement]).Single();
        return new("Edit sequence " + StatementName(replacement), id, model =>
        {
            var location = Locate(model, id);
            if (location.Statement.GetType() != copy.GetType())
                throw new ArgumentException("Use explicit add/remove operations to change a sequence item's kind.");
            ChangeList(model, location.ParentBranchId, items => items[location.Index] = SequenceDiagramModel.CloneStatements([copy]).Single());
        });
    }

    public static SequenceEditCommand Remove(string id) => new("Delete sequence item", null, model =>
    {
        var location = Locate(model, id);
        var removed = SequenceDiagramModel.Walk([location.Statement]).Select(s => s.Id).ToHashSet();
        var creations = SequenceDiagramModel.Walk([location.Statement]).OfType<SequenceMessage>()
            .Where(m => m.CreateParticipantId is not null).Select(m => m.CreateParticipantId!).ToArray();
        foreach (var participant in creations)
        {
            if (SequenceDiagramModel.Walk(model.Statements).Any(s => !removed.Contains(s.Id) && SequenceValidation.References(s, false).Contains(participant)))
                throw new ArgumentException("A participant created here is used elsewhere. Delete that participant or change its creation first.");
            model.Participants.RemoveAll(p => p.Id == participant);
        }
        ChangeList(model, location.ParentBranchId, items => items.RemoveAt(location.Index));
    });

    /// <summary>Destination index is measured after removal from the original list.</summary>
    public static SequenceEditCommand Move(string id, string? targetBranchId, int targetIndex) => new("Reorder sequence item", id, model =>
    {
        var location = Locate(model, id);
        if (location.Statement is SequenceBlock block && targetBranchId is not null &&
            SequenceDiagramModel.Walk([block]).OfType<SequenceBlock>().SelectMany(b => b.Branches).Any(b => b.Id == targetBranchId))
            throw new ArgumentException("A fragment cannot be moved inside itself.");
        ChangeList(model, location.ParentBranchId, items => items.RemoveAt(location.Index));
        ChangeList(model, targetBranchId, items =>
        {
            if (targetIndex < 0 || targetIndex > items.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
            items.Insert(targetIndex, location.Statement);
        });
    });

    public static SequenceBlock NewBlock(SequenceBlockKind kind, string text = "", string? color = null) =>
        new(NewId(), kind, kind == SequenceBlockKind.Rect ? color ?? "rgba(200,220,250,0.3)" : text,
            new[] { new SequenceBranch(NewId(), "", Array.Empty<SequenceStatement>()) }, color);

    public static SequenceEditCommand AddBranch(string blockId, string text)
    {
        var branchId = NewId();
        return new("Add fragment branch", branchId, model =>
        {
            var location = Locate(model, blockId);
            var block = location.Statement as SequenceBlock ?? throw new ArgumentException("Choose an interaction fragment.");
            _ = SequenceDiagramAdapter.BranchKeyword(block.Kind);
            ChangeList(model, location.ParentBranchId, items => items[location.Index] = block with
            {
                Branches = block.Branches.Append(new SequenceBranch(branchId, text, Array.Empty<SequenceStatement>())).ToArray(),
            });
        });
    }

    public static SequenceEditCommand SetBranchText(string branchId, string text) => new("Edit fragment condition", branchId, model =>
    {
        var branch = LocateBranch(model, branchId);
        var location = Locate(model, branch.BlockId);
        var block = (SequenceBlock)location.Statement;
        if (branch.Index == 0)
        {
            if (block.Kind == SequenceBlockKind.Rect) throw new ArgumentException("Edit the background color, not a condition on the first rect branch.");
            ChangeList(model, location.ParentBranchId, items => items[location.Index] = block with { Text = text });
        }
        else ChangeList(model, location.ParentBranchId, items => items[location.Index] = block with
        {
            Branches = block.Branches.Select((value, i) => i == branch.Index ? value with { Text = text } : value).ToArray(),
        });
    });

        public static SequenceEditCommand MoveBranch(string branchId, int targetIndex) => new("Reorder fragment branches", branchId, model =>
        {
            var location = LocateBranch(model, branchId);
            var parent = Locate(model, location.BlockId);
            var block = (SequenceBlock)parent.Statement;
            if (targetIndex < 0 || targetIndex >= block.Branches.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
            var branches = block.Branches.Select((b, i) => b with { Text = i == 0 ? block.Text : b.Text }).ToList();
            var branch = branches[location.Index];
            branches.RemoveAt(location.Index); branches.Insert(targetIndex, branch);
            var text = branches[0].Text;
            branches[0] = branches[0] with { Text = "" };
            ChangeList(model, parent.ParentBranchId, list => list[parent.Index] = block with { Text = text, Branches = branches });
        });

        public static SequenceEditCommand RemoveBranch(string branchId) => new("Delete fragment branch", null, model =>
        {
            var location = LocateBranch(model, branchId);
            var parent = Locate(model, location.BlockId);
            var block = (SequenceBlock)parent.Statement;
            if (block.Branches.Count == 1) throw new ArgumentException("Delete the whole fragment instead of its only branch.");
            var removedIds = SequenceDiagramModel.Walk(location.Branch.Statements).Select(s => s.Id).ToHashSet();
            foreach (var message in SequenceDiagramModel.Walk(location.Branch.Statements).OfType<SequenceMessage>())
                if (message.CreateParticipantId is { } created)
                {
                    if (SequenceDiagramModel.Walk(model.Statements).Any(s => !removedIds.Contains(s.Id) && SequenceValidation.References(s, false).Contains(created)))
                        throw new ArgumentException("A participant created in this branch is used elsewhere.");
                    model.Participants.RemoveAll(p => p.Id == created);
                }
            var branches = block.Branches.Where(b => b.Id != branchId).ToArray();
            var text = location.Index == 0 ? branches[0].Text : block.Text;
            branches[0] = branches[0] with { Text = "" };
            ChangeList(model, parent.ParentBranchId, items => items[parent.Index] = block with { Text = text, Branches = branches });
        });

    public static SequenceEditCommand SetOptions(SequenceOptions options) =>
        new("Change sequence display", null, model => model.Options = options);

    public static SequenceEditCommand AddBox(SequenceParticipantBox box) => new("Add participant box", box.Id, model =>
    {
        if (model.Boxes.Any(b => b.Id == box.Id)) throw new ArgumentException("Box identity already exists.");
        model.Boxes.Add(box);
    });

    public static SequenceEditCommand UpdateBox(SequenceParticipantBox box) => new("Edit participant box", box.Id, model =>
    {
        var index = model.Boxes.FindIndex(b => b.Id == box.Id);
        if (index < 0) throw new ArgumentException("Box no longer exists.");
        model.Boxes[index] = box;
    });

    public static SequenceEditCommand AssignBox(string participantId, string? boxId) => new("Move participant to box", participantId, model =>
    {
        if (boxId is not null && !model.Boxes.Any(b => b.Id == boxId)) throw new ArgumentException("Box does not exist.");
        var index = model.Participants.FindIndex(p => p.Id == participantId);
        if (index < 0) throw new ArgumentException("Participant no longer exists.");
        var participant = model.Participants[index] with { BoxId = boxId };
        model.Participants.RemoveAt(index);
        var last = boxId is null ? -1 : model.Participants.FindLastIndex(p => p.BoxId == boxId);
        model.Participants.Insert(last >= 0 ? last + 1 : Math.Min(index, model.Participants.Count), participant);
        NormalizeDeclarations(model);
    });

    public static SequenceEditCommand RemoveBox(string boxId) => new("Remove box, keep participants", null, model =>
    {
        if (model.Boxes.RemoveAll(b => b.Id == boxId) == 0) throw new ArgumentException("Box no longer exists.");
        for (var i = 0; i < model.Participants.Count; i++)
            if (model.Participants[i].BoxId == boxId) model.Participants[i] = model.Participants[i] with { BoxId = null };
    });

    public static string NewId() => "s" + Guid.NewGuid().ToString("N");

    private static void ChangeList(SequenceDiagramModel model, string? branchId, Action<List<SequenceStatement>> change)
    {
        if (branchId is null)
        {
            change(model.Statements);
            return;
        }
        var found = false;
        var replacement = Transform(model.Statements);
        if (!found) throw new ArgumentException("Destination branch no longer exists.");
        model.Statements.Clear(); model.Statements.AddRange(replacement);
        List<SequenceStatement> Transform(IEnumerable<SequenceStatement> input) => input.Select(statement =>
        {
            if (statement is not SequenceBlock block) return statement;
            return block with
            {
                Branches = block.Branches.Select(branch =>
                {
                    var items = branch.Statements.ToList();
                    if (branch.Id == branchId) { change(items); found = true; }
                    else items = Transform(items);
                    return branch with { Statements = items };
                }).ToArray(),
            };
        }).ToList();
    }

    private static void MapStatements(SequenceDiagramModel model, Func<SequenceStatement, SequenceStatement> map)
    {
        var values = Transform(model.Statements);
        model.Statements.Clear(); model.Statements.AddRange(values);
        List<SequenceStatement> Transform(IEnumerable<SequenceStatement> items) => items.Select(s =>
            map(s is SequenceBlock block ? block with
            {
                Branches = block.Branches.Select(b => b with { Statements = Transform(b.Statements) }).ToArray(),
            } : s)).ToList();
    }

    private static void FilterStatements(SequenceDiagramModel model, Func<SequenceStatement, bool> keep)
    {
        var values = Filter(model.Statements);
        model.Statements.Clear(); model.Statements.AddRange(values);
        List<SequenceStatement> Filter(IEnumerable<SequenceStatement> items) => items.Where(s => s is SequenceBlock || keep(s))
            .Select(s => s is SequenceBlock block ? block with
            {
                Branches = block.Branches.Select(b => b with { Statements = Filter(b.Statements) }).ToArray(),
            } : s).ToList();
    }

    private static void NormalizeDeclarations(SequenceDiagramModel model)
    {
        var existing = SequenceDiagramModel.Walk(model.Statements).OfType<SequenceDeclaration>()
            .GroupBy(d => d.ParticipantId).ToDictionary(g => g.Key, g => g.First().Id);
        FilterStatements(model, statement => statement is not SequenceDeclaration);
        var creations = SequenceDiagramModel.Walk(model.Statements).OfType<SequenceMessage>()
            .Where(m => m.CreateParticipantId is not null).Select(m => m.CreateParticipantId!).ToHashSet();
        var prefix = model.Participants.TakeWhile(p => !creations.Contains(p.Id))
            .Select(p => (SequenceStatement)new SequenceDeclaration(existing.GetValueOrDefault(p.Id) ??
                "__decl_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.Id)))[..24], p.Id)).ToArray();
        model.Statements.InsertRange(0, prefix);
    }

    private static void ReconcileCreationOrder(SequenceDiagramModel model)
    {
        var body = SequenceDiagramAdapter.Write(model);
        var parsed = new SequenceDiagramAdapter().ReadSource(body);
        if (!parsed.CanEdit) throw new SequenceNotEditableException(parsed);
        var participants = model.Participants.ToDictionary(p => p.Id);
        model.Participants.Clear();
        model.Participants.AddRange(parsed.Model.Participants.Select(p => participants[p.Id]));
    }

    private static string StatementName(SequenceStatement statement) => statement switch
    {
        SequenceMessage => "message", SequenceActivation => "activation", SequenceNote => "note",
        SequenceBlock => "fragment", SequenceNumbering => "numbering", _ => "item",
    };
}
