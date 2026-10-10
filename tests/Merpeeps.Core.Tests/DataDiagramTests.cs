using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class DataDiagramTests
{
    [Theory]
    [InlineData("timeline")]
    [InlineData("journey")]
    [InlineData("mindmap")]
    [InlineData("treeview")]
    public void AllFourFactoriesStartEmptyWithoutGraphSubstitution(string family)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty(family);
        Assert.Empty(draft.Model.Nodes); Assert.Empty(draft.Model.Edges);
        Assert.Null(draft.Model.Chart); Assert.Null(draft.Model.Sequence);
        Assert.False(draft.Capabilities.CanLayout);
    }
    [Fact]
    public void TimelinePeriodsAreTextNotDatesAndEventsRetainOrderAndIdentity()
    {
        const string source = "timeline TD\n title History\n section Early\n Industry 1.0 : Steam : Water\n : More\n section Later\n Future : AI\n";
        var model = MerpeepsDocument.Load(source);
        var timeline = model.Temporal!;
        Assert.True(timeline.TopDown);
        Assert.Equal("Industry 1.0", timeline.Periods[0].Label);
        Assert.Equal(new[] { "Steam", "Water", "More" }, timeline.Periods[0].Events.Select(e => e.Text));
        var history = new EditHistory(model);
        var period = timeline.Periods[0];
        history.Apply(TemporalEditing.UpdateEvent(period.Id, period.Events[1] with { Text = "Hydro" }));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved).Temporal!;
        Assert.Equal(period.Events[1].Id, reopened.Periods[0].Events[1].Id);
        Assert.Equal("Hydro", reopened.Periods[0].Events[1].Text);
        Assert.Contains("Hydro", SvgExporter.ToSvg(model));
        history.Undo(); Assert.Equal("Water", model.Temporal!.Periods[0].Events[1].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimelineConnectorsStayBetweenCardsAndLayoutNeverRewritesSource(bool topDown)
    {
        var source = "timeline" + (topDown ? " TD" : "") + "\nsection Work\nEarly period with a longer wrapped heading : First : Second\nLater : Third\n";
        var model = MerpeepsDocument.Load(source);
        var before = MerpeepsDocument.Save(source, model);
        var scene = DataDiagramLayouts.Create(model).Scene;
        var events = scene.Figures.Where(f => f.Id.StartsWith("event-")).ToArray();
        Assert.Equal(3, events.Length);
        foreach (var connector in scene.Figures.Where(f => f.Id.StartsWith("period-event-")))
            Assert.DoesNotContain(events, e => connector.Bounds.X < e.Bounds.X + e.Bounds.Width - .1 &&
                connector.Bounds.X + connector.Bounds.Width > e.Bounds.X + .1 &&
                connector.Bounds.Y < e.Bounds.Y + e.Bounds.Height - .1 &&
                connector.Bounds.Y + connector.Bounds.Height > e.Bounds.Y + .1);
        Assert.Equal(before, MerpeepsDocument.Save(source, model));
        if (topDown) Assert.DoesNotContain(scene.Figures, f => f.Id == "timeline-axis");
        else
        {
            var axis = Assert.Single(scene.Figures, f => f.Id == "timeline-axis");
            var periods = scene.Figures.Where(f => f.Owner.Role == "Period" && f.Kind == DrawingFigureKind.Rectangle).ToArray();
            Assert.Equal(2, periods.Length);
            Assert.Equal(periods[0].Bounds.Y + periods[0].Bounds.Height, periods[1].Bounds.Y + periods[1].Bounds.Height);
            Assert.All(periods, p => Assert.True(p.Bounds.Y + p.Bounds.Height < axis.Bounds.Y));
            Assert.All(events, e => Assert.True(e.Bounds.Y > axis.Bounds.Y));
        }
    }
    [Fact]
    public void JourneyActorsAndScoresRemainDataAndRejectInvalidEditsAtomically()
    {
        var model = MerpeepsDocument.Load("journey\nsection Work\nMake tea: 5: Me, Cat\nDo work: 1: Me\n");
        var task = model.Temporal!.Tasks[0];
        var history = new EditHistory(model);
        Assert.Throws<DataDiagramNotEditableException>(() => history.Apply(TemporalEditing.UpdateTask(task with { Score = 8 })));
        Assert.Equal(5, model.Temporal.Tasks[0].Score);
        history.Apply(TemporalEditing.UpdateTask(task with { Score = 3, Actors = ["Me"] }));
        var source = MerpeepsDocument.Save("journey\n", model);
        Assert.Equal(3, MerpeepsDocument.Load(source).Temporal!.Tasks[0].Score);
        Assert.Contains(DataDiagramLayouts.Create(model).Scene.Figures, f => f.Id.EndsWith("-face"));
    }
    [Fact]
    public void MindmapShapeMarkdownAndCompensatedIndentationRoundtrip()
    {
        const string source = "mindmap\nRoot\n    A[A]\n        B((Bee))\n      C[\"`**Bold** and *italic*`\"]\n";
        var model = MerpeepsDocument.Load(source);
        var root = model.Outline!.Roots.Single();
        Assert.Equal(2, root.Children[0].Children.Count);
        Assert.Equal(OutlineShape.Circle, root.Children[0].Children[0].Shape);
        var markdown = root.Children[0].Children[1];
        Assert.True(markdown.Markdown);
        var scene = DataDiagramLayouts.Create(model).Scene;
        Assert.Contains(scene.Labels, l => l.Text == "Bold" && l.Font.Weight == 700);
        Assert.Contains(scene.Labels, l => l.Text == "italic" && l.Font.Italic);
        var history = new EditHistory(model);
        history.Apply(OutlineEditing.SetPosition(markdown.Id, new(0, 0)));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(new DiagramPoint(0, 0), MerpeepsDocument.Load(saved).Outline!.Positions[markdown.Id]);
        history.Undo();
        Assert.False(MerpeepsDocument.Load(MerpeepsDocument.Save(saved, model)).Outline!.Positions.ContainsKey(markdown.Id));
    }
    [Fact]
    public void TreeViewBoxDrawingKeepsDirectoriesDescriptionsAndIconOverrides()
    {
        const string source = "treeView-beta\n├── src/\n│   ├── App.tsx :::highlight icon(logos:react) ## main component\n│   └── index.ts\n└── README.md\n";
        var model = MerpeepsDocument.Load(source);
        var tree = model.Outline!;
        Assert.Equal(2, tree.Roots.Count);
        Assert.True(tree.BoxDrawingSource);
        Assert.True(tree.Roots[0].Directory);
        var entry = tree.Roots[0].Children[0];
        Assert.Equal("main component", entry.Description);
        Assert.Equal("logos:react", entry.Icon);
        Assert.Contains("highlight", entry.Classes);
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved).Outline!;
        Assert.Equal(entry.Id, reopened.Roots[0].Children[0].Id);
        Assert.Equal(source, MerpeepsDoc.BodyOf(saved));
    }
    [Fact]
    public void OutlineReparentRejectsCyclesAndKeepsWholeSubtreeInOneUndo()
    {
        var model = MerpeepsDocument.Load("treeView-beta\nRoot/\n  A/\n    Leaf\n  B/\n");
        var root = model.Outline!.Roots[0];
        var a = root.Children[0]; var b = root.Children[1];
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(OutlineEditing.Move(a.Id, a.Children[0].Id, 0)));
        history.Apply(OutlineEditing.Move(a.Id, b.Id, 0));
        Assert.Equal(a.Id, model.Outline!.Node(b.Id).Children[0].Id);
        history.Undo();
        Assert.Equal(a.Id, model.Outline!.Roots[0].Children[0].Id);
    }

    [Fact]
    public void SectionMovesAndEventTransfersKeepIdentityAndSourceOrder()
    {
        const string source = "timeline\nsection First\nOld : One\nsection Second\nFuture : Two\n";
        var model = MerpeepsDocument.Load(source);
        var document = model.Temporal!;
        var first = document.Periods[0]; var second = document.Periods[1];
        var history = new EditHistory(model);
        history.Apply(TemporalEditing.TransferEvent(first.Id, second.Id, first.Events[0].Id, 1));
        Assert.Equal(first.Events[0].Id, model.Temporal!.Periods[1].Events[1].Id);
        history.Apply(TemporalEditing.MoveSection(document.Sections[1].Id, 0));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved).Temporal!;
        Assert.Equal(second.Id, reopened.Periods[0].Id);
        Assert.Equal(first.Events[0].Id, reopened.Periods[0].Events[1].Id);
        history.Undo(); history.Undo();
        Assert.Equal(first.Events[0].Id, model.Temporal!.Periods[0].Events[0].Id);
    }

    [Fact]
    public void ThemeCommandsPreserveUnrelatedConfigurationAndUndo()
    {
        const string source = "---\nconfig:\n  extra: keep\n---\ntreeView-beta\nroot/\n";
        var model = MerpeepsDocument.Load(source);
        var history = new EditHistory(model);
        history.Apply(OutlineEditing.SetThemeVariable("treeView.labelColor", System.Text.Json.Nodes.JsonValue.Create("#123456")));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Contains("extra: keep", saved);
        Assert.Equal("#123456", MerpeepsDocument.Load(saved).Outline!.ThemeVariables["treeView"]!["labelColor"]!.GetValue<string>());
        history.Undo();
        Assert.Null(model.Outline!.ThemeVariables["treeView"]);
    }
}
