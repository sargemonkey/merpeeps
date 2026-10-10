using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class VennDiagramTests
{
    [Fact]
    public void SetsOverlapsAndAttachedTextHaveSeparateStableIdentities()
    {
        var adapter = new VennDiagramAdapter();
        var model = adapter.Parse("venn-beta\nset A[\"Alpha\"]:20\n  text a1[\"First\"]\nset B[\"Beta\"]:12\nunion A,B[\"Both\"]:3\n  text ab[\"Shared\"]\nstyle A fill:red,fill-opacity:0.4\nstyle A,B color:blue\n");
        var d = model.Venn!;
        Assert.Empty(model.Nodes); Assert.Equal(20, d.Sets[0].EffectiveSize);
        Assert.Equal(d.Sets[0].Id, d.Texts[0].AreaId); Assert.Equal(d.Overlaps[0].Id, d.Texts[1].AreaId);
        var written = adapter.Write(model); Assert.Equal(written, adapter.Write(adapter.Parse(written)));
    }
    [Fact]
    public void TwoSetCircleAreasMatchTheActualNumericData()
    {
        var model = new VennDiagramAdapter().Parse("venn-beta\nset A:20\nset B:12\nunion A,B:3\n");
        var geometry = VennGeometry.Fit(model.Venn!);
        Assert.Equal(20, Math.PI * Math.Pow(geometry.Circles[0].Radius, 2), 8);
        Assert.Equal(12, Math.PI * Math.Pow(geometry.Circles[1].Radius, 2), 8);
        var overlap = geometry.Areas.Single(a => !a.Implied);
        Assert.Equal(3, overlap.ActualArea, 8);
        var scene = VennDiagramLayout.Create(model).Scene;
        Assert.Contains(scene.Figures, f => f.Owner.Role == "VennOverlap" && f.Path.Any(p => p.Kind == DrawingPathKind.Cubic));
    }
    [Fact]
    public void ThreeWayOverlapAddsOnlyDerivedPairwiseConstraints()
    {
        var model = new VennDiagramAdapter().Parse("venn-beta\nset A\nset B\nset C\nunion A,B,C[\"Shared\"]\n");
        var d = model.Venn!;
        var layout = VennDiagramLayout.Create(model);
        Assert.Single(d.Overlaps); Assert.Equal(3, layout.Circles.Count);
        Assert.Equal(3, layout.Areas.Count(a => a.Implied));
        Assert.True(layout.Areas.Single(a => !a.Implied).ActualArea > 0);
        Assert.Contains(layout.Scene.Labels, l => l.Text == "Shared");
    }
    [Fact]
    public void IntersectionAreaHandlesContainmentAndDisjointSets()
    {
        Assert.Equal(Math.PI, VennGeometry.IntersectionArea([new("a",0,0,1),new("b",0,0,2),new("c",0,0,3)]), 9);
        Assert.Equal(0, VennGeometry.IntersectionArea([new("a",0,0,1),new("b",5,0,1)]));
    }
    [Fact]
    public void InfeasibleOrNonfiniteAreasAreNotAcceptedAsValidEdits()
    {
        Assert.Throws<ArgumentException>(() => new VennDiagramAdapter().Parse("venn-beta\nset A:2\nset B:3\nunion A,B:4\n"));
        Assert.Throws<ArgumentException>(() => new VennDiagramAdapter().Parse("venn-beta\nset A:-1\n"));
    }
    [Fact]
    public void SetRenameAndOverlapEditsRoundtripWithoutChangingTextOwnership()
    {
        const string source = "venn-beta\nset A:20\nset B:12\nunion A,B[\"Both\"]:3\n  text shared[\"Text\"]\n";
        var model = MerpeepsDocument.Load(source); var d = model.Venn!; var setId = d.Sets[0].Id; var overlapId = d.Overlaps[0].Id;
        var history = new EditHistory(model);
        history.Apply(VennEditing.UpdateSet(d.Sets[0] with { Name = "Renamed A" }));
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model)).Venn!;
        Assert.Equal(setId, reopened.Sets[0].Id); Assert.Equal(overlapId, reopened.Overlaps[0].Id);
        Assert.Equal(overlapId, reopened.Texts[0].AreaId);
        var before = model.Venn;
        Assert.Throws<ArgumentException>(() => history.Apply(VennEditing.UpdateOverlap(before!.Overlaps[0] with { Size = 99 })));
        Assert.Same(before, model.Venn);
        history.Undo(); Assert.Equal("A", model.Venn!.Sets[0].Name);
    }
    [Fact]
    public void OverlapSelectionRequiresMembershipInEveryCircle()
    {
        var model = MerpeepsDocument.Load("venn-beta\nset A:20\nset B:12\nunion A,B:3\n");
        var scene = VennDiagramLayout.Create(model).Scene;
        var overlap = scene.Regions.Single(r => r.Owner.Role == "VennOverlap");
        var point = new DiagramPoint(overlap.Bounds.X + .01, overlap.Bounds.Y + .01);
        Assert.NotEqual("VennOverlap", DrawingHitTest.Hit(scene, point, 0)?.Role);
    }
}
