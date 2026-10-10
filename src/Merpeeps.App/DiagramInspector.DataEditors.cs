using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private OutlineDocument Outline => Model().Outline ?? throw new InvalidOperationException("Open an outline diagram.");
    private TemporalDocument Temporal => Model().Temporal ?? throw new InvalidOperationException("Open a temporal diagram.");
    private static DataDiagramTarget? DataTarget(Draft draft) => draft.Owner as DataDiagramTarget;
    public void EditDataSelection()
    {
        if (Model().Packet is not null || Model().Kanban is not null) { ShowBoardPacketSelection(); return; }
        if (_canvas.SelectedDataTarget is { } target)
            Open(new(target.Role switch
            {
                DataDiagramRole.Node => TaskKind.OutlineNode, DataDiagramRole.Section => TaskKind.TemporalSection,
                DataDiagramRole.Period => TaskKind.TimelinePeriod, DataDiagramRole.Event => TaskKind.TimelineEvent,
                DataDiagramRole.Task => TaskKind.JourneyTask, _ => TaskKind.TemporalTitle,
            }, target));
    }
    private void ShowDataSelection()
    {
        if (Model().Packet is not null || Model().Kanban is not null) { ShowBoardPacketSelection(); return; }
        if (_canvas.SelectedDataTarget is { } selected)
        {
            if (selected.Role == DataDiagramRole.Node)
            {
                var node = Outline.Node(selected.Id);
                Open(new(TaskKind.OutlineNode, selected), false);
                Button("Add child", () => NewOutlineNode(node.Id));
                var location = OutlineEditing.Locate(Outline, node.Id);
                Button("Add sibling", () => NewOutlineNode(location.ParentId)).IsEnabled = Outline.Family == OutlineFamily.TreeView || location.ParentId is not null;
                Button("Move under another node…", () => Open(new(TaskKind.OutlineMove, selected)));
                Button("Move node earlier", () => ApplyData(OutlineEditing.Move(node.Id, location.ParentId, location.Index - 1), selected)).IsEnabled = location.Index > 0;
                var count = location.ParentId is null ? Outline.Roots.Count : Outline.Node(location.ParentId).Children.Count;
                Button("Move node later", () => ApplyData(OutlineEditing.Move(node.Id, location.ParentId, location.Index + 1), selected)).IsEnabled = location.Index + 1 < count;
                Button("Delete subtree", () => ApplyData(OutlineEditing.Remove(node.Id), selected));
                foreach (var issue in _canvas.DataScene.Diagnostics.Where(d => d.Id == node.Id)) Text(issue.Message);
                return;
            }
            if (selected.Role == DataDiagramRole.Period)
            {
                var period = Temporal.Periods.Single(p => p.Id == selected.Id);
                Open(new(TaskKind.TimelinePeriod, selected), false);
                Button("Add event", () => Open(new(TaskKind.TimelineEvent, Model()) { Values = { ["$period"] = period.Id } }));
                foreach (var item in period.Events)
                    Button(item.Text, () => _canvas.SelectData(new(DataDiagramRole.Event, item.Id, period.Id)));
                var index = Temporal.Periods.FindIndex(p => p.Id == period.Id);
                Button("Move period earlier", () => ApplyData(TemporalEditing.MovePeriod(period.Id, index - 1), selected)).IsEnabled = index > 0;
                Button("Move period later", () => ApplyData(TemporalEditing.MovePeriod(period.Id, index + 1), selected)).IsEnabled = index + 1 < Temporal.Periods.Count;
                Button("Delete period and events", () => ApplyData(TemporalEditing.RemovePeriod(period.Id), selected));
                return;
            }
            if (selected.Role is DataDiagramRole.Event or DataDiagramRole.Task or DataDiagramRole.Section)
            {
                Open(new(selected.Role == DataDiagramRole.Event ? TaskKind.TimelineEvent
                    : selected.Role == DataDiagramRole.Task ? TaskKind.JourneyTask : TaskKind.TemporalSection, selected), false);
                return;
            }
        }
        if (Model().Outline is { } outline)
        {
            Heading(outline.Family == OutlineFamily.Mindmap ? "Mindmap" : "Tree View");
            Text(outline.Family == OutlineFamily.Mindmap ? "Select a node to add children or edit its content." : "Select an entry to edit, add children or move its subtree.");
            Button("Add root", () => NewOutlineNode(null)).IsEnabled = outline.Family == OutlineFamily.TreeView || outline.Roots.Count == 0;
            foreach (var root in outline.Roots) Button(root.Label, () => _canvas.SelectData(new(DataDiagramRole.Node, root.Id)));
        }
        else
        {
            Heading(Temporal.Title.Length > 0 ? Temporal.Title : Temporal.Family.ToString());
            Button("Title…", () => Open(new(TaskKind.TemporalTitle, Model())));
            Button("Add section", () => Open(new(TaskKind.TemporalSection, Model())));
            Button(Temporal.Family == TemporalFamily.Timeline ? "Add time period" : "Add task", AddClass);
            foreach (var section in Temporal.Sections) Button("Section: " + section.Label, () => _canvas.SelectData(new(DataDiagramRole.Section, section.Id)));
            if (Temporal.Family == TemporalFamily.Timeline)
                foreach (var period in Temporal.Periods) Button(period.Label, () => _canvas.SelectData(new(DataDiagramRole.Period, period.Id)));
            else foreach (var task in Temporal.Tasks) Button(task.Label, () => _canvas.SelectData(new(DataDiagramRole.Task, task.Id)));
        }
        Button("Diagram options…", DisplayOptions);
        foreach (var draft in _drafts.Values.Where(d => d.Dirty).ToArray())
            Button("Resume unapplied edit", () => Open(draft));
    }
    private void NewOutlineNode(string? parent)
    {
        var draft = new Draft(TaskKind.OutlineNode, Model());
        draft.Values["$parent"] = parent ?? "";
        Open(draft);
    }
    private bool BuildDataEditor(Draft draft)
    {
        if (BuildBoardPacketEditor(draft)) return true;
        switch (draft.Task)
        {
            case TaskKind.OutlineNode: OutlineNodeEditor(draft); return true;
            case TaskKind.OutlineMove: OutlineMoveEditor(draft); return true;
            case TaskKind.DataOptions: DataOptionsEditor(draft); return true;
            case TaskKind.DataItems:
                Heading("Diagram contents"); ShowDataSelection(); Button("Back to selection", CancelEditor); return true;
            case TaskKind.TemporalTitle:
                Heading("Diagram title"); Input(draft, "title", "Title", Temporal.Title, multiline: true);
                Footer("Apply title", () => ApplyData(TemporalEditing.SetTitle(draft.Get("title", "")), draft.Owner)); return true;
            case TaskKind.TemporalSection: SectionEditor(draft); return true;
            case TaskKind.TimelinePeriod: PeriodEditor(draft); return true;
            case TaskKind.TimelineEvent: EventEditor(draft); return true;
            case TaskKind.JourneyTask: TaskEditor(draft); return true;
            default: return false;
        }
    }
    private void OutlineNodeEditor(Draft draft)
    {
        var original = DataTarget(draft) is { } target ? Remember(draft, () => Outline.Node(target.Id)) : null;
        var mindmap = Outline.Family == OutlineFamily.Mindmap;
        Heading(original is null ? "Add node" : "Edit node");
        Input(draft, "label", "Node label", original?.Label ?? "", multiline: mindmap);
        if (mindmap)
        {
            EnumInput(draft, "shape", "Node shape", original?.Shape ?? OutlineShape.Default);
            Check(draft, "markdown", "Markdown label", original?.Markdown ?? false);
            Input(draft, "sourceId", "Source identity (optional)", original?.SourceId ?? "");
        }
        else
        {
            Check(draft, "directory", "Directory", original?.Directory ?? false);
            Input(draft, "description", "Description", original?.Description ?? "");
        }
        Input(draft, "icon", "Icon (blank auto, none hides)", original?.Icon ?? "");
        Input(draft, "classes", "Style classes", string.Join(" ", original?.Classes ?? []));
        Footer("Apply node", () =>
        {
            var node = new OutlineNode(original?.Id ?? OutlineEditing.NewId(), draft.Get("label", ""), original?.Children ?? [])
            {
                Shape = mindmap ? draft.Get("shape", OutlineShape.Default) : OutlineShape.Default,
                SourceId = mindmap ? Optional(draft, "sourceId") : null, Markdown = mindmap && draft.Get("markdown", false),
                Directory = !mindmap && draft.Get("directory", false), Description = mindmap ? null : Optional(draft, "description"),
                Icon = Optional(draft, "icon"), Classes = Value(draft, "classes").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            };
            ApplyData(original is null ? OutlineEditing.Add(node, Optional(draft, "$parent")) : OutlineEditing.Update(node.Id, node), draft.Owner);
        });
        if (original is not null && mindmap)
            Button("Reset node to automatic position", () => ApplyData(OutlineEditing.SetPosition(original.Id, null), draft.Owner));
    }
    private void OutlineMoveEditor(Draft draft)
    {
        var id = DataTarget(draft)!.Id;
        var location = OutlineEditing.Locate(Outline, id);
        var excluded = OutlineDocument.Walk([location.Node]).Select(n => n.Id).ToHashSet();
        var choices = Outline.Nodes.Where(n => !excluded.Contains(n.Id)).Select(n => new Choice(n.Id, n.Label)).ToList();
        if (Outline.Family == OutlineFamily.TreeView) choices.Insert(0, new("", "(root level)"));
        Heading("Move subtree");
        ChoiceInput(draft, "parent", "New parent", choices, location.ParentId ?? "");
        Footer("Move subtree", () =>
        {
            var parent = Optional(draft, "parent");
            var count = parent is null ? Outline.Roots.Count : Outline.Node(parent).Children.Count;
            if (parent == location.ParentId) count--;
            ApplyData(OutlineEditing.Move(id, parent, count), draft.Owner);
        });
    }
    private void SectionEditor(Draft draft)
    {
        var original = DataTarget(draft) is { } target ? Remember(draft, () => Temporal.Sections.Single(s => s.Id == target.Id)) : null;
        Heading("Section");
        Input(draft, "label", "Section label", original?.Label ?? "", multiline: true);
        Footer("Apply section", () =>
        {
            var section = new TemporalSection(original?.Id ?? TemporalEditing.NewId(), draft.Get("label", ""));
            ApplyData(original is null ? TemporalEditing.AddSection(section) : TemporalEditing.UpdateSection(section), draft.Owner);
        });
        if (original is not null) Button("Remove section, keep items", () => ApplyData(TemporalEditing.RemoveSection(original.Id), draft.Owner));
        if (original is not null)
        {
            var index = Temporal.Sections.FindIndex(s => s.Id == original.Id);
            Button("Move section earlier", () => ApplyData(TemporalEditing.MoveSection(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move section later", () => ApplyData(TemporalEditing.MoveSection(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < Temporal.Sections.Count;
        }
    }
    private void PeriodEditor(Draft draft)
    {
        var original = DataTarget(draft) is { } target ? Remember(draft, () => Temporal.Periods.Single(p => p.Id == target.Id)) : null;
        Heading("Time period");
        Input(draft, "label", "Period label", original?.Label ?? "", multiline: true);
        SectionChoice(draft, original?.SectionId);
        Footer("Apply time period", () =>
        {
            var period = new TimelinePeriod(original?.Id ?? TemporalEditing.NewId(), draft.Get("label", ""), Optional(draft, "section"), original?.Events ?? []);
            ApplyData(original is null ? TemporalEditing.AddPeriod(period) : TemporalEditing.UpdatePeriod(period), draft.Owner);
        });
    }
    private void EventEditor(Draft draft)
    {
        var target = DataTarget(draft);
        var periodId = target?.ParentId ?? draft.Get("$period", "");
        var period = Temporal.Periods.Single(p => p.Id == periodId);
        var original = target is null ? null : Remember(draft, () => period.Events.Single(e => e.Id == target.Id));
        Heading("Timeline event"); Input(draft, "text", "Event text", original?.Text ?? "", multiline: true);
        Footer("Apply event", () =>
        {
            var item = new TimelineEvent(original?.Id ?? TemporalEditing.NewId(), draft.Get("text", ""));
            ApplyData(original is null ? TemporalEditing.AddEvent(periodId, item) : TemporalEditing.UpdateEvent(periodId, item), draft.Owner);
        });
        if (original is not null)
        {
            var index = period.Events.ToList().FindIndex(e => e.Id == original.Id);
            Button("Move event earlier", () => ApplyData(TemporalEditing.MoveEvent(periodId, original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move event later", () => ApplyData(TemporalEditing.MoveEvent(periodId, original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < period.Events.Count;
            Button("Delete event", () => ApplyData(TemporalEditing.RemoveEvent(periodId, original.Id), draft.Owner));
            ChoiceInput(draft, "targetPeriod", "Move event to period", Temporal.Periods.Select(p => new Choice(p.Id, p.Label)), periodId);
            Button("Move event to period", () =>
            {
                var target = Value(draft, "targetPeriod");
                var count = Temporal.Periods.Single(p => p.Id == target).Events.Count - (target == periodId ? 1 : 0);
                ApplyData(TemporalEditing.TransferEvent(periodId, target, original.Id, count), draft.Owner);
            });
        }
    }
    private void TaskEditor(Draft draft)
    {
        var original = DataTarget(draft) is { } target ? Remember(draft, () => Temporal.Tasks.Single(t => t.Id == target.Id)) : null;
        Heading("Journey task");
        Input(draft, "label", "Task label", original?.Label ?? "", multiline: true);
        Input(draft, "score", "Score (1–5)", (original?.Score ?? 3).ToString(CultureInfo.InvariantCulture));
        Input(draft, "actors", "Actors (one per line)", string.Join("\n", original?.Actors ?? []), multiline: true);
        SectionChoice(draft, original?.SectionId);
        Footer("Apply task", () =>
        {
            var task = new JourneyTask(original?.Id ?? TemporalEditing.NewId(), draft.Get("label", ""), Optional(draft, "section"),
                int.Parse(Value(draft, "score"), CultureInfo.InvariantCulture),
                draft.Get("actors", "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
            ApplyData(original is null ? TemporalEditing.AddTask(task) : TemporalEditing.UpdateTask(task), draft.Owner);
        });
        if (original is not null)
        {
            var index = Temporal.Tasks.FindIndex(t => t.Id == original.Id);
            Button("Move task earlier", () => ApplyData(TemporalEditing.MoveTask(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move task later", () => ApplyData(TemporalEditing.MoveTask(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < Temporal.Tasks.Count;
            Button("Delete task", () => ApplyData(TemporalEditing.RemoveTask(original.Id), draft.Owner));
        }
    }
    private void DataOptionsEditor(Draft draft)
    {
        if (Model().Packet is not null || Model().Kanban is not null) { BoardPacketOptions(draft); return; }
        Heading("Diagram options");
        if (Model().Outline is { Family: OutlineFamily.Mindmap } mindmap)
        {
            Check(draft, "tidy", "Tidy tree layout", mindmap.GlobalConfiguration["layout"]?.GetValue<string>() == "tidy-tree");
        }
        else if (Model().Outline is { } tree)
        {
            Check(draft, "icons", "Show file and folder icons", tree.Configuration["showIcons"]?.GetValue<bool>() ?? false);
            foreach (var key in new[] { "rowIndent", "paddingX", "paddingY", "lineThickness" })
            {
                Input(draft, key, key, tree.Configuration[key]?.ToString() ?? (key == "rowIndent" ? "10" : key == "lineThickness" ? "1" : "5"));
            }
        }
        else if (Temporal.Family == TemporalFamily.Timeline)
        {
            Check(draft, "vertical", "Top-down timeline", Temporal.TopDown);
            Check(draft, "singleColor", "Use a single period color", Temporal.Configuration["disableMulticolor"]?.GetValue<bool>() ?? false);
        }
        else { Text("Task scores and actors determine this diagram. Edit tasks to change them."); Button("Back to selection", CancelEditor); }
        var themeKeys = Model().Outline?.Family == OutlineFamily.TreeView
            ? new[] { "treeView.labelColor", "treeView.lineColor", "treeView.descriptionColor", "treeView.iconColor", "treeView.highlightBg", "treeView.highlightStroke", "treeView.labelFontSize" }
            : Enumerable.Range(0, 12).SelectMany(i => new[] { "cScale" + i, "cScaleLabel" + i }).ToArray();
        ThemeValueInput(draft, themeKeys, "Theme value");
        Check(draft, "resetTheme", "Reset selected theme variable", false);
        Footer("Apply diagram options", () =>
        {
            var commands = new List<Merpeeps.Core.Editing.IEditCommand>();
            if (Model().Outline is { Family: OutlineFamily.Mindmap })
                commands.Add(OutlineEditing.SetMindmapLayout(draft.Get("tidy", false)));
            else if (Model().Outline is not null)
            {
                commands.Add(OutlineEditing.SetOption("showIcons", JsonValue.Create(draft.Get("icons", false))));
                foreach (var key in new[] { "rowIndent", "paddingX", "paddingY", "lineThickness" })
                    commands.Add(OutlineEditing.SetOption(key, JsonValue.Create(Numeric(draft, key))));
            }
            else if (Temporal.Family == TemporalFamily.Timeline)
            {
                commands.Add(TemporalEditing.SetDirection(draft.Get("vertical", false)));
                commands.Add(TemporalEditing.SetOption("disableMulticolor", JsonValue.Create(draft.Get("singleColor", false))));
            }
            if (draft.Get("resetTheme", false))
                commands.Add(Model().Outline is not null ? OutlineEditing.SetThemeVariable(Value(draft, "themeKey"), null)
                    : TemporalEditing.SetThemeVariable(Value(draft, "themeKey"), null));
            else if (Optional(draft, "themeValue") is { } value)
                commands.Add(Model().Outline is not null ? OutlineEditing.SetThemeVariable(Value(draft, "themeKey"), JsonValue.Create(value))
                    : TemporalEditing.SetThemeVariable(Value(draft, "themeKey"), JsonValue.Create(value)));
            Apply(new BatchCommand("Edit diagram options", commands.ToArray()), draft.Owner, after: () => _canvas.SelectData(null));
        });
    }
    private void SectionChoice(Draft draft, string? initial) =>
        ChoiceInput(draft, "section", "Section", new[] { new Choice("", "(default)") }.Concat(Temporal.Sections.Select(s => new Choice(s.Id, s.Label))), initial ?? "");
    private void ApplyData(DataDiagramEditCommand command, object owner) => Apply(command, owner,
        after: () => _canvas.SelectData(command.Result is { Role: DataDiagramRole.Diagram } ? null : command.Result));
}
