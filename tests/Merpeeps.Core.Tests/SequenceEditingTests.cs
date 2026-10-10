using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class SequenceEditingTests
{
    [Fact]
    public void NativeCreationUsesOrderedParticipantsAndMessageCommands()
    {
        var draft = MermaidDiagramCatalog.CreateEmpty("sequence");
        Assert.Empty(draft.Model.Nodes); Assert.Empty(draft.Model.Edges);
        Assert.False(draft.Capabilities.CanLayout);
        var history = new EditHistory(draft.Model);
        history.Apply(SequenceEditing.AddParticipant(new("A", SequenceParticipantKind.Actor)));
        history.Apply(SequenceEditing.AddParticipant(new("B", Alias: "Service")));
        history.Apply(SequenceEditing.Insert(new SequenceMessage("message", "A", "B", "Request")));
        Assert.Equal("message", draft.Model.Sequence!.Statements.OfType<SequenceMessage>().Single().Id);
        var saved = MerpeepsDocument.Save(draft.Source, draft.Model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(new[] { "A", "B" }, reopened.Sequence!.Participants.Select(p => p.Id));
        Assert.Equal("message", reopened.Sequence.Statements.OfType<SequenceMessage>().Single().Id);
        Assert.Contains("Request", SvgExporter.ToSvg(reopened));
        history.Undo();
        Assert.Empty(draft.Model.Sequence!.Statements.OfType<SequenceMessage>());
        history.Redo();
        Assert.Equal("Request", draft.Model.Sequence.Statements.OfType<SequenceMessage>().Single().Text);
    }

    [Fact]
    public void CreationIsAtomicAndReorderFailureLeavesLiveSequenceUntouched()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\nparticipant A\nA->>A: Existing\n");
        var history = new EditHistory(model);
        var command = SequenceEditing.CreateParticipantOnMessage(new("B", SequenceParticipantKind.Actor),
            new("create", "A", "B", "Created"));
        history.Apply(command);
        Assert.Equal("B", model.Sequence!.Statements.OfType<SequenceMessage>().Single(m => m.CreateParticipantId is not null).CreateParticipantId);
        var before = model.Sequence;
        Assert.Throws<ArgumentException>(() => history.Apply(SequenceEditing.MoveParticipant("B", 0)));
        Assert.Same(before, model.Sequence);
        history.Undo();
        Assert.Single(model.Sequence!.Participants);
        Assert.DoesNotContain(model.Sequence.Statements, s => s.Id == "create");
    }

    [Fact]
    public void FragmentBranchEditsKeepInitialConditionAndChildIds()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\nparticipant A\nparticipant B\n");
        var history = new EditHistory(model);
        var block = SequenceEditing.NewBlock(SequenceBlockKind.Alt, "First");
        history.Apply(SequenceEditing.Insert(block));
        history.Apply(SequenceEditing.Insert(new SequenceMessage("inside", "A", "B", "One"), block.Branches[0].Id));
        var addBranch = SequenceEditing.AddBranch(block.Id, "Second");
        history.Apply(addBranch);
        history.Apply(SequenceEditing.Insert(new SequenceMessage("other", "B", "A", "Two"), addBranch.ResultId));
        history.Apply(SequenceEditing.MoveBranch(addBranch.ResultId!, 0));
        var edited = (SequenceBlock)model.Sequence!.Statement(block.Id);
        Assert.Equal("Second", edited.Text);
        Assert.Equal("", edited.Branches[0].Text);
        Assert.Equal("First", edited.Branches[1].Text);
        var saved = MerpeepsDocument.Save("sequenceDiagram\n", model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(block.Id, reopened.Sequence!.Statements.OfType<SequenceBlock>().Single().Id);
        Assert.Equal("inside", SequenceEditing.Locate(reopened.Sequence, "inside").Statement.Id);
        history.Undo();
        Assert.Equal("First", ((SequenceBlock)model.Sequence!.Statement(block.Id)).Text);
    }

    [Fact]
    public void MessageEditPatchesOnlyItsLineAndStableMetadataRoundtrips()
    {
        const string source = "sequenceDiagram\r\n%% keep\r\nA->>B: Before\r\nB-->>A: Reply\r\n";
        var model = MerpeepsDocument.Load(source);
        var message = model.Sequence!.Statements.OfType<SequenceMessage>().First();
        var history = new EditHistory(model);
        history.Apply(SequenceEditing.Update(message.Id, message with { Text = "After" }));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(MerpeepsDoc.BodyOf(source).Replace("Before", "After"), MerpeepsDoc.BodyOf(saved));
        Assert.Equal(message.Id, MerpeepsDocument.Load(saved).Sequence!.Statements.OfType<SequenceMessage>().First().Id);
    }

    [Theory]
    [InlineData("alt", "else")]
    [InlineData("par", "and")]
    [InlineData("par_over", "and")]
    [InlineData("critical", "option")]
    public void SecondaryBranchConditionSurvivesSourcePreservingSaveUndoAndRedo(string opening, string separator)
    {
        var source = $"sequenceDiagram\r\n%% keep\r\nparticipant A\r\nparticipant B\r\n{opening} First\r\nA->>B: One\r\n{separator} Second\r\nB->>A: Two\r\nend\r\n";
        var model = MerpeepsDocument.Load(source);
        var block = model.Sequence!.Statements.OfType<SequenceBlock>().Single();
        var branch = block.Branches[1];
        var history = new EditHistory(model);
        history.Apply(SequenceEditing.SetBranchText(branch.Id, "Changed"));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Contains(separator + " Changed\r\n", saved);
        Assert.Contains("%% keep", saved);
        var reopened = MerpeepsDocument.Load(saved).Sequence!.Statements.OfType<SequenceBlock>().Single();
        Assert.Equal(branch.Id, reopened.Branches[1].Id);
        Assert.Equal("Changed", reopened.Branches[1].Text);
        Assert.Equal(branch.Statements.Select(s => s.Id), reopened.Branches[1].Statements.Select(s => s.Id));
        history.Undo();
        var undone = MerpeepsDocument.Save(saved, model);
        Assert.Equal("Second", MerpeepsDocument.Load(undone).Sequence!.Statements.OfType<SequenceBlock>().Single().Branches[1].Text);
        history.Redo();
        Assert.Equal("Changed", MerpeepsDocument.Load(MerpeepsDocument.Save(undone, model)).Sequence!.Statements.OfType<SequenceBlock>().Single().Branches[1].Text);
    }

    [Fact]
    public void MovingAcrossBranchesPreservesOwnershipEvenWhenStatementOrderIsUnchanged()
    {
        const string source = "sequenceDiagram\nparticipant A\nparticipant B\nalt First\nA->>B: One\nelse Second\nB->>A: Two\nend\n";
        var model = MerpeepsDocument.Load(source);
        var block = model.Sequence!.Statements.OfType<SequenceBlock>().Single();
        var message = block.Branches[0].Statements.Single();
        new EditHistory(model).Apply(SequenceEditing.Move(message.Id, block.Branches[1].Id, 0));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Sequence!.Statements.OfType<SequenceBlock>().Single();
        Assert.Empty(reopened.Branches[0].Statements);
        Assert.Equal(new[] { message.Id, block.Branches[1].Statements.Single().Id }, reopened.Branches[1].Statements.Select(s => s.Id));
    }

    [Fact]
    public void EmptyBranchAdditionAndRemovalPersistWithoutChangingStatementOrder()
    {
        const string source = "sequenceDiagram\nalt First\nA->>B: One\nend\n";
        var model = MerpeepsDocument.Load(source);
        var block = model.Sequence!.Statements.OfType<SequenceBlock>().Single();
        var history = new EditHistory(model);
        var add = SequenceEditing.AddBranch(block.Id, "Second");
        history.Apply(add);
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(2, MerpeepsDocument.Load(saved).Sequence!.Statements.OfType<SequenceBlock>().Single().Branches.Count);
        history.Apply(SequenceEditing.RemoveBranch(add.ResultId!));
        Assert.Single(MerpeepsDocument.Load(MerpeepsDocument.Save(saved, model)).Sequence!.Statements.OfType<SequenceBlock>().Single().Branches);
    }

    [Fact]
    public void RemovingRequiredActivationDoesNotCommitInvalidSequence()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\nA->>+B: Start\nB-->>-A: End\n");
        var sequence = model.Sequence!;
        var start = sequence.Statements.OfType<SequenceMessage>().First();
        var history = new EditHistory(model);
        Assert.Throws<SequenceNotEditableException>(() => history.Apply(SequenceEditing.Remove(start.Id)));
        Assert.Same(sequence, model.Sequence);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void EmptyBoxesAndAssignedParticipantsPersistWithoutFakeActors()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\n");
        var history = new EditHistory(model);
        history.Apply(SequenceEditing.AddBox(new("box", "Services", "#336699")));
        var empty = MerpeepsDocument.Save("sequenceDiagram\n", model);
        Assert.Contains("box rgb(51,102,153) Services", empty);
        Assert.Empty(MerpeepsDocument.Load(empty).Sequence!.Participants);
        Assert.Contains(SequenceDiagramLayout.Create(model).Figures, f => f.OwnerId == "box");
        history.Apply(SequenceEditing.AddParticipant(new("A")));
        history.Apply(SequenceEditing.AssignBox("A", "box"));
        var saved = MerpeepsDocument.Save(empty, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal("box", reopened.Sequence!.Participant("A").BoxId);
    }

    [Fact]
    public void ProjectionThemeOverrideAndNoteAlignmentDoNotMutateTheDocument()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\nNote over A,B: Long line<br/>Short\n");
        model.Theme = "dark";
        model.Sequence!.Options = model.Sequence.Options with { NoteAlign = "right" };
        var scene = SequenceDiagramLayout.Create(model, themeOverride: "default");
        Assert.Equal("dark", model.Theme);
        Assert.Equal(new ClassColor(255, 255, 255), scene.Figures[0].Fill);
        Assert.Equal("right", scene.Labels.Single(l => l.ElementKind == SequenceElementKind.Note).Alignment);
    }

    [Fact]
    public void GenericGraphOperationsCannotSilentlyMutateASequence()
    {
        var model = MerpeepsDocument.Load("sequenceDiagram\nA->>B: Request\n");
        var history = new EditHistory(model);
        var result = OperationApplier.Apply(model, history, [new DiagramOperation { Op = "addNode", Id = "C" }]);
        Assert.False(result.Ok);
        Assert.Empty(model.Nodes);
        Assert.Throws<NotSupportedException>(() => LayoutEngines.Apply(model, "layered"));
        var saved = MerpeepsDocument.SavePositions("sequenceDiagram\nA->>B: Request\n", model);
        Assert.DoesNotContain("layout:", saved);
        Assert.Throws<ArgumentException>(() => MerpeepsDocument.SavePositions("sequenceDiagram\n", new DiagramModel()));
        Assert.Throws<ArgumentException>(() => SvgExporter.ToSvg(new DiagramModel { Kind = "sequenceDiagram" }));
    }
}
