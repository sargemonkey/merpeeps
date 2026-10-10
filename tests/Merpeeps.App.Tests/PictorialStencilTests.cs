using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.App.Tests;

public class PictorialStencilTests
{
    [AvaloniaFact]
    public void EveryStencilDrawsPixelsAndDifferentObjectsAreRecognizablyDifferent()
    {
        var pictures = new Dictionary<StencilGlyph, string>();
        foreach (var glyph in Enum.GetValues<StencilGlyph>())
        {
            var preview = new DiagramStencilPreview(glyph);
            preview.Measure(new(64, 44)); preview.Arrange(new Rect(0, 0, 64, 44));
            using var image = new RenderTargetBitmap(new PixelSize(64, 44));
            using (var context = image.CreateDrawingContext()) preview.Render(context);
            var pixels = new int[64 * 44];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { image.CopyPixels(new PixelRect(0, 0, 64, 44), handle.AddrOfPinnedObject(), pixels.Length * 4, 64 * 4); }
            finally { handle.Free(); }
            Assert.True(pixels.Count(p => p != 0) > 20, glyph + " has no meaningful pictorial content.");
            pictures[glyph] = string.Join(',', pixels);
        }
        foreach (var pair in new[]
        {
            (StencilGlyph.Class, StencilGlyph.Entity), (StencilGlyph.Server, StencilGlyph.Junction),
            (StencilGlyph.Server, StencilGlyph.Group), (StencilGlyph.Actor, StencilGlyph.Participant),
            (StencilGlyph.Person, StencilGlyph.Actor),
            (StencilGlyph.Line, StencilGlyph.Bar), (StencilGlyph.Set, StencilGlyph.Overlap),
            (StencilGlyph.Block, StencilGlyph.Spacer), (StencilGlyph.Task, StencilGlyph.FlowContainer),
        }) Assert.NotEqual(pictures[pair.Item1], pictures[pair.Item2]);
    }

    [AvaloniaTheory]
    [MemberData(nameof(WorkspacePanelsTests.Dialects), MemberType = typeof(WorkspacePanelsTests))]
    public void PopulatedPaletteCreatesUniqueDefaultsWithOneUndoPerClick(string family, string dialect)
    {
        if (family is "flowchart" or "state") return; // Existing stencils have their own creation and drop tests.
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest(MermaidDiagramCatalog.CreateEmpty(family, dialect).Source); Settle();
            var palette = window.FindControl<Border>("FamilyPaletteHost")!;
            var first = Buttons(palette).First(b => b.IsEnabled);
            Click(first);
            var model = window.CanvasForTest.Model!;
            var one = Count(model);
            Assert.True(one > 0, family);
            var second = Buttons(palette).First(b => b.IsEnabled);
            Click(second);
            Assert.True(Count(model) > one, family + ": " + string.Join(" | ", window.FindControl<Border>("ClassInspectorHost")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
            Assert.Equal(Count(model), Count(MerpeepsDocument.Load(window.CurrentSource)));
            Assert.True(window.FindControl<Button>("SaveBtn")!.IsEnabled);
            window.CanvasForTest.Undo(); Settle();
            Assert.Equal(one, Count(model));
            window.CanvasForTest.Undo(); Settle();
            Assert.Equal(0, Count(model));
            Assert.False(window.CanvasForTest.History!.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("architecture-beta\n", "MenuArchitectureAlignment")]
    [InlineData("quadrantChart\n", "MenuQuadrantLabels")]
    [InlineData("pie\n", "MenuDiagramTitle")]
    [InlineData("timeline\n", "MenuDiagramTitle")]
    [InlineData("gantt\n", "MenuGanttReference")]
    public void DiagramActionsAreInTheDiagramMenuNotTheStencilCollection(string source, string menu)
    {
        var window = new MainWindow(); window.Show();
        try
        {
            window.LoadSourceForTest(source); Settle();
            var palette = window.FindControl<Border>("FamilyPaletteHost")!;
            Assert.All(Buttons(palette), b => Assert.Single(b.GetVisualDescendants().OfType<DiagramStencilPreview>()));
            Assert.DoesNotContain(Buttons(palette), b =>
                AutomationProperties.GetName(b) is "Diagram contents" or "Diagram display options" or "Align row / column...");
            var action = window.FindControl<MenuItem>(menu)!;
            Assert.True(action.IsVisible);
            action.RaiseClick(); Settle();
            Assert.False(window.CanvasForTest.History!.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DependentItemsCreateIntoTheirSelectedOwnerWithoutAForm()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            foreach (var source in new[] { "kanban\n", "timeline\n", "usecase-beta\n", "sequenceDiagram\n", "zenuml\n" })
            {
                window.LoadSourceForTest(source); Settle();
                var palette = window.FindControl<Border>("FamilyPaletteHost")!;
                Click(Buttons(palette).First(b => b.IsEnabled));
                var name = source.Trim() switch
                {
                    "kanban" => "Add board card", "timeline" => "Add time period",
                    "usecase-beta" => "Add use-case note", "sequenceDiagram" => "Add message...", _ => "Add statement",
                };
                Click(Buttons(palette).Single(b => AutomationProperties.GetName(b) == name));
                if (source.StartsWith("timeline", StringComparison.Ordinal))
                    Click(Buttons(palette).Single(b => AutomationProperties.GetName(b) == "Add timeline event"));
                Assert.True(window.FindControl<Button>("SaveBtn")!.IsEnabled, source);
                Assert.Equal(Count(window.CanvasForTest.Model!), Count(MerpeepsDocument.Load(window.CurrentSource)));
                var model = window.CanvasForTest.Model!;
                Assert.True(source.Trim() switch
                {
                    "kanban" => model.Kanban!.Columns.Single().Cards.Count == 1,
                    "timeline" => model.Temporal!.Periods.Single().Events.Count == 1,
                    "usecase-beta" => model.UseCase!.Nodes.Single(n => n.Kind == UseCaseNodeKind.Note).NoteTargetId is not null,
                    "sequenceDiagram" => model.Sequence!.Statements.OfType<SequenceMessage>().Count() == 1,
                    _ => model.Zen!.Statements.Count == 1,
                }, source + ": " + window.CurrentSource);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CancellingPropertiesReenablesTheExistingStencilButtons()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            window.LoadSourceForTest("architecture-beta\n"); Settle();
            var palette = window.FindControl<Border>("FamilyPaletteHost")!;
            var service = Buttons(palette).Single(b => AutomationProperties.GetName(b) == "Add service");
            Click(service);
            WorkspaceControls.Find<TextBox>(window, "Service label").Text = "Unapplied";
            Assert.True(service.IsEnabled);
            WorkspaceControls.Click(window, "Cancel edit");
            Assert.True(service.IsEnabled);
            Click(service);
            Assert.Equal(2, window.CanvasForTest.Model!.Architecture!.Nodes.Count);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("YYYY-MM-DD", "2026-09-30")]
    [InlineData("DD/MM/YYYY", "30/09/2026")]
    [InlineData("YYYY-MM-DD HH:mm:ss.SSS Z", "2026-09-30 14:25:36.789 +00:00")]
    [InlineData("MMM Do, YYYY", "Sep 30th, 2026")]
    [InlineData("DDDD YYYY", "273 2026")]
    [InlineData("hh:mm A", "02:25 PM")]
    [InlineData("[Day] D [at] H:m:s.S", "Day 30 at 14:25:36.7")]
    public void DefaultGanttStartUsesTheDocumentsDateFormat(string format, string expected)
    {
        var instant = new DateTimeOffset(2026, 9, 30, 14, 25, 36, 789, TimeSpan.Zero);
        var context = new GanttEvaluationContext(instant, TimeZoneInfo.Utc, CultureInfo.InvariantCulture);
        var text = GanttDateCodec.Format(instant, format, context, start: true);
        Assert.Equal(expected, text);
        Assert.Equal(2026, GanttDateCodec.Parse(text, format, context, start: true).Year);
    }

    [Theory]
    [InlineData("X", true)] [InlineData("X", false)] [InlineData("x", true)]
    public void GanttTimestampFormattingMatchesStartAndEndParsing(string format, bool start)
    {
        var instant = new DateTimeOffset(2026, 9, 30, 14, 25, 36, TimeSpan.Zero);
        var context = new GanttEvaluationContext(instant, TimeZoneInfo.Utc);
        Assert.Equal(instant, GanttDateCodec.Parse(GanttDateCodec.Format(instant, format, context, start), format, context, start));
    }

    [AvaloniaTheory]
    [InlineData(false, 900)] [InlineData(true, 1600)]
    public void PictorialCollectionsAndNewElementsAreVisibleAtDesktopSizes(bool dark, int width)
    {
        var window = new MainWindow { Width = width, Height = 780, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            foreach (var source in new[] { "classDiagram\n", "architecture-beta\n", "sequenceDiagram\n", "C4Context\n", "block\n" })
            {
                window.LoadSourceForTest(source); Settle();
                var palette = window.FindControl<Border>("FamilyPaletteHost")!;
                Click(Buttons(palette).First(b => b.IsEnabled));
                Assert.True(window.CanvasForTest.History!.CanUndo);
                if (Environment.GetEnvironmentVariable("MERPEEPS_STENCIL_CAPTURES") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered stencil frame.");
                    frame.Save(Path.Combine(directory, $"{source.Trim()}-{width}-{(dark ? "dark" : "light")}.png"));
                }
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<Button> Buttons(Visual root) => root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible);
    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private static void Click(Button button) { Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
    private static int Count(DiagramModel m) => m.Nodes.Count + m.Edges.Count + m.Subgraphs.Count
        + (m.Sequence is { } s ? s.Participants.Count + s.Boxes.Count + SequenceDiagramModel.Walk(s.Statements).Count(x => x is not (SequenceDeclaration or SequenceComment or SequenceDocumentText or SequenceParticipantMetadata)) : 0)
        + (m.Chart switch { PieChart p => p.Slices.Count, QuadrantChart q => q.Points.Count, XyChart x => x.Series.Count + x.Series.Sum(s => s.Values.Count), RadarChart r => r.Axes.Count + r.Curves.Count, _ => 0 })
        + (m.Outline?.Nodes.Count() ?? 0)
        + (m.Temporal is { } t ? t.Sections.Count + t.Periods.Count + t.Periods.Sum(p => p.Events.Count) + t.Tasks.Count : 0)
        + (m.Packet?.Fields.Count ?? 0) + (m.Kanban is { } k ? k.Columns.Count + k.Columns.Sum(c => c.Cards.Count) : 0)
        + (m.Requirements?.Items.Count ?? 0) + (m.GitDiagram?.Operations.Count ?? 0)
        + (m.Sankey is { } sk ? sk.Nodes.Count + sk.Links.Count : 0) + (m.Treemap?.Nodes.Count() ?? 0)
        + (m.Blocks?.AllItems.Count() ?? 0) + (m.Architecture is { } a ? a.Nodes.Count + a.Groups.Count : 0)
        + (m.C4?.Elements.Count ?? 0) + (m.Gantt is { } g ? g.Sections.Count + g.Tasks.Count : 0)
        + (m.Wardley is { } w ? w.Components.Count + w.Annotations.Count : 0)
        + (m.Ishikawa is { } i ? (i.Problem.Length > 0 ? 1 : 0) + i.AllCauses.Count() : 0)
        + (m.Cynefin is { } c ? c.Items.Count + c.Transitions.Count : 0)
        + (m.Venn is { } v ? v.Sets.Count + v.Overlaps.Count + v.Texts.Count : 0)
        + (m.Railroad?.Rules.Count ?? 0) + (m.Swimlane is { } sl ? sl.Groups.Count + sl.Nodes.Count : 0)
        + (m.UseCase is { } u ? u.Nodes.Count + u.Boundaries.Count : 0)
        + (m.AgentFlow?.Elements.Count ?? 0) + (m.EventModeling is { } e ? e.Frames.Count + e.DataBlocks.Count : 0)
        + (m.Zen is { } z ? z.Participants.Count + ZenSemantics.Walk(z.Statements).Count() : 0);
}
