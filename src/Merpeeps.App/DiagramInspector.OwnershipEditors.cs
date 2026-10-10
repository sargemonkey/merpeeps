using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static TaskKind? OwnershipTask(StructuralSelection target) => target.Role switch
    {
        "SwimlaneNode" => TaskKind.LaneNode, "SwimlaneGroup" when target.Id.Length > 0 => TaskKind.LaneGroup, "SwimlaneLink" => TaskKind.LaneLink,
        "UseCaseNode" => TaskKind.UseCaseNode, "UseCaseBoundary" => TaskKind.UseCaseBoundary, "UseCaseRelationship" => TaskKind.UseCaseLink, _ => null,
    };
    private bool ShowOwnershipSelection()
    {
        if (!_canvas.IsOwnershipDiagram) return false;
        if (_canvas.GridConnectionSource is not null)
        {
            Heading("Choose relationship target"); Text("Click a target step, actor or use case. Escape cancels.");
            Button("Cancel connection", _canvas.CancelRequirementConnection); return true;
        }
        if (_canvas.SelectedStructuralTarget is { } selected && OwnershipTask(selected) is { } task)
        {
            if (task is TaskKind.LaneLink or TaskKind.UseCaseLink) { Open(new(task, selected), false); return true; }
            Open(new(task, selected), false);
            if (task is TaskKind.LaneNode or TaskKind.UseCaseNode) Button("Add relationship", () => _canvas.BeginGridConnection(selected.Id));
            if (task == TaskKind.LaneGroup) Button("Add step to lane", () => Open(new(TaskKind.LaneNode, Model()) { Values = { ["$parent"] = selected.Id } }));
            if (task == TaskKind.UseCaseBoundary) Button("Add element inside boundary", () => Open(new(TaskKind.UseCaseNode, Model()) { Values = { ["$parent"] = selected.Id } }));
        }
        else if (Model().Swimlane is { } lanes)
        {
            Heading("Responsibility lanes"); Button("Add lane", AddClass);
            Button("Add step", () => Open(new(TaskKind.LaneNode, Model())));
            foreach (var lane in lanes.Groups) Button(lane.Label, () => _canvas.SelectStructural(new("SwimlaneGroup", lane.Id)));
            foreach (var node in lanes.Nodes) Button(node.Label, () => _canvas.SelectStructural(new("SwimlaneNode", node.Id)));
        }
        else if (Model().UseCase is { } cases)
        {
            Heading("Actors and use cases"); Button("Add use-case element", AddClass);
            Button("Add system boundary", () => Open(new(TaskKind.UseCaseBoundary, Model())));
            foreach (var node in cases.Nodes) Button(node.Label.Text, () => _canvas.SelectStructural(new("UseCaseNode", node.Id)));
            foreach (var boundary in cases.Boundaries) Button(boundary.Title.Text, () => _canvas.SelectStructural(new("UseCaseBoundary", boundary.Id)));
        }
        if (SelectionOwner is null) Button("Direction and display…", DisplayOptions);
        if (Model().UseCase is not null) foreach (var issue in _canvas.UseCaseLayout.Diagnostics.Take(3)) Text(issue.Message);
        return true;
    }
    private bool BuildOwnershipEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.LaneGroup: LaneGroupEditor(draft); return true;
            case TaskKind.LaneNode: LaneNodeEditor(draft); return true;
            case TaskKind.LaneLink: LaneLinkEditor(draft); return true;
            case TaskKind.UseCaseNode: UseCaseNodeEditor(draft); return true;
            case TaskKind.UseCaseBoundary: UseCaseBoundaryEditor(draft); return true;
            case TaskKind.UseCaseLink: UseCaseLinkEditor(draft); return true;
            case TaskKind.OwnershipOptions:
                Heading("Process direction"); EnumInput(draft, "direction", "Direction", Model().Swimlane?.Direction ?? Model().UseCase!.Direction);
                Footer("Apply direction", () =>
                {
                    if (Model().Swimlane is not null) ApplyLane(SwimlaneEditing.SetDirection(draft.Get("direction", Direction.TB)), draft.Owner);
                    else ApplyUseCase(UseCaseEditing.SetDirection(draft.Get("direction", Direction.TB)), draft.Owner);
                });
                return true;
            default: return false;
        }
    }
    private void LaneGroupEditor(Draft draft)
    {
        var d = Model().Swimlane!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => d.Groups.Single(g => g.Id == selected.Id)) : null;
        Heading("Lane / nested group"); Input(draft, "id", "Lane source ID", original?.SourceId ?? ""); Input(draft, "label", "Lane label", original?.Label ?? "");
        ChoiceInput(draft, "parent", "Parent lane / group", new[] { new Choice("", "Top-level responsibility lane") }.Concat(d.Groups.Where(g => g.Id != original?.Id).Select(g => new Choice(g.Id, g.Label))), original?.ParentId ?? "");
        Footer("Apply lane", () =>
        {
            var group = new SwimlaneGroup(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "id"), Value(draft, "label"), Optional(draft, "parent"), original?.Direction)
            { Classes = original?.Classes ?? [], Style = original?.Style ?? new() };
            ApplyLane(original is null ? SwimlaneEditing.AddGroup(group) : SwimlaneEditing.UpdateGroup(group), draft.Owner);
        });
        if (original is not null) Button("Remove lane, keep steps", () => ApplyLane(SwimlaneEditing.RemoveGroup(original.Id), draft.Owner));
    }
    private void LaneNodeEditor(Draft draft)
    {
        var d = Model().Swimlane!;
        var original = draft.Owner is StructuralSelection target ? Remember(draft, () => d.Nodes.Single(n => n.Id == target.Id)) : null;
        Heading("Lane step"); Input(draft, "id", "Step source ID", original?.SourceId ?? ""); Input(draft, "label", "Step label", original?.Label ?? "");
        ChoiceInput(draft, "parent", "Responsible lane / group", new[] { new Choice("", "Unassigned") }.Concat(d.Groups.Select(g => new Choice(g.Id, g.Label))), original?.GroupId ?? draft.Get("$parent", ""));
        EnumInput(draft, "shape", "Step shape", original?.Shape ?? NodeShape.Rectangle);
        Footer("Apply step", () =>
        {
            var node = new SwimlaneNode(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "id"), Value(draft, "label"), draft.Get("shape", NodeShape.Rectangle), Optional(draft, "parent"))
            { Classes = original?.Classes ?? [], Style = original?.Style ?? new() };
            ApplyLane(original is null ? SwimlaneEditing.AddNode(node) : SwimlaneEditing.UpdateNode(node), draft.Owner);
        });
        if (original is not null) Button("Delete step and handoffs", () => ApplyLane(SwimlaneEditing.RemoveNode(original.Id), draft.Owner));
    }
    private void LaneLinkEditor(Draft draft)
    {
        var d = Model().Swimlane!; var link = Remember(draft, () => d.Links.Single(l => l.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("Handoff"); ChoiceInput(draft, "from", "From step", d.Nodes.Select(n => new Choice(n.Id, n.Label)), link.From);
        ChoiceInput(draft, "to", "To step", d.Nodes.Select(n => new Choice(n.Id, n.Label)), link.To);
        Input(draft, "label", "Handoff label", link.Label); EnumInput(draft, "kind", "Handoff style", link.Kind);
        Footer("Apply handoff", () => ApplyLane(SwimlaneEditing.UpdateLink(link with { From = Value(draft, "from"), To = Value(draft, "to"), Label = Value(draft, "label"), Kind = draft.Get("kind", link.Kind) }), draft.Owner));
        Button("Delete handoff", () => ApplyLane(SwimlaneEditing.RemoveLink(link.Id), draft.Owner));
    }
    private void UseCaseNodeEditor(Draft draft)
    {
        var d = Model().UseCase!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => d.Nodes.Single(n => n.Id == selected.Id)) : null;
        Heading("Use-case element");
        var picker = EnumInput(draft, "kind", "Element kind", original?.Kind ?? UseCaseNodeKind.UseCase);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var kind = draft.Get("kind", original?.Kind ?? UseCaseNodeKind.UseCase);
        if (kind != UseCaseNodeKind.Note) Input(draft, "id", "Element source ID", original?.SourceId ?? "");
        Input(draft, "label", "Element label", original?.Label.Text ?? "", true); Check(draft, "markdown", "Markdown label", original?.Label.Markdown ?? false);
        if (kind == UseCaseNodeKind.Actor)
        {
            EnumInput(draft, "actor", "Actor variant", original?.ActorKind ?? UseCaseActorKind.Normal);
            Input(draft, "icon", "Actor icon", original?.Icon ?? "");
        }
        if (kind == UseCaseNodeKind.UseCase) EnumInput(draft, "shape", "Use-case shape", original?.Shape ?? UseCaseShape.Ellipse);
        if (kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)
        {
            Check(draft, "business", "Business element", original?.Business ?? false); Input(draft, "stereotype", "Stereotype", original?.Stereotype ?? "");
            ChoiceInput(draft, "parent", "System boundary", new[] { new Choice("", "Outside boundaries") }.Concat(d.Boundaries.Select(b => new Choice(b.Id, b.Title.Text))), original?.BoundaryId ?? draft.Get("$parent", ""));
        }
        if (kind == UseCaseNodeKind.Note) ChoiceInput(draft, "target", "Note target", d.Nodes.Where(n => n.Kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase).Select(n => new Choice(n.Id, n.Label.Text)), original?.NoteTargetId ?? "");
        if (kind == UseCaseNodeKind.JsonTable) Input(draft, "json", "JSON object", original?.Data?.ToJsonString() ?? "{}", true);
        Footer("Apply use-case element", () =>
        {
            var node = new UseCaseNode(original?.Id ?? Guid.NewGuid().ToString("N"), kind == UseCaseNodeKind.Note ? "" : Value(draft, "id"), kind, new(draft.Get("label", ""), draft.Get("markdown", false)))
            {
                ActorKind = kind == UseCaseNodeKind.Actor ? draft.Get("actor", UseCaseActorKind.Normal) : UseCaseActorKind.Normal,
                Icon = kind == UseCaseNodeKind.Actor ? Optional(draft, "icon") : null, Business = kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase && draft.Get("business", false),
                Shape = kind == UseCaseNodeKind.UseCase ? draft.Get("shape", UseCaseShape.Ellipse) : UseCaseShape.Ellipse,
                Stereotype = kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase ? Optional(draft, "stereotype") : null,
                BoundaryId = kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase ? Optional(draft, "parent") : null,
                NoteTargetId = kind == UseCaseNodeKind.Note ? Value(draft, "target") : null, Data = kind == UseCaseNodeKind.JsonTable ? UseCaseJson.Parse(Value(draft, "json")) : null,
                Classes = original?.Classes ?? [], Style = original?.Style ?? new(),
            };
            ApplyUseCase(original is null ? UseCaseEditing.AddNode(node) : UseCaseEditing.UpdateNode(node), draft.Owner);
        });
        if (original is not null) Button("Delete use-case element", () => ApplyUseCase(UseCaseEditing.RemoveNode(original.Id), draft.Owner));
    }
    private void UseCaseBoundaryEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => Model().UseCase!.Boundaries.Single(b => b.Id == selected.Id)) : null;
        Heading("System boundary"); Input(draft, "id", "Boundary source ID", original?.SourceId ?? ""); Input(draft, "label", "Boundary title", original?.Title.Text ?? "");
        Check(draft, "package", "Package title tab", original?.Package ?? false);
        Footer("Apply boundary", () =>
        {
            var boundary = new UseCaseBoundary(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "id"), new(Value(draft, "label"), original?.Title.Markdown ?? false), draft.Get("package", false))
            { Classes = original?.Classes ?? [], Style = original?.Style ?? new() };
            ApplyUseCase(original is null ? UseCaseEditing.AddBoundary(boundary) : UseCaseEditing.UpdateBoundary(boundary), draft.Owner);
        });
        if (original is not null) Button("Remove boundary, keep elements", () => ApplyUseCase(UseCaseEditing.RemoveBoundary(original.Id), draft.Owner));
    }
    private void UseCaseLinkEditor(Draft draft)
    {
        var d = Model().UseCase!; var relation = Remember(draft, () => d.Relationships.Single(r => r.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("UML relationship");
        ChoiceInput(draft, "from", "From element", d.Nodes.Where(n => n.Kind != UseCaseNodeKind.Note).Select(n => new Choice(n.Id, n.Label.Text)), relation.From);
        ChoiceInput(draft, "to", "To element", d.Nodes.Where(n => n.Kind != UseCaseNodeKind.Note).Select(n => new Choice(n.Id, n.Label.Text)), relation.To);
        EnumInput(draft, "kind", "UML semantics", relation.Kind);
        if (relation.Kind == UseCaseRelationshipKind.Association)
        {
            Input(draft, "label", "Relationship label", relation.Label.Text);
            EnumInput(draft, "fromEnd", "Source marker", relation.FromEnd);
            EnumInput(draft, "toEnd", "Target marker", relation.ToEnd);
            Input(draft, "length", "Minimum length", relation.MinimumLength.ToString(CultureInfo.InvariantCulture));
        }
        Input(draft, "id", "Relationship source ID (optional)", relation.SourceId ?? "");
        Footer("Apply UML relationship", () =>
        {
            var kind = draft.Get("kind", relation.Kind);
            var association = kind == UseCaseRelationshipKind.Association;
            ApplyUseCase(UseCaseEditing.UpdateRelationship(relation with
            {
                From = Value(draft, "from"), To = Value(draft, "to"), Kind = kind,
                Label = association ? new(draft.Get("label", relation.Label.Text), relation.Label.Markdown) : new(""),
                FromEnd = association ? draft.Get("fromEnd", UseCaseEnd.None) : UseCaseEnd.None,
                ToEnd = association ? draft.Get("toEnd", UseCaseEnd.Arrow) : kind == UseCaseRelationshipKind.Generalization ? UseCaseEnd.Triangle : UseCaseEnd.Arrow,
                SourceId = Optional(draft, "id"),
                MinimumLength = association ? int.Parse(draft.Get("length", "1"), CultureInfo.InvariantCulture) : 1,
            }), draft.Owner);
        });
        Button("Delete UML relationship", () => ApplyUseCase(UseCaseEditing.RemoveRelationship(relation.Id), draft.Owner));
    }
    private void ApplyLane(SwimlaneEditCommand command, object owner) => Apply(command, owner, after: () =>
    {
        var id = command.ResultId; var d = Model().Swimlane!;
        _canvas.SelectStructural(id is null ? null : new(d.Groups.Any(g => g.Id == id) ? "SwimlaneGroup" : d.Nodes.Any(n => n.Id == id) ? "SwimlaneNode" : "SwimlaneLink", id));
    });
    private void ApplyUseCase(UseCaseEditCommand command, object owner) => Apply(command, owner, after: () =>
    {
        var id = command.ResultId; var d = Model().UseCase!;
        _canvas.SelectStructural(id is null ? null : new(d.Boundaries.Any(b => b.Id == id) ? "UseCaseBoundary" : d.Nodes.Any(n => n.Id == id) ? "UseCaseNode" : "UseCaseRelationship", id));
    });
}
