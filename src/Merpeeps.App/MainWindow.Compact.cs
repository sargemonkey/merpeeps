using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public partial class MainWindow
{
    private bool _syncCompactTools;

    private void WireCompactWorkspace()
    {
        WireArrangement();
        Need<Button>("DocumentButton").Click += (_, _) => ShowNewGallery();
        Need<Button>("DiagramSettingsButton").Click += (_, _) => _classInspector?.DisplayOptions();
        Need<Button>("ExportButton").Click += async (_, _) => await ExportSvg();
        Need<Button>("ThemeToggle").Click += (_, _) =>
            Need<MenuItem>(ActualThemeVariant == ThemeVariant.Dark ? "MenuThemeLight" : "MenuThemeDark")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var select = Need<ToggleButton>("SelectToggle");
        select.IsCheckedChanged += (_, _) =>
        {
            if (_syncCompactTools) return;
            _canvas.Tool = CanvasTool.Select;
            SyncTools();
        };
        _canvas.ToolChanged += (_, _) => SyncTools();
        void SyncTools()
        {
            _syncCompactTools = true;
            try { select.IsChecked = _canvas.Tool == CanvasTool.Select; }
            finally { _syncCompactTools = false; }
        }
        Need<ToggleButton>("ItemsToggle").Click += (_, _) => ShowOutline(false);
        Need<ToggleButton>("OutlineToggle").Click += (_, _) => ShowOutline(true);
        foreach (var name in new[] { "OpenBtn", "SaveBtn", "UndoBtn", "RedoBtn", "FitBtn", "RelayoutBtn", "HandToggle", "SelectToggle", "StylingToggle", "ThemeToggle", "DiagramSettingsButton", "ItemsToggle", "OutlineToggle" })
        {
            var control = Need<Control>(name);
            var label = AutomationProperties.GetName(control) ?? name;
            var tip = ToolTip.GetTip(control);
            UiIcon.Describe(control, label);
            ToolTip.SetTip(control, tip ?? label);
        }
    }

    private void ShowOutline(bool show)
    {
        Need<ToggleButton>("ItemsToggle").IsChecked = !show;
        Need<ToggleButton>("OutlineToggle").IsChecked = show;
        Need<ScrollViewer>("OutlineScroller").IsVisible = show;
        SyncClassWorkspace();
        SetPaneVisibility(Need<Control>("NodePane"), Need<Grid>("WorkspaceGrid"), 0, 1, PaletteWidth,
            Need<MenuItem>("MenuShowNodes").IsChecked);
        if (show) RefreshOutline();
    }

    private void RefreshOutline()
    {
        if (this.FindControl<ToggleButton>("OutlineToggle")?.IsChecked != true) return;
        var items = Need<StackPanel>("OutlineItems");
        items.Children.Clear();
        if (_canvas.Model is not { } model) return;
        _classInspector!.FillOutline(items);
        foreach (var node in model.Nodes)
            Add(node.Display, () => _canvas.SelectByIds([node.Id]));
        foreach (var edge in model.Edges)
            Add((model.Node(edge.From)?.Display ?? edge.From) + " → " + (model.Node(edge.To)?.Display ?? edge.To), () => _canvas.SelectEdge(edge));
        if (items.Children.Count == 0) items.Children.Add(new TextBlock { Text = "No elements yet.", TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(8) });
        void Add(string label, Action select)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Classes = { "ghost" },
            };
            AutomationProperties.SetName(button, "Select " + label);
            button.Click += (_, _) => { select(); _canvas.RevealFirstSelected(); };
            items.Children.Add(button);
        }
    }
}
