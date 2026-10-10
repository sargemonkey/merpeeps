using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class SequenceLayoutTests
{
    private static DiagramModel Parse(string source) => new SequenceDiagramAdapter().Parse(source);
    private static SequenceTextSize Measure(string text, SequenceFont font) =>
        new(text.Split('\n').Max(l => l.Length) * font.Size * .65, text.Split('\n').Length * font.Size * 1.4);

    [Fact]
    public void LayoutContainsLifelinesActivationsMessagesAndNotesWithoutChangingTheTree()
    {
        const string source = """
            sequenceDiagram
            participant A
            participant B
            A->>+B: Request
            note right of B: First<br/>Second
            B-->>-A: Response
            """;
        var model = Parse(source);
        var before = SequenceDiagramAdapter.Write(model.Sequence!);
        var scene = SequenceDiagramLayout.Create(model, Measure);
        Assert.False(scene.UsesApproximateTextMetrics);
        Assert.Contains(scene.Figures, f => f.Id.EndsWith("-lifeline"));
        Assert.Contains(scene.Figures, f => f.ElementKind == SequenceElementKind.Activation);
        Assert.Contains(scene.Labels, l => l.Text == "First\nSecond" && l.ElementKind == SequenceElementKind.Note);
        Assert.Contains(scene.Regions, r => r.Kind == SequenceElementKind.Message);
        Assert.Equal(before, SequenceDiagramAdapter.Write(model.Sequence!));
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
    }

    [Fact]
    public void LifecycleDeterminesHeaderAndLifelineExtents()
    {
        var model = Parse("sequenceDiagram\nA->>B: Start\ncreate actor C\nA->>C: Create\ndestroy B\nB-->>A: Bye\n");
        model.Sequence!.Options = model.Sequence.Options with { MirrorActors = true };
        var scene = SequenceDiagramLayout.Create(model, Measure);
        var initial = scene.Regions.First(r => r.OwnerId == "A" && r.Kind == SequenceElementKind.Participant);
        var created = scene.Regions.Where(r => r.OwnerId == "C" && r.Kind == SequenceElementKind.Participant).OrderBy(r => r.Bounds.Y).First();
        Assert.True(created.Bounds.Y > initial.Bounds.Y);
        Assert.Contains(scene.Figures, f => f.Id == "A-mirror");
        Assert.DoesNotContain(scene.Figures, f => f.Id == "B-mirror");
        Assert.Contains(scene.Figures, f => f.Id.Contains("-destroy"));
    }

    [Fact]
    public void NestedFragmentsAndParticipantBoxesHaveDifferentRoles()
    {
        var model = Parse("sequenceDiagram\nbox Purple Team\nparticipant A\nparticipant B\nend\nalt condition\nloop repeat\nA->>B: Work\nend\nelse other\nB->>A: Fallback\nend\n");
        var scene = SequenceDiagramLayout.Create(model, Measure);
        Assert.Contains(scene.Regions, r => r.Kind == SequenceElementKind.ParticipantBox);
        Assert.Equal(2, scene.Regions.Count(r => r.Kind == SequenceElementKind.Fragment));
        Assert.Single(scene.Regions, r => r.Kind == SequenceElementKind.Branch);
        Assert.Contains(scene.Labels, l => l.Text.Contains("condition"));
        Assert.Contains(scene.Labels, l => l.Text.Contains("other"));
    }

    [Fact]
    public void SelfMessagesAndCentralConnectionsUseTheirOwnGeometry()
    {
        var model = Parse("sequenceDiagram\nA()->>()A: Self\n");
        var message = model.Sequence!.Statements.OfType<SequenceMessage>().Single();
        var scene = SequenceDiagramLayout.Create(model, Measure);
        var route = scene.Figures.Single(f => f.Id == message.Id + "-line");
        Assert.Equal(4, route.Points.Count);
        Assert.Equal(2, scene.Figures.Count(f => f.Id.Contains("-central-")));
        Assert.DoesNotContain(scene.Figures, f => f.ElementKind == SequenceElementKind.Activation);
    }

    [Fact]
    public void RealMeasurementReceivesRoleSpecificWeightAndBadResultsFail()
    {
        var model = Parse("sequenceDiagram\nparticipant A\nA->>B: Message\nNote over A,B: Note\n");
        model.Sequence!.Options = model.Sequence.Options with
        {
            ActorFont = new("Segoe UI", 19, 500),
            MessageFont = new("Consolas", 16, 700),
            NoteFont = new("Arial", 15, 400),
        };
        var fonts = new HashSet<SequenceFont>();
        var scene = SequenceDiagramLayout.Create(model, (text, font) => { fonts.Add(font); return Measure(text, font); });
        Assert.Contains(new SequenceFont("Segoe UI", 19, 500), fonts);
        Assert.Contains(new SequenceFont("Consolas", 16, 700), fonts);
        Assert.Contains(new SequenceFont("Arial", 15, 400), fonts);
        Assert.False(scene.UsesApproximateTextMetrics);
        Assert.Throws<ArgumentException>(() => SequenceDiagramLayout.Create(model, (_, _) => new(double.NaN, 10)));
    }
}
