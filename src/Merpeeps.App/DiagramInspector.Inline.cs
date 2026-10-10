using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private bool _committingInline;
    private bool _resettingInline;
    private readonly Dictionary<(TaskKind Task, string Title), bool> _sectionStates = new();

    private void DraftField(Draft draft, string key, string name, Control input)
    {
        Field(name, input);
        draft.Fields.Add((Children[^2], input, key));
    }

    private void OrganizeInlineFields(Draft draft)
    {
        if (!draft.Inline || draft.Task is TaskKind.ClassProperties or TaskKind.EntityProperties) return;
        var primary = (draft.Task switch
        {
            TaskKind.SequenceParticipant => "alias",
            TaskKind.SequenceMessage => "from to text arrow",
            TaskKind.SequenceBlock or TaskKind.SequenceBranch => "text kind",
            TaskKind.ArchitectureNode => "label kind",
            TaskKind.ArchitectureGroup => "label",
            TaskKind.C4Element => "label description",
            TaskKind.BlockItem => "label kind span",
            TaskKind.OutlineNode => "label",
            TaskKind.TreemapNode => "label value",
            TaskKind.CreateRequirement => "name text",
            TaskKind.KanbanCard => "label assigned",
            TaskKind.KanbanColumn => "label",
            TaskKind.WardleyComponent => "name visibility evolution",
            TaskKind.VennSet => "name label size",
            TaskKind.VennOverlap => "label size",
            TaskKind.AgentElement => "label kind",
            TaskKind.EventFrame => "entity kind",
            TaskKind.EventData => "name kind body",
            TaskKind.ZenParticipant => "alias kind",
            TaskKind.ZenStatement => "text from to kind",
            TaskKind.LaneNode or TaskKind.LaneGroup or TaskKind.UseCaseBoundary => "label",
            TaskKind.UseCaseNode => "label kind",
            TaskKind.EntityAttribute => "name type",
            TaskKind.Member => "signature visibility",
            _ => "",
        }).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (primary.Length == 0) return;
        var extras = draft.Fields.Where(f => Children.Contains(f.Field) && !primary.Contains(f.Key)).ToArray();
        if (extras.Length == 0) return;
        foreach (var field in extras) { Children.Remove(field.Label); Children.Remove(field.Field); }
        var insert = Children.TakeWhile(c => c is not Expander && c is not Avalonia.Controls.Button).Count();
        PropertySection("More properties", () =>
        {
            foreach (var field in extras) { Children.Add(field.Label); Children.Add(field.Field); }
        });
        var section = Children[^1]; Children.Remove(section); Children.Insert(insert, section);
    }

    private void WireInlineInput(Draft draft, TextBox input, bool multiline)
    {
        if (!draft.Inline) return;
        ToolTip.SetTip(input, multiline
            ? "Ctrl+Enter or leaving the field applies the edit. Escape cancels."
            : "Enter or leaving the field applies the edit. Escape cancels.");
        input.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!ReferenceEquals(_active, draft)) return;
            if (e.Key == Key.Escape)
            {
                var name = AutomationProperties.GetName(input);
                _resettingInline = true;
                try { CancelEditor(); }
                finally { _resettingInline = false; }
                FocusInlineField(name);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (!multiline || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                TryCommitInline(draft);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            if (_resettingInline || _committingInline) return;
            Dispatcher.UIThread.Post(() =>
            {
                // The color button commits before opening its dialog. Rebuilding it
                // during pointer-down would discard the matching pointer-up/click.
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is ColourField { IsPressed: true }) return;
                if (ReferenceEquals(_active, draft) && !input.IsKeyboardFocusWithin)
                    TryCommitInline(draft);
            });
        };
    }

    private bool TryCommitInline(Draft draft, bool restoreFocus = true)
    {
        if (_committingInline || _resettingInline || !draft.Inline || !draft.Dirty || draft.Commit is null)
            return !draft.Dirty;
        if (!ReferenceEquals(_model, _canvas.Model) || !ReferenceEquals(_active, draft)) return false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var fieldName = focused is not null && this.IsVisualAncestorOf(focused) ? AutomationProperties.GetName(focused) : null;
        _committingInline = true;
        try
        {
            draft.Commit();
            draft.Error = null;
            if (restoreFocus && fieldName is not null) FocusInlineField(fieldName);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or NotSupportedException)
        {
            draft.Error = error.Message;
            _feedback.Text = error.Message;
            if (focused is TextBox && this.IsVisualAncestorOf(focused)) DataValidationErrors.SetError(focused, error);
            return false;
        }
        finally { _committingInline = false; }
    }

    private void FocusInlineField(string? name)
    {
        if (name is null) return;
        var draft = _active;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(draft, _active)) return;
            this.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(c => c.IsEffectivelyVisible && c.Focusable && AutomationProperties.GetName(c) == name)?.Focus();
        });
    }

    private void RestoreInlineSelection(object? selected)
    {
        switch (selected)
        {
            case StructuralSelection target: _canvas.SelectStructural(target); break;
            case DataDiagramTarget target: _canvas.SelectData(target); break;
            case ChartTarget target: _canvas.SelectChart(target); break;
            case SequenceSelection target: _canvas.SelectSequenceId(target.Id); break;
            case DiagramNode node: _canvas.SelectByIds([node.Id]); break;
            case DiagramEdge edge: _canvas.SelectEdge(edge); break;
            case DiagramSubgraph group: _canvas.SelectGroup(group); break;
            case null:
                if (_canvas.IsWorkflowDiagram || IsStructuralDiagram) _canvas.SelectStructural(null);
                else if (IsDataDiagram) _canvas.SelectData(null);
                else if (Model().Chart is not null) _canvas.SelectChart(null);
                else if (Model().Sequence is not null) _canvas.SelectSequenceId(null);
                else { _canvas.SelectByIds([]); _canvas.SelectEdge(null); _canvas.SelectEntityGroup(null); }
                break;
        }
    }

    private void RunInspectorAction(string name, Action action)
    {
        if (_active is { Inline: true, Dirty: true } draft)
        {
            if (!TryCommitInline(draft, restoreFocus: false)) return;
            var replacement = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == name);
            if (replacement is not null)
            {
                replacement.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                return;
            }
            _feedback.Text = "The properties changed. Select the action again.";
            return;
        }
        Perform(action);
    }

    private void PropertySection(string title, Action build, bool expanded = false)
    {
        var start = Children.Count;
        build();
        var controls = Children.Skip(start).ToArray();
        var body = new StackPanel { Spacing = 8 };
        foreach (var control in controls) { Children.Remove(control); body.Children.Add(control); }
        var task = _active?.Task ?? TaskKind.Options;
        var key = (task, title);
        var expander = new Expander
        {
            Header = title, Content = body, HorizontalAlignment = HorizontalAlignment.Stretch,
            IsExpanded = _sectionStates.GetValueOrDefault(key, expanded),
        };
        AutomationProperties.SetName(expander, title);
        expander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty) _sectionStates[key] = expander.IsExpanded;
        };
        Children.Add(expander);
    }

    private void InlineNodeName(DiagramNode node, string label)
    {
        var draft = new Draft(TaskKind.Rename, node) { Inline = true };
        if (_drafts.TryGetValue(node, out var pending) && pending.Dirty)
        {
            if (pending.Task != TaskKind.Rename) { PendingAction(node); return; }
            draft = pending;
        }
        _active = draft;
        _drafts[node] = draft;
        Input(draft, "label", label, node.Display);
        draft.Commit = () => Apply(node.Entity is null
            ? new RenameNodeCommand(node, Optional(draft, "label"))
            : new SetEntityAliasCommand(node, Optional(draft, "label")), node);
        if (draft.Error is { } error) _feedback.Text = error;
    }
}
