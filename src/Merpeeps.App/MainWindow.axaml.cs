using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public partial class MainWindow : Window
{
    private readonly EditorCanvas _canvas = new();
    private string _source = "";
    private string? _path;
    private bool _dirty;
    private bool _suppressPanelEvents;
    private DiagramCapabilities? _capabilities;
    private bool _openingDiagram;

    private static readonly (string Name, NodeShape Shape)[] ShapeChoices =
    {
        ("Rectangle", NodeShape.Rectangle),
        ("Rounded", NodeShape.RoundedRect),
        ("Stadium", NodeShape.Stadium),
        ("Diamond", NodeShape.Rhombus),
        ("Hexagon", NodeShape.Hexagon),
        ("Circle", NodeShape.Circle),
        ("Cylinder", NodeShape.Cylinder),
        ("Subroutine", NodeShape.Subroutine),
        ("Parallelogram", NodeShape.Parallelogram),
    };

    private const string Sample = """
        flowchart TD
            web[Web Client] --> gw{{API Gateway}}
            gw -->|authenticate| auth([Auth Service])
            gw --> orders[Order Service]
            orders --> pay([Payment Service])
            orders --> inv[(Inventory DB)]
            pay --> stripe[/Stripe API/]
            orders --> bus[[Event Bus]]
            bus -.-> ship[Shipping]
            bus -.-> notify[Notifications]
            notify --> email[/Email Provider/]
        """;

    public MainWindow() : this(StartupOptions.Defaults) { }
    public MainWindow(string? openPath) : this(StartupOptions.Defaults with { OpenPath = openPath }) { }
    public MainWindow(StartupOptions options)
    {
        InitializeComponent();

        this.FindControl<Border>("CanvasHost")!.Child = _canvas;

        // The styling tooltip counts how many nodes carry their own colour, so
        // it has to follow any edit that could change that count.
        _canvas.ModelChanged += (_, _) =>
        {
            _dirty = true;
            RefreshSource();
            SyncStylingTooltip();
            UpdateStatus();
            _classInspector?.Refresh();
            RefreshOutline();
        };
        _canvas.SelectionChanged += (_, _) => { SyncPanel(); UpdateStatus(); };

        this.FindControl<Button>("OpenBtn")!.Click += OnOpen;
        this.FindControl<Button>("SaveBtn")!.Click += OnSave;
        this.FindControl<Button>("UndoBtn")!.Click += (_, _) => { _canvas.Undo(); _canvas.Focus(); };
        this.FindControl<Button>("RedoBtn")!.Click += (_, _) => { _canvas.Redo(); _canvas.Focus(); };
        this.FindControl<Button>("FitBtn")!.Click += (_, _) => { _canvas.FitToView(); _canvas.Focus(); };
        this.FindControl<Button>("RelayoutBtn")!.Click += OnRelayout;

        WireMenu(options.Theme);
        _classInspector = new DiagramInspector(_canvas);
        _classInspector.PendingEditsChanged += (_, _) => UpdateStatus();
        _canvas.EntityRenameRequested += (_, _) => _classInspector.EditEntitySelection();
        _canvas.SequenceEditRequested += (_, _) => _classInspector.EditSequenceSelection();
        _canvas.ChartEditRequested += (_, _) => _classInspector.EditChartSelection();
        _canvas.DataEditRequested += (_, _) => _classInspector.EditDataSelection();
        _canvas.StructuralEditRequested += (_, _) => _classInspector.EditStructuralSelection();
        _canvas.ChartEditReady = () => !_classInspector.HasPendingEdits;
        _canvas.StrategyEditReady = () => !_classInspector.HasPendingEdits;
        this.FindControl<Border>("ClassInspectorHost")!.Child = _classInspector;
        this.FindControl<Border>("FamilyPaletteHost")!.Child = _classInspector.ItemPalette;
        Closing += (_, e) =>
        {
            if (_classInspector.HasPendingEdits)
            {
                e.Cancel = true;
                CanLeaveInspectorDraft();
            }
        };
        WireNodeList();
        WireStylingToggle();
        WireHandToggle();
        WireCompactWorkspace();

        this.FindControl<TextBlock>("HintText")!.Text = CanvasHint;

        var engineBox = this.FindControl<ComboBox>("EngineBox")!;
        engineBox.ItemsSource = EditorLayouts.All.Select(c => c.Label).ToList();
        engineBox.SelectedIndex = 0;
        engineBox.SelectionChanged += OnEngineChanged;

        var shapeBox = this.FindControl<ComboBox>("ShapeBox")!;
        shapeBox.ItemsSource = ShapeChoices.Select(c => c.Name).ToList();
        shapeBox.SelectionChanged += OnShapeChanged;

        WireEdgeProperties();

        var iconBox = this.FindControl<ComboBox>("IconBox")!;
        iconBox.ItemsSource = IconSet.Names
            .Select(n => n.Length == 0 ? "(none)" : n).ToList();
        iconBox.SelectionChanged += OnIconChanged;

        BuildColourField("FillSwatches", "Fill color",
            c => _canvas.ApplyStyle(new StyleNodesCommand.Style(Fill: c)));
        BuildColourField("StrokeSwatches", "Outline color",
            c => _canvas.ApplyStyle(new StyleNodesCommand.Style(Stroke: c)));

        this.FindControl<CheckBox>("DashedBox")!.IsCheckedChanged += (_, _) =>
        {
            if (_suppressPanelEvents) return;
            var on = this.FindControl<CheckBox>("DashedBox")!.IsChecked == true;
            _canvas.ApplyStyle(new StyleNodesCommand.Style(Dashed: on));
        };

        this.FindControl<Button>("ClearStyleBtn")!.Click += (_, _) =>
        {
            _canvas.ClearStyle();
            SyncPanel();
        };

        Opened += (_, _) =>
        {
            if (options.OpenPath is { } path && File.Exists(path))
            {
                LoadSource(File.ReadAllText(path), path);
                _canvas.Focus();
            }
            else
                ShowNewGallery();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BuildColourField(string host, string label, Action<string> apply)
    {
        var field = new ColourField();
        Avalonia.Automation.AutomationProperties.SetName(field, label);
        field.Click += async (_, _) =>
        {
            var model = _canvas.Model;
            var nodes = _canvas.Selection.ToArray();
            var chosen = await ColourPickerDialog.PickAsync(this, label, field.Value, defaultValue: "");
            if (chosen is null) return;
            if (!ReferenceEquals(model, _canvas.Model) || !nodes.ToHashSet().SetEquals(_canvas.Selection))
            { ShowInfo("Selection changed", "Choose the color again for the current selection. No color was changed."); return; }
            if (nodes.All(n => (host == "FillSwatches" ? n.Fill : n.Stroke) == chosen)) return;
            apply(chosen);
            SyncPanel();
        };
        this.FindControl<ItemsControl>(host)!.ItemsSource = new[] { field };
    }

    private void LoadSource(string source, string? path)
    {
        var capabilities = MerpeepsDocument.GetCapabilities(source);
        DiagramModel? model = null;
        GanttEvaluationContext? evaluationContext = null;
        if (capabilities.CanEdit)
        {
            try
            {
                model = MerpeepsDocument.Load(source);
                if (model.Gantt is not null)
                {
                    evaluationContext = EditorCanvas.CaptureGanttContext();
                    _ = Merpeeps.Core.Layout.GanttDiagramLayout.Create(model, evaluationContext, EditorCanvas.MeasureDrawingText);
                }
                if (model.Wardley is not null) _ = Merpeeps.Core.Layout.StrategyDiagramLayouts.Wardley(model, EditorCanvas.MeasureDrawingText);
                if (model.Ishikawa is not null) _ = Merpeeps.Core.Layout.StrategyDiagramLayouts.Ishikawa(model, EditorCanvas.MeasureDrawingText);
                if (model.Cynefin is not null) _ = Merpeeps.Core.Layout.StrategyDiagramLayouts.Cynefin(model, EditorCanvas.MeasureDrawingText);
                if (model.Venn is not null) _ = Merpeeps.Core.Layout.VennDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.Railroad is not null) _ = Merpeeps.Core.Layout.RailroadDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.Swimlane is not null) _ = Merpeeps.Core.Layout.SwimlaneDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.UseCase is not null) _ = Merpeeps.Core.Layout.UseCaseDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.AgentFlow is not null) _ = Merpeeps.Core.Layout.AgentFlowDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.EventModeling is not null) _ = Merpeeps.Core.Layout.EventModelingLayout.Create(model, EditorCanvas.MeasureDrawingText);
                if (model.Zen is not null) _ = Merpeeps.Core.Layout.ZenDiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
            }
            catch (FormatException error)
            {
                capabilities = capabilities with
                {
                    CanEdit = false, CanLayout = false, CanRenderNatively = false,
                    Diagnostic = error.Message + " Source is preserved; open the original in a compatible editor.",
                };
            }
            catch (SequenceNotEditableException error)
            {
                capabilities = capabilities with
                {
                    CanEdit = false, CanLayout = false, CanRenderNatively = false,
                    Diagnostic = "Native sequence authoring is unavailable for this document:\n" +
                        string.Join("\n", error.Result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")),
                };
            }
            catch (ChartNotEditableException error)
            {
                capabilities = capabilities with
                {
                    CanEdit = false, CanLayout = false, CanRenderNatively = false,
                    Diagnostic = "Native chart editing is unavailable for this source:\n" +
                        string.Join("\n", error.Result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")),
                };
            }
            catch (DataDiagramNotEditableException error)
            {
                capabilities = capabilities with
                {
                    CanEdit = false, CanLayout = false, CanRenderNatively = false,
                    Diagnostic = "Native diagram editing is unavailable:\n" + string.Join("\n", error.Diagnostics.Select(d => d.Code + ": " + d.Message)),
                };
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException)
            {
                capabilities = capabilities with
                {
                    CanEdit = false, CanLayout = false, CanRenderNatively = false,
                    Diagnostic = "The native editor could not interpret this document. Source is preserved:\n" + error.Message,
                };
            }
        }
        _source = source;
        _path = path;
        _dirty = false;
        _capabilities = capabilities;
        if (!capabilities.CanEdit) model = null;
        if (model is null) _canvas.ClearModel();
        else _canvas.SetModel(model, evaluationContext);
        SyncClassWorkspace();
        RefreshPalette();
        RefreshOutline();
        CloseGalleryForDocument();
        SyncEngineBox(_canvas.Model);
        SyncDiagramTheme();
        RefreshSource();
        UpdateStatus();
        _canvas.Focus();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_dirty || _classInspector?.HasPendingEdits == true)
        {
            ShowNewGallery();
            _newGallery!.RequestOpen();
            return;
        }
        await PickDiagramAsync();
    }

    private async Task PickDiagramAsync()
    {
        if (_openingDiagram) return;
        _openingDiagram = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a mermaid diagram",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Mermaid / Markdown")
                        { Patterns = new[] { "*.mmd", "*.mermaid", "*.md" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 0) return;

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            LoadSource(await reader.ReadToEndAsync(), file.TryGetLocalPath());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            ShowInfo("Could not open diagram", error.Message + "\n\nYour current diagram has not been replaced.");
        }
        finally { _openingDiagram = false; }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!CanSaveCurrentDiagram()) return;
        if (_capabilities is null) return;
        var updated = CurrentSource;

        var path = _path;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save diagram",
                DefaultExtension = "mmd",
                SuggestedFileName = "diagram.mmd",
            });
            if (file is null) return;
            path = file.TryGetLocalPath();
            if (path is null) return;
        }

        await File.WriteAllTextAsync(path, updated);
        _source = updated;
        _path = path;
        _dirty = false;
        RefreshSource();
        UpdateStatus();
    }

    /// <summary>
    /// Position and style changes keep the body byte-identical; structural edits
    /// are patched surgically. Choosing the cheaper path means the common case
    /// never rewrites a line the user did not touch.
    /// </summary>
    private string Render(DiagramModel model) =>
        model.AgentFlow is not null || model.EventModeling is not null || model.Zen is not null || model.Swimlane is not null || model.UseCase is not null || model.ClassOptions is not null || model.EntityOptions is not null || model.Sequence is not null || model.Chart is not null || model.Outline is not null || model.Temporal is not null || model.Packet is not null || model.Kanban is not null || model.Requirements is not null || model.GitDiagram is not null || model.Sankey is not null || model.Treemap is not null || model.Blocks is not null || model.Architecture is not null || model.C4 is not null || model.Gantt is not null || model.Wardley is not null || model.Ishikawa is not null || model.Cynefin is not null || model.Venn is not null || model.Railroad is not null || StructureChanged(model)
            ? MerpeepsDocument.Save(_source, model)
            : MerpeepsDocument.SavePositions(_source, model);

    private bool StructureChanged(DiagramModel model)
    {
        var original = MerpeepsDocument.Load(_source);
        if (original.Nodes.Count != model.Nodes.Count) return true;
        if (original.Edges.Count != model.Edges.Count) return true;
        foreach (var n in model.Nodes)
        {
            var o = original.Node(n.Id);
            if (o is null || o.Label != n.Label || o.Shape != n.Shape) return true;
        }
        foreach (var edge in model.Edges)
            if (!original.Edges.Any(x => x.From == edge.From && x.To == edge.To &&
                                         x.Label == edge.Label && x.Kind == edge.Kind))
                return true;
        return false;
    }

    private void OnRelayout(object? sender, RoutedEventArgs e) => Relayout();

    private void OnEngineChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPanelEvents) return;
        Relayout();
    }

    /// <summary>
    /// Re-run the selected layout over the whole diagram.
    ///
    /// <para>Explicit positions are cleared first: the user asked for a fresh
    /// arrangement, and honouring the old one would leave the result looking
    /// like the engine had failed.</para>
    /// </summary>
    private void Relayout()
    {
        var model = _canvas.Model;
        if (model is null) return;

        var box = this.FindControl<ComboBox>("EngineBox")!;
        var choice = EditorLayouts.At(box.SelectedIndex);

        var engine = LayoutEngines.ById(choice.Id);
        _canvas.CancelGraphDrag();
        model.GroupLayouts.Clear();
        model.RenderedGeometry = null; model.EdgeWaypoints.Clear();
        foreach (var n in model.Nodes) n.HasExplicitPosition = false;
        if (model.EntityOptions is not null && engine.Id == "layered")
        {
            model.LayoutEngine = engine.Id;
            EntityDiagramLayout.Apply(model);
        }
        else engine.Apply(model, LayoutOptions.Default);
        foreach (var n in model.Nodes) n.HasExplicitPosition = true;

        model.LayoutEngine = engine.Id;
        _dirty = true;
        _canvas.FitToView();
        _canvas.InvalidateVisual();
        RefreshSource();
        UpdateStatus();
        _canvas.Focus();
    }

    /// <summary>Point the dropdown at whatever layout the document records.</summary>
    private void SyncEngineBox(DiagramModel? model)
    {
        var box = this.FindControl<ComboBox>("EngineBox")!;

        // "manual" is not an engine — it means a person placed the nodes — so
        // leave the current choice alone rather than snapping back to default.
        var i = EditorLayouts.IndexOf(model?.LayoutEngine);
        if (i < 0) return;

        _suppressPanelEvents = true;
        try { box.SelectedIndex = i; }
        finally { _suppressPanelEvents = false; }
    }

    private void OnShapeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPanelEvents) return;
        var box = this.FindControl<ComboBox>("ShapeBox")!;
        if (box.SelectedIndex < 0 || box.SelectedIndex >= ShapeChoices.Length) return;
        _canvas.ApplyShape(ShapeChoices[box.SelectedIndex].Shape);
        _canvas.Focus();
    }

    private void OnIconChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPanelEvents) return;
        var box = this.FindControl<ComboBox>("IconBox")!;
        if (box.SelectedIndex < 0 || box.SelectedIndex >= IconSet.Names.Length) return;
        var name = IconSet.Names[box.SelectedIndex];
        _canvas.ApplyStyle(name.Length == 0
            ? new StyleNodesCommand.Style(ClearIcon: true)
            : new StyleNodesCommand.Style(Icon: name));
        _canvas.Focus();
    }

    /// <summary>
    /// Wire the link properties: arrow style, label and delete.
    /// </summary>
    /// <remarks>
    /// Everything here changes the mermaid <em>body</em>, unlike node styling
    /// which lives in the Merpeeps block — so every one of these edits shows up in
    /// a stock render and in the diff.
    /// </remarks>
    private void WireEdgeProperties()
    {
        var kinds = this.FindControl<ComboBox>("EdgeKindBox")!;
        kinds.ItemsSource = EdgeKindChoices.Select(c => c.Name).ToList();
        kinds.SelectionChanged += (_, _) =>
        {
            if (_suppressPanelEvents) return;
            var i = kinds.SelectedIndex;
            if (i < 0 || i >= EdgeKindChoices.Length) return;

            _canvas.ApplyEdgeKind(EdgeKindChoices[i].Kind);
            _canvas.Focus();
        };

        // Committed on Enter or on losing focus, not per keystroke: an undo
        // entry per character would make Ctrl+Z useless for anything else.
        var label = this.FindControl<TextBox>("EdgeLabelBox")!;

        // The link being edited is captured when the box takes focus, not read
        // back when the edit commits. Reading it at commit time loses the edit
        // whenever focus is lost *because* the selection changed — clicking
        // straight from the box onto another node, which is the obvious way to
        // move on. The text would vanish and nothing would say why.
        DiagramEdge? target = null;
        label.GotFocus += (_, _) => target = _canvas.SelectedEdge;

        void CommitLabel()
        {
            if (_suppressPanelEvents) return;
            if (target is not { } edge) return;

            _canvas.RelabelEdge(edge, label.Text);
            target = null;
        }

        label.LostFocus += (_, _) => CommitLabel();
        label.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitLabel();

            // Focus back to the canvas so the next keystroke is a shortcut
            // rather than more text. Re-captured on the next GotFocus.
            target = _canvas.SelectedEdge;
            _canvas.Focus();
            e.Handled = true;
        };

        this.FindControl<Button>("DeleteEdgeBtn")!.Click += (_, _) =>
        {
            _canvas.DeleteSelection();
            _canvas.Focus();
        };
    }

    private void SyncPanel()
    {
        if (_canvas.IsWorkflowDiagram || _canvas.IsOwnershipDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || _canvas.Model?.ClassOptions is not null || _canvas.Model?.EntityOptions is not null || _canvas.Model?.Sequence is not null || _canvas.Model?.Chart is not null || _canvas.Model?.Outline is not null || _canvas.Model?.Temporal is not null || _canvas.Model?.Packet is not null || _canvas.Model?.Kanban is not null || _canvas.Model?.Requirements is not null || _canvas.Model?.GitDiagram is not null || _canvas.Model?.Sankey is not null || _canvas.Model?.Treemap is not null || _canvas.Model?.Blocks is not null || _canvas.Model?.Architecture is not null || _canvas.Model?.C4 is not null || _canvas.Model?.Gantt is not null)
        {
            this.FindControl<StackPanel>("EdgeProperties")!.IsVisible = false;
            this.FindControl<StackPanel>("NodeProperties")!.IsVisible = false;
            _classInspector?.Refresh();
            SyncArrangement();
            return;
        }
        _suppressPanelEvents = true;
        try
        {
            var edge = _canvas.SelectedEdge;
            var n = _canvas.Selected;

            // One or the other, never both: the canvas clears each when the
            // other is picked, so the pane can simply follow.
            this.FindControl<StackPanel>("EdgeProperties")!.IsVisible = edge is not null;
            this.FindControl<StackPanel>("NodeProperties")!.IsVisible = edge is null && n is not null;
            this.FindControl<Border>("MetadataExplanation")!.IsVisible = false;

            var info = this.FindControl<TextBlock>("SelInfo")!;
            if (_canvas.SelectedGroup is { } group)
            {
                info.Text = group.Title ?? group.Id;
                return;
            }

            if (edge is not null)
            {
                info.Text = $"{edge.From} → {edge.To}";

                var kindIdx = Array.FindIndex(EdgeKindChoices, c => c.Kind == edge.Kind);
                this.FindControl<ComboBox>("EdgeKindBox")!.SelectedIndex =
                    kindIdx >= 0 ? kindIdx : 0;

                this.FindControl<TextBox>("EdgeLabelBox")!.Text = edge.Label ?? "";
                return;
            }

            info.Text = n is not null ? $"{n.Id} - {n.Display}"
                : _canvas.SelectedObjectCount > 0 ? $"{_canvas.SelectedObjectCount} objects selected"
                : "Select an object on the canvas to edit its properties.";

            if (n is null) return;
            ((ColourField)Need<ItemsControl>("FillSwatches").Items[0]!).Value = n.Fill ?? "";
            ((ColourField)Need<ItemsControl>("StrokeSwatches").Items[0]!).Value = n.Stroke ?? "";

            var shapeIdx = Array.FindIndex(ShapeChoices, c => c.Shape == n.Shape);
            if (shapeIdx >= 0)
                this.FindControl<ComboBox>("ShapeBox")!.SelectedIndex = shapeIdx;

            var iconIdx = Array.FindIndex(IconSet.Names,
                i => string.Equals(i, n.Icon ?? "", StringComparison.OrdinalIgnoreCase));
            this.FindControl<ComboBox>("IconBox")!.SelectedIndex =
                iconIdx >= 0 ? iconIdx : 0;

            this.FindControl<CheckBox>("DashedBox")!.IsChecked = n.Dashed;
        }
        finally { _suppressPanelEvents = false; SyncArrangement(); }
    }

    /// <summary>
    /// The arrow styles offered for a link.
    /// </summary>
    /// <remarks>
    /// <c>Invisible</c> is offered last and named for what it does. It is real
    /// mermaid syntax (<c>~~~</c>) used to force layout without drawing a line,
    /// and leaving it out would mean a diagram containing one could not be
    /// edited without losing it.
    /// </remarks>
    internal static readonly (string Name, EdgeKind Kind)[] EdgeKindChoices =
    {
        ("Arrow  A --> B", EdgeKind.Arrow),
        ("Open line  A --- B", EdgeKind.Open),
        ("Dotted arrow  A -.-> B", EdgeKind.DottedArrow),
        ("Dotted line  A -.- B", EdgeKind.Dotted),
        ("Thick arrow  A ==> B", EdgeKind.ThickArrow),
        ("Thick line  A === B", EdgeKind.Thick),
        ("Invisible  A ~~~ B", EdgeKind.Invisible),
    };

    /// <summary>Show exactly what would be written, so Merpeeps is never a mystery.</summary>
    private void RefreshSource()
    {
        var model = _canvas.Model;
        var box = this.FindControl<TextBox>("SourceBox")!;
        box.Text = model is null ? _source : Render(model);

        // Keep the reference render in step with every edit. It no-ops unless
        // the pane is open, so this costs nothing in the common case.
        ShowReference();
    }

    /// <summary>
    /// The document as it stands, metadata and all — what the reference renderer
    /// and the source pane both display.
    /// </summary>
    internal string CurrentSource =>
        _canvas.Model is { } m ? Render(m) : _source;

    private void UpdateStatus()
    {
        var m = _canvas.Model;
        var h = _canvas.History;
        var name = _path is null ? (_source == Sample ? "(sample)" : "Untitled") : Path.GetFileName(_path);
        Need<TextBlock>("DocumentTitle").Text = name + (_dirty ? " *" : "");
        Need<TextBlock>("DocumentFamily").Text = MermaidDiagramCatalog.Families.FirstOrDefault(f => f.Dialects.Any(d => d.Directive == m?.Kind))?.DisplayName ?? m?.Kind ?? "Choose a diagram";
        Need<Button>("DiagramSettingsButton").IsEnabled = Need<MenuItem>("MenuClassDiagram").IsVisible;
        Need<Button>("ExportButton").IsEnabled = m is not null && _classInspector?.HasPendingEdits != true;
        var stats = m?.AgentFlow is { } agents ? $" · {agents.Elements.Count} workflow elements" : m?.EventModeling is { } events ? $" · {events.Frames.Count} ordered timeframes"
            : m?.Zen is { } zen ? $" · {zen.Participants.Count} participants, {ZenSemantics.Walk(zen.Statements).Count()} statements" : m?.Swimlane is { } lanes ? $" · {lanes.Groups.Count(g => g.ParentId is null)} lanes, {lanes.Nodes.Count} steps"
            : m?.UseCase is { } cases ? $" · {cases.Nodes.Count} elements, {cases.Relationships.Count} UML relationships"
            : m?.Venn is { } venn ? $" · {venn.Sets.Count} sets, {venn.Overlaps.Count} overlaps"
            : m?.Railroad is { } railroad ? $" · {railroad.Rules.Count} grammar rules"
            : m?.Wardley is { } map ? $" · {map.Components.Count} map components, {map.Links.Count} dependencies"
            : m?.Ishikawa is { } fish ? $" · {fish.AllCauses.Count()} causes"
            : m?.Cynefin is { } framework ? $" · {framework.Items.Count} domain items"
            : m?.C4 is { } c4 ? $" · {c4.Elements.Count} C4 elements, {c4.Relationships.Count} relationships"
            : m?.Gantt is { } gantt ? $" · {gantt.Tasks.Count} temporal tasks"
            : m?.Blocks is { } blocks ? $" · {blocks.AllItems.Count()} grid items, {blocks.Connections.Count} connections"
            : m?.Architecture is { } architecture ? $" · {architecture.Nodes.Count} services/junctions, {architecture.Groups.Count} groups"
            : m?.Sankey is { } flow ? $" · {flow.Nodes.Count} flow nodes, {flow.Links.Count} flows"
            : m?.Treemap is { } treemap ? $" · {treemap.Nodes.Count()} weighted nodes"
            : m?.Requirements is { } requirements ? $" · {requirements.Items.Count} elements, {requirements.Relations.Count} relationships"
            : m?.GitDiagram is { } git ? $" · {git.Replay().Commits.Count} diagram commits"
            : m?.Packet is { } packet ? $" · {packet.Fields.Count} packet fields"
            : m?.Kanban is { } board ? $" · {board.Columns.Count} columns, {board.Columns.Sum(c => c.Cards.Count)} cards"
            : m?.Outline is { } outline ? $" · {outline.Nodes.Count()} outline nodes"
            : m?.Temporal is { } temporal ? temporal.Family == TemporalFamily.Timeline
                ? $" · {temporal.Periods.Count} periods, {temporal.Periods.Sum(p => p.Events.Count)} events"
                : $" · {temporal.Tasks.Count} tasks"
            : m?.Chart is { } chart
            ? chart switch
            {
                PieChart pie => $" · {pie.Slices.Count} slices",
                QuadrantChart quadrant => $" · {quadrant.Points.Count} data points",
                XyChart xy => $" · {xy.Series.Count} series, {xy.Series.Sum(s => s.Values.Count)} values",
                RadarChart radar => $" · {radar.Axes.Count} axes, {radar.Curves.Count} curves",
                _ => "",
            }
            : m?.Sequence is { } sequence
            ? $" · {sequence.Participants.Count} participants, {SequenceDiagramModel.Walk(sequence.Statements).OfType<SequenceMessage>().Count()} messages"
            : m is null ? "" : $" · {m.Nodes.Count} nodes, {m.Edges.Count} edges";
        if (_capabilities is { CanEdit: false })
            stats = " · source only (native editing unavailable)";
        var undo = h?.NextUndo is { } u ? $" · undo: {u}" : "";
        var pending = _classInspector?.HasPendingEdits == true;
        var unconnectedStateMarker = HasUnconnectedStateMarker;
        this.FindControl<TextBlock>("StatusText")!.Text =
            $"{name}{(_dirty ? " *" : "")}{stats}{undo}{(pending ? " · unapplied inspector edits" : "")}{(unconnectedStateMarker ? " · connect the start/end marker before saving" : "")}";

        this.FindControl<Button>("UndoBtn")!.IsEnabled = h?.CanUndo ?? false;
        this.FindControl<Button>("RedoBtn")!.IsEnabled = h?.CanRedo ?? false;

        var native = m is not null;
        _canvas.IsEnabled = native;
        foreach (var control in new[] { "SaveBtn", "RelayoutBtn", "EngineBox",
                     "DiagramThemeBox", "NodePane", "PropertyPane" })
            Need<Control>(control).IsEnabled = native;
        foreach (var menu in new[] { "MenuSave", "MenuExportSvg", "MenuRelayout",
                     "MenuEngines", "MenuDiagramTheme", "MenuSelectAll", "MenuDelete", "MenuRename" })
            Need<MenuItem>(menu).IsEnabled = native;
        Need<Button>("SaveBtn").IsEnabled = native && !pending && !unconnectedStateMarker;
        Need<MenuItem>("MenuSave").IsEnabled = native && !pending && !unconnectedStateMarker;
        foreach (var menu in new[] { "MenuSaveAs", "MenuExport", "MenuExportBaked" })
            Need<MenuItem>(menu).IsEnabled = !pending && !unconnectedStateMarker;
        Need<MenuItem>("MenuExportSvg").IsEnabled = native && !pending && !unconnectedStateMarker;
        if (_canvas.IsWorkflowDiagram || _canvas.IsOwnershipDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || m?.Sequence is not null || m?.Chart is not null || m?.Outline is not null || m?.Temporal is not null || m?.Packet is not null || m?.Kanban is not null || m?.Requirements is not null || m?.GitDiagram is not null || m?.Sankey is not null || m?.Treemap is not null || m?.Blocks is not null || m?.Architecture is not null || m?.C4 is not null || m?.Gantt is not null)
        {
            Need<MenuItem>("MenuRelayout").IsEnabled = false;
            Need<MenuItem>("MenuEngines").IsEnabled = false;
            Need<MenuItem>("MenuSelectAll").IsEnabled = false;
        }

        var unsupported = _capabilities is { CanEdit: false };
        Need<Border>("CapabilityBanner").IsVisible = unsupported;
        Need<TextBlock>("CapabilityText").Text = unsupported
            ? _capabilities!.Diagnostic +
              "\nSource is kept unchanged. View > Show real mermaid opens the reference preview; Save As can copy the source."
            : "";
        SyncArrangement();
    }
}
