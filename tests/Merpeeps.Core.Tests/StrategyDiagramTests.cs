using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class StrategyDiagramTests
{
    [Fact]
    public void WardleyCoordinatesAndPipelineInheritanceStaySemantic()
    {
        const string source = """
            wardley-beta
            title Strategy
            anchor User [0.9,0.8]
            component Database [0.4,0.6] (buy) (inertia)
            pipeline Database {
              component "File System" [0.25]
            }
            User -> Database
            evolve Database 0.85
            annotation 1,[0.6,0.65] "Critical"
            annotations [0.1,0.9]
            """;
        var adapter = new WardleyDiagramAdapter(); var model = adapter.Parse(source); var d = model.Wardley!;
        Assert.Empty(model.Nodes);
        Assert.Equal(.9, d.Components[0].Visibility);
        Assert.Equal(.8, d.Components[0].Evolution);
        Assert.Equal(.4, d.VisibilityOf(d.Components[2].Id));
        Assert.Null(d.Components[2].Visibility);
        Assert.Equal(.65, d.Annotations[0].Evolution);
        Assert.Equal(new DiagramPoint(.9,.1), d.AnnotationBox);
        var written = adapter.Write(model);
        Assert.Equal(written, adapter.Write(adapter.Parse(written)));
    }
    [Fact]
    public void CauseHierarchyAndProblemAreNotGenericGraphNodes()
    {
        var model = new IshikawaDiagramAdapter().Parse("ishikawa-beta\nBlurry Photo\nEquipment\n    Lens\n        Dirty\nUser\n    Shaky hands\n");
        Assert.Empty(model.Nodes);
        Assert.Equal("Blurry Photo", model.Ishikawa!.Problem);
        Assert.Equal("Dirty", model.Ishikawa.Causes[0].Children[0].Children[0].Label);
        Assert.Equal(2, model.Ishikawa.Causes.Count);
    }
    [Fact]
    public void CynefinItemsAndDomainTransitionsRemainDistinct()
    {
        var adapter = new CynefinDiagramAdapter();
        var model = adapter.Parse("cynefin-beta\ncomplex\n\"Experiment\"\nclear\n\"Procedure\"\ncomplex --> clear : \"Learned\"\n");
        Assert.Empty(model.Nodes);
        Assert.Equal(CynefinDomain.Complex, model.Cynefin!.Items[0].Domain);
        Assert.Equal(CynefinDomain.Clear, model.Cynefin.Transitions.Single().To);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void WardleyMapTransformUsesEvolutionXAndVisibilityY()
    {
        var model = new WardleyDiagramAdapter().Parse("wardley-beta\ncomponent A [0.8,0.2]\ncomponent B [0.3,0.7]\nA +> B\nevolve A 0.6\n");
        var layout = StrategyDiagramLayouts.Wardley(model);
        var d = model.Wardley!;
        var a = layout.Coordinates.ToPoint(.8, .2); var b = layout.Coordinates.ToPoint(.3, .7);
        Assert.True(a.X < b.X && a.Y < b.Y);
        var inverse = layout.Coordinates.FromPoint(a);
        Assert.Equal(.2, inverse.X, 8); Assert.Equal(.8, inverse.Y, 8);
        Assert.Contains(layout.Scene.Figures, f => f.Id.EndsWith("-evolve") && f.Dashed);
        Assert.Equal(d.Components[0].Id, DrawingHitTest.Hit(layout.Scene, a)!.Value.Id);
        var history = new EditHistory(model); history.Apply(WardleyEditing.MoveComponent(d.Components[0].Id, .6, .4));
        Assert.Equal(.4, model.Wardley!.Components[0].Evolution);
        Assert.Equal(.6, model.Wardley.Components[0].Visibility);
        history.Undo(); Assert.Same(d, model.Wardley);
    }
    [Fact]
    public void PipelineMovesPreserveInheritedVisibilityAndRejectInvalidCandidate()
    {
        var model = new WardleyDiagramAdapter().Parse("wardley-beta\ncomponent Parent [0.4,0.5]\npipeline Parent {\ncomponent Child [0.2]\n}\n");
        var original = model.Wardley!; var child = original.Components[1];
        var history = new EditHistory(model);
        history.Apply(WardleyEditing.MoveComponent(child.Id, .9, .7));
        Assert.Null(model.Wardley!.Components[1].Visibility);
        Assert.Equal(.4, model.Wardley.VisibilityOf(child.Id));
        var before = model.Wardley;
        Assert.Throws<ArgumentException>(() => history.Apply(WardleyEditing.MoveComponent(child.Id, .4, 1.2)));
        Assert.Same(before, model.Wardley);
    }
    [Fact]
    public void CauseTreeEditsAndFishboneDrawingAreNonGraph()
    {
        var model = new IshikawaDiagramAdapter().Parse("ishikawa-beta\nProblem\nEquipment\n  Sensor\nUser\n");
        var original = model.Ishikawa!; var sensor = original.Causes[0].Children[0]; var user = original.Causes[1];
        var history = new EditHistory(model);
        history.Apply(IshikawaEditing.MoveCause(sensor.Id, user.Id, 0));
        Assert.Equal(sensor.Id, model.Ishikawa!.Causes[1].Children[0].Id);
        var scene = StrategyDiagramLayouts.Ishikawa(model).Scene;
        Assert.Contains(scene.Figures, f => f.Id == "spine");
        Assert.Contains(scene.Figures, f => f.Id == sensor.Id + "-bone");
        Assert.Contains(scene.Labels, l => l.Text == "Problem");
        Assert.Empty(model.Nodes);
        var before = model.Ishikawa;
        Assert.Throws<ArgumentException>(() => history.Apply(IshikawaEditing.MoveCause(user.Id, sensor.Id, 0)));
        Assert.Same(before, model.Ishikawa);
        history.Undo(); Assert.Same(original, model.Ishikawa);
    }
    [Fact]
    public void CynefinRendersAllDomainsAndRetainsHiddenConfusionItems()
    {
        var model = new CynefinDiagramAdapter().Parse("cynefin-beta\nconfusion\n\"One\"\n\"Two\"\n\"Three\"\n\"Four\"\n");
        var result = StrategyDiagramLayouts.Cynefin(model, renderId: "stable");
        Assert.Equal(5, result.Scene.Regions.Count(r => r.Owner.Role == "CynefinDomain"));
        Assert.DoesNotContain(result.Scene.Labels, l => l.Owner.Id == model.Cynefin!.Items[3].Id);
        Assert.Contains(result.Diagnostics, d => d.Code == "cynefin-confusion-overflow");
        Assert.Equal(4, model.Cynefin!.Items.Count);
        var second = StrategyDiagramLayouts.Cynefin(model, renderId: "stable");
        Assert.Equal(result.Scene.Figures.Single(f => f.Id == "fold").Path, second.Scene.Figures.Single(f => f.Id == "fold").Path);
        var history = new EditHistory(model);
        history.Apply(CynefinEditing.MoveItem(model.Cynefin.Items[3].Id, CynefinDomain.Clear, 0));
        Assert.Contains(StrategyDiagramLayouts.Cynefin(model).Scene.Labels, l => l.Text == "Four");
        history.Undo(); Assert.Equal(CynefinDomain.Confusion, model.Cynefin!.Items[3].Domain);
    }
    [Fact]
    public void InvalidDomainThemeAndSelfTransitionCannotModifyTheLiveDocument()
    {
        var model = new CynefinDiagramAdapter().Parse("cynefin-beta\n"); var before = model.Cynefin;
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(CynefinEditing.SetThemeVariable("complexBg", System.Text.Json.Nodes.JsonValue.Create("no-color"))));
        Assert.Same(before, model.Cynefin);
        Assert.Throws<DataDiagramNotEditableException>(() => history.Apply(CynefinEditing.SetTransition(new("loop", CynefinDomain.Clear, CynefinDomain.Clear, "Ignored upstream"))));
        Assert.Same(before, model.Cynefin);
    }
    [Fact]
    public void EditedStrategyDataPreservesStableIdsThroughActualSaveReopen()
    {
        const string source = "wardley-beta\ncomponent Parent [0.4,0.6]\npipeline Parent {\ncomponent Child [0.2]\n}\n";
        var model = MerpeepsDocument.Load(source); var old = model.Wardley!;
        var childId = old.Components[1].Id; var parentId = old.Components[0].Id;
        new EditHistory(model).Apply(WardleyEditing.UpdateComponent(old.Components[0] with { Name = "Renamed Parent" }));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model));
        Assert.Equal(parentId, reopened.Wardley!.Components[0].Id);
        Assert.Equal(childId, reopened.Wardley.Components[1].Id);
        Assert.Equal(.4, reopened.Wardley.VisibilityOf(childId));

        const string framework = "cynefin-beta\ncomplex\n\"Move me\"\n";
        var cynefin = MerpeepsDocument.Load(framework); var item = cynefin.Cynefin!.Items[0];
        new EditHistory(cynefin).Apply(CynefinEditing.MoveItem(item.Id, CynefinDomain.Clear, 0));
        Assert.Equal(item.Id, MerpeepsDocument.Load(MerpeepsDocument.Save(framework, cynefin)).Cynefin!.Items.Single().Id);

        const string fishbone = "ishikawa-beta\nProblem\nCause\n  Child\n";
        var fish = MerpeepsDocument.Load(fishbone); var cause = fish.Ishikawa!.Causes[0];
        new EditHistory(fish).Apply(IshikawaEditing.RenameCause(cause.Id, "Renamed cause"));
        Assert.Equal(cause.Id, MerpeepsDocument.Load(MerpeepsDocument.Save(fishbone, fish)).Ishikawa!.Causes[0].Id);
    }
    [Fact]
    public void AutomaticDualStagesAreNotRewrittenAsUnsupportedWidthSyntax()
    {
        var model = MerpeepsDocument.Load("wardley-beta\nevolution Genesis / Concept -> Custom / Emerging -> Product / Converging -> Commodity / Accepted\n");
        var text = WardleyDiagramAdapter.Write(model.Wardley!);
        Assert.DoesNotContain('@', text);
        var explicitModel = MerpeepsDocument.Load("wardley-beta\nevolution Genesis@0.25 -> Custom@0.5 -> Product@0.75 -> Commodity@1.0\n");
        Assert.Contains("@1.0", WardleyDiagramAdapter.Write(explicitModel.Wardley!));
    }
    [Fact]
    public void ConfusionHitTestingFollowsTheEllipseRatherThanItsBoundingBox()
    {
        var model = MerpeepsDocument.Load("cynefin-beta\n");
        var scene = StrategyDiagramLayouts.Cynefin(model).Scene;
        var region = scene.Regions.Single(r => r.Owner.Role == "CynefinDomain" && r.Owner.Id == "Confusion");
        var point = new DiagramPoint(region.Bounds.X + 2, region.Bounds.Y + 2);
        Assert.Equal("Complex", DrawingHitTest.Hit(scene, point, 0)!.Value.Id);
    }
    [Fact]
    public void AutomaticStagesCannotSilentlyDiscardAnEditedWidth()
    {
        var model = MerpeepsDocument.Load("wardley-beta\nevolution Genesis / Concept -> Product / Mature\n");
        var d = model.Wardley!;
        var changed = d.Stages.Select((s, i) => i == 0 ? s with { End = .3 } : s).ToArray();
        Assert.Throws<ArgumentException>(() => new EditHistory(model).Apply(WardleyEditing.SetMap(d.Title, d.Width, d.Height, changed, null)));
        Assert.Same(d, model.Wardley);
    }
}
