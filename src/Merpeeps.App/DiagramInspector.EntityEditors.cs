using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    public void EditEntitySelection()
    {
        ShowSelection();
        FocusInlineField(_canvas.SelectedEntityGroup is not null ? "Subgraph title" : "Entity name");
    }

    private void ShowEntitySelection()
    {
        if (_canvas.Selection.Count > 1)
        {
            Heading($"{_canvas.Selection.Count} entities selected");
            Text("Select one entity or relationship to edit its properties.");
            return;
        }
        if (_canvas.SelectedEdge is { EntityRelationship: not null } relationship)
        {
            Open(new(TaskKind.EntityRelationship, relationship), focus: false);
            return;
        }
        if (_canvas.SelectedEntityGroup is { } group)
        {
            Open(new(TaskKind.EntityGroups, group), false);
            Button("Add relationship", () => _canvas.BeginEntityRelationship(group.Id));
            Button("Remove subgraph, keep entities", () => Apply(new DeleteEntityGroupCommand(group), group));
            return;
        }
        if (_canvas.Selected is not { Entity: { } entity } node)
        {
            Heading("Entity relationship");
            Text("Add an entity, then select it to add attributes or connect it.");
            Button("Add entity", AddClass);
            foreach (var pending in _drafts.Values.Where(d => d.Dirty).ToArray())
                Button("Resume edit: " + OwnerName(pending.Owner), () => Open(pending));
            return;
        }
        Open(new(TaskKind.EntityProperties, node), false);
        if (_active?.Task != TaskKind.EntityProperties) return;
        var sections = Children.OfType<Expander>().ToArray();
        foreach (var section in sections) Children.Remove(section);
        PropertySection("Attributes", () =>
        {
            Button("Add attribute", () => Open(new(TaskKind.EntityAttribute, node)));
            foreach (var attribute in entity.Attributes)
                Button(attribute.Type + " " + attribute.Name.TrimStart('*'), () => Open(new(TaskKind.EntityAttribute, node, attribute.Id), false));
            if (entity.Attributes.Count == 0) Text("No attributes yet.");
        }, expanded: true);
        Button("Add relationship", () => _canvas.BeginEntityRelationship(node.Id));
        foreach (var section in sections) Children.Add(section);
        PropertySection("Actions", () => Button("Delete entity", () => Apply(new DeleteNodesCommand([node]), node)));
        var size = EntityRequiredSize(node);
        if (size.Width > node.Width + .1 || size.Height > node.Height + .1)
        {
            Text("Attributes exceed the entity's current bounds. Resize explicitly to fit the cells.");
            Button("Resize to content", () => FitEntity(node));
        }
        foreach (var diagnostic in EntityDiagramStyle.Resolve(node).Diagnostics) Text(diagnostic);
    }

    private void ShowEntityConnection()
    {
        StartPanel();
        Heading("Add or reconnect ER relationship");
        var source = EntityDiagramGeometry.Endpoint(Model(), _canvas.EntityConnectionSource!);
        Text("From: " + source.Name);
        Text(_canvas.EntityConnectionMessage ?? "Click an entity or subgraph. Escape cancels.");
        var targets = Model().Nodes.Select(n => new Choice(n.Id, n.Display))
            .Concat(Model().Subgraphs.Select(g => new Choice(g.Id, "Subgraph: " + (g.Title ?? g.Id)))).ToArray();
        var fallback = new ComboBox { ItemsSource = targets };
        Field("Keyboard target", fallback);
        Button("Connect selected target", () =>
        {
            if (fallback.SelectedItem is not Choice target) throw new ArgumentException("Choose an endpoint.");
            _canvas.CompleteEntityRelationship(target.Id);
        });
        Button("Cancel relationship", _canvas.CancelEntityConnection);
        FinishPanel();
    }

    private bool BuildEntityEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.CreateEntity:
                Heading("New entity");
                Input(draft, "id", "Entity identity");
                Input(draft, "alias", "Display alias (optional)");
                Footer("Create entity", () =>
                {
                    var entity = EntityEditing.CreateEntity(Value(draft, "id"), Optional(draft, "alias"));
                    Place(entity);
                    Apply(new AddEntityCommand(entity), draft.Owner, entity.Id);
                });
                return true;
            case TaskKind.EntityAttribute:
                EntityAttributeEditor(draft);
                return true;
            case TaskKind.EntityProperties:
                EntityPropertiesEditor(draft);
                return true;
            case TaskKind.EntityRelationship:
                EntityRelationshipEditor(draft);
                return true;
            case TaskKind.EntityGroups:
                EntityGroupEditor(draft);
                return true;
            case TaskKind.EntityOptions:
                Heading("ER diagram direction");
                EnumInput(draft, "direction", "Direction", Model().Direction);
                Footer("Apply direction", () => Apply(new SetEntityDirection(draft.Get("direction", Model().Direction)), draft.Owner));
                Text("Re-layout applies direction changes; manual positions remain unchanged until requested.");
                return true;
            default:
                return false;
        }
    }

    private void EntityAttributeEditor(Draft draft)
    {
        var node = (DiagramNode)draft.Owner;
        var attribute = node.Entity!.Attributes.FirstOrDefault(a => a.Id == draft.MemberId);
        Heading(attribute is null ? "Add attribute" : "Edit attribute");
        Text("Entity: " + node.Display);
        Input(draft, "type", "Data type", attribute?.Type ?? "");
        Input(draft, "name", "Attribute name", attribute?.Name.TrimStart('*') ?? "");
        Input(draft, "comment", "Comment", attribute?.Comment ?? "");
        var keys = attribute?.EffectiveKeys ?? EntityKey.None;
        Check(draft, "pk", "Primary key (PK)", keys.HasFlag(EntityKey.Primary));
        Check(draft, "fk", "Foreign key (FK)", keys.HasFlag(EntityKey.Foreign));
        Check(draft, "uk", "Unique key (UK)", keys.HasFlag(EntityKey.Unique));
        Footer("Apply attribute", () =>
        {
            if (Value(draft, "name").StartsWith('*')) throw new ArgumentException("Use the Primary key checkbox rather than an asterisk in the displayed name.");
            var selectedKeys = (draft.Get("pk", false) ? EntityKey.Primary : EntityKey.None)
                | (draft.Get("fk", false) ? EntityKey.Foreign : EntityKey.None)
                | (draft.Get("uk", false) ? EntityKey.Unique : EntityKey.None);
            var next = new EntityAttribute(draft.MemberId ?? Guid.NewGuid().ToString("N"),
                Value(draft, "type"), Value(draft, "name"), selectedKeys, Optional(draft, "comment"));
            Apply(draft.MemberId is null ? EntityEditing.AddAttribute(node, next) : EntityEditing.EditAttribute(node, next), node);
        });
        if (draft.MemberId is not null)
        {
            var index = node.Entity.Attributes.FindIndex(a => a.Id == draft.MemberId);
            Button("Move attribute up", () => Apply(EntityEditing.MoveAttribute(node, draft.MemberId, index - 1), node)).IsEnabled = index > 0;
            Button("Move attribute down", () => Apply(EntityEditing.MoveAttribute(node, draft.MemberId, index + 1), node)).IsEnabled = index < node.Entity.Attributes.Count - 1;
            Button("Delete attribute", () => Apply(EntityEditing.RemoveAttribute(node, draft.MemberId), node));
        }
    }

    private void EntityPropertiesEditor(Draft draft)
    {
        var node = (DiagramNode)draft.Owner;
        Heading("Entity");
        Input(draft, "alias", "Entity name", node.Display);
        PropertySection("Identity and grouping", () =>
        {
            Input(draft, "id", "Entity identity", node.Id);
            ChoiceInput(draft, "group", "Subgraph", NamespaceChoices(), node.SubgraphId ?? "");
        });
        PropertySection("Appearance", () =>
        {
            ColourInput(draft, "fill", "Fill color", node.Entity!.InlineStyle.GetValueOrDefault("fill") ?? "");
            ColourInput(draft, "stroke", "Outline color", node.Entity.InlineStyle.GetValueOrDefault("stroke") ?? "");
            ColourInput(draft, "color", "Text color", node.Entity.InlineStyle.GetValueOrDefault("color") ?? "");
            Input(draft, "width", "Width (px)", node.Width.ToString(CultureInfo.InvariantCulture));
            Input(draft, "height", "Height (px)", node.Height.ToString(CultureInfo.InvariantCulture));
        });
        Footer("Apply entity properties", () =>
        {
            var definition = node.Entity!.Clone();
            foreach (var key in new[] { "fill", "stroke", "color" })
            {
                if (Optional(draft, key) is { } value) definition.InlineStyle[key] = value;
                else definition.InlineStyle.Remove(key);
            }
            var commands = new List<IEditCommand>
            {
                new SetEntityDefinitionCommand(node, definition),
                new SetEntityGroupMembershipCommand(node, Optional(draft, "group")),
            };
            if (draft.Get("alias", node.Display) != node.Display) commands.Add(new SetEntityAliasCommand(node, Optional(draft, "alias")));
            if (Number(draft, "width") != node.Width || Number(draft, "height") != node.Height)
                commands.Add(new ResizeClassNode(node, Number(draft, "width"), Number(draft, "height")));
            RenameEntityIdentityCommand? rename = null;
            if (Value(draft, "id") != node.Id) { rename = new(node, Value(draft, "id")); commands.Add(rename); }
            Apply(new BatchCommand("Edit entity properties", commands.ToArray()), node,
                after: () => { if (rename?.Result is { } result) _canvas.SelectByIds([result.Id]); });
        });
    }

    private void EntityRelationshipEditor(Draft draft)
    {
        var edge = (DiagramEdge)draft.Owner;
        var relationship = edge.EntityRelationship!;
        var from = EntityDiagramGeometry.Endpoint(Model(), edge.From);
        var to = EntityDiagramGeometry.Endpoint(Model(), edge.To);
        Heading("ER relationship");
        Text(from.Name + " → " + to.Name);
        var sourceCardinality = EnumInput(draft, "from", "Source cardinality: " + from.Name, relationship.FromCardinality);
        var targetCardinality = EnumInput(draft, "to", "Target cardinality: " + to.Name, relationship.ToCardinality);
        sourceCardinality.ItemTemplate = CardinalityTemplate();
        targetCardinality.ItemTemplate = CardinalityTemplate();
        Check(draft, "identifying", "Identifying (solid line)", relationship.Identifying);
        Input(draft, "role", "Relationship role", edge.Label ?? "");
        Footer("Apply ER relationship", () => Apply(Change(), edge));
        Button("Reverse direction", () =>
        {
            var reverse = new ReconnectEntityRelationshipCommand(edge, edge.To, edge.From);
            Apply(new BatchCommand("Reverse ER relationship", Change(), reverse), edge,
                after: () => _canvas.SelectEdge(reverse.Result));
        });
        ActionMenu("Reconnect…", [
            ("Change source…", () => { _active = null; _canvas.BeginEntityRelationship(edge.To, edge, true); }),
            ("Change target…", () => { _active = null; _canvas.BeginEntityRelationship(edge.From, edge, false); }),
        ]);
        Button("Delete relationship", () => Apply(new DeleteEdgeCommand(edge), edge));
        SetEntityRelationshipCommand Change() => new(edge,
            new(draft.Get("from", relationship.FromCardinality), draft.Get("to", relationship.ToCardinality), draft.Get("identifying", relationship.Identifying)),
            Value(draft, "role"));
    }

    private void EntityGroupEditor(Draft draft)
    {
        Heading("ER subgraphs");
        if (draft.Owner is not DiagramSubgraph && !draft.Get("create", false))
        {
            foreach (var group in Model().Subgraphs)
                Button(group.Title ?? group.Id, () => Open(new(TaskKind.EntityGroups, group)));
            Button("Add subgraph", () => { draft.Values["create"] = true; RebuildEditor(draft); });
            Button("Back to selection", CancelEditor);
            return;
        }
        var existing = draft.Owner as DiagramSubgraph;
        Input(draft, "id", "Subgraph identity", existing?.Id ?? "");
        Input(draft, "title", "Subgraph title", existing?.Title ?? "");
        ChoiceInput(draft, "parent", "Parent subgraph", NamespaceChoices(), existing?.ParentId ?? "");
        var directions = new[] { new Choice("", "Inherit direction") }.Concat(Enum.GetValues<Direction>().Select(d => new Choice(d.ToString(), d.ToString())));
        ChoiceInput(draft, "direction", "Subgraph direction", directions, existing?.Direction?.ToString() ?? "");
        Footer("Apply subgraph", () =>
        {
            var raw = Optional(draft, "direction");
            var direction = raw is null ? (Direction?)null : Enum.Parse<Direction>(raw);
            if (existing is null)
            {
                var group = new DiagramSubgraph
                {
                    Id = Value(draft, "id"),
                    Title = Optional(draft, "title"),
                    ParentId = Optional(draft, "parent"),
                    Direction = direction,
                };
                Apply(new AddEntityGroupCommand(group), draft.Owner, after: () => _canvas.SelectEntityGroup(group));
            }
            else
            {
                var command = new SetEntityGroupCommand(existing, Value(draft, "id"), Optional(draft, "title"), Optional(draft, "parent"), direction);
                Apply(command, draft.Owner, after: () => _canvas.SelectEntityGroup(command.Result));
            }
        });
        if (existing is not null) Button("Remove subgraph, keep entities", () => Apply(new DeleteEntityGroupCommand(existing), draft.Owner));
    }

    private (double Width, double Height) EntityRequiredSize(DiagramNode node)
    {
        var style = EntityDiagramStyle.Resolve(node);
        var face = new Typeface(style.FontFamily, style.Italic ? FontStyle.Italic : FontStyle.Normal, style.Bold ? FontWeight.Bold : FontWeight.Normal);
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, style.FontSize, Brushes.Black).Width;
        var content = EntityDiagramText.Content(node);
        var columns = EntityDiagramGeometry.Columns(node, style.FontSize, Measure);
        var headerWidth = content.NameRuns.Sum(run => new FormattedText(run.Text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(style.FontFamily,
                run.Italic || style.Italic ? FontStyle.Italic : FontStyle.Normal,
                run.Bold || style.Bold ? FontWeight.Bold : FontWeight.Normal),
            style.FontSize, Brushes.Black).Width);
        return (Math.Max(columns.Sum(), headerWidth + 24),
            style.FontSize * 1.4 + 16 + Math.Max(1, content.Attributes.Count) * (style.FontSize * 1.4 + 8));
    }
    private static FuncDataTemplate<EntityCardinality> CardinalityTemplate() => new((cardinality, _) =>
        new TextBlock
        {
            Text = cardinality switch
            {
                EntityCardinality.ExactlyOne => "Exactly one (1)",
                EntityCardinality.ZeroOrOne => "Zero or one (0..1)",
                EntityCardinality.OneOrMore => "One or more (1..*)",
                EntityCardinality.ZeroOrMore => "Zero or more (0..*)",
                _ => throw new ArgumentOutOfRangeException(nameof(cardinality)),
            },
        });
    private void FitEntity(DiagramNode node)
    {
        var size = EntityRequiredSize(node);
        Apply(new ResizeClassNode(node, Math.Ceiling(size.Width), Math.Ceiling(size.Height)), node);
    }
    private sealed class SetEntityDirection(Direction direction) : IEditCommand
    {
        private Direction _before;
        public string Description => "Change ER direction";
        public void Do(DiagramModel model) { _before = model.Direction; model.Direction = direction; }
        public void Undo(DiagramModel model) => model.Direction = _before;
    }
}
