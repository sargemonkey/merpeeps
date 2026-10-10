using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class WeightedDiagramTests
{
    [Fact]
    public void SankeyCsvPreservesQuotesAndWeightsAndUsesGradientRibbons()
    {
        const string source = "sankey\n%% source,target,value\nA,\"Heating, \"\"homes\"\"\",3\nA,B,1\n";
        var model = MerpeepsDocument.Load(source);
        var flow = model.Sankey!;
        Assert.Contains(flow.Nodes, n => n.Label == "Heating, \"homes\"");
        var scene = WeightedDiagramLayout.Create(model).Scene;
        Assert.Equal(2, scene.Figures.Count(f => f.Owner.Role == "Flow" && f.Gradient is not null));
        Assert.All(scene.Figures.Where(f => f.Owner.Role == "Flow"), f => Assert.Contains(f.Path, p => p.Kind == DrawingPathKind.Cubic));
        Assert.Contains("linearGradient", SvgExporter.ToSvg(model));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(flow.Links[0].Id, MerpeepsDocument.Load(saved).Sankey!.Links[0].Id);
    }
    [Fact]
    public void FlowEditsRejectCyclesWithoutChangingLiveWeights()
    {
        var model = MerpeepsDocument.Load("sankey\nA,B,2\n");
        var before = model.Sankey;
        Assert.Throws<ArgumentException>(() => new EditHistory(model).Apply(SankeyEditing.AddFlow("B", "A", 1)));
        Assert.Same(before, model.Sankey);
        var history = new EditHistory(model);
        history.Apply(SankeyEditing.UpdateFlow(model.Sankey!.Links[0] with { Value = 4 }));
        Assert.Equal(4, model.Sankey!.Links[0].Value);
        history.Undo(); Assert.Equal(2, model.Sankey!.Links[0].Value);
    }
    [Fact]
    public void TreemapWeightsDriveAreaAndChildHitsWinOverParent()
    {
        var model = MerpeepsDocument.Load("treemap-beta\n\"Root\"\n  \"Small\": 1\n  \"Large\": 3\n");
        var root = model.Treemap!.Roots[0];
        var scene = WeightedDiagramLayout.Create(model).Scene;
        var a = scene.Figures.Single(f => f.Id == root.Children[0].Id).Bounds;
        var b = scene.Figures.Single(f => f.Id == root.Children[1].Id).Bounds;
        Assert.Equal(3, b.Width * b.Height / (a.Width * a.Height), 6);
        Assert.Equal(root.Children[0].Id, DrawingHitTest.Hit(scene, new(a.X + a.Width / 2, a.Y + a.Height / 2))!.Value.Id);
        Assert.Equal("$700,000", WeightedDiagramLayout.Format(700000, "$0,0"));
        Assert.Equal("$35.0%", WeightedDiagramLayout.Format(.35, "$.1%"));
    }
    [Fact]
    public void WeightedSubtreeMovesRetainIdsAndRejectCycles()
    {
        var model = MerpeepsDocument.Load("treemap-beta\n\"A\"\n \"Leaf\": 2\n\"B\"\n \"Other\": 1\n");
        var tree = model.Treemap!;
        var leaf = tree.Roots[0].Children[0]; var target = tree.Roots[1];
        var history = new EditHistory(model);
        history.Apply(TreemapEditing.Move(leaf.Id, target.Id, 1));
        Assert.Equal(leaf.Id, model.Treemap!.Roots[1].Children[1].Id);
        var saved = MerpeepsDocument.Save("treemap-beta\n", model);
        Assert.Equal(leaf.Id, MerpeepsDocument.Load(saved).Treemap!.Roots[1].Children[1].Id);
        history.Undo(); Assert.Equal(leaf.Id, model.Treemap!.Roots[0].Children[0].Id);
    }

    [Fact]
    public void TreemapNarrowTileWarnsAndDoesNotDrawTextOutsideItsMeasuredArea()
    {
        const string source = "treemap-beta\n\"Root\"\n  \"Large\": 100000\n  \"Very long retained tiny label\": 0.0001\n";
        var model = MerpeepsDocument.Load(source);
        var tiny = model.Treemap!.Roots[0].Children[1];
        var layout = WeightedDiagramLayout.Create(model,
            (text, font) => new DrawingTextSize(text.Length * font.Size * .7, font.Size * 1.4));
        Assert.Contains(layout.Diagnostics, diagnostic => diagnostic.Code == "tile-label" && diagnostic.Id == tiny.Id);
        Assert.DoesNotContain(layout.Scene.Labels, label => label.Owner.Id == tiny.Id);
        Assert.Equal("Very long retained tiny label", model.Treemap.Roots[0].Children[1].Label);
        Assert.Equal(.0001, model.Treemap.Roots[0].Children[1].Value);
    }

    [Fact]
    public void TreemapFormattedValueMustFitEvenWhenItsLabelFits()
    {
        var model = MerpeepsDocument.Load("treemap-beta\n\"Root\"\n  \"A\": 1\n  \"Large\": 19\n");
        model.Treemap!.Configuration["valueFormat"] = ".10f";
        var item = model.Treemap.Roots[0].Children[0];
        var layout = WeightedDiagramLayout.Create(model,
            (text, font) => new DrawingTextSize(text.Length * font.Size * .5, font.Size * 1.4));
        Assert.Contains(layout.Scene.Labels, label => label.Owner.Id == item.Id && label.Text == "A");
        Assert.DoesNotContain(layout.Scene.Labels, label => label.Owner.Id == item.Id && label.Text.Contains("1.000"));
        Assert.Contains(layout.Diagnostics, diagnostic => diagnostic.Code == "tile-value" && diagnostic.Id == item.Id);
    }
}
