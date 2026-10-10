using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static TaskKind? WorkflowTask(StructuralSelection target) => target.Role switch
    {
        "AgentFlowElement" => TaskKind.AgentElement, "AgentFlowLink" => TaskKind.AgentLink,
        "EventFrame" => TaskKind.EventFrame, "EventData" => TaskKind.EventData,
        "ZenParticipant" => TaskKind.ZenParticipant, "ZenStatement" => TaskKind.ZenStatement, _ => null,
    };
    private bool ShowWorkflowSelection()
    {
        if (!_canvas.IsWorkflowDiagram) return false;
        if (_canvas.GridConnectionSource is not null)
        {
            Heading("Choose workflow target"); Text("Click an element or container. Escape cancels.");
            Button("Cancel workflow connection", _canvas.CancelRequirementConnection); return true;
        }
        if (_canvas.SelectedStructuralTarget is { } target && WorkflowTask(target) is { } task)
        {
            Open(new(task, target), false);
            if (task == TaskKind.AgentElement)
            {
                var e = Model().AgentFlow!.Elements.Single(e => e.Id == target.Id);
                Button("Add workflow connection", () => _canvas.BeginGridConnection(e.Id));
                if (e.Kind == AgentFlowElementKind.Flow)
                {
                    Button("Add contained item", () => Open(new(TaskKind.AgentElement, Model()) { Values = { ["$parent"] = e.Id } }));
                    Button(e.Collapsed ? "Expand flow" : "Collapse flow", () =>
                    {
                        var metadata = (JsonObject)e.Metadata.DeepClone(); metadata["view"] = e.Collapsed ? "expanded" : "collapsed";
                        ApplyAgent(AgentFlowEditing.UpdateElement(e with { Metadata = metadata }), target);
                    });
                }
            }
            if (task == TaskKind.ZenStatement)
            {
                var s = ZenEditing.Locate(Model().Zen!, target.Id).Statement;
                if (s.Kind is ZenStatementKind.Call or ZenStatementKind.Create or ZenStatementKind.Loop or ZenStatementKind.Conditional or ZenStatementKind.Optional or ZenStatementKind.Parallel or ZenStatementKind.Try)
                    Button("Add nested statement", () => Open(new(TaskKind.ZenStatement, Model()) { Values = { ["$parent"] = s.Id } }));
                foreach (var child in s.Children) Button(child.Kind + ": " + child.Text, () => _canvas.SelectStructural(new("ZenStatement", child.Id)));
            }
        }
        else if (Model().AgentFlow is { } agent)
        {
            Heading("Agent workflow"); Button("Add agent-flow element", AddClass);
            foreach (var e in agent.Elements) Button(e.Kind + ": " + e.Display, () => _canvas.SelectStructural(new("AgentFlowElement", e.Id)));
        }
        else if (Model().EventModeling is { } events)
        {
            Heading("Event timeline"); Button("Add timeframe", AddClass); Button("Add data example", () => Open(new(TaskKind.EventData, Model())));
            foreach (var frame in events.Frames) Button(frame.Number + ": " + frame.EntityName, () => _canvas.SelectStructural(new("EventFrame", frame.Id)));
            foreach (var data in events.DataBlocks) Button("Data: " + data.Name, () => _canvas.SelectStructural(new("EventData", data.Id)));
        }
        else if (Model().Zen is { } zen)
        {
            Heading("ZenUML interactions"); Button("Add participant", AddClass); Button("Add statement", () => Open(new(TaskKind.ZenStatement, Model())));
            foreach (var p in zen.Participants) Button(p.Alias ?? p.SourceId, () => _canvas.SelectStructural(new("ZenParticipant", p.Id)));
            foreach (var statement in ZenSemantics.Walk(zen.Statements)) Button(statement.Kind + ": " + statement.Text, () => _canvas.SelectStructural(new("ZenStatement", statement.Id)));
        }
        if (SelectionOwner is null) Button("Diagram options…", DisplayOptions);
        return true;
    }
    private bool BuildWorkflowEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.AgentElement: AgentElementEditor(draft); return true;
            case TaskKind.AgentLink: AgentLinkEditor(draft); return true;
            case TaskKind.EventFrame: EventFrameEditor(draft); return true;
            case TaskKind.EventData: EventDataEditor(draft); return true;
            case TaskKind.ZenParticipant: ZenParticipantEditor(draft); return true;
            case TaskKind.ZenStatement: ZenStatementEditor(draft); return true;
            case TaskKind.WorkflowOptions:
                if (Model().AgentFlow is { } agent)
                {
                    Heading("Workflow direction"); EnumInput(draft, "direction", "Direction", agent.Direction);
                    Footer("Apply workflow direction", () => ApplyAgent(AgentFlowEditing.SetDirection(draft.Get("direction", agent.Direction)), draft.Owner));
                }
                else if (Model().Zen is { } zen)
                {
                    Heading("ZenUML title"); Input(draft, "title", "Diagram title", zen.Title);
                    Footer("Apply title", () => ApplyZen(ZenEditing.SetTitle(Value(draft, "title")), draft.Owner));
                }
                else { Heading("Event timeline order"); Text("Timeframes follow declaration order, not their source numbers. Edit a timeframe to change type, data or explicit references."); Button("Back to selection", CancelEditor); }
                return true;
            default: return false;
        }
    }
    private void AgentElementEditor(Draft draft)
    {
        var d = Model().AgentFlow!; var original = draft.Owner is StructuralSelection t ? Remember(draft, () => d.Elements.Single(e => e.Id == t.Id)) : null;
        Heading("Agent-flow element"); Input(draft, "id", "Element source ID", original?.SourceId ?? ""); Input(draft, "label", "Element label", original?.Label ?? "");
        EnumInput(draft, "kind", "Workflow element kind", original?.Kind ?? AgentFlowElementKind.Node);
        ChoiceInput(draft, "parent", "Containing flow", new[] { new Choice("", "Top level") }.Concat(d.Elements.Where(e => e.Kind == AgentFlowElementKind.Flow && e.Id != original?.Id).Select(e => new Choice(e.Id, e.Display))), original?.ParentId ?? draft.Get("$parent", ""));
        Check(draft, "global", "Shared global element", original?.Global ?? false);
        Input(draft, "metadata", "Metadata (inert YAML / JSON)", original?.Metadata.ToJsonString() ?? "{\"shape\":\"task\"}", true);
        Footer("Apply workflow element", () =>
        {
            var global = draft.Get("global", false); var kind = draft.Get("kind", AgentFlowElementKind.Node);
            var e = new AgentFlowElement(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "id"), kind, Value(draft, "label"))
            { Global = global, ParentId = global || kind == AgentFlowElementKind.Connector ? null : Optional(draft, "parent"), Metadata = AgentFlowDiagramAdapter.Metadata(Value(draft, "metadata")) };
            ApplyAgent(original is null ? AgentFlowEditing.AddElement(e) : AgentFlowEditing.UpdateElement(e), draft.Owner);
        });
        if (original is not null) Button("Delete workflow subtree", () => ApplyAgent(AgentFlowEditing.RemoveElement(original.Id), draft.Owner));
    }
    private void AgentLinkEditor(Draft draft)
    {
        var d = Model().AgentFlow!; var original = Remember(draft, () => d.Links.Single(l => l.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("Workflow relationship");
        ChoiceInput(draft, "from", "From element", d.Elements.Select(e => new Choice(e.Id, e.Display)), original.From);
        ChoiceInput(draft, "to", "To element", d.Elements.Select(e => new Choice(e.Id, e.Display)), original.To);
        EnumInput(draft, "kind", "Relationship meaning", original.Kind); Input(draft, "label", "Relationship label", original.Label);
        Footer("Apply workflow relationship", () => ApplyAgent(AgentFlowEditing.UpdateLink(original with { From = Value(draft, "from"), To = Value(draft, "to"), Kind = draft.Get("kind", original.Kind), Label = Value(draft, "label") }), draft.Owner));
        Button("Delete workflow relationship", () => ApplyAgent(AgentFlowEditing.RemoveLink(original.Id), draft.Owner));
    }
    private void EventFrameEditor(Draft draft)
    {
        var d = Model().EventModeling!; var original = draft.Owner is StructuralSelection t ? Remember(draft, () => d.Frames.Single(f => f.Id == t.Id)) : null;
        Heading("Timeframe occurrence"); Input(draft, "number", "Unique source number", original?.Number ?? "");
        Input(draft, "entity", "Entity / namespace name", original?.EntityName ?? ""); EnumInput(draft, "kind", "Entity type", original?.Kind ?? EventEntityKind.UserInterface);
        Check(draft, "reset", "Reset incoming inference", original?.Reset ?? false);
        var details = Children.Count;
        foreach (var frame in d.Frames.Where(f => f.Id != original?.Id)) Check(draft, "ref:" + frame.Id, "Reference frame " + frame.Number + ": " + frame.EntityName, original?.References.Contains(frame.Id) ?? false);
        ChoiceInput(draft, "data", "Named data block", new[] { new Choice("", "None / inline data") }.Concat(d.DataBlocks.Select(b => new Choice(b.Id, b.Name))), original?.DataBlockId ?? "");
        EnumInput(draft, "dataKind", "Inline data type", original?.InlineData?.Kind ?? EventDataKind.JsObject);
        Input(draft, "inline", "Inline data body (inert)", original?.InlineData?.Source ?? "", true);
        CollapseDetails(details, "References and data");
        Footer("Apply timeframe", () =>
        {
            var inline = Optional(draft, "inline"); var data = Optional(draft, "data");
            var frame = new EventFrame(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "number"), draft.Get("kind", EventEntityKind.UserInterface), Value(draft, "entity"))
            {
                Reset = draft.Get("reset", false), RelaxedSyntax = original?.RelaxedSyntax ?? false, DataBlockId = data,
                InlineData = data is null && inline is not null ? new(draft.Get("dataKind", EventDataKind.JsObject), draft.Get("inline", ""), true) : null,
                References = d.Frames.Where(f => draft.Get("ref:" + f.Id, false)).Select(f => f.Id).ToArray(),
            };
            ApplyEvent(original is null ? EventModelingEditing.AddFrame(frame) : EventModelingEditing.UpdateFrame(frame), draft.Owner);
        });
        if (original is not null)
        {
            var index = d.Frames.IndexOf(original);
            Button("Move timeframe earlier", () => ApplyEvent(EventModelingEditing.MoveFrame(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Delete timeframe", () => ApplyEvent(EventModelingEditing.RemoveFrame(original.Id), draft.Owner));
        }
    }
    private void EventDataEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection t ? Remember(draft, () => Model().EventModeling!.DataBlocks.Single(b => b.Id == t.Id)) : null;
        Heading("Data example"); Input(draft, "name", "Data block name", original?.Name ?? "");
        EnumInput(draft, "kind", "Data type", original?.Data.Kind ?? EventDataKind.JsObject); Input(draft, "body", "Data body (never executed)", original?.Data.Source ?? "", true);
        Footer("Apply data example", () => ApplyEvent(EventModelingEditing.SetDataBlock(new(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "name"), new(draft.Get("kind", EventDataKind.JsObject), draft.Get("body", ""), true))), draft.Owner));
    }
    private void ZenParticipantEditor(Draft draft)
    {
        var original = draft.Owner is StructuralSelection t ? Remember(draft, () => Model().Zen!.Participants.Single(p => p.Id == t.Id)) : null;
        Heading("ZenUML participant"); Input(draft, "id", "Participant source ID", original?.SourceId ?? ""); Input(draft, "alias", "Participant alias", original?.Alias ?? "");
        EnumInput(draft, "kind", "Participant symbol", original?.Kind ?? ZenParticipantKind.Participant);
        Footer("Apply participant", () =>
        {
            var kind = draft.Get("kind", ZenParticipantKind.Participant);
            var p = new ZenParticipant(original?.Id ?? Guid.NewGuid().ToString("N"), Value(draft, "id"), Optional(draft, "alias"), kind)
            { Declared = true, Annotator = kind == ZenParticipantKind.Participant ? null : kind.ToString(), Comments = original?.Comments ?? [] };
            ApplyZen(original is null ? ZenEditing.AddParticipant(p) : ZenEditing.UpdateParticipant(p), draft.Owner);
        });
    }
    private void ZenStatementEditor(Draft draft)
    {
        var d = Model().Zen!; var original = draft.Owner is StructuralSelection t ? Remember(draft, () => ZenEditing.Locate(d, t.Id).Statement) : null;
        Heading("ZenUML statement"); var picker = EnumInput(draft, "kind", "Statement kind", original?.Kind ?? ZenStatementKind.Call);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft); var kind = draft.Get("kind", original?.Kind ?? ZenStatementKind.Call);
        if (kind is ZenStatementKind.Call or ZenStatementKind.Create or ZenStatementKind.AsyncMessage or ZenStatementKind.ReplyMessage)
        {
            ChoiceInput(draft, "from", "Sender", new[] { new Choice("", "Enclosing caller / outside") }.Concat(d.Participants.Select(p => new Choice(p.Id, p.Alias ?? p.SourceId))), original?.From ?? "");
            ChoiceInput(draft, "to", "Recipient", d.Participants.Select(p => new Choice(p.Id, p.Alias ?? p.SourceId)), original?.To ?? "");
        }
        Input(draft, "text", kind == ZenStatementKind.Call ? "Method name" : kind is ZenStatementKind.Loop or ZenStatementKind.Conditional ? "Condition" : "Statement text", original?.Text ?? "", kind == ZenStatementKind.Comment);
        if (kind is ZenStatementKind.Call or ZenStatementKind.Create) Input(draft, "args", "Arguments (inert text)", original?.Arguments ?? "");
        if (kind == ZenStatementKind.Loop) Input(draft, "keyword", "Loop keyword", original?.Keyword is { Length: > 0 } keyword ? keyword : "while");
        Footer("Apply ZenUML statement", () =>
        {
            var s = new ZenStatement(original?.Id ?? Guid.NewGuid().ToString("N"), kind)
            {
                From = Optional(draft, "from"), To = Optional(draft, "to"), ExplicitSender = Optional(draft, "from") is not null,
                Text = draft.Get("text", ""), Arguments = kind is ZenStatementKind.Call or ZenStatementKind.Create ? draft.Get("args", "") : null,
                Keyword = kind == ZenStatementKind.Loop ? Value(draft, "keyword") : kind == ZenStatementKind.ReplyMessage ? original?.Keyword ?? "return" : "",
                Assignment = original?.Assignment, AssignmentType = original?.AssignmentType, Children = original?.Children ?? [], Branches = original?.Branches ?? [],
            };
            ApplyZen(original is null ? ZenEditing.AddStatement(s, Optional(draft, "$parent")) : ZenEditing.UpdateStatement(s), draft.Owner);
        });
        if (original is not null) Button("Delete statement subtree", () => ApplyZen(ZenEditing.RemoveStatement(original.Id), draft.Owner));
    }
    private void ApplyAgent(AgentFlowEditCommand command, object owner) => Apply(command, owner, after: () =>
    { var id = command.ResultId; _canvas.SelectStructural(id is null ? null : new(Model().AgentFlow!.Links.Any(l => l.Id == id) ? "AgentFlowLink" : "AgentFlowElement", id)); });
    private void ApplyEvent(EventModelingEditCommand command, object owner) => Apply(command, owner, after: () =>
    { var id = command.ResultId; _canvas.SelectStructural(id is null ? null : new(Model().EventModeling!.DataBlocks.Any(b => b.Id == id) ? "EventData" : "EventFrame", id)); });
    private void ApplyZen(ZenEditCommand command, object owner) => Apply(command, owner, after: () =>
    { var id = command.ResultId; _canvas.SelectStructural(id is null ? null : new(Model().Zen!.Participants.Any(p => p.Id == id) ? "ZenParticipant" : "ZenStatement", id)); });
}
