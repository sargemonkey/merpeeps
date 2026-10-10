using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class AgentEventDiagramTests
{
    [Fact]
    public void CollapsedFlowsRetargetVisibleEdgesWithoutDeletingTheirChildren()
    {
        const string source = "agentflow-beta TB\ninput[\"Input\"]@{ shape: input }\nflow work[\"Work\"]\na[\"Analyze\"]@{ shape: task }\nb[\"Tool\"]@{ shape: tool }\na --> b\nend\nwork@{ view: collapsed, custom: { keep: true } }\ninput --> a\nb --> output[\"Output\"]@{ shape: action }\n";
        var adapter = new AgentFlowDiagramAdapter(); var model = adapter.Parse(source); var d = model.AgentFlow!;
        Assert.Empty(model.Nodes); Assert.Equal(5, d.Elements.Count); Assert.Equal(3, d.Links.Count);
        var flow = d.Elements.Single(e => e.SourceId == "work"); var a = d.Elements.Single(e => e.SourceId == "a");
        var layout = AgentFlowDiagramLayout.Create(model);
        Assert.Equal(flow.Id, layout.VisibleEndpoints[a.Id]);
        Assert.DoesNotContain(layout.Scene.Regions, r => r.Owner.Id == a.Id);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void GlobalElementsAndMetadataRetainTheirDeclaredOwnership()
    {
        var model = new AgentFlowDiagramAdapter().Parse("agentflow-beta\nglobal\nshared[\"Shared\"]@{ shape: refdoc, custom: { value: 3, constructor: bad } }\nend\nflow work\nrun --> shared\nend\n");
        var shared = model.AgentFlow!.Elements.Single(e => e.SourceId == "shared");
        Assert.True(shared.Global); Assert.Null(shared.ParentId);
        Assert.Equal(3, shared.Metadata["custom"]!["value"]!.GetValue<int>());
        Assert.Null(shared.Metadata["custom"]!["constructor"]);
    }
    [Fact]
    public void EventFramesKeepOccurrenceOrderAndDeriveRelationsFromDifferentLanes()
    {
        var model = new EventModelingAdapter().Parse("eventmodeling\ntf 03 evt ItemAdded\ntf 02 rmo CartItems\ntf 04 ui CartUI\nrf 05 evt External.Change\ntf 06 pcr Processor\n");
        var d = model.EventModeling!;
        Assert.Equal(new[] { "03", "02", "04", "05", "06" }, d.Frames.Select(f => f.Number));
        var relations = EventModelingSemantics.Relations(d);
        Assert.Equal(3, relations.Count);
        Assert.DoesNotContain(relations, r => r.To == d.Frames[3].Id);
        Assert.Equal(d.Frames[3].Id, relations[^1].From);
        Assert.Empty(model.Nodes);
    }
    [Fact]
    public void ExplicitEventReferencesPointIntoCurrentFrameAndKeepDataInert()
    {
        const string source = "eventmodeling\nrf 02 evt ItemAdded\nrf 03 evt ItemRemoved\ntf 01 rmo Cart ->> 02 ->> 03\ndata sample `html`{ <script>ignored()</script> }\n";
        var adapter = new EventModelingAdapter(); var model = adapter.Parse(source); var d = model.EventModeling!;
        Assert.All(EventModelingSemantics.Relations(d), r => Assert.Equal(d.Frames[2].Id, r.To));
        Assert.Equal(EventDataKind.Html, d.DataBlocks[0].Data.Kind);
        Assert.Contains("<script>", d.DataBlocks[0].Data.Source);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void AgentAndEventActualSavePreservesCanonicalIdsAndInertPayloads()
    {
        const string source = "agentflow-beta\nflow f\nA[\"Task\"]@{shape:task}\nend\n";
        var model = MerpeepsDocument.Load(source); var node = model.AgentFlow!.Elements.Single(e => e.SourceId == "A");
        new EditHistory(model).Apply(AgentFlowEditing.UpdateElement(node with { Label = "Updated" }));
        Assert.Equal(node.Id, MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).AgentFlow!.Elements.Single(e => e.SourceId == "A").Id);
        const string events = "eventmodeling\ntf 02 evt Event\ntf 01 rmo View\n";
        var eventModel = MerpeepsDocument.Load(events); var frame = eventModel.EventModeling!.Frames[0];
        new EditHistory(eventModel).Apply(EventModelingEditing.UpdateFrame(frame with { Number = "03" }));
        Assert.Equal(frame.Id, MerpeepsDocument.Load(MerpeepsDocument.Save(events, eventModel)).EventModeling!.Frames[0].Id);
    }
    [Fact]
    public void NonDefaultDataKindCannotDisappearWhenExplicitFlagWasOmitted()
    {
        var d = new EventModelingDocument();
        d.DataBlocks.Add(new("data", "example", new(EventDataKind.Html, "<b>inert</b>")));
        var source = EventModelingAdapter.Write(d);
        Assert.Contains("`html`", source);
        Assert.Equal(EventDataKind.Html, new EventModelingAdapter().Parse(source).EventModeling!.DataBlocks.Single().Data.Kind);
    }
    [Fact]
    public void AgentflowYamlShapeRolesReachDistinctNativeGeometry()
    {
        var model = new AgentFlowDiagramAdapter().Parse("agentflow-beta\nA[\"Task\"]@{ shape: task }\nB[\"Tool\"]@{ shape: tool }\nC[\"Input\"]@{ shape: input }\n");
        var scene = AgentFlowDiagramLayout.Create(model).Scene; var d = model.AgentFlow!;
        Assert.Contains(scene.Figures, f => f.Id == d.Elements[0].Id && f.Kind == DrawingFigureKind.Path);
        Assert.Contains(scene.Figures, f => f.Id == d.Elements[1].Id + "-left");
        Assert.Contains(scene.Figures, f => f.Id == d.Elements[2].Id && f.Kind == DrawingFigureKind.Polygon);
    }
}
