using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Merpeeps.Core.Editing;
using Xunit;

namespace Merpeeps.App.Tests;

public class UnsupportedDiagramTests
{
    private const string Unsupported =
        "---\nx-merpeeps:\n  spec: 1\n  future: keep-me\n---\n" +
        "futureDiagram\n    title Work\n";

    [AvaloniaFact]
    public void UnsupportedSourceRemainsReadableWithoutAnInventedCanvas()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest(Unsupported);

            Assert.Null(window.CanvasForTest.Model);
            Assert.Null(window.CanvasForTest.History);
            Assert.Equal(Unsupported, window.CurrentSource);
            Assert.Equal(Unsupported, window.FindControl<TextBox>("SourceBox")!.Text);
            Assert.True(window.FindControl<Border>("CapabilityBanner")!.IsVisible);
            Assert.Contains("futureDiagram", window.FindControl<TextBlock>("CapabilityText")!.Text);
            Assert.False(window.CanvasForTest.IsEnabled);
            Assert.False(window.FindControl<MenuItem>("MenuExportSvg")!.IsEnabled);
            Assert.False(window.FindControl<Button>("RelayoutBtn")!.IsEnabled);
            Assert.True(window.FindControl<MenuItem>("MenuSaveAs")!.IsEnabled);
            Assert.False(window.FindControl<DockPanel>("ReferencePane")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnsupportedLoadClearsThePreviousEditableModelAndHistory()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest("flowchart TD\n    a[Alpha] --> b[Beta]\n");
            window.CanvasForTest.SelectByIds(["a"]);
            window.CanvasForTest.ApplyStyle(new StyleNodesCommand.Style(Fill: "#ff0000"));
            Assert.True(window.CanvasForTest.History!.CanUndo);

            window.LoadSourceForTest(Unsupported);
            window.CanvasForTest.Undo();
            window.CanvasForTest.SelectAll();
            window.CanvasForTest.DeleteSelection();

            Assert.Null(window.CanvasForTest.Model);
            Assert.Empty(window.CanvasForTest.Selection);
            Assert.Equal(Unsupported, window.CurrentSource);
            Assert.False(window.FindControl<Button>("UndoBtn")!.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReturningToASupportedFamilyRestoresNativeEditing()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest(Unsupported);
            window.LoadSourceForTest("stateDiagram-v2\n    Idle --> Working\n");

            Assert.Equal("stateDiagram-v2", window.CanvasForTest.Model!.Kind);
            Assert.Equal(2, window.CanvasForTest.Model.Nodes.Count);
            Assert.True(window.CanvasForTest.IsEnabled);
            Assert.True(window.FindControl<MenuItem>("MenuExportSvg")!.IsEnabled);
            Assert.True(window.FindControl<Button>("RelayoutBtn")!.IsEnabled);
            Assert.True(window.FindControl<ScrollViewer>("PropertyPane")!.IsEnabled);
            Assert.False(window.FindControl<Border>("CapabilityBanner")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UnsupportedBakeKeepsAnExistingDestination()
    {
        var window = new MainWindow();
        var path = Path.Combine(Path.GetTempPath(), $"merpeeps-unsupported-bake-{Guid.NewGuid():N}.mmd");
        window.Show();
        try
        {
            window.LoadSourceForTest(Unsupported);
            await File.WriteAllTextAsync(path, "ORIGINAL");
            var notice = await window.WriteBaked(path);

            Assert.Contains("Nothing was written", notice);
            Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(path));
            Assert.Equal(Unsupported, window.CurrentSource);
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }
}
