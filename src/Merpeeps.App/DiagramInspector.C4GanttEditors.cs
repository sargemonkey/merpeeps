using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Avalonia.Controls;
using Avalonia.Automation;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private enum StartMode { Date, Previous, After }
    private enum EndMode { Date, Duration, Until }
    private bool ShowC4GanttSelection()
    {
        if (Model().C4 is { } c4)
        {
            if (_canvas.GridConnectionSource is { } source)
            {
                Heading("Choose C4 relationship target");
                Text("From: " + c4.Elements.Single(e => e.Id == source).Label);
                Text("Click the target element. Escape cancels.");
                Button("Cancel relationship", _canvas.CancelRequirementConnection); return true;
            }
            if (_canvas.SelectedStructuralTarget is { Role: "C4Relationship" } relation) { Open(new(TaskKind.C4Relationship, relation), false); return true; }
            if (_canvas.SelectedStructuralTarget is { Role: "C4Element" } selected)
            {
                var element = c4.Elements.Single(e => e.Id == selected.Id);
                Open(new(TaskKind.C4Element, selected), false);
                Button("C4 appearance…", () => Open(new(TaskKind.C4Appearance, selected)));
                Button("Add relationship", () => _canvas.BeginGridConnection(element.Id));
                if (element.IsContainer) Button("Add contained element", () => Open(new(TaskKind.C4Element, Model()) { Values = { ["$parent"] = element.Id } }));
                Button("Delete C4 subtree", () => ApplyC4(C4DiagramEditing.RemoveElement(element.Id), selected));
            }
            else
            {
                Heading(c4.Directive); Button("Add C4 element", AddClass);
                foreach (var element in c4.Elements) Button(element.Label, () => _canvas.SelectStructural(new("C4Element", element.Id)));
            }
            if (SelectionOwner is null) Button("C4 layout and title…", DisplayOptions);
            return true;
        }
        if (Model().Gantt is { } gantt)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "GanttTask" } selectedTask) { Open(new(TaskKind.GanttTask, selectedTask), false); return true; }
            if (_canvas.SelectedStructuralTarget is { Role: "GanttSection" } section && section.Id.Length > 0) { Open(new(TaskKind.GanttSection, section), false); return true; }
            Heading(gantt.Title.Length == 0 ? "Gantt schedule" : gantt.Title);
            Text($"Reference: {_canvas.GanttContext.ReferenceLocal:yyyy-MM-dd HH:mm:ss} · {_canvas.GanttContext.TimeZone.Id}");
            Button("Add task", AddClass); Button("Add section", () => Open(new(TaskKind.GanttSection, Model())));
            foreach (var task in gantt.Tasks) Button(task.Label, () => _canvas.SelectStructural(new("GanttTask", task.Id)));
            Button("Schedule options…", DisplayOptions);
            Button("Refresh view reference time", () => _canvas.SetGanttContext(EditorCanvas.CaptureGanttContext()));
            return true;
        }
        return false;
    }
    private bool BuildC4GanttEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.C4Element: C4ElementEditor(draft); return true;
            case TaskKind.C4Relationship: C4RelationshipEditor(draft); return true;
            case TaskKind.C4Appearance: C4AppearanceEditor(draft); return true;
            case TaskKind.C4Options: C4OptionsEditor(draft); return true;
            case TaskKind.GanttTask: GanttTaskEditor(draft); return true;
            case TaskKind.GanttSection: GanttSectionEditor(draft); return true;
            case TaskKind.GanttOptions: GanttOptionsEditor(draft); return true;
            default: return false;
        }
    }
    private void C4ElementEditor(Draft draft)
    {
        var document = Model().C4!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.Elements.Single(e => e.Id == selected.Id)) : null;
        Heading(original is null ? "Add C4 element" : "Edit C4 element");
        Input(draft, "alias", "Source alias", original?.Alias ?? "");
        Input(draft, "label", "Element label", original?.Label ?? "");
        var picker = EnumInput(draft, "level", "Element level", original?.Level ?? C4ElementLevel.System);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var level = draft.Get("level", original?.Level ?? C4ElementLevel.System);
        if (level is C4ElementLevel.Container or C4ElementLevel.Component or C4ElementLevel.DeploymentNode)
            Input(draft, "technology", "Technology / deployment type", original?.Technology ?? "");
        Input(draft, "description", "Description", original?.Description ?? "", multiline: true);
        var detailsStart = Children.Count;
        if (level is C4ElementLevel.Person or C4ElementLevel.System or C4ElementLevel.Container or C4ElementLevel.Component)
            Check(draft, "external", "External element", original?.External ?? false);
        if (level is C4ElementLevel.System or C4ElementLevel.Container or C4ElementLevel.Component)
            EnumInput(draft, "shape", "Element shape", original?.Shape ?? C4ElementShape.Box);
        if (level == C4ElementLevel.Boundary)
        {
            EnumInput(draft, "boundary", "Boundary kind", original?.BoundaryKind ?? C4BoundaryKind.Generic);
            Input(draft, "boundaryType", "Generic boundary type", original?.BoundaryType ?? "");
        }
        if (level == C4ElementLevel.DeploymentNode) EnumInput(draft, "alignment", "Deployment alignment", original?.Alignment ?? C4HorizontalAlignment.Default);
        ChoiceInput(draft, "parent", "Container / boundary", new[] { new Choice("", "Top level") }.Concat(document.Elements.Where(e => e.IsContainer && e.Id != original?.Id).Select(e => new Choice(e.Id, e.Label))), original?.ParentId ?? draft.Get("$parent", ""));
        CollapseDetails(detailsStart, "Type and containment");
        Footer("Apply C4 element", () =>
        {
            var element = new C4Element(original?.Id ?? "c4" + Guid.NewGuid().ToString("N"), Value(draft, "alias"), Value(draft, "label"), level)
            {
                ParentId = Optional(draft, "parent"), Description = draft.Get("description", ""),
                Technology = level is C4ElementLevel.Container or C4ElementLevel.Component or C4ElementLevel.DeploymentNode ? Value(draft, "technology") : "",
                External = level is not (C4ElementLevel.Boundary or C4ElementLevel.DeploymentNode) && draft.Get("external", false),
                Shape = level is C4ElementLevel.System or C4ElementLevel.Container or C4ElementLevel.Component ? draft.Get("shape", C4ElementShape.Box) : C4ElementShape.Box,
                BoundaryKind = level == C4ElementLevel.Boundary ? draft.Get("boundary", C4BoundaryKind.Generic) : C4BoundaryKind.Generic,
                BoundaryType = level == C4ElementLevel.Boundary ? Value(draft, "boundaryType") : "",
                Alignment = level == C4ElementLevel.DeploymentNode ? draft.Get("alignment", C4HorizontalAlignment.Default) : C4HorizontalAlignment.Default,
                Style = original is null ? new() : (JsonObject)original.Style.DeepClone(), ExtraArguments = original?.ExtraArguments ?? [],
            };
            ApplyC4(original is null ? C4DiagramEditing.AddElement(element) : C4DiagramEditing.UpdateElement(element), draft.Owner);
        });
        if (original is not null)
        {
            var siblings = document.Elements.Where(e => e.ParentId == original.ParentId).ToArray();
            var index = Array.IndexOf(siblings, original);
            Button("Move declaration earlier", () => ApplyC4(C4DiagramEditing.MoveElement(original.Id, original.ParentId, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move declaration later", () => ApplyC4(C4DiagramEditing.MoveElement(original.Id, original.ParentId, index + 1), draft.Owner)).IsEnabled = index + 1 < siblings.Length;
        }
    }
    private void C4RelationshipEditor(Draft draft)
    {
        var document = Model().C4!;
        var relation = Remember(draft, () => document.Relationships.Single(r => r.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("C4 relationship");
        ChoiceInput(draft, "from", "From element", document.Elements.Select(e => new Choice(e.Id, e.Label)), relation.From);
        ChoiceInput(draft, "to", "To element", document.Elements.Select(e => new Choice(e.Id, e.Label)), relation.To);
        Input(draft, "label", "Relationship label", relation.Label); Input(draft, "technology", "Relationship technology", relation.Technology);
        EnumInput(draft, "kind", "Relationship macro", relation.Kind);
        Input(draft, "index", "RelIndex source argument", relation.IndexArgument ?? "");
        foreach (var key in new[] { "textColor", "lineColor", "offsetX", "offsetY" }) StyleInput(draft, key, relation.Style[key]?.ToString() ?? "");
        Text("Declared order controls C4 placement. Dynamic numbers follow relationship order.");
        Footer("Apply C4 relationship", () =>
        {
            var style = (JsonObject)relation.Style.DeepClone();
            foreach (var key in new[] { "textColor", "lineColor", "offsetX", "offsetY" }) if (Optional(draft, key) is { } value) style[key] = value; else style.Remove(key);
            ApplyC4(C4DiagramEditing.UpdateRelationship(relation with
            {
                From = Value(draft, "from"), To = Value(draft, "to"), Label = Value(draft, "label"), Technology = Value(draft, "technology"),
                Kind = draft.Get("kind", relation.Kind), IndexArgument = Optional(draft, "index"), Style = style,
            }), draft.Owner);
        });
        var index = document.Relationships.FindIndex(r => r.Id == relation.Id);
        Button("Move relationship earlier", () => ApplyC4(C4DiagramEditing.MoveRelationship(relation.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
        Button("Delete C4 relationship", () => ApplyC4(C4DiagramEditing.RemoveRelationship(relation.Id), draft.Owner));
    }
    private void C4AppearanceEditor(Draft draft)
    {
        var element = Remember(draft, () => Model().C4!.Elements.Single(e => e.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("C4 appearance");
        foreach (var key in new[] { "bgColor", "fontColor", "borderColor" }) ColourInput(draft, key, key, element.Style[key]?.ToString() ?? "");
        Footer("Apply C4 appearance", () =>
        {
            var style = (JsonObject)element.Style.DeepClone();
            foreach (var key in new[] { "bgColor", "fontColor", "borderColor" }) if (Optional(draft, key) is { } value) style[key] = value; else style.Remove(key);
            ApplyC4(C4DiagramEditing.UpdateElement(element with { Style = style }), draft.Owner);
        });
    }
    private void C4OptionsEditor(Draft draft)
    {
        var document = Model().C4!;
        Heading("C4 layout");
        Input(draft, "title", "C4 title", document.Title);
        Input(draft, "shapes", "Shapes per row", document.LayoutOptions["c4ShapeInRow"]?.ToString() ?? document.Configuration["c4ShapeInRow"]?.ToString() ?? "4");
        Input(draft, "boundaries", "Boundaries per row", document.LayoutOptions["c4BoundaryInRow"]?.ToString() ?? document.Configuration["c4BoundaryInRow"]?.ToString() ?? "2");
        Check(draft, "wrap", "Wrap C4 text", document.Configuration["wrap"]?.GetValue<bool>() ?? document.GlobalConfiguration["wrap"]?.GetValue<bool>() ?? true);
        Footer("Apply C4 layout", () => Apply(new BatchCommand("Edit C4 layout", [
            C4DiagramEditing.SetTitle(Value(draft, "title")), C4DiagramEditing.SetLayout(int.Parse(Value(draft, "shapes"), CultureInfo.InvariantCulture), int.Parse(Value(draft, "boundaries"), CultureInfo.InvariantCulture)),
            C4DiagramEditing.SetWrap(draft.Get("wrap", true)),
        ]), draft.Owner, after: () => _canvas.SelectStructural(null)));
    }
    private void GanttTaskEditor(Draft draft)
    {
        var document = Model().Gantt!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.Tasks.Single(t => t.Id == selected.Id)) : null;
        Heading(original is null ? "Add Gantt task" : "Edit Gantt task");
        Text("Dates use " + document.DateFormat + "; reference " + _canvas.GanttContext.ReferenceLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Input(draft, "label", "Task label", original?.Label ?? "");
        var startDefault = original?.Start switch { GanttStartSpec.After => StartMode.After, GanttStartSpec.Previous => StartMode.Previous, _ => StartMode.Date };
        var startPicker = EnumInput(draft, "startMode", "Start mode", startDefault);
        startPicker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var start = draft.Get("startMode", startDefault);
        if (start == StartMode.Date) Input(draft, "startDate", "Start date / time", (original?.Start as GanttStartSpec.Date)?.Text ?? "");
        if (start == StartMode.After) Input(draft, "after", "After source IDs (comma separated)", original?.Start is GanttStartSpec.After after ? ReferenceNames(after.TaskIds) : "");
        var endDefault = original?.End switch { GanttEndSpec.Date => EndMode.Date, GanttEndSpec.Until => EndMode.Until, _ => EndMode.Duration };
        var endPicker = EnumInput(draft, "endMode", "End mode", endDefault); endPicker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var end = draft.Get("endMode", endDefault);
        if (end == EndMode.Date) Input(draft, "endDate", "End date / time", (original?.End as GanttEndSpec.Date)?.Text ?? "");
        if (end == EndMode.Until) Input(draft, "until", "Until source IDs (comma separated)", original?.End is GanttEndSpec.Until until ? ReferenceNames(until.TaskIds) : "");
        if (end == EndMode.Duration)
        {
            Input(draft, "duration", "Duration", (original?.End as GanttEndSpec.Duration)?.Amount.ToString("R", CultureInfo.InvariantCulture) ?? "1");
            EnumInput(draft, "unit", "Duration unit", (original?.End as GanttEndSpec.Duration)?.Unit ?? GanttDurationUnit.Day);
        }
        var detailsStart = Children.Count;
        Input(draft, "sourceId", "Task source ID", original?.SourceId ?? "");
        ChoiceInput(draft, "section", "Task section", new[] { new Choice("", "No section") }.Concat(document.Sections.Select(s => new Choice(s.Id, s.Label))), original?.SectionId ?? "");
        foreach (var flag in new[] { GanttTaskFlags.Active, GanttTaskFlags.Done, GanttTaskFlags.Critical, GanttTaskFlags.Milestone, GanttTaskFlags.VerticalMarker })
            Check(draft, flag.ToString(), flag.ToString(), original?.Flags.HasFlag(flag) ?? false);
        CollapseDetails(detailsStart, "Identity, section and status");
        Footer("Apply Gantt task", () =>
        {
            GanttStartSpec startSpec = start switch { StartMode.Date => new GanttStartSpec.Date(Value(draft, "startDate")), StartMode.After => new GanttStartSpec.After(ReferenceIds(Value(draft, "after"))), _ => new GanttStartSpec.Previous() };
            GanttEndSpec endSpec = end switch { EndMode.Date => new GanttEndSpec.Date(Value(draft, "endDate")), EndMode.Until => new GanttEndSpec.Until(ReferenceIds(Value(draft, "until"))), _ => new GanttEndSpec.Duration(Numeric(draft, "duration"), draft.Get("unit", GanttDurationUnit.Day)) };
            var flags = GanttTaskFlags.None;
            foreach (var flag in new[] { GanttTaskFlags.Active, GanttTaskFlags.Done, GanttTaskFlags.Critical, GanttTaskFlags.Milestone, GanttTaskFlags.VerticalMarker })
                if (draft.Get(flag.ToString(), false)) flags |= flag;
            var task = new GanttTask(original?.Id ?? "task" + Guid.NewGuid().ToString("N"), Value(draft, "label"), startSpec, endSpec)
            { SourceId = Optional(draft, "sourceId"), SectionId = Optional(draft, "section"), Flags = flags };
            ApplyGantt(original is null ? GanttDiagramEditing.AddTask(task, _canvas.GanttContext) : GanttDiagramEditing.UpdateTask(task, _canvas.GanttContext), draft.Owner);
        });
        if (original is not null)
        {
            var index = document.Tasks.FindIndex(t => t.Id == original.Id);
            Button("Move task earlier", () => ApplyGantt(GanttDiagramEditing.MoveTask(original.Id, index - 1, _canvas.GanttContext), draft.Owner)).IsEnabled = index > 0;
            Button("Move task later", () => ApplyGantt(GanttDiagramEditing.MoveTask(original.Id, index + 1, _canvas.GanttContext), draft.Owner)).IsEnabled = index + 1 < document.Tasks.Count;
            Button("Delete task", () => ApplyGantt(GanttDiagramEditing.RemoveTask(original.Id, _canvas.GanttContext), draft.Owner));
        }
        string ReferenceNames(IReadOnlyList<string> ids) => string.Join(",", ids.Select(id => document.Tasks.Single(t => t.Id == id).SourceId));
        IReadOnlyList<string> ReferenceIds(string text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => document.Tasks.FirstOrDefault(t => t.SourceId == name)?.Id ?? throw new ArgumentException("Unknown task source ID: " + name)).ToArray();
    }
    private void GanttSectionEditor(Draft draft)
    {
        var document = Model().Gantt!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.Sections.Single(s => s.Id == selected.Id)) : null;
        Heading("Gantt section"); Input(draft, "label", "Section label", original?.Label ?? "");
        Footer("Apply section", () => ApplyGantt(original is null ? GanttDiagramEditing.AddSection(new("section" + Guid.NewGuid().ToString("N"), Value(draft, "label")), _canvas.GanttContext)
            : GanttDiagramEditing.RenameSection(original.Id, Value(draft, "label"), _canvas.GanttContext), draft.Owner));
    }
    private void GanttOptionsEditor(Draft draft)
    {
        var document = Model().Gantt!;
        Heading("Gantt options"); Input(draft, "title", "Schedule title", document.Title);
        Input(draft, "format", "Input date format", document.DateFormat); Input(draft, "axis", "Axis date format", document.AxisFormat ?? "");
        Input(draft, "ticks", "Tick interval", document.TickInterval ?? "");
        EnumInput(draft, "weekday", "Week tick starts", document.Weekday);
        EnumInput(draft, "weekend", "Weekend starts", document.WeekendStart, [DayOfWeek.Friday, DayOfWeek.Saturday]);
        Input(draft, "excludes", "Excluded dates / days", string.Join(",", document.Excludes));
        Input(draft, "includes", "Included dates", string.Join(",", document.Includes));
        Check(draft, "compact", "Compact rows", document.Compact); Input(draft, "today", "Today marker (off or style)", document.TodayMarker ?? "");
        Footer("Apply schedule options", () => Apply(new BatchCommand("Edit Gantt options", [
            GanttDiagramEditing.SetTitle(Value(draft, "title"), _canvas.GanttContext),
            GanttDiagramEditing.SetDateFormat(Value(draft, "format"), _canvas.GanttContext),
            GanttDiagramEditing.SetAxis(Optional(draft, "axis"), Optional(draft, "ticks"), draft.Get("weekday", DayOfWeek.Sunday), _canvas.GanttContext),
            GanttDiagramEditing.SetCalendar(Value(draft, "excludes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                Value(draft, "includes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), draft.Get("weekend", DayOfWeek.Saturday), _canvas.GanttContext),
            GanttDiagramEditing.SetCompact(draft.Get("compact", false), _canvas.GanttContext),
            GanttDiagramEditing.SetTodayMarker(Optional(draft, "today"), _canvas.GanttContext),
        ]), draft.Owner, after: () => _canvas.SelectStructural(null)));
    }
    private void ApplyC4(C4EditCommand command, object owner) => Apply(command, owner, after: () => _canvas.SelectStructural(command.ResultId is { } id
        ? new(Model().C4!.Relationships.Any(r => r.Id == id) ? "C4Relationship" : "C4Element", id) : null));
    private void ApplyGantt(GanttEditCommand command, object owner) => Apply(command, owner, after: () => _canvas.SelectStructural(command.ResultId is { } id
        ? new(Model().Gantt!.Sections.Any(s => s.Id == id) ? "GanttSection" : "GanttTask", id) : null));
    private void CollapseDetails(int start, string title)
    {
        var controls = Children.Skip(start).ToArray();
        foreach (var control in controls) Children.Remove(control);
        PropertySection(title, () => { foreach (var control in controls) Children.Add(control); });
    }
}
