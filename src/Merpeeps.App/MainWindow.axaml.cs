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

    private static readonly string[] Fills =
    {
        "#DAE8FC", "#D5E8D4", "#FFE6CC", "#F8CECC", "#E1D5E7",
        "#FFF2CC", "#F5F5F5", "#FFFFFF",
    };

    private static readonly string[] Strokes =
    {
        "#6C8EBF", "#82B366", "#D79B00", "#B85450", "#9673A6",
        "#D6B656", "#666666", "#1A1A1A",
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
        };
        _canvas.SelectionChanged += (_, _) => { SyncPanel(); UpdateStatus(); };

        this.FindControl<Button>("OpenBtn")!.Click += OnOpen;
        this.FindControl<Button>("SaveBtn")!.Click += OnSave;
        this.FindControl<Button>("UndoBtn")!.Click += (_, _) => { _canvas.Undo(); _canvas.Focus(); };
        this.FindControl<Button>("RedoBtn")!.Click += (_, _) => { _canvas.Redo(); _canvas.Focus(); };
        this.FindControl<Button>("FitBtn")!.Click += (_, _) => { _canvas.FitToView(); _canvas.Focus(); };
        this.FindControl<Button>("RelayoutBtn")!.Click += OnRelayout;

        WireMenu(options.Theme);
        WireNodeList();
        WireStylingToggle();

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

        BuildSwatches("FillSwatches", Fills,
            c => _canvas.ApplyStyle(new StyleNodesCommand.Style(Fill: c)));
        BuildSwatches("StrokeSwatches", Strokes,
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
                LoadSource(File.ReadAllText(path), path);
            else
                LoadSource(Sample, null);
            _canvas.Focus();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Build a row of colour swatches, ending in one that opens a full picker.
    /// </summary>
    /// <remarks>
    /// The eight presets are the fast path and stay first. The picker exists
    /// because a fixed palette is a wall — the format has always stored an
    /// arbitrary hex string, so the only thing stopping a brand colour was this
    /// row of buttons.
    ///
    /// <para>The picker button keeps the last colour chosen through it as its
    /// own background, so using the same custom colour on a second node is one
    /// click rather than a round trip through the dialogue.</para>
    /// </remarks>
    private void BuildSwatches(string host, string[] colours, Action<string> apply)
    {
        var items = new List<Control>();

        Button Swatch(string? colour, string tip) => new()
        {
            Width = 26, Height = 26, Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(0),
            Background = colour is null
                ? Brushes.Transparent
                : new SolidColorBrush(Color.Parse(colour)),
            BorderBrush = new SolidColorBrush(Color.Parse("#999")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            [ToolTip.TipProperty] = tip,
        };

        foreach (var c in colours)
        {
            var btn = Swatch(c, c);
            var colour = c;
            btn.Click += (_, _) => { apply(colour); _canvas.Focus(); };
            items.Add(btn);
        }

        var custom = Swatch(null, "Pick any colour…");
        custom.Content = new TextBlock
        {
            Text = "+",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        custom.Click += async (_, _) =>
        {
            // Seed with what the selected node already uses, so adjusting a
            // shade is a nudge rather than starting from nothing.
            var seed = _lastCustom[host]
                       ?? (host == "FillSwatches" ? _canvas.Selected?.Fill : _canvas.Selected?.Stroke);

            var chosen = await ColourPickerDialog.PickAsync(
                this, host == "FillSwatches" ? "Fill colour" : "Outline colour", seed);

            if (chosen is null) { _canvas.Focus(); return; }

            _lastCustom[host] = chosen;
            custom.Background = new SolidColorBrush(Color.Parse(chosen));
            custom.Content = null;
            ToolTip.SetTip(custom, $"{chosen} — click to reuse, or pick again from the menu");

            apply(chosen);
            _canvas.Focus();
        };

        items.Add(custom);
        this.FindControl<ItemsControl>(host)!.ItemsSource = items;
    }

    /// <summary>The last colour picked from each row's picker, for reuse.</summary>
    private readonly Dictionary<string, string?> _lastCustom = new()
    {
        ["FillSwatches"] = null,
        ["StrokeSwatches"] = null,
    };

    private void LoadSource(string source, string? path)
    {
        _source = source;
        _path = path;
        _dirty = false;
        _canvas.SetModel(MerpeepsDocument.Load(source));
        SyncEngineBox(_canvas.Model);
        SyncDiagramTheme();
        RefreshSource();
        UpdateStatus();
        _canvas.Focus();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
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

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var model = _canvas.Model;
        if (model is null) return;

        var updated = Render(model);

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
        StructureChanged(model)
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
        foreach (var n in model.Nodes) n.HasExplicitPosition = false;
        engine.Apply(model, LayoutOptions.Default);
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
        _suppressPanelEvents = true;
        try
        {
            var edge = _canvas.SelectedEdge;
            var n = _canvas.Selected;

            // One or the other, never both: the canvas clears each when the
            // other is picked, so the pane can simply follow.
            this.FindControl<StackPanel>("EdgeProperties")!.IsVisible = edge is not null;
            this.FindControl<StackPanel>("NodeProperties")!.IsVisible = edge is null;

            var info = this.FindControl<TextBlock>("SelInfo")!;

            if (edge is not null)
            {
                info.Text = $"{edge.From} → {edge.To}";

                var kindIdx = Array.FindIndex(EdgeKindChoices, c => c.Kind == edge.Kind);
                this.FindControl<ComboBox>("EdgeKindBox")!.SelectedIndex =
                    kindIdx >= 0 ? kindIdx : 0;

                this.FindControl<TextBox>("EdgeLabelBox")!.Text = edge.Label ?? "";
                return;
            }

            info.Text = _canvas.Selection.Count switch
            {
                0 => "Nothing selected",
                1 => $"{n!.Id} — {n.Display}",
                var c => $"{c} nodes selected",
            };

            if (n is null) return;

            var shapeIdx = Array.FindIndex(ShapeChoices, c => c.Shape == n.Shape);
            if (shapeIdx >= 0)
                this.FindControl<ComboBox>("ShapeBox")!.SelectedIndex = shapeIdx;

            var iconIdx = Array.FindIndex(IconSet.Names,
                i => string.Equals(i, n.Icon ?? "", StringComparison.OrdinalIgnoreCase));
            this.FindControl<ComboBox>("IconBox")!.SelectedIndex =
                iconIdx >= 0 ? iconIdx : 0;

            this.FindControl<CheckBox>("DashedBox")!.IsChecked = n.Dashed;
        }
        finally { _suppressPanelEvents = false; }
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
        var name = _path is null ? "(sample)" : Path.GetFileName(_path);
        var stats = m is null ? "" : $" · {m.Nodes.Count} nodes, {m.Edges.Count} edges";
        var undo = h?.NextUndo is { } u ? $" · undo: {u}" : "";
        this.FindControl<TextBlock>("StatusText")!.Text =
            $"{name}{(_dirty ? " *" : "")}{stats}{undo}";

        this.FindControl<Button>("UndoBtn")!.IsEnabled = h?.CanUndo ?? false;
        this.FindControl<Button>("RedoBtn")!.IsEnabled = h?.CanRedo ?? false;
    }
}

