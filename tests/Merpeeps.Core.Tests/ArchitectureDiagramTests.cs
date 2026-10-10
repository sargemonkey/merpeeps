using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ArchitectureDiagramTests
{
    [Fact]
    public void ExplicitPortsGroupsJunctionsAndAlignmentHaveDistinctSemantics()
    {
        const string source = """
            architecture-beta
            group api(cloud)[API]
            group internal(server)[Internal] in api
            service db(database)[Database] in internal
            service worker(server)[Worker] in internal
            service gateway(internet)[Gateway]
            junction split in api
            db:R --> L:worker
            worker{group}:R <--> L:gateway
            split:T -- B:gateway
            align row db worker
            """;
        var adapter = new ArchitectureDiagramAdapter();
        var model = adapter.Parse(source);
        var document = model.Architecture!;
        Assert.Empty(model.Nodes);
        Assert.Equal(ArchitectureNodeKind.Junction, document.Nodes[^1].Kind);
        Assert.Equal(document.Groups[0].Id, document.Groups[1].ParentId);
        Assert.True(document.Connections[1].From.GroupBoundary);
        Assert.True(document.Connections[1].FromArrow);
        Assert.True(document.Connections[1].ToArrow);
        Assert.Equal(ArchitecturePort.Right, document.Connections[0].From.Port);
        Assert.Equal(ArchitectureAlignmentAxis.Row, document.Alignments.Single().Axis);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }
    [Fact]
    public void RenameAndUngroupRepairReferencesAtomically()
    {
        var model = new ArchitectureDiagramAdapter().Parse("architecture-beta\ngroup g[Group]\nservice a(server)[A] in g\nservice b(server)[B]\na{group}:R --> L:b\n");
        var original = model.Architecture!;
        var history = new EditHistory(model);
        history.Apply(ArchitectureDiagramEditing.UpdateNode(original.Nodes[0] with { SourceId = "renamed" }));
        Assert.Equal(original.Nodes[0].Id, model.Architecture!.Connections[0].From.NodeId);
        Assert.Contains("renamed{group}:R", ArchitectureDiagramAdapter.Write(model.Architecture));
        history.Apply(ArchitectureDiagramEditing.RemoveGroup(original.Groups[0].Id));
        Assert.Null(model.Architecture!.Nodes[0].GroupId);
        Assert.False(model.Architecture.Connections[0].From.GroupBoundary);
        history.Undo(); Assert.True(model.Architecture!.Connections[0].From.GroupBoundary);
    }
    [Fact]
    public void InvalidGroupNestingLeavesTheLiveDocumentUnchanged()
    {
        var model = new ArchitectureDiagramAdapter().Parse("architecture-beta\ngroup outer[Outer]\ngroup inner[Inner] in outer\n");
        var before = model.Architecture!;
        Assert.Throws<DataDiagramNotEditableException>(() => new EditHistory(model).Apply(
            ArchitectureDiagramEditing.UpdateGroup(before.Groups[0] with { ParentId = before.Groups[1].Id })));
        Assert.Same(before, model.Architecture);
    }

    [Fact]
    public void AlignmentAndPortsAreReflectedInTheActualRoute()
    {
        var model = new ArchitectureDiagramAdapter().Parse("architecture-beta\nservice a(database)[A]\nservice b(database)[B]\nservice c(server)[C]\na:R --> L:c\nb:R --> L:c\nalign column a b\n");
        var d = model.Architecture!;
        var layout = ArchitectureDiagramLayout.Create(model);
        var a = layout.Nodes[d.Nodes[0].Id].IconBounds;
        var b = layout.Nodes[d.Nodes[1].Id].IconBounds;
        var c = layout.Nodes[d.Nodes[2].Id].IconBounds;
        Assert.Equal(a.X, b.X);
        Assert.True(b.Y > a.Y);
        Assert.True(c.X > a.X);
        var route = layout.Routes[d.Connections[1].Id];
        Assert.Equal(b.X + b.Width, route[0].X);
        Assert.Equal(c.X, route[^1].X);
        for (var i = 1; i < route.Count; i++)
            Assert.True(route[i - 1].X == route[i].X || route[i - 1].Y == route[i].Y);
    }

    [Fact]
    public void ConflictingAlignmentFailsBeforeLiveAssignment()
    {
        var model = new ArchitectureDiagramAdapter().Parse("architecture-beta\nservice a(server)[A]\nservice b(server)[B]\na:L --> R:b\n");
        var d = model.Architecture!;
        Assert.Throws<DataDiagramNotEditableException>(() => new EditHistory(model).Apply(
            ArchitectureDiagramEditing.SetAlignment(new("align", ArchitectureAlignmentAxis.Row, d.Nodes.Select(n => n.Id).ToArray()))));
        Assert.Same(d, model.Architecture);
    }

    [Fact]
    public void ArchitectureSpacingChangesAndIdentityRoundtripAreCanonical()
    {
        const string source = "architecture-beta\nservice a(server)[A]\nservice b(server)[B]\na:R --> L:b\n";
        var model = MerpeepsDocument.Load(source);
        var before = ArchitectureDiagramLayout.Create(model);
        var ids = model.Architecture!.Nodes.Select(n => n.Id).ToArray();
        new EditHistory(model).Apply(ArchitectureDiagramEditing.SetOption("idealEdgeLengthMultiplier", System.Text.Json.Nodes.JsonValue.Create(3)));
        var after = ArchitectureDiagramLayout.Create(model);
        Assert.True(after.Nodes[ids[1]].Bounds.X - after.Nodes[ids[0]].Bounds.X > before.Nodes[ids[1]].Bounds.X - before.Nodes[ids[0]].Bounds.X);
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(ids, MerpeepsDocument.Load(saved).Architecture!.Nodes.Select(n => n.Id));
        var current = model.Architecture;
        Assert.Throws<NotSupportedException>(() => new EditHistory(model).Apply(ArchitectureDiagramEditing.SetOption("numIter", System.Text.Json.Nodes.JsonValue.Create(5000))));
        Assert.Same(current, model.Architecture);
    }
}
