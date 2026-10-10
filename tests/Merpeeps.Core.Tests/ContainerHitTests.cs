using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ContainerHitTests
{
    [Theory]
    [InlineData("swimlane")]
    [InlineData("usecase")]
    [InlineData("agentflow")]
    public void NodeThenLinkThenContainerPriorityMatchesDrawingOrder(string family)
    {
        var source = family switch
        {
            "swimlane" => "swimlane-beta LR\nsubgraph G[Team]\nA[One]\nB[Two]\nend\nA --> B\n",
            "usecase" => "usecase-beta\ndirection LR\nsystemBoundary G[\"System\"]\nA(\"One\")\nB(\"Two\")\nend\nA --> B\n",
            _ => "agentflow-beta LR\nflow G[\"Flow\"]\nA[\"One\"]@{shape:task}\nB[\"Two\"]@{shape:task}\nA --> B\nend\n",
        };
        var model = MerpeepsDocument.Load(source);
        var scene = family switch
        {
            "swimlane" => SwimlaneDiagramLayout.Create(model).Scene,
            "usecase" => UseCaseDiagramLayout.Create(model).Scene,
            _ => AgentFlowDiagramLayout.Create(model).Scene,
        };
        var link = Assert.Single(scene.Regions, r => r.Kind == DrawingHitKind.Polyline);
        var segment = Enumerable.Range(1, link.Points.Count - 1).First(i => link.Points[i] != link.Points[i - 1]);
        var a = link.Points[segment - 1]; var b = link.Points[segment];
        Assert.Equal(link.Owner, DrawingHitTest.Hit(scene, new((a.X + b.X) / 2, (a.Y + b.Y) / 2)));
        var regions = scene.Regions.Where(r => r.Kind == DrawingHitKind.Rectangle).ToArray();
        var container = regions[0];
        Assert.Equal(container.Owner, DrawingHitTest.Hit(scene, new(container.Bounds.X + 10, container.Bounds.Y + 10)));
        foreach (var node in regions.Skip(1))
        {
            var center = new DiagramPoint(node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2);
            Assert.Equal(node.Owner, DrawingHitTest.Hit(scene, center));
        }
        Assert.DoesNotContain(DrawingHitTest.Hit(scene, link.Points[0])!.Value.Role, new[] { "SwimlaneGroup", "UseCaseBoundary" });
        Assert.NotEqual(link.Owner, DrawingHitTest.Hit(scene, link.Points[0]));
    }
}
