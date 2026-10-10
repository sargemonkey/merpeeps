using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static TaskKind? StrategyTask(StructuralSelection target) => target.Role switch
    {
        "WardleyComponent" => TaskKind.WardleyComponent, "WardleyLink" => TaskKind.WardleyLink, "WardleyAnnotation" => TaskKind.WardleyAnnotation,
        "IshikawaCause" => TaskKind.IshikawaCause, "IshikawaProblem" => TaskKind.IshikawaProblem,
        "CynefinItem" => TaskKind.CynefinItem, "CynefinTransition" => TaskKind.CynefinTransition, _ => null,
    };
    private bool ShowStrategySelection()
    {
        if (!_canvas.IsStrategyDiagram) return false;
        if (Model().Wardley is { } map)
        {
            if (_canvas.GridConnectionSource is { } source)
            {
                Heading("Choose dependency target"); Text("From: " + map.Components.Single(c => c.Id == source).Name);
                Text("Click a component on the map. Escape cancels."); Button("Cancel dependency", _canvas.CancelRequirementConnection); return true;
            }
            if (_canvas.SelectedStructuralTarget is { Role: "WardleyComponent" } selected)
            {
                var component = map.Components.Single(c => c.Id == selected.Id);
                Open(new(TaskKind.WardleyComponent, selected), false);
                Button("Add dependency", () => _canvas.BeginGridConnection(component.Id));
                if (component.PipelineParentId is null) Button("Add pipeline variant", () => Open(new(TaskKind.WardleyComponent, Model()) { Values = { ["$pipeline"] = component.Id } }));
                Button("Delete component and variants", () => ApplyStrategy(WardleyEditing.RemoveComponent(component.Id), selected)); return true;
            }
            if (_canvas.SelectedStructuralTarget is { } selectedOther && StrategyTask(selectedOther) is { } task) { Open(new(task, selectedOther), false); return true; }
            Heading(map.Title.Length == 0 ? "Wardley map" : map.Title); Button("Add component", AddClass);
            Button("Add map annotation", () => Open(new(TaskKind.WardleyAnnotation, Model())));
            foreach (var component in map.Components) Button(component.Name, () => _canvas.SelectStructural(new("WardleyComponent", component.Id)));
            Button("Map size and evolution stages…", DisplayOptions);
        }
        else if (Model().Ishikawa is { } fish)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "IshikawaCause" } selected)
            {
                var cause = fish.AllCauses.Single(c => c.Id == selected.Id);
                Open(new(TaskKind.IshikawaCause, selected), false);
                Button("Add subcause", () => Open(new(TaskKind.IshikawaCause, Model()) { Values = { ["$parent"] = cause.Id } }));
                Button("Move cause subtree…", () => Open(new(TaskKind.IshikawaMove, selected)));
                Button("Delete cause subtree", () => ApplyStrategy(IshikawaEditing.RemoveCause(cause.Id), selected));
            }
            else if (_canvas.SelectedStructuralTarget is { Role: "IshikawaProblem" } problem)
                Open(new(TaskKind.IshikawaProblem, problem), false);
            else
            {
                Heading(fish.Problem.Length == 0 ? "Cause and effect" : fish.Problem);
                Button(fish.Problem.Length == 0 ? "Set problem" : "Add cause", AddClass);
                Button("Edit problem statement", DisplayOptions);
                foreach (var cause in fish.AllCauses) Button(cause.Label, () => _canvas.SelectStructural(new("IshikawaCause", cause.Id)));
            }
        }
        else if (Model().Cynefin is { } framework)
        {
            if (_canvas.SelectedStructuralTarget is { } selected && StrategyTask(selected) is { } task) { Open(new(task, selected), false); return true; }
            var domain = _canvas.SelectedStructuralTarget is { Role: "CynefinDomain" } domainSelection && Enum.TryParse<CynefinDomain>(domainSelection.Id, out var parsed) ? parsed : (CynefinDomain?)null;
            Heading(domain?.ToString() ?? (framework.Title.Length == 0 ? "Cynefin framework" : framework.Title));
            Button("Add domain item", () => Open(new(TaskKind.CynefinItem, Model()) { Values = { ["domain"] = domain ?? CynefinDomain.Complex } }));
            Button("Add domain transition", () => Open(new(TaskKind.CynefinTransition, Model())));
            foreach (var item in framework.Items.Where(i => domain is null || i.Domain == domain))
                Button(item.Domain + ": " + item.Label, () => _canvas.SelectStructural(new("CynefinItem", item.Id)));
            foreach (var transition in framework.Transitions)
                Button(transition.From + " → " + transition.To, () => _canvas.SelectStructural(new("CynefinTransition", transition.Id)));
            Button("Framework display…", DisplayOptions);
        }
        foreach (var issue in _canvas.StrategyDiagnostics.Take(3)) Text(issue.Message);
        return true;
    }
    private bool BuildStrategyEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.WardleyComponent: WardleyComponentEditor(draft); return true;
            case TaskKind.WardleyLink: WardleyLinkEditor(draft); return true;
            case TaskKind.WardleyAnnotation: WardleyAnnotationEditor(draft); return true;
            case TaskKind.WardleyOptions: WardleyOptionsEditor(draft); return true;
            case TaskKind.IshikawaProblem:
                Heading("Problem statement"); Input(draft, "problem", "Problem", Model().Ishikawa!.Problem);
                Footer("Apply problem", () => ApplyStrategy(IshikawaEditing.SetProblem(Value(draft, "problem")), draft.Owner)); return true;
            case TaskKind.IshikawaCause:
                var original = draft.Owner is StructuralSelection causeTarget ? Remember(draft, () => Model().Ishikawa!.AllCauses.Single(c => c.Id == causeTarget.Id)) : null;
                Heading(original is null ? "Add cause" : "Edit cause"); Input(draft, "label", "Cause label", original?.Label ?? "");
                Footer("Apply cause", () => ApplyStrategy(original is null ? IshikawaEditing.AddCause(new("cause" + Guid.NewGuid().ToString("N"), Value(draft, "label"), []), Optional(draft, "$parent"))
                    : IshikawaEditing.RenameCause(original.Id, Value(draft, "label")), draft.Owner)); return true;
            case TaskKind.IshikawaMove:
                var target = (StructuralSelection)draft.Owner; var location = IshikawaEditing.Locate(Model().Ishikawa!, target.Id);
                var excluded = IshikawaDocument.Walk([location.Cause]).Select(c => c.Id).ToHashSet();
                Heading("Move cause subtree");
                ChoiceInput(draft, "parent", "Destination cause", new[] { new Choice("", "Main spine") }.Concat(Model().Ishikawa!.AllCauses.Where(c => !excluded.Contains(c.Id)).Select(c => new Choice(c.Id, c.Label))), location.ParentId ?? "");
                Input(draft, "position", "Position after removal (zero based)", location.Index.ToString(CultureInfo.InvariantCulture));
                Footer("Move cause", () => ApplyStrategy(IshikawaEditing.MoveCause(target.Id, Optional(draft, "parent"), int.Parse(Value(draft, "position"), CultureInfo.InvariantCulture)), draft.Owner)); return true;
            case TaskKind.CynefinItem: CynefinItemEditor(draft); return true;
            case TaskKind.CynefinTransition: CynefinTransitionEditor(draft); return true;
            case TaskKind.CynefinOptions: CynefinOptionsEditor(draft); return true;
            default: return false;
        }
    }
    private void WardleyComponentEditor(Draft draft)
    {
        var d = Model().Wardley!;
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => d.Components.Single(c => c.Id == target.Id)) : null;
        var parent = original?.PipelineParentId ?? Optional(draft, "$pipeline");
        Heading(original is null ? "Add map component" : "Edit map component");
        Input(draft, "name", "Component name", original?.Name ?? "");
        if (parent is null) Input(draft, "visibility", "Visibility (0–1, vertical)", (original?.Visibility ?? .5).ToString("R", CultureInfo.InvariantCulture));
        else Text("Visibility inherited from " + d.Components.Single(c => c.Id == parent).Name + ".");
        Input(draft, "evolution", "Evolution (0–1, horizontal)", (original?.Evolution ?? .5).ToString("R", CultureInfo.InvariantCulture));
        var details = Children.Count;
        if (parent is null)
        {
            Check(draft, "anchor", "Anchor / user need", original?.Anchor ?? false);
            EnumInput(draft, "strategy", "Sourcing strategy", original?.Strategy ?? WardleyStrategy.None);
            Check(draft, "inertia", "Resists change (inertia)", original?.Inertia ?? false);
        }
        Input(draft, "evolve", "Evolve to (blank for none)", original?.EvolveTo?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Input(draft, "labelX", "Label horizontal offset (px)", (original?.LabelOffset.X ?? 0).ToString("R", CultureInfo.InvariantCulture));
        Input(draft, "labelY", "Label vertical offset (px)", (original?.LabelOffset.Y ?? 0).ToString("R", CultureInfo.InvariantCulture));
        CollapseDetails(details, "Strategy and label placement");
        Footer("Apply map component", () =>
        {
            var component = new WardleyComponent(original?.Id ?? "component" + Guid.NewGuid().ToString("N"), Value(draft, "name"), Numeric(draft, "evolution"))
            {
                Visibility = parent is null ? Numeric(draft, "visibility") : null, PipelineParentId = parent,
                Anchor = parent is null && draft.Get("anchor", false), Inertia = parent is null && draft.Get("inertia", false),
                Strategy = parent is null ? draft.Get("strategy", WardleyStrategy.None) : WardleyStrategy.None,
                EvolveTo = Optional(draft, "evolve") is null ? null : Numeric(draft, "evolve"),
                LabelOffset = new(Numeric(draft, "labelX"), Numeric(draft, "labelY")), Trend = original?.Trend,
            };
            ApplyStrategy(original is null ? WardleyEditing.AddComponent(component) : WardleyEditing.UpdateComponent(component), draft.Owner);
        });
    }
    private void WardleyLinkEditor(Draft draft)
    {
        var d = Model().Wardley!; var original = Remember(draft, () => d.Links.Single(l => l.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("Dependency / flow");
        ChoiceInput(draft, "from", "Source component", d.Components.Select(c => new Choice(c.Id, c.Name)), original.From);
        ChoiceInput(draft, "to", "Target component", d.Components.Select(c => new Choice(c.Id, c.Name)), original.To);
        EnumInput(draft, "kind", "Link kind", original.Kind); Input(draft, "label", "Link label", original.Label);
        Footer("Apply map link", () => ApplyStrategy(WardleyEditing.UpdateLink(original with { From = Value(draft, "from"), To = Value(draft, "to"), Kind = draft.Get("kind", original.Kind), Label = Value(draft, "label") }), draft.Owner));
        Button("Delete map link", () => ApplyStrategy(WardleyEditing.RemoveLink(original.Id), draft.Owner));
    }
    private void WardleyAnnotationEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => Model().Wardley!.Annotations.Single(a => a.Id == target.Id)) : null;
        Heading("Map annotation");
        var picker = EnumInput(draft, "kind", "Annotation kind", original?.Kind ?? WardleyAnnotationKind.Note);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft);
        Input(draft, "text", "Annotation text", original?.Text ?? "");
        Input(draft, "visibility", "Visibility", (original?.Visibility ?? .5).ToString("R", CultureInfo.InvariantCulture));
        Input(draft, "evolution", "Evolution", (original?.Evolution ?? .5).ToString("R", CultureInfo.InvariantCulture));
        var kind = draft.Get("kind", original?.Kind ?? WardleyAnnotationKind.Note);
        if (kind == WardleyAnnotationKind.Numbered) Input(draft, "number", "Annotation number", (original?.Number ?? 1).ToString(CultureInfo.InvariantCulture));
        Footer("Apply annotation", () => ApplyStrategy(WardleyEditing.SetAnnotation(new(original?.Id ?? "note" + Guid.NewGuid().ToString("N"), kind, Value(draft, "text"), Numeric(draft, "visibility"), Numeric(draft, "evolution"))
        { Number = kind == WardleyAnnotationKind.Numbered ? int.Parse(Value(draft, "number"), CultureInfo.InvariantCulture) : null }), draft.Owner));
    }
    private void WardleyOptionsEditor(Draft draft)
    {
        var d = Model().Wardley!; Heading("Map options"); Input(draft, "title", "Map title", d.Title);
        Input(draft, "width", "Map width (px)", d.Width.ToString("R", CultureInfo.InvariantCulture)); Input(draft, "height", "Map height (px)", d.Height.ToString("R", CultureInfo.InvariantCulture));
        var stages = d.Stages.Count == 0 ? new[] { new WardleyStage("Genesis", null, .25), new("Custom", null, .5), new("Product", null, .75), new("Commodity", null, 1) } : d.Stages.ToArray();
        var details = Children.Count;
        for (var i = 0; i < stages.Length; i++)
        {
            Input(draft, "stage" + i, $"Stage {i + 1} label", stages[i].Label);
            Input(draft, "end" + i, $"Stage {i + 1} end", stages[i].End.ToString("R", CultureInfo.InvariantCulture));
        }
        CollapseDetails(details, "Evolution stages");
        Input(draft, "boxVis", "Annotation legend visibility (blank hides)", d.AnnotationBox?.Y.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Input(draft, "boxEvo", "Annotation legend evolution", d.AnnotationBox?.X.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Footer("Apply map options", () => ApplyStrategy(WardleyEditing.SetMap(Value(draft, "title"), Numeric(draft, "width"), Numeric(draft, "height"),
            stages.Select((s, i) => s with { Label = Value(draft, "stage" + i), End = Numeric(draft, "end" + i), ExplicitEnd = s.ExplicitEnd || Numeric(draft, "end" + i) != s.End }).ToArray(),
            Optional(draft, "boxVis") is null ? null : new DiagramPoint(Numeric(draft, "boxEvo"), Numeric(draft, "boxVis"))), draft.Owner));
    }
    private void CynefinItemEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => Model().Cynefin!.Items.Single(i => i.Id == target.Id)) : null;
        Heading("Domain item"); Input(draft, "label", "Item label", original?.Label ?? ""); EnumInput(draft, "domain", "Complexity domain", original?.Domain ?? CynefinDomain.Complex);
        Footer("Apply domain item", () =>
        {
            var item = new CynefinItem(original?.Id ?? "item" + Guid.NewGuid().ToString("N"), draft.Get("domain", CynefinDomain.Complex), Value(draft, "label"));
            ApplyStrategy(original is null ? CynefinEditing.AddItem(item) : CynefinEditing.UpdateItem(item), draft.Owner);
        });
        if (original is not null) Button("Delete domain item", () => ApplyStrategy(CynefinEditing.RemoveItem(original.Id), draft.Owner));
    }
    private void CynefinTransitionEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => Model().Cynefin!.Transitions.Single(t => t.Id == target.Id)) : null;
        Heading("Domain transition");
        EnumInput(draft, "from", "From domain", original?.From ?? CynefinDomain.Complex);
        EnumInput(draft, "to", "To domain", original?.To ?? CynefinDomain.Complicated);
        Input(draft, "label", "Transition label", original?.Label ?? "");
        Footer("Apply transition", () => ApplyStrategy(CynefinEditing.SetTransition(new(original?.Id ?? "transition" + Guid.NewGuid().ToString("N"),
            draft.Get("from", CynefinDomain.Complex), draft.Get("to", CynefinDomain.Complicated), Value(draft, "label"))), draft.Owner));
        if (original is not null) Button("Delete transition", () => ApplyStrategy(CynefinEditing.RemoveTransition(original.Id), draft.Owner));
    }
    private void CynefinOptionsEditor(Draft draft)
    {
        var d = Model().Cynefin!; Heading("Framework display"); Input(draft, "title", "Framework title", d.Title);
        foreach (var (key, initial) in new[] { ("width", 800), ("height", 600), ("padding", 40), ("boundaryAmplitude", 8), ("seed", 0) })
            Input(draft, key, key, d.Configuration[key]?.ToString() ?? initial.ToString(CultureInfo.InvariantCulture));
        Check(draft, "descriptions", "Show domain descriptions", d.Configuration["showDomainDescriptions"]?.GetValue<bool>() ?? true);
        Footer("Apply framework display", () =>
        {
            var commands = new List<IEditCommand> { CynefinEditing.SetTitle(Value(draft, "title")), CynefinEditing.SetOption("showDomainDescriptions", JsonValue.Create(draft.Get("descriptions", true))) };
            commands.AddRange(new[] { "width", "height", "padding", "boundaryAmplitude", "seed" }.Select(key => CynefinEditing.SetOption(key, JsonValue.Create(Numeric(draft, key)))));
            Apply(new BatchCommand("Edit framework", commands.ToArray()), draft.Owner, after: () => _canvas.SelectStructural(null));
        });
    }
    private void ApplyStrategy(StrategyEditCommand command, object owner) => Apply(command, owner, after: () =>
    {
        var id = command.ResultId; if (id is null) { _canvas.SelectStructural(null); return; }
        var role = Model().Wardley is { } map ? map.Components.Any(c => c.Id == id) ? "WardleyComponent" : map.Links.Any(l => l.Id == id) ? "WardleyLink" : "WardleyAnnotation"
            : Model().Ishikawa is not null ? "IshikawaCause" : Model().Cynefin!.Items.Any(i => i.Id == id) ? "CynefinItem" : "CynefinTransition";
        _canvas.SelectStructural(new(role, id));
    });
}
