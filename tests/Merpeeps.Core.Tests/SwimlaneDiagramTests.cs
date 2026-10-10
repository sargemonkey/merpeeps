using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class SwimlaneDiagramTests
{
    private const string Source = """
        swimlane-beta LR
        subgraph Customer
          request[Request service]
          receive[Receive update]
        end
        subgraph Support
          triage{Known issue?}
          answer[Send answer]
        end
        request --> triage
        triage -->|Yes| answer
        answer --> receive
        classDef attention fill:#fff2cc,stroke:#d6a500,color:#111
        class triage attention
        """;
    [Fact]
    public void ResponsibilityLanesDriveActualPlacementAndSourceOwnership()
    {
        var adapter = new SwimlaneDiagramAdapter(); var model = adapter.Parse(Source); var d = model.Swimlane!;
        Assert.Empty(model.Nodes); Assert.Equal(2, d.Groups.Count);
        var layout = SwimlaneDiagramLayout.Create(model);
        Assert.Equal(2, layout.Lanes.Count);
        foreach (var node in d.Nodes)
        {
            var lane = layout.Lanes[d.LaneOf(node.Id)!]; var rect = layout.Nodes[node.Id];
            Assert.True(rect.X >= lane.X && rect.Y >= lane.Y && rect.X + rect.Width <= lane.X + lane.Width && rect.Y + rect.Height <= lane.Y + lane.Height);
        }
        Assert.True(layout.Lanes[d.Groups[0].Id].Y < layout.Lanes[d.Groups[1].Id].Y);
        Assert.Equal(3, layout.Routes.Count);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void MovingStepsChangesOwnershipAndUndoRestoresIt()
    {
        var model = new SwimlaneDiagramAdapter().Parse(Source); var d = model.Swimlane!; var node = d.Nodes[0];
        var history = new EditHistory(model); history.Apply(SwimlaneEditing.UpdateNode(node with { GroupId = d.Groups[1].Id }));
        Assert.Equal(d.Groups[1].Id, model.Swimlane!.LaneOf(node.Id));
        history.Undo(); Assert.Same(d, model.Swimlane);
    }
    [Fact]
    public void NestedGroupsDoNotBecomeExtraResponsibilityLanes()
    {
        var model = new SwimlaneDiagramAdapter().Parse("swimlane-beta TB\nsubgraph Team\nsubgraph Work\nA[Do work]\nend\nend\n");
        Assert.Equal(model.Swimlane!.Groups[0].Id, model.Swimlane.Groups[1].ParentId);
        Assert.Single(SwimlaneDiagramLayout.Create(model).Lanes);
    }
    [Fact]
    public void LaneAndStepIdentityRoundtripAfterOwnershipChanges()
    {
        var model = MerpeepsDocument.Load(Source); var d = model.Swimlane!; var step = d.Nodes[0];
        new EditHistory(model).Apply(SwimlaneEditing.UpdateNode(step with { GroupId = d.Groups[1].Id }));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(Source, model));
        Assert.Equal(step.Id, reopened.Swimlane!.Nodes.Single(n => n.SourceId == step.SourceId).Id);
        Assert.Equal(d.Groups[1].Id, reopened.Swimlane.LaneOf(step.Id));
    }
}
