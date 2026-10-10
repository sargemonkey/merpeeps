using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static TaskKind? SetGrammarTask(StructuralSelection target) => target.Role switch
    {
        "VennSet" => TaskKind.VennSet, "VennOverlap" => TaskKind.VennOverlap, "VennText" => TaskKind.VennText,
        "RailroadRule" => TaskKind.RailroadRule, "RailroadExpression" => TaskKind.RailroadExpression, _ => null,
    };
    private bool ShowSetGrammarSelection()
    {
        if (!_canvas.IsSetGrammarDiagram) return false;
        if (Model().Venn is { } venn)
        {
            if (_canvas.SelectedStructuralTarget is { } selected && SetGrammarTask(selected) is { } task)
            {
                if (task == TaskKind.VennText) { Open(new(task, selected), false); return true; }
                var title = task == TaskKind.VennSet ? venn.Sets.Single(s => s.Id == selected.Id).Label ?? venn.Sets.Single(s => s.Id == selected.Id).Name
                    : venn.Overlaps.Single(o => o.Id == selected.Id).Label ?? "Overlap";
                Open(new(task, selected), false);
                Button("Add text to region", () => Open(new(TaskKind.VennText, Model()) { Values = { ["$area"] = selected.Id } }));
                if (task == TaskKind.VennSet) Button("Create overlap", () => Open(new(TaskKind.VennOverlap, Model()) { Values = { ["set:" + selected.Id] = true } }));
                foreach (var text in venn.Texts.Where(t => t.AreaId == selected.Id)) Button(text.Label ?? text.Name, () => _canvas.SelectStructural(new("VennText", text.Id)));
                Button("Delete selected region", () => ApplyVenn(task == TaskKind.VennSet ? VennEditing.RemoveSet(selected.Id) : VennEditing.RemoveOverlap(selected.Id), selected));
            }
            else
            {
                Heading(venn.Title.Length == 0 ? "Venn sets and overlaps" : venn.Title);
                Button("Add set", AddClass); Button("Create overlap", () => Open(new(TaskKind.VennOverlap, Model())));
                foreach (var set in venn.Sets) Button("Set: " + (set.Label ?? set.Name), () => _canvas.SelectStructural(new("VennSet", set.Id)));
                foreach (var overlap in venn.Overlaps) Button("Overlap: " + (overlap.Label ?? string.Join(", ", overlap.SetIds.Select(id => venn.Sets.Single(s => s.Id == id).Name))), () => _canvas.SelectStructural(new("VennOverlap", overlap.Id)));
            }
            foreach (var diagnostic in _canvas.VennLayout.Diagnostics.Take(3)) Text(diagnostic.Message);
        }
        else if (Model().Railroad is { } grammar)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "RailroadExpression" } selected)
            {
                var node = RailroadEditing.Locate(grammar, selected.Id).Expression;
                Open(new(TaskKind.RailroadExpression, selected), false);
                Button("Combine with another symbol", () => Open(new(TaskKind.RailroadCombine, selected)));
                Button("Make optional", () => ApplyRailroad(RailroadEditing.Wrap(node.Id, RailroadExpressionKind.Optional, Guid.NewGuid().ToString("N")), selected));
                Button("Repeat zero or more", () => ApplyRailroad(RailroadEditing.Wrap(node.Id, RailroadExpressionKind.Repeat, Guid.NewGuid().ToString("N")), selected));
                if (grammar.Notation == RailroadNotation.Peg)
                {
                    Button("Require lookahead", () => ApplyRailroad(RailroadEditing.Wrap(node.Id, RailroadExpressionKind.AndPredicate, Guid.NewGuid().ToString("N")), selected));
                    Button("Exclude lookahead", () => ApplyRailroad(RailroadEditing.Wrap(node.Id, RailroadExpressionKind.NotPredicate, Guid.NewGuid().ToString("N")), selected));
                }
                foreach (var child in node.Children) Button(child.Kind + ": " + child.Text, () => _canvas.SelectStructural(new("RailroadExpression", child.Id)));
            }
            else if (_canvas.SelectedStructuralTarget is { Role: "RailroadRule" } selectedRule) { Open(new(TaskKind.RailroadRule, selectedRule), false); return true; }
            else
            {
                Heading(grammar.Title.Length == 0 ? grammar.Notation + " grammar" : grammar.Title); Button("Add grammar rule", AddClass);
                foreach (var rule in grammar.Rules) Button(rule.Name, () => _canvas.SelectStructural(new("RailroadRule", rule.Id)));
            }
        }
        if (SelectionOwner is null) Button("Diagram display…", DisplayOptions);
        return true;
    }
    private bool BuildSetGrammarEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.VennSet: VennSetEditor(draft); return true;
            case TaskKind.VennOverlap: VennOverlapEditor(draft); return true;
            case TaskKind.VennText: VennTextEditor(draft); return true;
            case TaskKind.RailroadRule: RailroadRuleEditor(draft); return true;
            case TaskKind.RailroadExpression: RailroadExpressionEditor(draft); return true;
            case TaskKind.RailroadCombine: RailroadCombineEditor(draft); return true;
            case TaskKind.SetGrammarOptions:
                var venn = Model().Venn; var grammar = Model().Railroad;
                Heading("Diagram display"); Input(draft, "title", "Diagram title", venn?.Title ?? grammar!.Title);
                var config = venn?.Configuration ?? grammar!.Configuration;
                var keys = venn is not null ? new[] { ("width", 800), ("height", 450), ("padding", 25), ("fontSize", 24) }
                    : new[] { ("padding", 10), ("horizontalSeparation", 10), ("verticalSeparation", 8), ("arcRadius", 10), ("fontSize", 14) };
                foreach (var (key, value) in keys) Input(draft, key, key, config[key]?.ToString() ?? value.ToString(CultureInfo.InvariantCulture));
                Footer("Apply display", () =>
                {
                    var commands = new List<IEditCommand> { venn is not null ? VennEditing.SetTitle(Value(draft, "title")) : RailroadEditing.SetTitle(Value(draft, "title")) };
                    foreach (var (key, _) in keys) commands.Add(venn is not null ? VennEditing.SetOption(key, JsonValue.Create(Numeric(draft, key))) : RailroadEditing.SetOption(key, JsonValue.Create(Numeric(draft, key))));
                    Apply(new BatchCommand("Edit diagram display", commands.ToArray()), draft.Owner, after: () => _canvas.SelectStructural(null));
                });
                return true;
            default: return false;
        }
    }
    private void VennSetEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => Model().Venn!.Sets.Single(s => s.Id == target.Id)) : null;
        Heading(original is null ? "Add set" : "Edit set");
        Input(draft, "name", "Set identifier", original?.Name ?? ""); Input(draft, "label", "Set label (optional)", original?.Label ?? "");
        Input(draft, "size", "Set size (blank uses default)", original?.Size?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        VennStyleFields(draft, original?.Style ?? new());
        Footer("Apply set", () =>
        {
            var set = new VennSet(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "name"), Label(draft, original?.Label),
                Optional(draft, "size") is null ? null : Numeric(draft, "size")) { Style = VennStyle(draft, original?.Style) };
            ApplyVenn(original is null ? VennEditing.AddSet(set) : VennEditing.UpdateSet(set), draft.Owner);
        });
    }
    private void VennOverlapEditor(Draft draft)
    {
        var d = Model().Venn!;
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => d.Overlaps.Single(o => o.Id == target.Id)) : null;
        Heading("Common overlap");
        foreach (var set in d.Sets) Check(draft, "set:" + set.Id, "Include " + set.Name, original?.SetIds.Contains(set.Id) ?? false);
        Input(draft, "label", "Overlap label (optional)", original?.Label ?? "");
        Input(draft, "size", "Overlap size (blank uses default)", original?.Size?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        VennStyleFields(draft, original?.Style ?? new());
        Footer("Apply overlap", () =>
        {
            var overlap = new VennOverlap(original?.Id ?? Guid.NewGuid().ToString("N"), d.Sets.Where(s => draft.Get("set:" + s.Id, false)).Select(s => s.Id).ToArray(),
                Label(draft, original?.Label), Optional(draft, "size") is null ? null : Numeric(draft, "size")) { Style = VennStyle(draft, original?.Style) };
            ApplyVenn(original is null ? VennEditing.AddOverlap(overlap) : VennEditing.UpdateOverlap(overlap), draft.Owner);
        });
    }
    private void VennTextEditor(Draft draft)
    {
        var d = Model().Venn!;
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => d.Texts.Single(t => t.Id == target.Id)) : null;
        Heading("Region text"); Input(draft, "name", "Text identifier", original?.Name ?? ""); Input(draft, "label", "Text label (optional)", original?.Label ?? "");
        ChoiceInput(draft, "area", "Containing region", d.Sets.Select(s => new Choice(s.Id, s.Label ?? s.Name)).Concat(d.Overlaps.Select(o => new Choice(o.Id, o.Label ?? "Overlap"))), original?.AreaId ?? draft.Get("$area", ""));
        VennStyleFields(draft, original?.Style ?? new());
        Footer("Apply region text", () =>
        {
            var text = new VennText(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "name"), Value(draft, "area"), Label(draft, original?.Label)) { Style = VennStyle(draft, original?.Style) };
            ApplyVenn(original is null ? VennEditing.AddText(text) : VennEditing.UpdateText(text), draft.Owner);
        });
    }
    private void VennStyleFields(Draft draft, JsonObject style)
    {
        var start = Children.Count;
        foreach (var key in new[] { "fill", "color", "stroke", "stroke-width", "fill-opacity" }) StyleInput(draft, key, style[key]?.ToString() ?? "");
        CollapseDetails(start, "Region appearance");
    }
    private static JsonObject VennStyle(Draft draft, JsonObject? original)
    {
        var style = original is null ? new JsonObject() : (JsonObject)original.DeepClone();
        foreach (var key in new[] { "fill", "color", "stroke", "stroke-width", "fill-opacity" })
            if (Optional(draft, key) is { } value) style[key] = value; else style.Remove(key);
        return style;
    }
    private static string? Label(Draft draft, string? original) => Value(draft, "label").Length > 0 ? Value(draft, "label") : original is { Length: 0 } ? "" : null;
    private void RailroadRuleEditor(Draft draft)
    {
        var d = Model().Railroad!;
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => d.Rules.Single(r => r.Id == target.Id)) : null;
        Heading(original is null ? "Add grammar rule" : "Grammar rule");
        Input(draft, "name", "Rule name", original?.Name ?? "");
        if (original is null)
        {
            EnumInput(draft, "kind", "First symbol kind", RailroadExpressionKind.Terminal, LeafKinds(d.Notation));
            Input(draft, "text", "First symbol text");
        }
        Footer("Apply grammar rule", () => ApplyRailroad(original is null ? RailroadEditing.AddRule(new(Guid.NewGuid().ToString("N"), Value(draft, "name"),
                new(Guid.NewGuid().ToString("N"), draft.Get("kind", RailroadExpressionKind.Terminal), Value(draft, "text"), [])))
            : RailroadEditing.RenameRule(original.Id, Value(draft, "name")), draft.Owner));
        if (original is not null)
        {
            Button("Edit rule expression", () => _canvas.SelectStructural(new("RailroadExpression", original.Expression.Id)));
            var index = d.Rules.FindIndex(r => r.Id == original.Id);
            Button("Move rule earlier", () => ApplyRailroad(RailroadEditing.MoveRule(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move rule later", () => ApplyRailroad(RailroadEditing.MoveRule(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < d.Rules.Count;
            Button("Delete grammar rule", () => ApplyRailroad(RailroadEditing.RemoveRule(original.Id), draft.Owner));
        }
    }
    private static IEnumerable<RailroadExpressionKind> LeafKinds(RailroadNotation notation)
    {
        yield return RailroadExpressionKind.Terminal; yield return RailroadExpressionKind.Nonterminal;
        if (notation is RailroadNotation.Ebnf or RailroadNotation.Primitives) yield return RailroadExpressionKind.Special;
        if (notation == RailroadNotation.Abnf) yield return RailroadExpressionKind.NumericLiteral;
        if (notation == RailroadNotation.Peg) yield return RailroadExpressionKind.AnyCharacter;
    }
    private void RailroadExpressionEditor(Draft draft)
    {
        var d = Model().Railroad!;
        var original = Remember(draft, () => RailroadEditing.Locate(d, ((StructuralSelection)draft.Owner).Id).Expression);
        Heading(original.Kind.ToString());
        if (original.Children.Count == 0)
        {
            EnumInput(draft, "kind", "Symbol kind", original.Kind, LeafKinds(d.Notation));
            Input(draft, "text", "Symbol text", original.Text);
        }
        if (original.Kind == RailroadExpressionKind.Repeat)
        {
            Input(draft, "min", "Minimum repetitions", original.Minimum.ToString(CultureInfo.InvariantCulture));
            if (d.Notation == RailroadNotation.Abnf) Input(draft, "max", "Maximum repetitions (blank is unbounded)", original.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "");
        }
        Footer("Apply expression", () => ApplyRailroad(RailroadEditing.UpdateExpression(original with
        {
            Kind = original.Children.Count == 0 ? draft.Get("kind", original.Kind) : original.Kind,
            Text = original.Children.Count == 0 ? Value(draft, "text") : original.Text,
            Minimum = original.Kind == RailroadExpressionKind.Repeat ? int.Parse(Value(draft, "min"), CultureInfo.InvariantCulture) : original.Minimum,
            Maximum = original.Kind == RailroadExpressionKind.Repeat && d.Notation == RailroadNotation.Abnf ? Optional(draft, "max") is null ? null : int.Parse(Value(draft, "max"), CultureInfo.InvariantCulture) : original.Maximum,
        }), draft.Owner));
        foreach (var (child, index) in original.Children.Select((child, index) => (child, index)))
        {
            Button("Edit " + child.Kind + ": " + child.Text, () => _canvas.SelectStructural(new("RailroadExpression", child.Id)));
            if (original.Kind is RailroadExpressionKind.Sequence or RailroadExpressionKind.Choice or RailroadExpressionKind.OrderedChoice)
            {
                Button("Move child " + (index + 1) + " earlier", () => ApplyRailroad(RailroadEditing.MoveChild(original.Id, index, index - 1), draft.Owner)).IsEnabled = index > 0;
                if (original.Children.Count > 2) Button("Remove child " + (index + 1), () => ApplyRailroad(RailroadEditing.RemoveChild(original.Id, index), draft.Owner));
            }
            Button("Keep only child " + (index + 1), () => ApplyRailroad(RailroadEditing.KeepChild(original.Id, index), draft.Owner));
        }
    }
    private void RailroadCombineEditor(Draft draft)
    {
        var d = Model().Railroad!; var target = (StructuralSelection)draft.Owner;
        Heading("Compose grammar");
        var kinds = new List<RailroadExpressionKind> { RailroadExpressionKind.Sequence, d.Notation == RailroadNotation.Peg ? RailroadExpressionKind.OrderedChoice : RailroadExpressionKind.Choice };
        if (d.Notation == RailroadNotation.Ebnf) kinds.Add(RailroadExpressionKind.Exception);
        EnumInput(draft, "operator", "Composition", RailroadExpressionKind.Sequence, kinds);
        EnumInput(draft, "kind", "New symbol kind", RailroadExpressionKind.Terminal, LeafKinds(d.Notation)); Input(draft, "text", "New symbol text");
        Footer("Compose expressions", () => ApplyRailroad(RailroadEditing.WrapWithSibling(target.Id, draft.Get("operator", RailroadExpressionKind.Sequence), Guid.NewGuid().ToString("N"),
            new(Guid.NewGuid().ToString("N"), draft.Get("kind", RailroadExpressionKind.Terminal), Value(draft, "text"), [])), draft.Owner));
    }
    private void ApplyVenn(VennEditCommand command, object owner) => Apply(command, owner, after: () =>
    {
        var id = command.ResultId; if (id is null) { _canvas.SelectStructural(null); return; }
        var d = Model().Venn!;
        _canvas.SelectStructural(new(d.Sets.Any(s => s.Id == id) ? "VennSet" : d.Overlaps.Any(o => o.Id == id) ? "VennOverlap" : "VennText", id));
    });
    private void ApplyRailroad(RailroadEditCommand command, object owner) => Apply(command, owner, after: () =>
    {
        var id = command.ResultId;
        _canvas.SelectStructural(id is null ? null : new(Model().Railroad!.Rules.Any(r => r.Id == id) ? "RailroadRule" : "RailroadExpression", id));
    });
}
