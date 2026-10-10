using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class SequenceDiagramTests
{
    private readonly SequenceDiagramAdapter _adapter = new();

    [Fact]
    public void EmptySequenceIsNotAnInventedGraph()
    {
        var model = _adapter.Parse("sequenceDiagram\n");
        Assert.NotNull(model.Sequence);
        Assert.Empty(model.Nodes); Assert.Empty(model.Edges);
        Assert.Empty(model.Sequence.Participants); Assert.Empty(model.Sequence.Statements);
        Assert.Equal("sequenceDiagram\n", _adapter.Write(model));
    }

    [Fact]
    public void ParticipantsRetainOrderStereotypesAliasPrecedenceAndUnknownConfiguration()
    {
        const string source = """
            sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Internal", "future": 17 } as Public API
            actor DB@{ "type": "database" } as Database
            API->>DB: Request
            DB-->>API: Response
            """;
        var model = _adapter.Parse(source).Sequence!;
        Assert.Equal(new[] { "API", "DB" }, model.Participants.Select(p => p.Id));
        Assert.Equal("Public API", model.Participants[0].Display);
        Assert.Equal(SequenceParticipantShape.Boundary, model.Participants[0].Shape);
        Assert.Equal(17, model.Participants[0].Configuration["future"]!.GetValue<int>());
        Assert.Equal("Internal", model.Participants[0].Configuration["alias"]!.GetValue<string>());
        Assert.Equal(SequenceParticipantKind.Actor, model.Participants[1].Kind);
        var written = SequenceDiagramAdapter.Write(model);
        Assert.Equal(written, _adapter.Write(_adapter.Parse(written)));
        Assert.Equal(17, _adapter.Parse(written).Sequence!.Participants[0].Configuration["future"]!.GetValue<int>());
    }

    [Fact]
    public void EveryGrammarArrowRemainsDistinctThroughRoundtrip()
    {
        Assert.Equal(26, SequenceArrows.All.Count);
        Assert.Equal(26, SequenceArrows.All.Select(a => a.Syntax).Distinct().Count());
        foreach (var arrow in SequenceArrows.All)
        {
            var source = $"sequenceDiagram\nAlice{arrow.Syntax}Bob: {arrow.Id}\n";
            var parsed = _adapter.Parse(source);
            Assert.Equal(arrow.Id, Assert.IsType<SequenceMessage>(parsed.Sequence!.Statements.Single()).ArrowId);
            var reopened = _adapter.Parse(_adapter.Write(parsed));
            Assert.Equal(arrow.Id, reopened.Sequence!.Statements.OfType<SequenceMessage>().Single().ArrowId);
        }
    }

    [Fact]
    public void ActivationAndCentralConnectionFlagsAreNotConflated()
    {
        var parsed = _adapter.Parse("sequenceDiagram\nA->>+B: begin\nB-->>-A: return\nA()->>()B: center\n").Sequence!;
        var messages = parsed.Statements.Cast<SequenceMessage>().ToArray();
        Assert.True(messages[0].ActivateTarget);
        Assert.True(messages[1].DeactivateSource);
        Assert.True(messages[2].CentralSource); Assert.True(messages[2].CentralTarget);
        Assert.False(messages[2].ActivateTarget);
        Assert.Empty(SequenceValidation.Validate(parsed));
        Assert.Throws<SequenceNotEditableException>(() => _adapter.Parse("sequenceDiagram\nA->>()B: center\ndeactivate B\n"));
        Assert.NotNull(_adapter.Parse("sequenceDiagram\nA->>+B: remains active\n"));
    }

    [Fact]
    public void NestedFragmentsHaveBranchesAndOrderedChildren()
    {
        const string source = """
            sequenceDiagram
            alt allowed
              A->>B: Request
              par first
                B->>C: One
              and second
                B->>D: Two
              end
            else denied
              Note over A,B: Not allowed
            end
            """;
        var parsed = _adapter.Parse(source).Sequence!;
        var alt = Assert.IsType<SequenceBlock>(parsed.Statements.Single());
        Assert.Equal(SequenceBlockKind.Alt, alt.Kind);
        Assert.Equal(2, alt.Branches.Count);
        Assert.Equal("denied", alt.Branches[1].Text);
        var parallel = Assert.IsType<SequenceBlock>(alt.Branches[0].Statements[1]);
        Assert.Equal(SequenceBlockKind.Par, parallel.Kind);
        Assert.Equal(2, parallel.Branches.Count);
        Assert.Equal(new[] { "Request", "One", "Two" }, SequenceDiagramModel.Walk(parsed.Statements).OfType<SequenceMessage>().Select(m => m.Text));
        var saved = SequenceDiagramAdapter.Write(parsed);
        Assert.Equal(saved, _adapter.Write(_adapter.Parse(saved)));
    }

    [Fact]
    public void CreationAndDestructionBelongToTheirMessage()
    {
        const string source = """
            sequenceDiagram
            A->>B: Existing
            create actor C as Customer
            B->>C: Created
            destroy B
            B-->>A: Leaving
            """;
        var parsed = _adapter.Parse(source).Sequence!;
        var messages = parsed.Statements.OfType<SequenceMessage>().ToArray();
        Assert.Equal("C", messages[1].CreateParticipantId);
        Assert.Equal("B", messages[2].DestroyParticipantId);
        Assert.Equal(SequenceParticipantKind.Actor, parsed.Participant("C").Kind);
        Assert.Equal(SequenceDiagramAdapter.Write(parsed), _adapter.Write(_adapter.Parse(SequenceDiagramAdapter.Write(parsed))));
        Assert.Throws<FormatException>(() => _adapter.Parse("sequenceDiagram\nparticipant B\ncreate participant B\nA->>B: again\n"));
        Assert.Throws<SequenceNotEditableException>(() => _adapter.Parse("sequenceDiagram\ncreate participant B\nB->>A: wrong endpoint\n"));
    }

    [Fact]
    public void EscapesNumbersBoxesAndInertLinksRetainTheirMeaning()
    {
        const string source = """
            sequenceDiagram
            box hsla(10, 40%, 90%, 0.5) Services
              participant A
              participant B as Bee<br/>Service
            end
            autonumber 2.25 0.25
            A->>B: I #9829; you #59; twice
            links A: {"Docs":"https://example.invalid","extra":{"unknown":true}}
            """;
        var parsed = _adapter.Parse(source).Sequence!;
        Assert.Equal("Bee\nService", parsed.Participant("B").Display);
        Assert.Equal("I \u2665 you ; twice", parsed.Statements.OfType<SequenceMessage>().Single().Text);
        Assert.Equal(.25m, parsed.Statements.OfType<SequenceNumbering>().Single().Step);
        Assert.Equal("hsla(10, 40%, 90%, 0.5)", parsed.Boxes.Single().Color);
        var saved = SequenceDiagramAdapter.Write(parsed);
        Assert.Contains("#59;", saved);
        Assert.Equal(saved, _adapter.Write(_adapter.Parse(saved)));
    }

    [Fact]
    public void NotesAndOptionsRetainRoleSpecificFonts()
    {
        const string source = """
            ---
            title: Retained
            config:
              sequence:
                mirrorActors: true
                actorFontSize: 17
                messageFontSize: 19
                noteFontFamily: Arial
                future: kept
            ---
            sequenceDiagram
            Note left of A: First<br/>Second
            A->>B: Hello
            """;
        var parsed = _adapter.Parse(source);
        Assert.True(parsed.Sequence!.Options.MirrorActors);
        Assert.Equal(17, parsed.Sequence.Options.ActorFont.Size);
        Assert.Equal(19, parsed.Sequence.Options.MessageFont.Size);
        Assert.Equal("Arial", parsed.Sequence.Options.NoteFont.Family);
        var note = parsed.Sequence.Statements.OfType<SequenceNote>().Single();
        Assert.Equal("First\nSecond", note.Text);
        Assert.Equal(SequenceNotePlacement.Left, note.Placement);
    }

    [Fact]
    public void NativePolicyRejectionRetainsParsedSourceSemanticsAndCodes()
    {
        const string source = "sequenceDiagram\nA->>B: First\ndestroy B\nA-xB: End\nB->>A: Later\n";
        var result = _adapter.ReadSource(source);
        Assert.False(result.CanEdit);
        Assert.Equal(3, result.Model.Statements.OfType<SequenceMessage>().Count());
        Assert.Contains(result.Diagnostics, d => d.Code == "participant-lifetime" && d.Kind == SequenceDiagnosticKind.NativeUnsupported);
        var exception = Assert.Throws<SequenceNotEditableException>(() => _adapter.Parse(source));
        Assert.NotEmpty(exception.Result.Diagnostics);
    }

    [Fact]
    public void FirstBranchCannotSilentlyLoseACondition()
    {
        var model = _adapter.Parse("sequenceDiagram\nalt condition\nA->>B: Text\nend\n").Sequence!;
        var block = (SequenceBlock)model.Statements[0];
        model.Statements[0] = block with { Branches = new[] { block.Branches[0] with { Text = "must not vanish" } } };
        Assert.Contains(SequenceValidation.Validate(model), d => d.Code == "first-branch-label");
        Assert.Throws<ArgumentException>(() => SequenceDiagramAdapter.Write(model));
    }

    [Theory]
    [InlineData("literal #59; & <br/> <script>")]
    [InlineData("First\nSecond \u2665;")]
    public void EncodeDecodeIsNotASecondMarkupInterpretation(string text) =>
        Assert.Equal(text, SequenceText.Decode(SequenceText.Encode(text)));
}
