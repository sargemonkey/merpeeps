using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private StackPanel? _secondaryProperties;
    private ToggleButton? _secondaryToggle;

    private void CompactSecondarySections()
    {
        if (_active is not { Inline: true } draft) return;
        var secondary = Children.OfType<Expander>().Where(e => AutomationProperties.GetName(e) is
            "Identity and grouping" or "Identity and type" or "Appearance" or "Size" or "Actions"
            or "More properties" or "Type and containment" or "References and data" or "Region appearance"
            or "Identity, section and status").ToArray();
        if (secondary.Length == 0) return;
        if (_secondaryProperties is null)
        {
            var key = (draft.Task, "$more");
            _secondaryProperties = new StackPanel { Spacing = 8, IsVisible = _sectionStates.GetValueOrDefault(key) };
            _secondaryToggle = new ToggleButton
            {
                Content = new UiIcon { Glyph = UiGlyph.Settings }, IsChecked = _secondaryProperties.IsVisible,
                Classes = { "ghost", "icon-action" }, HorizontalAlignment = HorizontalAlignment.Right,
            };
            UiIcon.Describe(_secondaryToggle, "More properties");
            var body = _secondaryProperties;
            var toggle = _secondaryToggle;
            toggle.IsCheckedChanged += (_, _) =>
            {
                body.IsVisible = toggle.IsChecked == true;
                _sectionStates[key] = body.IsVisible;
            };
            var title = Children.OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("selection-name"));
            if (title is not null)
            {
                var index = Children.IndexOf(title); Children.Remove(title);
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
                header.Children.Add(title); Grid.SetColumn(_secondaryToggle, 1); header.Children.Add(_secondaryToggle);
                Children.Insert(index, header);
            }
            else Children.Insert(0, _secondaryToggle);
            Children.Add(body);
        }
        foreach (var section in secondary)
        {
            Children.Remove(section);
            _secondaryProperties.Children.Add(section);
        }
        Children.Remove(_secondaryProperties);
        Children.Add(_secondaryProperties);
    }
    private void CompactPrimaryField(Draft draft)
    {
        if (!draft.Inline) return;
        var key = draft.Task switch
        {
            TaskKind.ClassProperties or TaskKind.Rename or TaskKind.EntityProperties => draft.Task == TaskKind.EntityProperties ? "alias" : "label",
            TaskKind.SequenceParticipant or TaskKind.ZenParticipant => "alias",
            TaskKind.CreateRequirement => "name",
            TaskKind.EventFrame => "entity",
            TaskKind.RailroadRule => "name",
            TaskKind.WardleyComponent => "name",
            TaskKind.PieSlice or TaskKind.QuadrantPoint or TaskKind.OutlineNode or TaskKind.TemporalSection
                or TaskKind.TimelinePeriod or TaskKind.JourneyTask or TaskKind.PacketField or TaskKind.KanbanCard
                or TaskKind.KanbanColumn or TaskKind.TreemapNode or TaskKind.BlockItem or TaskKind.ArchitectureNode
                or TaskKind.ArchitectureGroup or TaskKind.C4Element or TaskKind.GanttTask or TaskKind.GanttSection
                or TaskKind.LaneNode or TaskKind.LaneGroup or TaskKind.AgentElement or TaskKind.UseCaseNode or TaskKind.UseCaseBoundary => "label",
            TaskKind.TimelineEvent => "text",
            _ => null,
        };
        var field = draft.Fields.FirstOrDefault(f => f.Key == key && f.Field is TextBox && Children.Contains(f.Field));
        if (field.Field is not TextBox input) return;
        if (Children.FirstOrDefault() is TextBlock heading && heading.Classes.Contains("inspector-heading"))
            heading.IsVisible = false;
        field.Label.IsVisible = false;
        input.Classes.Add("selection-name");
        input.MinHeight = 36;
        if (input.AcceptsReturn) input.MinHeight = 58;
    }

    private static UiGlyph? ActionGlyph(string label)
    {
        if (label.StartsWith("Delete ", StringComparison.Ordinal) || label is "Remove member" or "Delete and reflow") return UiGlyph.Delete;
        if (label.StartsWith("Add ", StringComparison.Ordinal) && !label.EndsWith("…", StringComparison.Ordinal) &&
            !label.EndsWith("...", StringComparison.Ordinal))
            return label.Contains("relationship", StringComparison.OrdinalIgnoreCase) || label.Contains("connection", StringComparison.OrdinalIgnoreCase)
                ? UiGlyph.Link : UiGlyph.Add;
        if (label.StartsWith("Move ", StringComparison.Ordinal) && !label.EndsWith("…", StringComparison.Ordinal))
            return label.Contains("earlier", StringComparison.Ordinal) || label.Contains("up", StringComparison.Ordinal) || label.Contains("left", StringComparison.Ordinal)
                ? UiGlyph.Up : UiGlyph.Down;
        return label switch
        {
            "Connect ports" or "Reverse direction" => UiGlyph.Link,
            "Resize to content" => UiGlyph.Fit,
            "Back to selection" => UiGlyph.Back,
            "Size…" or "Block appearance…" or "Element appearance…" or "C4 appearance…" => UiGlyph.Settings,
            _ => null,
        };
    }

    private static void CompactAction(Button button, UiGlyph glyph)
    {
        button.Content = new UiIcon { Glyph = glyph };
        button.Classes.Add("ghost");
        button.Classes.Add("icon-action");
        button.HorizontalAlignment = HorizontalAlignment.Left;
    }

    private static void GroupActions(Panel panel)
    {
        foreach (var expander in panel.Children.OfType<Expander>())
            if (expander.Content is Panel content) GroupActions(content);
        for (var i = 0; i < panel.Children.Count;)
        {
            if (panel.Children[i] is not Button b || !b.Classes.Contains("icon-action")) { i++; continue; }
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2) };
            while (i < panel.Children.Count && panel.Children[i] is Button action && action.Classes.Contains("icon-action"))
            {
                panel.Children.RemoveAt(i);
                action.Margin = new Thickness(0, 0, 4, 4);
                row.Children.Add(action);
            }
            panel.Children.Insert(i++, row);
        }
    }
}
