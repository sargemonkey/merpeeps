using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private bool ShowWeightedSelection()
    {
        if (Model().Sankey is { } flow)
        {
            if (_canvas.SelectedStructuralTarget is { } selected) { Open(new(selected.Role == "FlowNode" ? TaskKind.SankeyNode : TaskKind.SankeyFlow, selected), false); return true; }
            Heading("Sankey flows"); Button("Add weighted flow", AddClass);
            foreach (var link in flow.Links)
                Button(flow.Nodes.Single(n => n.Id == link.From).Label + " → " + flow.Nodes.Single(n => n.Id == link.To).Label + ": " + link.Value.ToString("G6", CultureInfo.InvariantCulture),
                    () => _canvas.SelectStructural(new("Flow", link.Id)));
            Button("Flow options…", DisplayOptions); return true;
        }
        if (Model().Treemap is { } tree)
        {
            if (_canvas.SelectedStructuralTarget is { } selected)
            {
                var node = tree.Nodes.Single(n => n.Id == selected.Id);
                Open(new(TaskKind.TreemapNode, selected), false);
                Text("Total: " + node.Total.ToString("G6", CultureInfo.InvariantCulture));
                Button("Add child", () => Open(new(TaskKind.TreemapNode, Model()) { Values = { ["$parent"] = node.Id } })).IsEnabled = node.Value is null;
                foreach (var child in node.Children) Button(child.Label, () => _canvas.SelectStructural(new("WeightedNode", child.Id)));
                Button("Delete weighted subtree", () => ApplyWeighted(TreemapEditing.Remove(node.Id), selected)); return true;
            }
            Heading("Treemap"); Button("Add root", AddClass);
            foreach (var root in tree.Roots) Button(root.Label, () => _canvas.SelectStructural(new("WeightedNode", root.Id)));
            Button("Treemap options…", DisplayOptions); return true;
        }
        return false;
    }
    private bool BuildWeightedEditor(Draft draft)
    {
        if (draft.Task == TaskKind.SankeyNode)
        {
            var id = ((StructuralSelection)draft.Owner).Id;
            Heading("Flow node"); Input(draft, "label", "Node label", Model().Sankey!.Nodes.Single(n => n.Id == id).Label);
            Footer("Apply node label", () => ApplyWeighted(SankeyEditing.RenameNode(id, Value(draft, "label")), draft.Owner)); return true;
        }
        if (draft.Task == TaskKind.SankeyFlow)
        {
            var flow = Model().Sankey!;
            var original = draft.Owner is StructuralSelection target ? Remember(draft, () => flow.Links.Single(l => l.Id == target.Id)) : null;
            Heading("Weighted flow");
            if (original is null) { Input(draft, "from", "Source label"); Input(draft, "to", "Target label"); }
            else
            {
                ChoiceInput(draft, "from", "Source node", flow.Nodes.Select(n => new Choice(n.Id, n.Label)), original.From);
                ChoiceInput(draft, "to", "Target node", flow.Nodes.Select(n => new Choice(n.Id, n.Label)), original.To);
            }
            Input(draft, "value", "Flow value", original?.Value.ToString("R", CultureInfo.InvariantCulture) ?? "");
            Footer("Apply weighted flow", () => ApplyWeighted(original is null ? SankeyEditing.AddFlow(Value(draft, "from"), Value(draft, "to"), Numeric(draft, "value"))
                : SankeyEditing.UpdateFlow(original with { From = Value(draft, "from"), To = Value(draft, "to"), Value = Numeric(draft, "value") }), draft.Owner));
            if (original is not null) Button("Delete flow", () => ApplyWeighted(SankeyEditing.RemoveFlow(original.Id), draft.Owner));
            return true;
        }
        if (draft.Task == TaskKind.TreemapNode)
        {
            var original = draft.Owner is StructuralSelection target ? Remember(draft, () => Model().Treemap!.Nodes.Single(n => n.Id == target.Id)) : null;
            Heading("Weighted hierarchy node");
            Input(draft, "label", "Node label", original?.Label ?? "");
            Input(draft, "value", "Leaf value (blank is section)", original?.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            Input(draft, "classes", "Style classes", string.Join(",", original?.Classes ?? []));
            Footer("Apply weighted node", () =>
            {
                var node = new TreemapNode(original?.Id ?? "w" + Guid.NewGuid().ToString("N"), Value(draft, "label"),
                    Optional(draft, "value") is null ? null : Numeric(draft, "value"), original?.Children ?? [])
                { Classes = Value(draft, "classes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) };
                ApplyWeighted(original is null ? TreemapEditing.Add(node, Optional(draft, "$parent")) : TreemapEditing.Update(node), draft.Owner);
            });
            return true;
        }
        if (draft.Task == TaskKind.WeightedOptions)
        {
            Heading("Weighted diagram options");
            var keys = Model().Sankey is not null ? new[] { "width", "height", "nodeWidth", "nodePadding", "nodeAlignment", "linkColor", "labelStyle", "showValues" }
                : new[] { "padding", "diagramPadding", "showValues", "nodeWidth", "nodeHeight", "borderWidth", "valueFontSize", "labelFontSize", "valueFormat" };
            var key = draft.Get("key", keys[0]);
            var choices = keys.Select(k => new Choice(k, k)).ToArray();
            var selector = new Avalonia.Controls.ComboBox { ItemsSource = choices, SelectedItem = choices.Single(c => c.Id == key) };
            Field("Option", selector);
            selector.SelectionChanged += (_, _) =>
            {
                if (selector.SelectedItem is not Choice choice || choice.Id == Value(draft, "key")) return;
                if (!string.IsNullOrEmpty(draft.Get("value", "")))
                {
                    _feedback.Text = "Apply or clear this value before choosing another option.";
                    selector.SelectedItem = choices.Single(c => c.Id == Value(draft, "key"));
                    return;
                }
                draft.Values["key"] = choice.Id; RebuildEditor(draft);
            };
            if (key == "linkColor")
            {
                ChoiceInput(draft, "$colorMode", "Link color mode",
                    new[] { new Choice("gradient", "Gradient"), new Choice("source", "Source node"), new Choice("target", "Target node"), new Choice("custom", "Custom color") },
                    "gradient").SelectionChanged += (_, _) => RebuildEditor(draft);
                if (draft.Get("$colorMode", "gradient") == "custom") ColourInput(draft, "value", "Link color", "#6C8EBF", null);
            }
            else Input(draft, "value", "Option value");
            Footer("Apply option", () =>
            {
                var mode = draft.Get("$colorMode", "gradient");
                var text = Value(draft, "key") == "linkColor" && mode != "custom" ? mode : Value(draft, "value");
                JsonNode value = bool.TryParse(text, out var flag) ? JsonValue.Create(flag)! : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number)! : JsonValue.Create(text)!;
                ApplyWeighted(Model().Sankey is not null ? SankeyEditing.SetOption(Value(draft, "key"), value) : TreemapEditing.SetOption(Value(draft, "key"), value), draft.Owner);
            });
            return true;
        }
        return false;
    }
    private void ApplyWeighted(WeightedEditCommand command, object owner) => Apply(command, owner, after: () =>
        _canvas.SelectStructural(command.ResultId is { } id ? new(Model().Treemap is not null ? "WeightedNode" : Model().Sankey!.Nodes.Any(n => n.Id == id) ? "FlowNode" : "Flow", id) : null));
}
