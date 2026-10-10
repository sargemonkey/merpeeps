using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.App.Tests;

public class FullColourDialogTests
{
    [Fact]
    public void EveryChannelValueAndAlphaValueSurvivesCssSerialization()
    {
        for (var channel = 0; channel <= 255; channel++)
        {
            var color = Color.FromArgb((byte)channel, (byte)channel, (byte)(255 - channel), 127);
            Assert.Equal(color, ColourPickerDialog.ParseHex(ColourPickerDialog.Hex(color)));
            Assert.True(SequenceColors.TryParse(ColourPickerDialog.Source(color), out var parsed));
            Assert.Equal((color.R, color.G, color.B, color.A), (parsed.Red, parsed.Green, parsed.Blue, parsed.Alpha));
        }
    }

    [AvaloniaFact]
    public async Task DialogSupportsExactChannelsHexAndTransparencyWithoutChangingOnCancel()
    {
        var owner = new Window(); owner.Show();
        try
        {
            var task = ColourPickerDialog.PickAsync(owner, "Fill", "rgba(1,2,3,0.5)", true, "");
            Settle();
            var dialog = Assert.Single(owner.OwnedWindows);
            Assert.Equal("#01020380", Field<TextBox>(dialog, "Hex color").Text);
            Field<TextBox>(dialog, "Red").Text = "255";
            Field<TextBox>(dialog, "Green").Text = "0";
            Field<TextBox>(dialog, "Blue").Text = "127";
            Field<TextBox>(dialog, "Alpha").Text = "1";
            Assert.Equal("#FF007F01", Field<TextBox>(dialog, "Hex color").Text);
            Click(Field<Button>(dialog, "Use color"));
            var selected = await task;
            Assert.Equal(Color.FromArgb(1, 255, 0, 127), ColourPickerDialog.ParseColour(selected));
            var cancel = ColourPickerDialog.PickAsync(owner, "Fill", selected, true);
            Settle();
            dialog = Assert.Single(owner.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#00FF00";
            Click(Field<Button>(dialog, "Cancel color"));
            Assert.Null(await cancel);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("#12")] [InlineData("rgb(no)")] [InlineData("unknown-color")]
    public async Task InvalidColorCannotConfirmThePreviousValidColor(string invalid)
    {
        var owner = new Window(); owner.Show();
        try
        {
            var result = ColourPickerDialog.PickAsync(owner, "Fill", "#123456");
            Settle(); var dialog = Assert.Single(owner.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = invalid;
            Assert.False(Field<Button>(dialog, "Use color").IsEnabled);
            Assert.NotEmpty(Field<TextBlock>(dialog, "Color error").Text!);
            Click(Field<Button>(dialog, "Use color"));
            Assert.False(result.IsCompleted);
            Click(Field<Button>(dialog, "Cancel color")); Assert.Null(await result);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task OpaqueFieldsDoNotSilentlyDropAlphaAndResetIsDifferentFromCancel()
    {
        var owner = new Window(); owner.Show();
        try
        {
            var result = ColourPickerDialog.PickAsync(owner, "Fill", "#abcdef", false, "");
            Settle(); var dialog = Assert.Single(owner.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#12345678";
            Assert.False(Field<Button>(dialog, "Use color").IsEnabled);
            Field<TextBox>(dialog, "Hex color").Text = "#123456";
            Assert.True(Field<Button>(dialog, "Use color").IsEnabled);
            Click(Field<Button>(dialog, "Use default")); Assert.Equal("", await result);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public void EntityColorPickerCommitsOnlyOnConfirmAndUndoRestoresTheOldColor()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("erDiagram\nA\n");
        try
        {
            window.CanvasForTest.SelectByIds(["A"]); Settle();
            var panel = window.FindControl<Border>("ClassInspectorHost")!;
            ShowAppearance(panel); Settle();
            var before = window.CurrentSource;
            Click(Field<ColourField>(panel, "Fill color"));
            var dialog = Assert.Single(window.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#2468AC80";
            Assert.Equal(before, window.CurrentSource);
            Click(Field<Button>(dialog, "Use color")); Settle();
            var node = window.CanvasForTest.Model!.Nodes[0];
            Assert.True(SequenceColors.TryParse(node.Entity!.InlineStyle["fill"], out var color));
            Assert.Equal(new ClassColor(36, 104, 172, 128), color);
            Assert.True(window.FindControl<Button>("SaveBtn")!.IsEnabled);
            Assert.Equal(color, EntityDiagramStyle.Resolve(MerpeepsDocument.Load(window.CurrentSource).Nodes[0]).Fill);
            window.CanvasForTest.Undo(); Settle();
            Assert.False(node.Entity.InlineStyle.ContainsKey("fill"));
            Click(Field<ColourField>(panel, "Fill color"));
            Click(Field<Button>(Assert.Single(window.OwnedWindows), "Cancel color"));
            Assert.False(window.CanvasForTest.History!.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FlowchartUsesOneFullRangePickerInsteadOfALimitedSwatchRow()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("flowchart LR\nA[Alpha]\n");
        try
        {
            window.CanvasForTest.SelectByIds(["A"]); Settle();
            var host = window.FindControl<ItemsControl>("FillSwatches")!;
            Assert.Single(host.Items);
            var field = Assert.IsType<ColourField>(host.Items[0]);
            Click(field);
            var dialog = Assert.Single(window.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#13AB79";
            Click(Field<Button>(dialog, "Use color")); Settle();
            Assert.Equal("#13AB79", window.CanvasForTest.Selected!.Fill);
            Click(field);
            Click(Field<Button>(Assert.Single(window.OwnedWindows), "Use default")); Settle();
            Assert.True(NodeAppearance.For(window.CanvasForTest.Selected!, true).IsPlain);
            window.CanvasForTest.Undo(); Settle();
            Assert.Equal("#13AB79", window.CanvasForTest.Selected.Fill);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingAColorAfterEditingTheNameUsesTheNewActiveInspector()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("erDiagram\nA\n");
        try
        {
            window.CanvasForTest.SelectByIds(["A"]); Settle();
            var panel = window.FindControl<Border>("ClassInspectorHost")!;
            ShowAppearance(panel); Settle();
            var name = Field<TextBox>(panel, "Entity name");
            name.Focus(); name.Text = "Accounts";
            var color = Field<ColourField>(panel, "Fill color");
            color.BringIntoView(); Settle();
            var point = color.TranslatePoint(new(color.Bounds.Width / 2, color.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.MouseUp(point, Avalonia.Input.MouseButton.Left); Settle();
            Assert.True(window.CanvasForTest.Model!.Nodes[0].Display == "Accounts",
                $"Color target {point}, window {window.Bounds}, name focused {name.IsFocused}");
            var dialog = Assert.Single(window.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#FC8012";
            Click(Field<Button>(dialog, "Use color")); Settle();
            Assert.Equal("#FC8012", window.CanvasForTest.Model.Nodes[0].Entity!.InlineStyle["fill"]);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public void ChartThemeColorsAndSeriesPaletteOpenColorDialogs()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("xychart\nline [1,2,3]\n");
        try
        {
            window.FindControl<MenuItem>("MenuClassDisplay")!.RaiseClick(); Settle();
            var panel = window.FindControl<Border>("ClassInspectorHost")!;
            var option = Field<ComboBox>(panel, "Option");
            option.SelectedItem = option.Items.Cast<object>().Single(c => c.ToString() == "theme.xyChart.plotColorPalette"); Settle();
            Click(Field<ColourField>(panel, "Series color 1"));
            var dialog = Assert.Single(window.OwnedWindows);
            Field<TextBox>(dialog, "Hex color").Text = "#DA2197";
            Click(Field<Button>(dialog, "Use color")); Settle();
            Click(Field<Button>(panel, "Add series color"));
            Assert.Equal("#DA2197", Field<ColourField>(panel, "Series color 1").Value);
            Click(Field<Button>(panel, "Apply chart option")); Settle();
            Assert.Contains("#DA2197", window.CurrentSource);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void DialogRendersWithAllChannelsAndNoClippedConfirmation(bool dark)
    {
        var owner = new Window(); owner.Show();
        var dialog = new ColourPickerWindow("Fill color", "#2468AC80", true, "") { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            _ = dialog.ShowDialog(owner); Settle();
            var confirm = Field<Button>(dialog, "Use color");
            var point = confirm.TranslatePoint(default, dialog)!.Value;
            Assert.True(point.Y + confirm.Bounds.Height <= dialog.Bounds.Height);
            Assert.All(new[] { "Red", "Green", "Blue", "Alpha" }, name => Assert.NotNull(Field<TextBox>(dialog, name)));
            if (Environment.GetEnvironmentVariable("MERPEEPS_COLOR_CAPTURES") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path);
                using var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException("No color dialog frame.");
                frame.Save(Path.Combine(path, dark ? "color-dark.png" : "color-light.png"));
            }
        }
        finally { dialog.Close(); owner.Close(); }
    }

    internal static T Field<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    internal static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
    private static void ShowAppearance(Visual panel)
    {
        var more = panel.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .FirstOrDefault(b => AutomationProperties.GetName(b) == "More properties");
        if (more is not null) more.IsChecked = true;
        Settle();
        Field<Expander>(panel, "Appearance").IsExpanded = true;
    }
    private static void Settle() => Dispatcher.UIThread.RunJobs();
}
