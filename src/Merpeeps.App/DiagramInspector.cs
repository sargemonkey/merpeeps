using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector : StackPanel
{
    private readonly EditorCanvas _canvas;
    private readonly Dictionary<object, Draft> _drafts = new();
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private DiagramModel? _model;
    private object? _selection;
    private Draft? _active;
    private bool _applying;
    private ChartDocument? _lastChart;
    private object? _lastData;
    private object? _lastStructural;

    public event EventHandler? PendingEditsChanged;
    public bool HasPendingEdits => _drafts.Values.Any(d => d.Dirty);
    public bool TryFinishInlineEdit() =>
        (_active is not { Inline: true, Dirty: true } draft || TryCommitInline(draft, restoreFocus: false)) && !HasPendingEdits;

    private enum TaskKind { CreateClass, Rename, ClassProperties, Member, Relationship, Size, CreateNote, CreateRequirement, RequirementRelation, RequirementOptions, RequirementStyle, GitOperation, GitOptions, SankeyFlow, SankeyNode, TreemapNode, WeightedOptions, StructuralItems, Note, Interface, Namespaces, Options, Navigator, CreateEntity, EntityAttribute, EntityProperties, EntityRelationship, EntityGroups, EntityOptions, SequenceParticipant, SequenceMessage, SequenceNote, SequenceActivation, SequenceBlock, SequenceBranch, SequenceBox, SequenceOptions, SequenceTimeline, SequenceCreation, SequenceNumbering, SequenceLinks, ChartTitle, ChartOptions, ChartData, PieSlice, QuadrantPoint, QuadrantLabels, XySeries, XyValue, XyAxis, RadarAxis, RadarCurve, RadarValue, RadarOptions, OutlineNode, OutlineMove, DataOptions, DataItems, TemporalTitle, TemporalSection, TimelinePeriod, TimelineEvent, JourneyTask, PacketField, PacketOptions, KanbanColumn, KanbanCard, KanbanMove, BlockItem, BlockMove, BlockLink, BlockAppearance, GridSettings, ArchitectureNode, ArchitectureGroup, ArchitectureLink, ArchitectureAlignment, C4Element, C4Relationship, C4Appearance, C4Options, GanttTask, GanttSection, GanttOptions, WardleyComponent, WardleyLink, WardleyAnnotation, WardleyOptions, IshikawaCause, IshikawaProblem, IshikawaMove, CynefinItem, CynefinTransition, CynefinOptions, VennSet, VennOverlap, VennText, SetGrammarOptions, RailroadRule, RailroadExpression, RailroadCombine, LaneGroup, LaneNode, LaneLink, UseCaseNode, UseCaseBoundary, UseCaseLink,     OwnershipOptions, AgentElement, AgentLink, EventFrame, EventData, ZenParticipant, ZenStatement, WorkflowOptions }
    private sealed class Draft(TaskKind task, object owner, string? memberId = null, ClassMemberKind memberKind = ClassMemberKind.Attribute)
    {
        public TaskKind Task { get; } = task;
        public object Owner { get; } = owner;
        public string? MemberId { get; } = memberId;
        public ClassMemberKind MemberKind { get; } = memberKind;
        public Dictionary<string, object?> Values { get; } = new();
        public Dictionary<string, object?> OriginalValues { get; } = new();
        public List<(Control Label, Control Field, string Key)> Fields { get; } = new();
        public bool Inline { get; set; }
        public Action? Commit { get; set; }
        public string? Error { get; set; }
        public bool Dirty { get; set; }
        public T Get<T>(string key, T initial)
        {
            if (Values.TryGetValue(key, out var value) && value is T found) return found;
            Values[key] = initial;
            return initial;
        }
    }
    private sealed record Choice(string Id, string Text)
    {
        public override string ToString() => Text;
    }

    public DiagramInspector(EditorCanvas canvas)
    {
        _canvas = canvas;
        Spacing = 8;
        AutomationProperties.SetName(ItemPalette, "Diagram items");
        AutomationProperties.SetName(this, "Diagram inspector");
        AutomationProperties.SetLiveSetting(_feedback, AutomationLiveSetting.Polite);
        _feedback.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty) _feedback.IsVisible = !string.IsNullOrEmpty(_feedback.Text);
        };
        void Remap(DiagramEdge before, DiagramEdge after)
        {
            if (!_drafts.Remove(before, out var saved)) return;
            var replacement = new Draft(saved.Task, after) { Dirty = saved.Dirty };
            foreach (var entry in saved.Values) replacement.Values.Add(entry.Key, entry.Value);
            _drafts[after] = replacement;
        }
        _canvas.ClassRelationshipReconnected += Remap;
        _canvas.EntityRelationshipReconnected += Remap;
        _canvas.HistoryNavigated += (_, _) =>
        {
            if (_active?.Dirty != true) _active = null;
            Refresh();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_canvas.SequenceMessageSource is not null) _canvas.CancelSequenceMessage();
            else if (_canvas.EntityConnectionSource is not null) _canvas.CancelEntityConnection();
            else if (_canvas.ClassRelationshipSource is not null) _canvas.CancelClassRelationship();
            else if (_active is not null) CancelEditor();
            else return;
            e.Handled = true;
        };
    }

    public void Refresh()
    {
        if (_applying || _committingInline) return;
        if (_canvas.Model is null || !_canvas.IsWorkflowDiagram && !_canvas.IsStrategyDiagram && !_canvas.IsSetGrammarDiagram && !_canvas.IsOwnershipDiagram && _canvas.Model.ClassOptions is null && _canvas.Model.EntityOptions is null && _canvas.Model.Sequence is null && _canvas.Model.Chart is null && _canvas.Model.Outline is null && _canvas.Model.Temporal is null && _canvas.Model.Packet is null && _canvas.Model.Kanban is null && _canvas.Model.Requirements is null && _canvas.Model.GitDiagram is null && _canvas.Model.Sankey is null && _canvas.Model.Treemap is null && _canvas.Model.Blocks is null && _canvas.Model.Architecture is null && _canvas.Model.C4 is null && _canvas.Model.Gantt is null)
        {
            ItemPalette.Children.Clear();
            _paletteModel = null;
            if (_model is not null || _drafts.Count > 0)
            {
                _model = null;
                _active = null;
                _drafts.Clear();
                PendingEditsChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        if (!ReferenceEquals(_model, _canvas.Model))
        {
            _sectionStates.Clear();
            _model = _canvas.Model;
            _drafts.Clear();
            _active = null;
            PendingEditsChanged?.Invoke(this, EventArgs.Empty);
        }
        var selected = SelectionOwner;
        if (!Equals(_selection, selected) && _active is { Inline: true, Dirty: true } pending)
            TryCommitInline(pending, restoreFocus: false);
        var structural = (object?)_canvas.Model.AgentFlow ?? (object?)_canvas.Model.EventModeling ?? (object?)_canvas.Model.Zen ?? (object?)_canvas.Model.Swimlane ?? (object?)_canvas.Model.UseCase ?? (object?)_canvas.Model.Requirements ?? (object?)_canvas.Model.GitDiagram ?? (object?)_canvas.Model.Sankey ?? (object?)_canvas.Model.Treemap ?? (object?)_canvas.Model.Blocks ?? (object?)_canvas.Model.Architecture ?? (object?)_canvas.Model.C4 ?? (object?)_canvas.Model.Gantt ?? (object?)_canvas.Model.Wardley ?? (object?)_canvas.Model.Ishikawa ?? (object?)_canvas.Model.Cynefin ?? (object?)_canvas.Model.Venn ?? _canvas.Model.Railroad;
        if (!ReferenceEquals(structural, _lastStructural)) { _lastStructural = structural; if (_active?.Dirty != true) _active = null; }
        var data = (object?)_canvas.Model.Outline ?? (object?)_canvas.Model.Temporal ?? (object?)_canvas.Model.Packet ?? _canvas.Model.Kanban;
        if (!ReferenceEquals(data, _lastData))
        {
            _lastData = data;
            if (_active?.Dirty != true) _active = null;
        }
        if (!ReferenceEquals(_lastChart, _canvas.Model.Chart))
        {
            _lastChart = _canvas.Model.Chart;
            if (_active?.Dirty != true) _active = null;
        }
        if (!Equals(_selection, selected))
        {
            _active = null;
            _selection = selected;
        }
        if (_canvas.SequenceMessageSource is not null)
        {
            _active = null;
            ShowSequenceConnection();
        }
        else if (_canvas.EntityConnectionSource is not null)
        {
            _active = null;
            ShowEntityConnection();
        }
        else if (_canvas.ClassRelationshipSource is not null)
        {
            _active = null;
            ShowConnection();
        }
        else if (_active is null)
            ShowSelection();
        if (_canvas.SequenceActionError is { } sequenceError && _canvas.Model?.Sequence is not null)
            _feedback.Text = sequenceError;
        if (_canvas.ChartActionError is { } chartError && _canvas.Model?.Chart is not null)
            _feedback.Text = chartError;
        if (_canvas.StructuralError is { } structuralError) _feedback.Text = structuralError;
        RefreshItemPalette();
    }

    public void AddClass()
    {
        if (_canvas.IsWorkflowDiagram) { Open(new(Model().AgentFlow is not null ? TaskKind.AgentElement : Model().EventModeling is not null ? TaskKind.EventFrame : TaskKind.ZenParticipant, Model())); return; }
        if (_canvas.IsOwnershipDiagram) { Open(new(Model().Swimlane is not null ? TaskKind.LaneGroup : TaskKind.UseCaseNode, Model())); return; }
        if (_canvas.IsSetGrammarDiagram) { Open(new(Model().Venn is not null ? TaskKind.VennSet : TaskKind.RailroadRule, Model())); return; }
        if (Model().Wardley is not null) { Open(new(TaskKind.WardleyComponent, Model())); return; }
        if (Model().Ishikawa is not null) { Open(new(Model().Ishikawa!.Problem.Length == 0 ? TaskKind.IshikawaProblem : TaskKind.IshikawaCause, Model())); return; }
        if (Model().Cynefin is not null) { Open(new(TaskKind.CynefinItem, Model())); return; }
        if (Model().C4 is not null) { Open(new(TaskKind.C4Element, Model())); return; }
        if (Model().Gantt is not null) { Open(new(TaskKind.GanttTask, Model())); return; }
        if (Model().Blocks is not null) { Open(new(TaskKind.BlockItem, Model())); return; }
        if (Model().Architecture is not null) { Open(new(TaskKind.ArchitectureNode, Model())); return; }
        if (Model().Sankey is not null) { Open(new(TaskKind.SankeyFlow, Model())); return; }
        if (Model().Treemap is not null) { Open(new(TaskKind.TreemapNode, Model())); return; }
        if (Model().Requirements is not null) { Open(new(TaskKind.CreateRequirement, Model())); return; }
        if (Model().GitDiagram is not null) { Open(new(TaskKind.GitOperation, Model())); return; }
        if (Model().Packet is not null) { Open(new(TaskKind.PacketField, Model())); return; }
        if (Model().Kanban is not null) { Open(new(TaskKind.KanbanColumn, Model())); return; }
        if (Model().Outline is not null) { NewOutlineNode(null); return; }
        if (Model().Temporal is { } temporal) { Open(new(temporal.Family == TemporalFamily.Timeline ? TaskKind.TimelinePeriod : TaskKind.JourneyTask, Model())); return; }
        if (Model().Chart is { } chart)
        {
            Open(new(chart.Family switch
            {
                ChartFamily.Pie => TaskKind.PieSlice, ChartFamily.Quadrant => TaskKind.QuadrantPoint,
                ChartFamily.Xy => TaskKind.XySeries, _ => TaskKind.RadarAxis,
            }, Model()));
            return;
        }
        Open(new(Model().Sequence is not null ? TaskKind.SequenceParticipant : Model().EntityOptions is null ? TaskKind.CreateClass : TaskKind.CreateEntity, Model()));
    }
    public void AddDiagramNote() => Open(new(Model().Sequence is null ? TaskKind.CreateNote : TaskKind.SequenceNote, Model()));
    private bool IsDataDiagram => Model().Outline is not null || Model().Temporal is not null || Model().Packet is not null || Model().Kanban is not null;
    private bool IsStructuralDiagram => _canvas.IsWorkflowDiagram || _canvas.IsOwnershipDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || Model().Requirements is not null || Model().GitDiagram is not null || Model().Sankey is not null || Model().Treemap is not null || Model().Blocks is not null || Model().Architecture is not null || Model().C4 is not null || Model().Gantt is not null;
    public void ManageNamespaces() => Open(new(IsStructuralDiagram ? TaskKind.StructuralItems : IsDataDiagram ? TaskKind.DataItems : Model().Chart is not null ? TaskKind.ChartData : Model().Sequence is not null ? TaskKind.SequenceBox : Model().EntityOptions is null ? TaskKind.Namespaces : TaskKind.EntityGroups, Model()));
    public void DisplayOptions() => Open(new(_canvas.IsWorkflowDiagram ? TaskKind.WorkflowOptions : _canvas.IsOwnershipDiagram ? TaskKind.OwnershipOptions : _canvas.IsSetGrammarDiagram ? TaskKind.SetGrammarOptions : Model().Wardley is not null ? TaskKind.WardleyOptions : Model().Ishikawa is not null ? TaskKind.IshikawaProblem : Model().Cynefin is not null ? TaskKind.CynefinOptions : Model().C4 is not null ? TaskKind.C4Options : Model().Gantt is not null ? TaskKind.GanttOptions : Model().Blocks is not null || Model().Architecture is not null ? TaskKind.GridSettings : Model().Sankey is not null || Model().Treemap is not null ? TaskKind.WeightedOptions : Model().Requirements is not null ? TaskKind.RequirementOptions : Model().GitDiagram is not null ? TaskKind.GitOptions : IsDataDiagram ? TaskKind.DataOptions : Model().Chart is not null ? TaskKind.ChartOptions : Model().Sequence is not null ? TaskKind.SequenceOptions : Model().EntityOptions is null ? TaskKind.Options : TaskKind.EntityOptions, Model()));
    public void NavigateElements() => Open(new(IsStructuralDiagram ? TaskKind.StructuralItems : IsDataDiagram ? TaskKind.DataItems : Model().Chart is not null ? TaskKind.ChartData : Model().Sequence is null ? TaskKind.Navigator : TaskKind.SequenceTimeline, Model()));

    private void ShowSelection()
    {
        StartPanel();
        if (SelectionOwner is null && _canvas.GridConnectionSource is null && _canvas.RequirementConnectionSource is null)
        {
            Heading("Properties");
            Text("Select an object.");
            foreach (var draft in _drafts.Values.Where(d => d.Dirty).ToArray())
            {
                var label = Model().Chart is not null ? "Resume unapplied chart edit"
                    : Model().Sequence is not null ? "Resume unapplied sequence edit"
                    : IsDataDiagram || IsStructuralDiagram ? "Resume unapplied edit"
                    : "Resume edit: " + OwnerName(draft.Owner);
                Button(label, () => Open(draft));
            }
            FinishPanel();
            RefreshItemPalette();
            return;
        }
        if (IsStructuralDiagram) { ShowStructuralSelection(); FinishPanel(); return; }
        if (Model().Outline is not null || Model().Temporal is not null || Model().Packet is not null || Model().Kanban is not null)
        {
            ShowDataSelection(); FinishPanel(); return;
        }
        if (Model().Chart is not null)
        {
            ShowChartSelection(); FinishPanel(); return;
        }
        if (Model().Sequence is not null)
        {
            ShowSequenceSelection();
            FinishPanel();
            return;
        }
        if (Model().EntityOptions is not null)
        {
            ShowEntitySelection();
            FinishPanel();
            return;
        }
        if (_canvas.SelectedGroup is { } group && Model().ClassOptions is not null)
        {
            Open(new(TaskKind.Namespaces, group), focus: false);
            return;
        }
        if (_canvas.SelectedEdge is { ClassRelationship: not null } edge)
        {
            Open(new(TaskKind.Relationship, edge), focus: false);
            return;
        }
        if (_canvas.Selection.Count > 1)
        {
            Heading($"{_canvas.Selection.Count} elements selected");
            Text("Select one class or relationship to edit its properties.");
            return;
        }
        if (_canvas.Selected is not { Class: { } definition } node)
        {
            Heading("Class diagram");
            Text("Add a class, then select it on the canvas to add members or connect it.");
            Button("Add class", AddClass);
            foreach (var draft in _drafts.Values.Where(d => d.Dirty).ToArray())
            {
                if (!IsPresent(draft.Owner)) Text("An unapplied edit refers to an element that is no longer in this diagram.");
                Button($"Resume edit: {OwnerName(draft.Owner)}", () =>
                {
                    if (draft.Owner is DiagramNode n && Model().Nodes.Contains(n)) _canvas.SelectByIds([n.Id]);
                    else if (draft.Owner is DiagramEdge e && Model().Edges.Contains(e)) _canvas.SelectEdge(e);
                    Open(draft);
                });
            }
            FinishPanel();
            return;
        }
        if (definition.Kind == ClassNodeKind.Note)
        {
            Open(new(TaskKind.Note, node), false);
            Button("Size…", () => Open(new(TaskKind.Size, node)));
            Button("Delete note", () => Apply(new DeleteNodesCommand([node]), node));
        }
        else if (definition.Kind == ClassNodeKind.Lollipop)
        {
            Heading("Interface");
            InlineNodeName(node, "Interface name");
            var owner = Model().Edges.Single(e => e.From == node.Id || e.To == node.Id);
            Text($"Connected to {Model().Node(owner.From == node.Id ? owner.To : owner.From)!.Display}");
            Button("Select relationship", () => _canvas.SelectEdge(owner));
            Button("Delete interface", () => Apply(new DeleteClassRelationshipCommand(owner), node));
        }
        else
        {
            Open(new(TaskKind.ClassProperties, node), false);
            if (_active?.Task != TaskKind.ClassProperties) return;
            var sections = Children.OfType<Expander>().ToArray();
            foreach (var section in sections) Children.Remove(section);
            PropertySection("Attributes", () =>
            {
                MemberRows(node, ClassMemberKind.Attribute, "Attributes");
                Button("Add attribute", () => Open(new(TaskKind.Member, node)));
            }, expanded: true);
            PropertySection("Methods", () =>
            {
                MemberRows(node, ClassMemberKind.Operation, "Methods");
                Button("Add method", () => Open(new(TaskKind.Member, node, memberKind: ClassMemberKind.Operation)));
            }, expanded: true);
            Button("Add relationship", () => _canvas.BeginClassRelationship(node));
            foreach (var section in sections) Children.Add(section);
            ActionMenu("More class actions", [
                ("Add note…", () => Open(new(TaskKind.CreateNote, node))),
                ("Add interface…", () => Open(new(TaskKind.Interface, node))),
                ("Delete class", () => Apply(new DeleteNodesCommand([node]), node)),
            ]);
            var style = ClassDiagramStyle.Resolve(node);
            var content = ClassDiagramText.Content(node, Model().ClassOptions!);
            if (TextWidth(content, style) + 20 > node.Width ||
                ClassDiagramGeometry.CardRegions(node, Model().ClassOptions!, style.FontSize * 1.4).RequiredHeight > node.Height)
            {
                Text("Content exceeds the card. Resize explicitly to show all rows.");
                Button("Resize to content", () => Fit(node));
            }
            foreach (var diagnostic in style.Diagnostics) Text(diagnostic);
        }
        FinishPanel();
    }

    private void MemberRows(DiagramNode node, ClassMemberKind kind, string title)
    {
        var members = node.Class!.Members.Where(m => m.Kind == kind).ToArray();
        if (members.Length == 0) Text(kind == ClassMemberKind.Attribute ? "No attributes yet." : "No methods yet.");
        foreach (var member in members)
        {
            var button = Button(member.DisplayText, () => Open(new(TaskKind.Member, node, member.Id, member.Kind), false));
            AutomationProperties.SetName(button, "Edit " + member.DisplayText);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
    }

    private void ShowConnection()
    {
        StartPanel();
        Heading(_canvas.IsReconnectingClassRelationship ? "Reconnect relationship" : "Add relationship");
        Text("From: " + _canvas.ClassRelationshipSource!.Display);
        Text(_canvas.ClassRelationshipMessage ?? "Choose another class on the canvas.");
        Text("Tab chooses a target; Enter connects. No changes are made until a target is chosen.");
        Button("Cancel relationship", _canvas.CancelClassRelationship);
        FinishPanel();
    }

    private void Open(Draft draft, bool focus = true)
    {
        if (!_committingInline && !_applying && _active is { Inline: true, Dirty: true } previous &&
            !ReferenceEquals(previous, draft) && !TryCommitInline(previous, restoreFocus: false))
            return;
        draft.Inline = !focus && !ReferenceEquals(draft.Owner, Model());
        _canvas.CancelSequenceMessage();
        _canvas.CancelEntityConnection();
        _canvas.CancelClassRelationship();
        var restored = false;
        if (_drafts.TryGetValue(draft.Owner, out var pending) && pending.Dirty)
        {
            restored = !ReferenceEquals(draft, pending);
            draft = pending;
        }
        _active = draft;
        _drafts[draft.Owner] = draft;
        _selection = SelectionOwner;
        StartPanel();
        draft.Fields.Clear();
        BuildEditor(draft);
        OrganizeInlineFields(draft);
        CompactPrimaryField(draft);
        FinishPanel();
        if (restored) _feedback.Text = "Your unapplied edit is restored. Apply or cancel it before starting another edit here.";
        if (draft.Error is { } error) _feedback.Text = error;
        if (focus)
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_active, draft)) return;
                Children.OfType<TextBox>().FirstOrDefault()?.Focus();
            });
    }

    private void CancelEditor()
    {
        if (_active is { } draft) _drafts.Remove(draft.Owner);
        _active = null;
        PendingEditsChanged?.Invoke(this, EventArgs.Empty);
        ShowSelection();
        RefreshItemPalette();
        _canvas.Focus();
    }

    private void PendingAction(object owner)
    {
        if (!_drafts.TryGetValue(owner, out var draft) || !draft.Dirty) return;
        Text("An unapplied edit is saved in this inspector.");
        Button("Resume unapplied edit", () => Open(draft));
        Button("Discard unapplied edit", () =>
        {
            _drafts.Remove(owner);
            PendingEditsChanged?.Invoke(this, EventArgs.Empty);
            ShowSelection();
            RefreshItemPalette();
        });
    }

    private void Apply(IEditCommand command, object owner, string? selectedId = null, Action? after = null)
    {
        var selectionBefore = SelectionOwner;
        var preserveSelection = _committingInline && !Equals(selectionBefore, owner);
        _applying = true;
        try
        {
            _canvas.ApplyDiagramEdit(command, selectedId);
            after?.Invoke();
            if (preserveSelection) RestoreInlineSelection(selectionBefore);
            _drafts.Remove(owner);
            _active = null;
        }
        finally { _applying = false; }
        PendingEditsChanged?.Invoke(this, EventArgs.Empty);
        _selection = SelectionOwner;
        ShowSelection();
        RefreshItemPalette();
    }

    private void StartPanel()
    {
        _secondaryProperties = null;
        _secondaryToggle = null;
        var resetting = _resettingInline;
        _resettingInline = true;
        try { Children.Clear(); }
        finally { _resettingInline = resetting; }
        _feedback.Text = "";
    }
    private void FinishPanel()
    {
        CompactSecondarySections();
        GroupActions(this);
        if (!Children.Contains(_feedback)) Children.Add(_feedback);
    }
    private void Heading(string title) => Children.Add(new TextBlock
    {
        Text = title,
        FontSize = 18,
        FontWeight = FontWeight.SemiBold,
        Classes = { "inspector-heading" },
        TextWrapping = TextWrapping.Wrap,
    });
    private void Text(string text) => Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    private Button Button(string label, Action action)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
        UiIcon.Describe(button, label);
        if (ActionGlyph(label) is { } glyph) CompactAction(button, glyph);
        button.Click += (_, _) => RunInspectorAction(label, action);
        Children.Add(button);
        return button;
    }
    private void Perform(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or NotSupportedException)
        { _feedback.Text = error.Message; }
    }
    private void ActionMenu(string label, (string Label, Action Action)[] actions)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        UiIcon.Describe(button, label);
        CompactAction(button, UiGlyph.More);
        var menu = new ContextMenu();
        var items = actions.Select(action =>
        {
            var item = new CommandMenuItem { Header = action.Label };
            item.Click += (_, _) => Perform(action.Action);
            return item;
        }).ToArray();
        menu.ItemsSource = items;
        button.ContextMenu = menu;
        button.Click += (_, _) => RunInspectorAction(label, () => menu.Open(button));
        Children.Add(button);
    }
    private TextBox Input(Draft draft, string key, string label, string initial = "", bool multiline = false)
    {
        var input = new TextBox { Text = draft.Get(key, initial), AcceptsReturn = multiline, MinHeight = multiline ? 90 : 0 };
        DraftField(draft, key, label, input);
        input.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) Changed(draft, key, input.Text ?? "");
        };
        WireInlineInput(draft, input, multiline);
        return input;
    }
    private ComboBox EnumInput<T>(Draft draft, string key, string label, T initial, IEnumerable<T>? values = null) where T : struct, Enum
    {
        var box = new ComboBox { ItemsSource = (values ?? Enum.GetValues<T>()).ToArray(), SelectedItem = draft.Get(key, initial) };
        DraftField(draft, key, label, box);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) Changed(draft, key, value, commitOnChange: true); };
        return box;
    }
    private ComboBox ChoiceInput(Draft draft, string key, string label, IEnumerable<Choice> choices, string initial)
    {
        var selected = draft.Get(key, initial);
        var items = choices.ToList();
        if (!items.Any(i => i.Id == selected)) items.Add(new(selected, "(unavailable) " + selected));
        var box = new ComboBox { ItemsSource = items, SelectedItem = items.Single(i => i.Id == selected) };
        DraftField(draft, key, label, box);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice choice) Changed(draft, key, choice.Id, commitOnChange: true); };
        return box;
    }
    private CheckBox Check(Draft draft, string key, string label, bool initial)
    {
        var check = new CheckBox { Content = label, IsChecked = draft.Get(key, initial) };
        AutomationProperties.SetName(check, label);
        check.IsCheckedChanged += (_, _) => Changed(draft, key, check.IsChecked == true, commitOnChange: true);
        Children.Add(check);
        return check;
    }
    private void Changed(Draft draft, string key, object value, bool commitOnChange = false)
    {
        if (draft.Values.TryGetValue(key, out var prior) && Equals(prior, value)) return;
        draft.OriginalValues.TryAdd(key, prior);
        draft.Values[key] = value;
        draft.Dirty = draft.OriginalValues.Any(p => !Equals(draft.Values.GetValueOrDefault(p.Key), p.Value));
        if (!draft.Dirty && draft.Error is not null)
        {
            draft.Error = null;
            _feedback.Text = "";
            foreach (var field in draft.Fields) DataValidationErrors.SetError(field.Field, null);
        }
        PendingEditsChanged?.Invoke(this, EventArgs.Empty);
        RefreshItemPalette();
        if (draft.Inline && commitOnChange && !_resettingInline)
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_active, draft) && draft.Dirty)
                    TryCommitInline(draft);
            });
    }
    private void Field(string name, Control input)
    {
        var label = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap };
        label.Classes.Add("muted");
        label.FontSize = 12;
        AutomationProperties.SetName(input, name);
        AutomationProperties.SetLabeledBy(input, label);
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(label); Children.Add(input);
    }
    private void Footer(string applyName, Action apply)
    {
        if (_active is { Inline: true } draft)
        {
            draft.Commit = apply;
            return;
        }
        var applyButton = Button(applyName, apply);
        applyButton.Content = applyName.StartsWith("Apply", StringComparison.Ordinal) ? "Apply" : applyName;
        var cancelButton = Button("Cancel edit", CancelEditor);
        cancelButton.Content = "Cancel";
        Children.Remove(applyButton);
        Children.Remove(cancelButton);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        applyButton.Margin = new Thickness(0, 0, 4, 0);
        cancelButton.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(cancelButton, 1);
        row.Children.Add(applyButton); row.Children.Add(cancelButton);
        Children.Add(row);
        ToolTip.SetTip(row, "Apply commits one edit. Cancel leaves the diagram unchanged.");
    }
    private DiagramModel Model() => _canvas.Model ?? throw new InvalidOperationException("No diagram is open.");
    private object? SelectionOwner => (object?)_canvas.SelectedStructuralTarget ?? (object?)_canvas.SelectedDataTarget ?? (object?)_canvas.SelectedChartTarget ?? (object?)_canvas.SelectedSequenceElement ?? (object?)_canvas.SelectedEdge ?? (object?)_canvas.Selected ?? _canvas.SelectedGroup;
    private bool IsPresent(object owner) => ReferenceEquals(owner, Model()) ||
        owner is ChartTarget chartTarget && _canvas.ChartTargetExists(chartTarget) ||
        owner is DiagramNode node && Model().Nodes.Contains(node) || owner is DiagramEdge edge && Model().Edges.Contains(edge) ||
        owner is DiagramSubgraph group && Model().Subgraphs.Contains(group);
    private static string OwnerName(object owner) => owner switch
    {
        DiagramNode node => node.Display,
        DiagramEdge edge => $"{edge.From} to {edge.To}",
        _ => "Diagram",
    };
    private static string Value(Draft draft, string key) => draft.Get(key, "").Trim();
    private static string? Optional(Draft draft, string key) => Value(draft, key) is { Length: > 0 } value ? value : null;
    private static double Number(Draft draft, string key) =>
        double.TryParse(Value(draft, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0
            ? value : throw new ArgumentException("Enter a positive finite size in pixels.");
    private void Place(DiagramNode node)
    {
        node.X = Model().Nodes.Count == 0 ? 0 : Model().Nodes.Min(n => n.X);
        node.Y = Model().Nodes.Count == 0 ? 0 : Model().Nodes.Max(n => n.Y + n.Height) + 60;
        node.HasExplicitPosition = true;
    }
    private static double TextWidth(ClassCardContent content, ClassResolvedStyle style) =>
        content.Header.Concat(content.Attributes).Concat(content.Operations).Max(row =>
            new FormattedText(row.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(style.FontFamily, row.Italic || style.Italic ? FontStyle.Italic : FontStyle.Normal,
                    row.Bold || style.Bold ? FontWeight.Bold : FontWeight.Normal),
                style.FontSize, Brushes.Black).Width);
    private void Fit(DiagramNode node)
    {
        if (node.Entity is not null) { FitEntity(node); return; }
        var style = ClassDiagramStyle.Resolve(node);
        var content = ClassDiagramText.Content(node, Model().ClassOptions!);
        var height = ClassDiagramGeometry.CardRegions(node, Model().ClassOptions!, style.FontSize * 1.4).RequiredHeight;
        Apply(new ResizeClassNode(node, Math.Ceiling(TextWidth(content, style) + 28), Math.Ceiling(height)), node);
    }
    private sealed class ResizeClassNode(DiagramNode node, double width, double height) : IEditCommand
    {
        private (double Width, double Height, bool Explicit) _before;
        public string Description => "Resize class diagram element";
        public void Do(DiagramModel model)
        {
            if (!model.Nodes.Contains(node)) throw new InvalidOperationException("The selected element no longer exists.");
            _before = (node.Width, node.Height, node.HasExplicitPosition);
            node.Width = width; node.Height = height; node.HasExplicitPosition = true;
        }
        public void Undo(DiagramModel model)
        {
            node.Width = _before.Width; node.Height = _before.Height; node.HasExplicitPosition = _before.Explicit;
        }
    }
    private sealed class BatchCommand(string description, params IEditCommand[] commands) : IEditCommand
    {
        private int _applied;
        public string Description => description;
        public void Do(DiagramModel model)
        {
            _applied = 0;
            try { foreach (var command in commands) { command.Do(model); _applied++; } }
            catch
            {
                for (var i = _applied - 1; i >= 0; i--) commands[i].Undo(model);
                _applied = 0;
                throw;
            }
        }
        public void Undo(DiagramModel model)
        {
            for (var i = _applied - 1; i >= 0; i--) commands[i].Undo(model);
        }
    }
}
