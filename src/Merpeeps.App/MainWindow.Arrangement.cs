using Avalonia.Automation;
using Avalonia.Controls;
using Merpeeps.Core.Editing;

namespace Merpeeps.App;

public partial class MainWindow
{
    private readonly List<(GraphArrangement Arrangement, Button Button, MenuItem Menu)> _arrangementActions = new();

    private void WireArrangement()
    {
        var align = new List<MenuItem>();
        var distribute = new List<MenuItem>();
        Add(GraphArrangement.Left, UiGlyph.AlignLeft, "Align left");
        Add(GraphArrangement.HorizontalCenter, UiGlyph.AlignHorizontalCenter, "Align horizontal centers");
        Add(GraphArrangement.Right, UiGlyph.AlignRight, "Align right");
        Add(GraphArrangement.Top, UiGlyph.AlignTop, "Align top");
        Add(GraphArrangement.VerticalCenter, UiGlyph.AlignVerticalCenter, "Align vertical centers");
        Add(GraphArrangement.Bottom, UiGlyph.AlignBottom, "Align bottom");
        Add(GraphArrangement.DistributeHorizontally, UiGlyph.DistributeHorizontal, "Distribute horizontally");
        Add(GraphArrangement.DistributeVertically, UiGlyph.DistributeVertical, "Distribute vertically");
        Need<MenuItem>("MenuAlignSelection").ItemsSource = align;
        Need<MenuItem>("MenuDistributeSelection").ItemsSource = distribute;
        SyncArrangement();

        void Add(GraphArrangement arrangement, UiGlyph glyph, string name)
        {
            var distribution = IsDistribution(arrangement);
            var tip = name + (distribution
                ? " - equal gaps; the two end objects stay fixed."
                : " - relative to the selection's outer bounds.");
            var button = new Button { Content = new UiIcon { Glyph = glyph }, Classes = { "ghost", "icon-action" } };
            UiIcon.Describe(button, name);
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => RunArrangement(arrangement);
            Need<StackPanel>(distribution ? "DistributionActions" : "AlignmentActions").Children.Add(button);
            var menu = new MenuItem { Header = name };
            AutomationProperties.SetName(menu, name);
            ToolTip.SetTip(menu, tip);
            menu.Click += (_, _) => RunArrangement(arrangement);
            (distribution ? distribute : align).Add(menu);
            _arrangementActions.Add((arrangement, button, menu));
        }
    }

    private static bool IsDistribution(GraphArrangement arrangement) =>
        arrangement is GraphArrangement.DistributeHorizontally or GraphArrangement.DistributeVertically;

    private void SyncArrangement()
    {
        var supported = _canvas.SupportsArrangement;
        var count = _canvas.SelectedObjectCount;
        var multiple = supported && count >= 2;
        Need<Border>("ArrangementPanel").IsVisible = multiple;
        Need<TextBlock>("ArrangementSelectionCount").Text = $"{count} objects selected";
        Need<MenuItem>("MenuAlignSelection").IsEnabled = supported && count >= 2;
        Need<MenuItem>("MenuDistributeSelection").IsEnabled = supported && count >= 3;
        foreach (var (arrangement, button, menu) in _arrangementActions)
            button.IsEnabled = menu.IsEnabled = supported && count >= (IsDistribution(arrangement) ? 3 : 2);
        if (multiple && _classInspector?.HasPendingEdits != true)
            Need<Border>("ClassInspectorHost").IsVisible = false;
        else if (_canvas.Model?.ClassOptions is not null || _canvas.Model?.EntityOptions is not null)
            Need<Border>("ClassInspectorHost").IsVisible = true;
        if (multiple)
        {
            Need<StackPanel>("NodeProperties").IsVisible = false;
            Need<StackPanel>("EdgeProperties").IsVisible = false;
            Need<TextBlock>("SelInfo").IsVisible = false;
        }
        else Need<TextBlock>("SelInfo").IsVisible = true;
    }

    private void RunArrangement(GraphArrangement arrangement)
    {
        if (_classInspector?.TryFinishInlineEdit() == false)
        {
            ShowInfo("Finish the pending edit", "Correct, apply or cancel the pending properties before arranging objects.");
            return;
        }
        try
        {
            _canvas.ArrangeSelection(arrangement);
            _canvas.Focus();
        }
        catch (ArgumentException error)
        {
            ShowInfo("Cannot arrange this selection", error.Message);
        }
        SyncArrangement();
    }
}
