using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private bool ShowGridSelection()
    {
        if (Model().Blocks is not null || Model().Architecture is not null)
        {
            if (_canvas.GridConnectionSource is { } source)
            {
                Heading("Choose connection target");
                Text("From: " + (Model().Blocks?.AllItems.FirstOrDefault(n => n.Id == source)?.Display ?? Model().Architecture?.Nodes.FirstOrDefault(n => n.Id == source)?.Display));
                Text("Click a named block or service/junction on the canvas. Escape cancels.");
                Button("Cancel connection", _canvas.CancelRequirementConnection); return true;
            }
        }
        if (Model().Blocks is { } blocks)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "BlockConnection" } edge) { Open(new(TaskKind.BlockLink, edge), false); return true; }
            if (_canvas.SelectedStructuralTarget is { Role: "Block" } selected)
            {
                var item = blocks.AllItems.Single(n => n.Id == selected.Id);
                Open(new(TaskKind.BlockItem, selected), false);
                if (item.Kind != BlockItemKind.Space) Button("Block appearance…", () => Open(new(TaskKind.BlockAppearance, selected)));
                if (item.Kind == BlockItemKind.Composite) Button("Add child block", () => Open(new(TaskKind.BlockItem, Model()) { Values = { ["$parent"] = item.Id } }));
                if (item.SourceId is not null && item.Kind != BlockItemKind.Space) Button("Add connection", () => _canvas.BeginGridConnection(item.Id));
                Button("Move in grid…", () => Open(new(TaskKind.BlockMove, selected)));
                Button("Delete block", () => ApplyBlock(BlockDiagramEditing.Remove(item.Id), selected));
            }
            else
            {
                Heading("Block grid"); Button("Add block", AddClass);
                foreach (var item in blocks.AllItems)
                    Button(item.Kind == BlockItemKind.Space ? $"Spacer · {item.Span} column(s)" : item.Display.Length == 0 ? "Composite block" : item.Display,
                        () => _canvas.SelectStructural(new("Block", item.Id)));
            }
            if (SelectionOwner is null) Button("Grid columns…", DisplayOptions);
            return true;
        }
        if (Model().Architecture is { } architecture)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "ArchitectureConnection" } edge) { Open(new(TaskKind.ArchitectureLink, edge), false); return true; }
            if (_canvas.SelectedStructuralTarget is { } selected)
            {
                if (selected.Role == "ArchitectureGroup")
                {
                    var group = architecture.Groups.Single(g => g.Id == selected.Id);
                    Open(new(TaskKind.ArchitectureGroup, selected), false);
                    Button("Add member", () => Open(new(TaskKind.ArchitectureNode, Model()) { Values = { ["$group"] = group.Id } }));
                    Button("Ungroup members", () => ApplyArchitecture(ArchitectureDiagramEditing.RemoveGroup(group.Id), selected));
                }
                else
                {
                    var node = architecture.Nodes.Single(n => n.Id == selected.Id);
                    Open(new(TaskKind.ArchitectureNode, selected), false);
                    Button("Connect ports", () => _canvas.BeginGridConnection(node.Id));
                    Button("Delete service / junction", () => ApplyArchitecture(ArchitectureDiagramEditing.RemoveNode(node.Id), selected));
                }
            }
            else
            {
                Heading("Architecture"); Button("Add service / junction", AddClass);
                Button("Add group", () => Open(new(TaskKind.ArchitectureGroup, Model())));
                foreach (var group in architecture.Groups) Button("Group: " + group.Display, () => _canvas.SelectStructural(new("ArchitectureGroup", group.Id)));
                foreach (var node in architecture.Nodes) Button(node.Display, () => _canvas.SelectStructural(new("ArchitectureNode", node.Id)));
            }
            if (SelectionOwner is null)
            {
                Button("Align row / column…", () => Open(new(TaskKind.ArchitectureAlignment, Model())));
                Button("Architecture spacing…", DisplayOptions);
            }
            return true;
        }
        return false;
    }
    private bool BuildGridEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.BlockItem: BlockItemEditor(draft); return true;
            case TaskKind.BlockMove: BlockMoveEditor(draft); return true;
            case TaskKind.BlockLink: BlockLinkEditor(draft); return true;
            case TaskKind.BlockAppearance: BlockAppearanceEditor(draft); return true;
            case TaskKind.ArchitectureNode: ArchitectureNodeEditor(draft); return true;
            case TaskKind.ArchitectureGroup: ArchitectureGroupEditor(draft); return true;
            case TaskKind.ArchitectureLink: ArchitectureLinkEditor(draft); return true;
            case TaskKind.ArchitectureAlignment: ArchitectureAlignmentEditor(draft); return true;
            case TaskKind.GridSettings: GridSettingsEditor(draft); return true;
            default: return false;
        }
    }
    private void BlockItemEditor(Draft draft)
    {
        var document = Model().Blocks!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.AllItems.Single(n => n.Id == selected.Id)) : null;
        Heading(original is null ? "Add block" : "Edit block");
        var kinds = original is { Children.Count: > 0 } ? new[] { BlockItemKind.Composite } : Enum.GetValues<BlockItemKind>();
        var picker = EnumInput(draft, "kind", "Block kind", original?.Kind ?? BlockItemKind.Shape, kinds);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var kind = draft.Get("kind", original?.Kind ?? BlockItemKind.Shape);
        if (kind != BlockItemKind.Space)
        {
            Input(draft, "id", "Source identifier", original?.SourceId ?? "");
            Input(draft, "label", "Block label", original?.Label ?? "");
            Check(draft, "markdown", "Markdown label", original?.Markdown ?? false);
            Input(draft, "classes", "Style classes (comma separated)", string.Join(",", original?.Classes ?? []));
        }
        Input(draft, "span", "Column span", (original?.Span ?? 1).ToString(CultureInfo.InvariantCulture));
        if (kind == BlockItemKind.Shape) EnumInput(draft, "shape", "Block shape", original?.Shape ?? NodeShape.Rectangle);
        if (kind == BlockItemKind.Composite) Input(draft, "columns", "Child columns (blank is auto)", original?.Columns?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (kind == BlockItemKind.Arrow)
            foreach (var direction in new[] { BlockArrowDirections.Left, BlockArrowDirections.Right, BlockArrowDirections.Up, BlockArrowDirections.Down })
                Check(draft, direction.ToString(), direction.ToString() + " arrowhead", original?.Arrows.HasFlag(direction) ?? direction == BlockArrowDirections.Right);
        Footer("Apply block", () =>
        {
            var arrows = BlockArrowDirections.None;
            if (kind == BlockItemKind.Arrow)
                foreach (var direction in new[] { BlockArrowDirections.Left, BlockArrowDirections.Right, BlockArrowDirections.Up, BlockArrowDirections.Down })
                    if (draft.Get(direction.ToString(), direction == BlockArrowDirections.Right)) arrows |= direction;
            var item = new BlockItem(original?.Id ?? "block" + Guid.NewGuid().ToString("N"), kind, original?.Children ?? [])
            {
                SourceId = kind == BlockItemKind.Space ? null : Optional(draft, "id"),
                Label = kind == BlockItemKind.Space ? null : Optional(draft, "label"),
                Markdown = draft.Get("markdown", false), Span = int.Parse(Value(draft, "span"), CultureInfo.InvariantCulture),
                Columns = kind == BlockItemKind.Composite && Optional(draft, "columns") is { } columns ? int.Parse(columns, CultureInfo.InvariantCulture) : null,
                Shape = kind == BlockItemKind.Shape ? draft.Get("shape", NodeShape.Rectangle) : NodeShape.Rectangle, Arrows = arrows,
                Style = original is null ? new() : (JsonObject)original.Style.DeepClone(),
                Classes = kind == BlockItemKind.Space ? [] : Value(draft, "classes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            };
            ApplyBlock(original is null ? BlockDiagramEditing.Add(item, Optional(draft, "$parent")) : BlockDiagramEditing.Update(item), draft.Owner);
        });
    }
    private void BlockMoveEditor(Draft draft)
    {
        var target = (StructuralSelection)draft.Owner; var document = Model().Blocks!;
        var location = BlockDiagramEditing.Locate(document, target.Id);
        var forbidden = BlockDiagramDocument.Descendants([location.Item]).Select(n => n.Id).ToHashSet();
        Heading("Move block in grid");
        ChoiceInput(draft, "parent", "Destination grid", new[] { new Choice("", "Root grid") }.Concat(document.AllItems.Where(n => n.Kind == BlockItemKind.Composite && !forbidden.Contains(n.Id)).Select(n => new Choice(n.Id, n.Display))), location.ParentId ?? "");
        Input(draft, "index", "Position (zero based, after removal)", location.Index.ToString(CultureInfo.InvariantCulture));
        Footer("Move block", () => ApplyBlock(BlockDiagramEditing.Move(target.Id, Optional(draft, "parent"), int.Parse(Value(draft, "index"), CultureInfo.InvariantCulture)), draft.Owner));
    }
    private void BlockAppearanceEditor(Draft draft)
    {
        var document = Model().Blocks!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.AllItems.Single(n => n.Id == selected.Id)) : null;
        var className = draft.Get("$class", "");
        var initial = original?.Style ?? (document.Classes.TryGetValue(className, out var style) ? style : new JsonObject());
        Heading(original is null ? "Block style class" : "Block appearance");
        if (original is null) Input(draft, "name", "Style class name", className);
        var keys = new[] { "fill", "stroke", "color", "stroke-width", "stroke-dasharray", "font-size", "font-family", "font-weight", "font-style" };
        foreach (var key in keys) StyleInput(draft, key, initial[key]?.ToString() ?? "");
        Footer(original is null ? "Add / update style class" : "Apply block appearance", () =>
        {
            var values = (JsonObject)initial.DeepClone();
            foreach (var key in keys) if (Optional(draft, key) is { } value) values[key] = value; else values.Remove(key);
            ApplyBlock(original is null ? BlockDiagramEditing.SetStyleClass(Value(draft, "name"), values) :
                BlockDiagramEditing.Update(original with { Style = values }), draft.Owner);
        });
    }
    private void BlockLinkEditor(Draft draft)
    {
        var target = (StructuralSelection)draft.Owner; var document = Model().Blocks!;
        var original = Remember(draft, () => document.Connections.Single(e => e.Id == target.Id));
        Heading("Block connection");
        var nodes = document.AllItems.Where(n => n.SourceId is not null && n.Kind != BlockItemKind.Space).ToArray();
        ChoiceInput(draft, "from", "From block", nodes.Select(n => new Choice(n.Id, n.Display)), original.From);
        ChoiceInput(draft, "to", "To block", nodes.Select(n => new Choice(n.Id, n.Display)), original.To);
        Input(draft, "label", "Connection label", original.Label);
        EnumInput(draft, "start", "Start marker", original.FromEnd); EnumInput(draft, "end", "End marker", original.ToEnd);
        EnumInput(draft, "stroke", "Connection line", original.Stroke);
        Footer("Apply block connection", () => ApplyBlock(BlockDiagramEditing.UpdateConnection(original with
        { From = Value(draft, "from"), To = Value(draft, "to"), Label = Value(draft, "label"), FromEnd = draft.Get("start", original.FromEnd), ToEnd = draft.Get("end", original.ToEnd), Stroke = draft.Get("stroke", original.Stroke) }), draft.Owner));
        Button("Delete connection", () => ApplyBlock(BlockDiagramEditing.RemoveConnection(original.Id), draft.Owner));
    }
    private void ArchitectureNodeEditor(Draft draft)
    {
        var d = Model().Architecture!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => d.Nodes.Single(n => n.Id == selected.Id)) : null;
        Heading("Service or junction");
        Input(draft, "id", "Source identifier", original?.SourceId ?? "");
        var picker = EnumInput(draft, "kind", "Node kind", original?.Kind ?? ArchitectureNodeKind.Service);
        picker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var kind = draft.Get("kind", original?.Kind ?? ArchitectureNodeKind.Service);
        if (kind == ArchitectureNodeKind.Service)
        {
            Input(draft, "label", "Service label", original?.Label ?? "");
            ChoiceInput(draft, "icon", "Service icon", new[] { "", "server", "database", "disk", "internet", "cloud" }.Select(value => new Choice(value, value.Length == 0 ? "No icon" : value)), original is null ? "server" : original.Icon ?? "");
        }
        ChoiceInput(draft, "group", "Group", new[] { new Choice("", "No group") }.Concat(d.Groups.Select(g => new Choice(g.Id, g.Display))), original?.GroupId ?? draft.Get("$group", ""));
        Footer("Apply service / junction", () =>
        {
            var node = new ArchitectureNode(original?.Id ?? "node" + Guid.NewGuid().ToString("N"), Value(draft, "id"), kind)
            { Label = kind == ArchitectureNodeKind.Service ? Optional(draft, "label") : null, Icon = kind == ArchitectureNodeKind.Service ? Optional(draft, "icon") : null, GroupId = Optional(draft, "group") };
            ApplyArchitecture(original is null ? ArchitectureDiagramEditing.AddNode(node) : ArchitectureDiagramEditing.UpdateNode(node), draft.Owner);
        });
    }
    private void ArchitectureGroupEditor(Draft draft)
    {
        var d = Model().Architecture!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => d.Groups.Single(g => g.Id == selected.Id)) : null;
        Heading("Architecture group");
        Input(draft, "id", "Group identifier", original?.SourceId ?? "");
        Input(draft, "label", "Group label", original?.Label ?? "");
        ChoiceInput(draft, "icon", "Group icon", new[] { "", "cloud", "server", "database", "disk", "internet" }.Select(value => new Choice(value, value.Length == 0 ? "No icon" : value)), original?.Icon ?? "");
        ChoiceInput(draft, "parent", "Parent group", new[] { new Choice("", "Top level") }.Concat(d.Groups.Where(g => g.Id != original?.Id).Select(g => new Choice(g.Id, g.Display))), original?.ParentId ?? "");
        Footer("Apply group", () =>
        {
            var group = new ArchitectureGroup(original?.Id ?? "group" + Guid.NewGuid().ToString("N"), Value(draft, "id"))
            { Label = Optional(draft, "label"), Icon = Optional(draft, "icon"), ParentId = Optional(draft, "parent") };
            ApplyArchitecture(original is null ? ArchitectureDiagramEditing.AddGroup(group) : ArchitectureDiagramEditing.UpdateGroup(group), draft.Owner);
        });
    }
    private void ArchitectureLinkEditor(Draft draft)
    {
        var target = (StructuralSelection)draft.Owner; var d = Model().Architecture!;
        var original = Remember(draft, () => d.Connections.Single(e => e.Id == target.Id));
        Heading("Architecture connection");
        ChoiceInput(draft, "from", "From service / junction", d.Nodes.Select(n => new Choice(n.Id, n.Display)), original.From.NodeId);
        EnumInput(draft, "fromPort", "From port", original.From.Port);
        Check(draft, "fromGroup", "From member's group boundary", original.From.GroupBoundary); Check(draft, "fromArrow", "Arrow at source", original.FromArrow);
        ChoiceInput(draft, "to", "To service / junction", d.Nodes.Select(n => new Choice(n.Id, n.Display)), original.To.NodeId);
        EnumInput(draft, "toPort", "To port", original.To.Port);
        Check(draft, "toGroup", "To member's group boundary", original.To.GroupBoundary); Check(draft, "toArrow", "Arrow at target", original.ToArrow);
        Footer("Apply port connection", () => ApplyArchitecture(ArchitectureDiagramEditing.UpdateConnection(original with
        {
            From = new(Value(draft, "from"), draft.Get("fromPort", original.From.Port), draft.Get("fromGroup", false)),
            To = new(Value(draft, "to"), draft.Get("toPort", original.To.Port), draft.Get("toGroup", false)),
            FromArrow = draft.Get("fromArrow", false), ToArrow = draft.Get("toArrow", false),
        }), draft.Owner));
        Button("Delete port connection", () => ApplyArchitecture(ArchitectureDiagramEditing.RemoveConnection(original.Id), draft.Owner));
    }
    private void ArchitectureAlignmentEditor(Draft draft)
    {
        var d = Model().Architecture!;
        Heading("Architecture alignment");
        EnumInput(draft, "axis", "Alignment axis", ArchitectureAlignmentAxis.Row);
        Input(draft, "members", "Ordered source identifiers (comma separated)");
        Footer("Apply alignment", () =>
        {
            var members = Value(draft, "members").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(name => d.Nodes.FirstOrDefault(n => n.SourceId == name)?.Id ?? throw new ArgumentException("Unknown source identifier: " + name)).ToArray();
            ApplyArchitecture(ArchitectureDiagramEditing.SetAlignment(new("align" + Guid.NewGuid().ToString("N"), draft.Get("axis", ArchitectureAlignmentAxis.Row), members)), draft.Owner);
        });
        foreach (var alignment in d.Alignments)
            Button("Remove " + alignment.Axis + ": " + string.Join(", ", alignment.Members.Select(id => d.Nodes.Single(n => n.Id == id).SourceId)),
                () => ApplyArchitecture(ArchitectureDiagramEditing.RemoveAlignment(alignment.Id), draft.Owner));
    }
    private void GridSettingsEditor(Draft draft)
    {
        if (Model().Blocks is { } blocks)
        {
            Heading("Root grid");
            Input(draft, "columns", "Columns (blank is auto)", blocks.Columns?.ToString(CultureInfo.InvariantCulture) ?? "");
            Footer("Apply grid columns", () => ApplyBlock(BlockDiagramEditing.SetColumns(null, Optional(draft, "columns") is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null), draft.Owner));
            Button("Add style class…", () => Open(new(TaskKind.BlockAppearance, Model())));
            foreach (var name in blocks.Classes.Keys) Button("Edit style class: " + name, () => Open(new(TaskKind.BlockAppearance, Model()) { Values = { ["$class"] = name } }));
        }
        else
        {
            var config = Model().Architecture!.Configuration;
            Heading("Architecture spacing");
            foreach (var (key, fallback) in new[] { ("iconSize", "80"), ("nodeSeparation", "75"), ("idealEdgeLengthMultiplier", "1.5") })
                Input(draft, key, key, config[key]?.ToString() ?? fallback);
            Text("Native spacing controls. fcose-specific settings in source are preserved, not applied.");
            Footer("Apply architecture spacing", () => Apply(new BatchCommand("Edit architecture spacing",
                new[] { "iconSize", "nodeSeparation", "idealEdgeLengthMultiplier" }.Select(key => (IEditCommand)ArchitectureDiagramEditing.SetOption(key, JsonValue.Create(Numeric(draft, key)))).ToArray()),
                draft.Owner, after: () => _canvas.SelectStructural(null)));
        }
    }
    private void ApplyBlock(BlockEditCommand command, object owner) => Apply(command, owner, after: () => _canvas.SelectStructural(command.ResultId is { } id
        ? new(Model().Blocks!.Connections.Any(e => e.Id == id) ? "BlockConnection" : "Block", id) : null));
    private void ApplyArchitecture(ArchitectureEditCommand command, object owner) => Apply(command, owner, after: () => _canvas.SelectStructural(command.ResultId is { } id
        ? Model().Architecture!.Connections.Any(e => e.Id == id) ? new("ArchitectureConnection", id)
            : Model().Architecture!.Groups.Any(g => g.Id == id) ? new("ArchitectureGroup", id)
            : Model().Architecture!.Nodes.Any(n => n.Id == id) ? new("ArchitectureNode", id) : null : null));
}
