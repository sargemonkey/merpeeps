using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Render;

namespace Merpeeps.App;

/// <summary>
/// Menu, node list, theming and the plain/styled preview.
///
/// <para>Kept apart from the constructor wiring so the window's setup stays
/// readable; these are cohesive features rather than a grab bag.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Find a named control, or say which one is missing.
    ///
    /// <para><c>FindControl&lt;T&gt;(name)!</c> turns a renamed or deleted XAML
    /// element into a bare NullReferenceException at startup, pointing at the
    /// line that used it rather than the name that was wrong. Losing a minute
    /// to that once is enough.</para>
    /// </summary>
    private T Need<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"the XAML has no {typeof(T).Name} named '{name}'");

    // ------------------------------------------------------------------ menu

    private void WireMenu(ThemeVariant startupTheme)
    {
        void On(string name, Action act) =>
            Need<MenuItem>(name).Click += (_, _) => act();

        On("MenuNew", async () => await NewDiagram());
        On("MenuOpen", () => OnOpen(this, new Avalonia.Interactivity.RoutedEventArgs()));
        On("MenuSave", () => OnSave(this, new Avalonia.Interactivity.RoutedEventArgs()));
        On("MenuSaveAs", async () => await SaveAs());
        On("MenuExport", async () => await ExportPlain());
        On("MenuExportBaked", async () => await ExportBaked());
        On("MenuExportSvg", async () => await ExportSvg());
        On("MenuSample", () => { if (CanLeaveInspectorDraft()) LoadSource(Sample, null); });
        On("MenuExit", () =>
        {
            if (!CanLeaveInspectorDraft()) return;
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        });

        On("MenuUndo", () => { _canvas.Undo(); _canvas.Focus(); });
        On("MenuRedo", () => { _canvas.Redo(); _canvas.Focus(); });
        On("MenuSelectAll", () => { _canvas.SelectAll(); _canvas.Focus(); });
        On("MenuDelete", () => { _canvas.DeleteSelection(); _canvas.Focus(); });
        On("MenuRename", () =>
        {
            if (_canvas.IsWorkflowDiagram || _canvas.IsOwnershipDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || _canvas.Model?.Requirements is not null || _canvas.Model?.GitDiagram is not null || _canvas.Model?.Sankey is not null || _canvas.Model?.Treemap is not null || _canvas.Model?.Blocks is not null || _canvas.Model?.Architecture is not null || _canvas.Model?.C4 is not null || _canvas.Model?.Gantt is not null) _classInspector?.EditStructuralSelection();
            else if (_canvas.Model?.Outline is not null || _canvas.Model?.Temporal is not null || _canvas.Model?.Packet is not null || _canvas.Model?.Kanban is not null) _classInspector?.EditDataSelection();
            else if (_canvas.Model?.Chart is not null) _classInspector?.EditChartSelection();
            else if (_canvas.Model?.Sequence is not null) _classInspector?.EditSequenceSelection();
            else { _canvas.RenameSelected(); _canvas.Focus(); }
        });
        On("MenuClassAdd", () => _classInspector?.AddClass());
        On("MenuClassNote", () => _classInspector?.AddDiagramNote());
        On("MenuClassNamespaces", () => _classInspector?.ManageNamespaces());
        On("MenuClassDisplay", () => _classInspector?.DisplayOptions());
        On("MenuClassNavigate", () => _classInspector?.NavigateElements());
        On("MenuDiagramTitle", () => _classInspector?.EditDiagramTitle());
        On("MenuQuadrantLabels", () => _classInspector?.EditQuadrantLabels());
        On("MenuArchitectureAlignment", () => _classInspector?.EditArchitectureAlignment());
        On("MenuGanttReference", () => _canvas.SetGanttContext(EditorCanvas.CaptureGanttContext()));
        On("MenuIshikawaProblem", () => _classInspector?.DisplayOptions());

        On("MenuZoomIn", () => { _canvas.ZoomBy(1.2); _canvas.Focus(); });
        On("MenuZoomOut", () => { _canvas.ZoomBy(1 / 1.2); _canvas.Focus(); });
        On("MenuZoom100", () => { _canvas.ResetZoom(); _canvas.Focus(); });
        On("MenuFit", () => { _canvas.FitToView(); _canvas.Focus(); });
        On("MenuRelayout", Relayout);

        // Pane visibility. Collapsing a column rather than hiding the control
        // so the splitter and the space both go with it.
        WirePaneToggle("MenuShowNodes", "NodePane", 0, 1, 80);
        WirePaneToggle("MenuShowSource", "SourcePane", 6, 5, 360);
        Need<ToggleButton>("SourceToggle").IsCheckedChanged += (_, _) =>
            SetSourcePaneVisible(Need<ToggleButton>("SourceToggle").IsChecked == true);
        SizeChanged += (_, _) => UpdateWorkspacePanes();
        _canvas.SizeChanged += (_, _) =>
        {
            if (_canvas.Model?.Blocks is not null || _canvas.Model?.Architecture is not null || _canvas.Model?.C4 is not null) _canvas.FitToView();
        };
        WireReferencePane();

        var styling = Need<MenuItem>("MenuShowStyling");
        styling.Click += (_, _) => SetStyling(styling.IsChecked);

        WireTheme(startupTheme);
        WireDiagramTheme();

        // The Layout > Engine submenu mirrors the toolbar dropdown, built from
        // the same list so the two cannot drift apart.
        var engines = Need<MenuItem>("MenuEngines");
        var items = new List<MenuItem>();
        foreach (var (choice, index) in EditorLayouts.All.Select((c, i) => (c, i)))
        {
            var item = new MenuItem
            {
                Header = choice.Label,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "Engine",
                IsChecked = index == 0,
            };
            item.Click += (_, _) =>
            {
                Need<ComboBox>("EngineBox").SelectedIndex = index;
            };
            ToolTip.SetTip(item, choice.Description);
            items.Add(item);
        }
        engines.ItemsSource = items;

        On("MenuShortcuts", ShowShortcuts);
        On("MenuAbout", ShowAbout);
    }

    /// <summary>
    /// Show or hide a pane by collapsing its grid column and the splitter next
    /// to it. Hiding only the control would leave an empty gutter behind.
    /// </summary>
    private void WirePaneToggle(
        string menuName, string paneName, int column, int splitterColumn, double width)
    {
        var menu = Need<MenuItem>(menuName);
        menu.Click += (_, _) =>
        {
            var pane = Need<Control>(paneName);
            var grid = (Grid)pane.Parent!;
            var show = menu.IsChecked;
            if (paneName == "SourcePane") SetSourcePaneVisible(show);
            else SetPaneVisibility(pane, grid, column, splitterColumn,
                paneName == "NodePane" ? PaletteWidth : width, show);
        };
    }
    private bool? _compactWorkspace;
    private void UpdateWorkspacePanes()
    {
        var compact = Bounds.Width is > 0 and < 1100;
        var changed = _compactWorkspace != compact;
        _compactWorkspace = compact;
        var grid = Need<Grid>("WorkspaceGrid");
        if (changed) grid.ColumnDefinitions[4].Width = new GridLength(compact ? 264 : 288);
        var show = Need<MenuItem>("MenuShowNodes").IsChecked;
        if (changed || Need<Control>("NodePane").IsVisible != show)
            SetPaneVisibility(Need<Control>("NodePane"), grid, 0, 1, PaletteWidth, show);
        Need<ComboBox>("EngineBox").IsVisible = !compact && Need<Button>("RelayoutBtn").IsVisible;
        Need<ComboBox>("DiagramThemeBox").IsVisible = !compact;
    }
    private double PaletteWidth => Need<ToggleButton>("OutlineToggle").IsChecked == true ? 216 : 80;
    private void SetSourcePaneVisible(bool show)
    {
        Need<MenuItem>("MenuShowSource").IsChecked = show;
        if (Need<ToggleButton>("SourceToggle").IsChecked != show)
            Need<ToggleButton>("SourceToggle").IsChecked = show;
        var pane = Need<Control>("SourcePane");
        if (pane.IsVisible != show)
            SetPaneVisibility(pane, (Grid)pane.Parent!, 6, 5, 360, show);
    }
    private void SetPaneVisibility(Control pane, Grid grid, int column, int splitterColumn, double width, bool show)
    {
        if (!show && pane.IsKeyboardFocusWithin) _canvas.Focus();
        pane.IsVisible = show;
        grid.ColumnDefinitions[column].Width = new GridLength(show ? width : 0);
        grid.ColumnDefinitions[splitterColumn].Width = show ? GridLength.Auto : new GridLength(0);
        foreach (var splitter in grid.Children.OfType<GridSplitter>())
            if (Grid.GetColumn(splitter) == splitterColumn) splitter.IsVisible = show;
    }

    // ----------------------------------------------------------------- theme

    /// <summary>
    /// Wire the side-by-side reference render.
    ///
    /// <para>Off by default and created lazily. A webview is expensive to start
    /// and needs a platform web engine, so an editor that spun one up on launch
    /// would be slower for everyone to serve the minority who want a comparison
    /// open.</para>
    /// </summary>
    private void WireReferencePane()
    {
        var split = Need<Grid>("CanvasSplit");
        var toggle = Need<MenuItem>("MenuShowReference");
        var modeBox = Need<ComboBox>("ReferenceModeBox");

        modeBox.ItemsSource = ReferenceRenderPane.Choices.Select(c => c.Label).ToList();
        modeBox.SelectedIndex = 0;

        void Collapse(bool collapsed)
        {
            // Both, and both are needed. Zeroing the column reclaims the space,
            // but a zero-width Grid column still lays its children out and
            // Avalonia does not clip them by default — the mode dropdown drew
            // straight over the property pane next door. IsVisible is what
            // actually takes the pane out of the visual tree.
            split.ColumnDefinitions[1].Width = new GridLength(collapsed ? 0 : 4);
            split.ColumnDefinitions[2].Width =
                collapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

            Need<GridSplitter>("ReferenceSplitter").IsVisible = !collapsed;
            Need<DockPanel>("ReferencePane").IsVisible = !collapsed;
        }

        Collapse(true);

        // Kept so the pane can be opened from elsewhere. Choosing a borrowed
        // layout has to open it, and setting the menu item's IsChecked is not
        // enough on its own: the pane would stay collapsed, the web view would
        // never lay out, and the harvest would come back empty — which looked
        // exactly like a broken layout engine.
        _openReferencePane = () =>
        {
            toggle.IsChecked = true;
            Collapse(false);
        };

        toggle.Click += (_, _) =>
        {
            var on = toggle.IsChecked;
            Collapse(!on);
            if (on) ShowReference(force: true);
        };

        modeBox.SelectionChanged += (_, _) =>
        {
            if (toggle.IsChecked) ShowReference(force: true);
        };
    }

    /// <summary>Opens the reference pane for real — menu tick and layout both.</summary>
    private Action? _openReferencePane;

    /// <summary>Push the current document into the reference pane, if it is open.</summary>
    private void ShowReference(bool force = false)
    {
        var toggle = this.FindControl<MenuItem>("MenuShowReference");
        if (toggle?.IsChecked != true) return;

        var host = Need<Border>("ReferenceHost");

        if (_reference is null)
        {
            _reference = new ReferenceRenderPane();
            host.Child = _reference;
        }

        var picked = this.FindControl<ComboBox>("ReferenceModeBox")?.SelectedIndex ?? 0;
        if (picked < 0 || picked >= ReferenceRenderPane.Choices.Length) picked = 0;
        var choice = ReferenceRenderPane.Choices[picked];

        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        if (force) _reference.SetMode(choice.Mode, choice.Layout, dark);

        _ = _reference.ShowAsync(CurrentSource);
    }

    private ReferenceRenderPane? _reference;

    // ----------------------------------------------------------------- theme

    /// <summary>
    /// Wires the Diagram theme submenu — mermaid's own themes, stored in the
    /// file's frontmatter under <c>config: theme:</c>.
    /// </summary>
    /// <remarks>
    /// Written to mermaid's key rather than the Merpeeps block on purpose: this
    /// is a setting every renderer already understands, so a themed diagram
    /// looks the same on GitHub as it does here with nothing installed. The
    /// editor's job is to offer it, not to own it.
    ///
    /// <para>"None" is offered and is not the same as Default. Removing the key
    /// leaves the file exactly as it was before anyone touched the theme, which
    /// matters for a document someone else maintains.</para>
    /// </remarks>
    private void WireDiagramTheme()
    {
        var root = Need<MenuItem>("MenuDiagramTheme");
        var items = new List<MenuItem>();

        void Add(string header, string? theme, string tip)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "DiagramTheme",
                Tag = theme,
            };
            item.Click += (_, _) => SetDiagramTheme(theme);
            ToolTip.SetTip(item, tip);
            items.Add(item);
        }

        Add("_None", null, "No theme in the file — each renderer uses its own default");
        foreach (var entry in DiagramPalette.All)
            Add(entry.Label, entry.Name, $"mermaid's '{entry.Name}' theme");

        root.ItemsSource = items;
        _diagramThemeItems = items;

        // The toolbar dropdown offers exactly the same choices in the same
        // order, built from the same list, so the two cannot drift apart.
        var box = Need<ComboBox>("DiagramThemeBox");
        box.ItemsSource = items.Select(i => ((string)i.Header!).Replace("_", "")).ToList();
        box.SelectionChanged += (_, _) =>
        {
            if (_suppressPanelEvents) return;
            var i = box.SelectedIndex;
            if (i >= 0 && i < _diagramThemeItems.Count)
                SetDiagramTheme(_diagramThemeItems[i].Tag as string);
        };

        SyncDiagramTheme();
    }

    private List<MenuItem> _diagramThemeItems = new();

    /// <summary>Record a theme in the document and recolour the canvas to match.</summary>
    private void SetDiagramTheme(string? theme)
    {
        if (_canvas.Model is not { } model) return;
        if (model.Theme == theme) return;

        model.Theme = theme;
        _canvas.DiagramTheme = DiagramPalette.Find(theme);
        _dirty = true;

        RefreshSource();
        ShowReference();   // the pane reads the theme straight from the frontmatter
        UpdateStatus();
        SyncDiagramTheme();

        // Only when the user picks one. Opening a file that already names a
        // theme is not a surprise that needs explaining, and a message on every
        // open would be noise.
        FlashStylingTooltip();
    }

    /// <summary>Tick the menu item matching the document, without firing Click.</summary>
    private void SyncDiagramTheme()
    {
        var theme = _canvas.Model?.Theme;
        _canvas.DiagramTheme = DiagramPalette.Find(theme);

        var index = -1;
        for (var i = 0; i < _diagramThemeItems.Count; i++)
        {
            var match = (_diagramThemeItems[i].Tag as string) == theme;
            _diagramThemeItems[i].IsChecked = match;
            if (match) index = i;
        }

        // The styling toggle explains how the two interact, so it has to be
        // refreshed whenever either of them moves.
        SyncStylingTooltip();

        // A theme the file names but this build does not know keeps the
        // dropdown blank rather than claiming a theme that is not in the file.
        var box = this.FindControl<ComboBox>("DiagramThemeBox");
        if (box is null) return;

        _suppressPanelEvents = true;
        try { box.SelectedIndex = index; }
        finally { _suppressPanelEvents = false; }
    }

    /// <summary>
    /// Show the styling toggle's explanation without waiting to be hovered.
    /// </summary>
    /// <remarks>
    /// <para>Setting the tooltip text is not enough, and assuming otherwise was
    /// the mistake: a tooltip only appears on hover, so it reaches exactly the
    /// people who already suspect something is wrong — never the person who
    /// picked a theme, saw nothing happen, and concluded the feature is broken.
    /// That is the case it exists for.</para>
    ///
    /// <para>Shown only when there is genuinely a conflict, and dismissed on its
    /// own. A message that appeared every time, or stayed until clicked, would
    /// be noise within a minute.</para>
    /// </remarks>
    private void FlashStylingTooltip()
    {
        var toggle = this.FindControl<ToggleButton>("StylingToggle");
        if (toggle is null) return;

        var model = _canvas.Model;
        if (model?.Theme is null) return;
        if (!_canvas.ShowMerpeepsStyling) return;
        if (!model.Nodes.Any(n => n.Fill is not null || n.Stroke is not null)) return;

        // Tooltips default to PlacementMode.Pointer, which is right for hover and
        // wrong here: opening one from code has no pointer to anchor to, so it
        // lands in the corner of the screen, hundreds of pixels from the toggle it
        // is talking about. Anchoring to the control keeps the two together
        // whichever way the tooltip was opened.
        ToolTip.SetPlacement(toggle, PlacementMode.BottomEdgeAlignedRight);

        _tooltipTimer?.Stop();
        ToolTip.SetIsOpen(toggle, true);

        _tooltipTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _tooltipTimer.Tick -= CloseStylingTooltip;
        _tooltipTimer.Tick += CloseStylingTooltip;
        _tooltipTimer.Start();
    }

    private void CloseStylingTooltip(object? sender, EventArgs e)
    {
        _tooltipTimer?.Stop();
        if (this.FindControl<ToggleButton>("StylingToggle") is { } toggle)
            ToolTip.SetIsOpen(toggle, false);
    }

    private DispatcherTimer? _tooltipTimer;

    /// <summary>
    /// Wires the Theme submenu and applies the startup choice, ticking the menu
    /// item that matches so the menu never disagrees with the window.
    /// </summary>
    private void WireTheme(ThemeVariant startupTheme)
    {
        var picks = new List<(MenuItem Item, ThemeVariant Variant)>();

        void Pick(string name, ThemeVariant variant)
        {
            var item = Need<MenuItem>(name);
            item.Click += (_, _) => ApplyTheme(variant);
            picks.Add((item, variant));
        }

        Pick("MenuThemeLight", ThemeVariant.Light);
        Pick("MenuThemeDark", ThemeVariant.Dark);
        Pick("MenuThemeSystem", ThemeVariant.Default);

        // Following the system means reacting when it changes underneath us.
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => SyncCanvasTheme();

        foreach (var (item, variant) in picks) item.IsChecked = variant == startupTheme;

        ApplyTheme(startupTheme);
    }

    private void ApplyTheme(ThemeVariant variant)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = variant;
        SyncCanvasTheme();
    }

    /// <summary>
    /// Point the canvas at the palette matching the resolved theme. The canvas
    /// draws with its own brushes rather than styled resources, because a
    /// diagram needs colours chosen for legibility on a page, not chrome.
    /// </summary>
    private void SyncCanvasTheme()
    {
        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        _canvas.CanvasColours = dark ? CanvasTheme.Dark : CanvasTheme.Light;
    }

    // ------------------------------------------------------- styling preview

    /// <summary>
    /// The ordinary hint line.
    /// </summary>
    /// <remarks>
    /// A constant because it is shown from two places — the XAML sets it at
    /// startup and <see cref="SetStyling"/> restores it — and the two had
    /// already drifted apart, so editing the visible one silently changed
    /// nothing the moment anybody toggled styling. Panning leads, because the
    /// gesture people miss is the one worth naming first.
    /// </remarks>
    internal const string CanvasHint =
        "Pan: hold space, press H, or right-drag · Zoom: Ctrl+scroll · " +
        "Double-click canvas to add, node or F2 to rename · " +
        "drag a green handle to connect · click a link to select it · " +
        "Delete removes · Shift-click adds/removes objects · drag background to select · " +
        "Layout menu aligns and distributes the selection";

    private void WireStylingToggle()
    {
        var toggle = Need<ToggleButton>("StylingToggle");
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_suppressPanelEvents) return;
            SetStyling(toggle.IsChecked == true);
        };
    }

    /// <summary>
    /// Connect the pan button to the canvas tool, in both directions.
    /// </summary>
    /// <remarks>
    /// <para>The button exists because panning was reported missing while four
    /// gestures for it already worked. They were documented in the hint bar and
    /// that was not enough: a capability with no visible control is one people
    /// conclude you do not have. The gestures stay — this only makes them
    /// findable.</para>
    /// <para>Listening to <see cref="EditorCanvas.ToolChanged"/> matters as much
    /// as setting the tool. H and Escape change it from the keyboard, and a
    /// button that only wrote would then sit unlit while the canvas panned,
    /// which is worse than having no button at all.</para>
    /// </remarks>
    private void WireHandToggle()
    {
        var toggle = Need<ToggleButton>("HandToggle");

        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_suppressPanelEvents) return;
            _canvas.Tool = toggle.IsChecked == true ? CanvasTool.Hand : CanvasTool.Select;

            // Keys only reach the canvas when it holds focus, and clicking the
            // button takes it away — so Escape and H would stop working right
            // after the one action most likely to be followed by them.
            _canvas.Focus();
        };

        _canvas.ToolChanged += (_, _) =>
        {
            _suppressPanelEvents = true;
            try { toggle.IsChecked = _canvas.Tool == CanvasTool.Hand; }
            finally { _suppressPanelEvents = false; }
        };
    }

    /// <summary>
    /// Switch between the Merpeeps view and what a stock mermaid renderer would
    /// draw. The toolbar button and the menu item are two doors to one state,
    /// so both are updated together.
    /// </summary>
    private void SetStyling(bool on)
    {
        _canvas.ShowMerpeepsStyling = on;

        _suppressPanelEvents = true;
        try
        {
            Need<ToggleButton>("StylingToggle").IsChecked = on;
            Need<MenuItem>("MenuShowStyling").IsChecked = on;
        }
        finally { _suppressPanelEvents = false; }

        Need<TextBlock>("HintText").Text = on
            ? CanvasHint
            : "Showing the plain diagram — this is what a stock mermaid renderer draws. Merpie styling is hidden, not removed.";

        SyncStylingTooltip();
        UpdateStatus();
        _canvas.Focus();
    }

    /// <summary>
    /// Explain, on the styling toggle, why a chosen theme may not be visible.
    /// </summary>
    /// <remarks>
    /// <para>Merpeeps styling sits above the theme, which is exactly what mermaid
    /// does — measured: under <c>forest</c> an unstyled node is #CDE498, and one
    /// carrying <c>style a fill:#FF0000</c> is red. The precedence is also
    /// load-bearing rather than merely faithful, because <c>merpeeps bake</c>
    /// rewrites Merpeeps styling as <c>style</c> statements; if the theme won on
    /// the canvas, the canvas and the baked file would disagree.</para>
    ///
    /// <para>But the consequence is confusing without a word of explanation:
    /// pick a theme on a fully styled diagram and nothing appears to happen. The
    /// toggle is already the switch that resolves it, so the explanation belongs
    /// there — and it names the number of nodes involved, because "some nodes
    /// override the theme" invites the question this is supposed to answer.</para>
    /// </remarks>
    private void SyncStylingTooltip()
    {
        var toggle = this.FindControl<ToggleButton>("StylingToggle");
        if (toggle is null) return;

        const string Plain =
            "Off shows what a stock mermaid renderer would draw";

        var model = _canvas.Model;
        var theme = model?.Theme;

        if (model is null || theme is null)
        {
            ToolTip.SetTip(toggle, Plain);
            return;
        }

        var styled = model.Nodes.Count(n => n.Fill is not null || n.Stroke is not null);

        if (styled == 0)
        {
            ToolTip.SetTip(toggle,
                $"{Plain}.\n\nThe '{theme}' theme is colouring every node — none of "
                + "them carry a Merpie colour of their own.");
            return;
        }

        var total = model.Nodes.Count;

        // Two phrasings, because the two sentences need different grammar. One
        // shared fragment read "restore the 9 nodes have their own colour" —
        // the sort of thing that only shows up once the words are on screen.
        var owns = styled == 1 ? "node has its own" : "nodes have their own";
        var theirs = styled == 1 ? "node's own colour" : "nodes' own colours";

        ToolTip.SetTip(toggle, _canvas.ShowMerpeepsStyling
            ? $"Merpie styling is ON, so it overrides the '{theme}' theme.\n\n"
              + $"{styled} of {total} {owns} colour and keep it; the theme colours "
              + "the rest. Turn this off for the theme to apply everywhere.\n\n"
              + "This is what mermaid does too — an explicit style beats the theme."
            : $"Merpie styling is OFF, so the '{theme}' theme colours every node.\n\n"
              + $"Turn it back on to restore the {styled} {theirs}.");
    }

    // --------------------------------------------------------- shape palette

    private void WireNodeList()
    {
        var list = Need<ListBox>("PaletteList");

        // Double-click to place. A single click only selects, so browsing the
        // palette does not litter the canvas.
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is PaletteItem item) AddFromPalette(item);
        };

        // Show what the highlighted stencil is for, rather than making the
        // names carry all the meaning.
        list.SelectionChanged += (_, _) =>
        {
            Need<TextBlock>("PaletteHint").Text = list.SelectedItem is PaletteItem it
                && it.Hint.Length > 0
                ? it.Hint
                : "Double-click a shape to add it, or drag it onto the canvas.";
        };

        WirePaletteDrag(list);

        RefreshPalette();
    }

    private void RefreshPalette()
    {
        var kind = _canvas.Model?.Kind;
        var isState = kind is "stateDiagram" or "stateDiagram-v2";
        var isFlow = kind is "flowchart" or "graph" or "flowchart-elk";
        var items = isState
            ? NodePalette.State.Items.Where(i => i.Name != "Choice").ToArray()
            : isFlow ? NodePalette.Flowchart.Items : Array.Empty<PaletteItem>();
        Need<ListBox>("PaletteList").ItemsSource = items;
        var family = MermaidDiagramCatalog.Families.FirstOrDefault(f => f.Dialects.Any(d => d.Directive == kind));
        Need<TextBlock>("PaletteTitle").Text = isState ? "State diagram tools" : isFlow ? "Flowchart shapes" : family?.DisplayName ?? "Diagram items";
        Need<TextBlock>("PaletteTypeHint").Text = isState
            ? "States, start and end markers. File > New chooses a different diagram type."
            : isFlow ? "Shapes for this flowchart. File > New chooses a different diagram type."
            : _canvas.Model?.Temporal?.Family == TemporalFamily.Timeline ? "Time period adds a column; Event adds a detail beneath the selected period."
            : "Click a picture to add an element. Select it on the canvas to edit.";
    }

    /// <summary>
    /// Let a shape be dragged out of the palette and dropped on the canvas,
    /// landing where it was dropped.
    /// </summary>
    /// <remarks>
    /// <para>Double-click already added a shape, but always to the centre of the
    /// view — so placing three nodes meant adding one, dragging it aside, adding
    /// the next onto the same spot, and so on. Dropping where you meant it is
    /// the gesture every diagram tool has.</para>
    ///
    /// <para>Avalonia 12 replaced <c>DataObject</c> and <c>DoDragDrop</c> with
    /// <c>DataTransfer</c> and <c>DoDragDropAsync</c>, and the new call takes
    /// the <em>pressed</em> event rather than a moved one — so the drag is
    /// started from PointerPressed and the platform decides when the pointer has
    /// travelled far enough to count. A click that never moves returns None and
    /// the list selects as usual.</para>
    ///
    /// <para>An in-process format carries the item itself rather than a string
    /// to be parsed back: the drag never leaves the application, so there is
    /// nothing to serialise and nothing to get wrong on the way home.</para>
    /// </remarks>
    private void WirePaletteDrag(ListBox list)
    {
        list.AddHandler(InputElement.PointerPressedEvent,
            async (object? _, PointerPressedEventArgs e) =>
            {
                if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;

                // Read the item from what was pressed, not from SelectedItem:
                // selection changes after this event, so the first drag of a
                // session would otherwise carry the previously selected shape.
                if ((e.Source as Control)?.DataContext is not PaletteItem item) return;

                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.Create(PaletteFormat, item));
                await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
            },
            RoutingStrategies.Tunnel);

        // The canvas is the drop target, and so is the border around it: the
        // margin still reads as "the canvas" to anyone dropping onto it.
        var host = Need<Border>("CanvasHost");
        DragDrop.SetAllowDrop(host, true);
        DragDrop.SetAllowDrop(_canvas, true);

        void Over(object? _, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(PaletteFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        void Drop(object? _, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetValue(PaletteFormat) is not { } item) return;

            // Measured against the canvas, not the host, so the shape lands
            // under the pointer rather than offset by the host's border.
            try
            {
                _canvas.AddFromPalette(item, _canvas.ScreenToWorld(e.GetPosition(_canvas)));
                _dirty = true;
                RefreshSource();
                UpdateStatus();
            }
            catch (ArgumentException error) { ShowInfo("Cannot add this shape", error.Message); }
            e.Handled = true;
        }

        host.AddHandler(DragDrop.DragOverEvent, Over);
        host.AddHandler(DragDrop.DropEvent, Drop);
        _canvas.AddHandler(DragDrop.DragOverEvent, Over);
        _canvas.AddHandler(DragDrop.DropEvent, Drop);
    }

    /// <summary>
    /// The drag payload: the palette item itself, not a serialised copy.
    /// </summary>
    /// <remarks>
    /// In-process because the drag never leaves this application. Nothing is
    /// written to the clipboard and nothing has to be parsed back, so a shape
    /// cannot arrive subtly different from the one that was picked up.
    /// </remarks>
    private static readonly DataFormat<PaletteItem> PaletteFormat =
        DataFormat.CreateInProcessFormat<PaletteItem>("merpeeps-palette-item");

    /// <summary>
    /// Drop a palette shape into the middle of the current view, already
    /// styled, selected and ready to rename.
    /// </summary>
    private void AddFromPalette(PaletteItem item)
    {
        if (_canvas.Model is null) return;
        try
        {
            _canvas.AddFromPalette(item);
            _dirty = true;
            RefreshSource();
            UpdateStatus();
            _canvas.Focus();
        }
        catch (ArgumentException error) { ShowInfo("Cannot add this shape", error.Message); }
    }

    // ------------------------------------------------------------- dialogues

    private async Task SaveAs()
    {
        if (!CanSaveCurrentDiagram()) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagram as",
            DefaultExtension = "mmd",
            SuggestedFileName = Path.GetFileName(_path) ?? "diagram.mmd",
        });
        if (file is null) return;
        _path = file.Path.LocalPath;
        OnSave(this, new Avalonia.Interactivity.RoutedEventArgs());
    }

    /// <summary>
    /// Write the diagram with no Merpeeps metadata — the file to hand to a tool
    /// that only speaks mermaid. Saved alongside rather than over the original,
    /// because exporting is not the same as discarding your layout.
    /// </summary>
    private async Task ExportPlain()
    {
        if (!CanSaveCurrentDiagram()) return;
        if (_capabilities is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export plain mermaid",
            DefaultExtension = "mmd",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path ?? "diagram")
                                + "-plain.mmd",
        });
        if (file is null) return;

        var plain = MerpeepsDoc.Serialise(CurrentSource, null);
        await File.WriteAllTextAsync(file.Path.LocalPath, plain);
        Need<TextBlock>("StatusText").Text =
            $"exported {Path.GetFileName(file.Path.LocalPath)} (no metadata)";
    }

    /// <summary>
    /// Write the diagram as a standalone SVG.
    /// </summary>
    /// <remarks>
    /// Exports what this editor shows — the stored positions, the Merpeeps
    /// styling, the icons — rather than what stock mermaid would draw. Those
    /// last two are precisely what mermaid cannot express, so exporting its
    /// version would hand the user a picture missing the parts they came here
    /// to add. <c>merpeeps bake</c> covers the other need: a file that renders
    /// with the styling anywhere, unaided.
    /// </remarks>
    private async Task ExportSvg()
    {
        if (!CanSaveCurrentDiagram()) return;
        var model = _canvas.Model;
        if (model is null) return;
        string svg;
        try { svg = SvgExporter.ToSvg(model, evaluationContext: model.Gantt is null ? null : _canvas.GanttContext); }
        catch (NotSupportedException error)
        {
            ShowInfo("SVG export unavailable", error.Message + "\n\nNothing was written. The source and diagram remain unchanged.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as SVG",
            DefaultExtension = "svg",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path ?? "diagram") + ".svg",
        });
        if (file is null) return;

        await File.WriteAllTextAsync(file.Path.LocalPath, svg);
        Need<TextBlock>("StatusText").Text =
            $"exported {Path.GetFileName(file.Path.LocalPath)}";
    }

    /// <summary>
    /// Write plain mermaid that keeps the styling, as native <c>style</c>
    /// statements.
    /// </summary>
    /// <remarks>
    /// <para>The export most people actually want, and it was command-line only
    /// until now — which made it invisible to anyone who never opens a terminal.
    /// "Export plain mermaid" strips the Merpeeps block and the colours with it;
    /// SVG keeps everything but stops being a diagram. This is the middle: a file
    /// that still renders your colours on GitHub, and is still text you can
    /// edit.</para>
    ///
    /// <para>Lossy by nature, and the loss is reported rather than left to be
    /// discovered. The CLI writes its notes to stderr; nobody in a window sees
    /// stderr, so here it is a dialog — but only when something was actually
    /// dropped, because a dialog that appears every time is one nobody reads.</para>
    ///
    /// <para>Always to a new file. Baking mutates the diagram body, and the whole
    /// design rests on the body being untouched, so overwriting the document in
    /// place would trade away the invariant that makes the format work.</para>
    /// </remarks>
    private async Task ExportBaked()
    {
        if (!CanSaveCurrentDiagram()) return;
        if (_capabilities is null) return;

        var result = MermaidBake.Bake(CurrentSource);

        // Ask before asking for a filename. Some grammars reject `style`
        // outright, so there is nothing to bake and the output would be an
        // ordinary strip — making someone choose a path for that is a waste of
        // their time, and writing it silently would answer a question they did
        // not ask.
        if (result.UnsupportedKind is { } kind)
        {
            ShowInfo("Export with styling baked in",
                (result.Diagnostic ?? $"This build cannot bake '{kind}'.") +
                "\n\nNothing was written. Use a Mermaid renderer to preview the source; " +
                "native SVG export is unavailable for unsupported families.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export with styling baked in",
            DefaultExtension = "mmd",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path ?? "diagram")
                                + "-baked.mmd",
        });
        if (file is null) return;

        if (await WriteBaked(file.Path.LocalPath) is { } notice)
            ShowInfo("Export with styling baked in", notice);
    }

    /// <summary>
    /// Bake the document as it currently stands to <paramref name="path"/>,
    /// returning what the user should be told, or null when nothing was lost.
    /// </summary>
    /// <remarks>
    /// Everything the export does except choose the filename. Split out because
    /// the file picker is a native dialog that no headless test — and no
    /// synthetic click — can drive, which would otherwise leave the only path
    /// that touches disk untested.
    ///
    /// <para>Reads <see cref="CurrentSource"/>, not the file on disk, so an
    /// unsaved edit is carried into the export. Baking a stale copy of a document
    /// you are looking at would be a quiet way to hand someone the wrong file.</para>
    /// </remarks>
    internal async Task<string?> WriteBaked(string path)
    {
        var result = MermaidBake.Bake(CurrentSource);
        if (result.UnsupportedKind is not null)
        {
            var diagnostic = "Nothing was written. " + result.Diagnostic;
            Need<TextBlock>("StatusText").Text = diagnostic;
            return diagnostic;
        }
        await File.WriteAllTextAsync(path, result.Output);

        var name = Path.GetFileName(path);
        Need<TextBlock>("StatusText").Text =
            $"exported {name} — styling baked into {result.StyledNodes.Count} node(s)";

        return BakeNotice(result, name);
    }

    /// <summary>
    /// What to tell the user after a bake, or null when there is nothing worth
    /// interrupting them for.
    /// </summary>
    /// <remarks>
    /// <para>Separate from the export so it can be tested. The dialog text *is*
    /// the feature here — the export itself is one call into Core, which is
    /// already covered — and text is where the mistakes live. A previous message
    /// in this window shipped reading "restore the 9 nodes have their own
    /// colour", because one fragment was reused across two sentences needing
    /// different grammar.</para>
    ///
    /// <para>Null on a lossless bake. A dialog that appears every time is one
    /// nobody reads, including on the occasion it matters.</para>
    /// </remarks>
    internal static string? BakeNotice(MermaidBake.Result result, string fileName)
    {
        if (!result.Lossy) return null;

        var lines = new List<string>
        {
            $"Wrote {fileName} with styling for {result.StyledNodes.Count} node(s) " +
            "as native `style` statements. Those colours now show up anywhere " +
            "mermaid runs, with nothing installed.",
            "",
            "What could not come with it:",
        };

        // Named node by node, so it can be acted on rather than merely noticed.
        if (result.DroppedIcons.Count > 0)
        {
            lines.Add($"  · Icons on {Naming(result.DroppedIcons)}. Mermaid's icon shapes " +
                      "need a pack registered by the page at runtime, so no icon can be " +
                      "written into a file that has to render unaided.");
        }

        if (result.DroppedPositions.Count > 0)
        {
            lines.Add($"  · {result.DroppedPositions.Count} stored position(s). Mermaid has " +
                      "no syntax for placing a node, so it will lay the diagram out itself.");
        }

        lines.Add("");
        lines.Add("Your own file is untouched — this was written as a copy.");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Name a handful of ids, then stop counting aloud.
    /// </summary>
    /// <remarks>
    /// Listing every id on a large diagram produces a wall of text that is read
    /// as "lots" and no more usefully than a number would have been, while
    /// hiding the ones that would have been worth seeing.
    /// </remarks>
    private static string Naming(IReadOnlyList<string> ids) =>
        ids.Count <= 4
            ? string.Join(", ", ids)
            : $"{string.Join(", ", ids.Take(4))} and {ids.Count - 4} more";

    private void ShowShortcuts() => ShowInfo("Keyboard shortcuts", """
        Ctrl+N        New
        Ctrl+O        Open
        Ctrl+S        Save
        Ctrl+Z        Undo
        Ctrl+Shift+Z  Redo
        Ctrl+A        Select all
        Ctrl+F        Fit to window
        Ctrl+0        Actual size
        Ctrl+R        Re-layout
        F2            Rename the selected node
        Delete        Delete the selection

        Double-click canvas   Add a node
        Double-click node     Rename it
        Click a link          Select it (Delete removes, F2 edits the label)
        Double-click a link   Edit its label
        Drag green handle     Connect to another node
        Space-drag            Pan
        Middle-drag           Pan
        Right-drag            Pan
        Ctrl-drag             Pan
        Wheel                 Scroll up and down
        Shift+wheel           Scroll sideways
        Ctrl+wheel            Zoom about the pointer
        Drag background       Marquee-select
        """);

    private void ShowAbout()
    {
        var close = new Button { Name = "CloseAbout", Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "Close About Merpie");
        var window = new Window
        {
            Title = "About " + ProductBrand.Name,
            Icon = ProductBrand.WindowIcon,
            Width = 440, MaxHeight = 620, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 14,
                Children =
                {
                    new MerpieMascot { Name = "AboutMascot", Width = 144, Height = 144, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = ProductBrand.Name, FontSize = 24, FontWeight = FontWeight.SemiBold, Classes = { "display" }, HorizontalAlignment = HorizontalAlignment.Center },
                    new SelectableTextBlock
                    {
                        FontSize = 13, TextWrapping = TextWrapping.Wrap,
                        Text = """
                            A native visual editor for Mermaid diagrams.

                            Positions, styling and icons stay in your .mmd file. Existing x-merpeeps metadata, merpeeps CLI/MCP tools and Merpeeps library/package IDs remain compatible.

                            The sea-magpie combines the magpie's detailed head and feather streaks with a broad split mermaid tail.

                            Not affiliated with Mermaid Chart Inc. or the Mermaid project.
                            """,
                    },
                    close,
                },
            },
        };
        close.Click += (_, _) => window.Close();
        _ = window.ShowDialog(this);
    }

    /// <summary>Show a message in a modal window.</summary>
    /// <remarks>
    /// The text block sizes the window and the scroll viewer only caps it. The
    /// obvious arrangement — a ScrollViewer as the content, with
    /// <c>SizeToContent.Height</c> — collapses to an empty strip, because a
    /// scroll viewer reports no natural height of its own. Every dialogue in the
    /// app rendered as a black bar with a title until this was found; nobody had
    /// opened Help → About.
    /// </remarks>
    private void ShowInfo(string title, string body)
    {
        var text = new SelectableTextBlock
        {
            Text = body,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18),
        };

        var window = new Window
        {
            Title = title,
            Icon = ProductBrand.WindowIcon,
            Width = 460,
            MaxHeight = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = text,
            },
        };

        // Not awaited on purpose — callers are synchronous — but the dialogue is
        // modal to this window, so a second one cannot be opened behind it.
        _ = window.ShowDialog(this);
    }

    // --------------------------------------------------------------- testing

    /// <summary>
    /// The canvas, for headless UI tests.
    ///
    /// <para>Two narrow hooks rather than making the internals public: the
    /// tests drive the real controls and handlers, and only need a way in.</para>
    /// </summary>
    internal EditorCanvas CanvasForTest => _canvas;

    /// <summary>Load a document without going through a file dialogue.</summary>
    internal void LoadSourceForTest(string source) => LoadSource(source, null);
}
