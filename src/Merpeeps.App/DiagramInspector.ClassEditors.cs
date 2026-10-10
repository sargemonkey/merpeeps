using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static readonly ClassRelationEnd[] Ends =
        [ClassRelationEnd.None, ClassRelationEnd.Arrow, ClassRelationEnd.Triangle, ClassRelationEnd.Composition, ClassRelationEnd.Aggregation];
    private sealed record RelationshipPreset(string Name, ClassRelationEnd From, ClassRelationEnd To, bool Dashed)
    {
        public override string ToString() => Name;
    }
    private static readonly RelationshipPreset[] RelationshipTypes =
    [
        new("Association", ClassRelationEnd.None, ClassRelationEnd.Arrow, false),
        new("Inheritance", ClassRelationEnd.None, ClassRelationEnd.Triangle, false),
        new("Realization", ClassRelationEnd.None, ClassRelationEnd.Triangle, true),
        new("Composition", ClassRelationEnd.Composition, ClassRelationEnd.None, false),
        new("Aggregation", ClassRelationEnd.Aggregation, ClassRelationEnd.None, false),
        new("Dependency", ClassRelationEnd.None, ClassRelationEnd.Arrow, true),
        new("Solid link", ClassRelationEnd.None, ClassRelationEnd.None, false),
        new("Dashed link", ClassRelationEnd.None, ClassRelationEnd.None, true),
    ];

    private void BuildEditor(Draft draft)
    {
        if (BuildStructuralEditor(draft)) return;
        if (BuildDataEditor(draft)) return;
        if (BuildChartEditor(draft)) return;
        if (BuildSequenceEditor(draft)) return;
        if (BuildEntityEditor(draft)) return;
        switch (draft.Task)
        {
            case TaskKind.CreateClass:
                Heading("New class");
                Input(draft, "id", "Class identity");
                Input(draft, "label", "Display name (optional)");
                Footer("Create class", () =>
                {
                    var node = ClassEditing.CreateClass(Value(draft, "id"), Optional(draft, "label"));
                    Place(node);
                    Apply(new AddClassElementCommand(node), draft.Owner, node.Id);
                });
                break;
            case TaskKind.Rename:
                var renamed = (DiagramNode)draft.Owner;
                Heading("Rename " + renamed.Display);
                Input(draft, "label", "Display name", renamed.Display);
                Footer("Apply name", () => Apply(renamed.Entity is null
                    ? new RenameNodeCommand(renamed, Optional(draft, "label"))
                    : new SetEntityAliasCommand(renamed, Optional(draft, "label")), renamed));
                break;
            case TaskKind.Member:
                MemberEditor(draft);
                break;
            case TaskKind.Relationship:
                RelationshipEditor(draft);
                break;
            case TaskKind.ClassProperties:
                ClassProperties(draft);
                break;
            case TaskKind.Size:
                var sized = (DiagramNode)draft.Owner;
                Heading("Size: " + sized.Display);
                Input(draft, "width", "Width (px)", sized.Width.ToString(CultureInfo.InvariantCulture));
                Input(draft, "height", "Height (px)", sized.Height.ToString(CultureInfo.InvariantCulture));
                Footer("Apply size", () => Apply(new ResizeClassNode(sized, Number(draft, "width"), Number(draft, "height")), sized));
                if (sized.Class?.Kind == ClassNodeKind.Class || sized.Entity is not null) Button("Resize to content", () => Fit(sized));
                break;
            case TaskKind.CreateNote:
            case TaskKind.Note:
                NoteEditor(draft);
                break;
            case TaskKind.Interface:
                var owner = (DiagramNode)draft.Owner;
                Heading("Add interface to " + owner.Display);
                Input(draft, "name", "Interface name");
                Footer("Create interface", () =>
                {
                    var command = new AddClassInterfaceCommand(owner.Id, "__interface_" + Guid.NewGuid().ToString("N"), Value(draft, "name"));
                    command.Node.HasExplicitPosition = true;
                    Apply(command, owner, command.Node.Id);
                });
                break;
            case TaskKind.Options:
                Heading("Class diagram display");
                Check(draft, "hide", "Hide empty member compartments", Model().ClassOptions!.HideEmptyMembersBox);
                Check(draft, "nested", "Show nested namespace frames", Model().ClassOptions!.HierarchicalNamespaces);
                Footer("Apply display options", () => Apply(new SetClassOptionsCommand(
                    draft.Get("hide", false), draft.Get("nested", true)), draft.Owner));
                break;
            case TaskKind.Namespaces:
                NamespaceEditor(draft);
                break;
            case TaskKind.Navigator:
                Heading("Select an element");
                foreach (var node in Model().Nodes)
                    Button($"{(node.Entity is null ? node.Class?.Kind.ToString() : "Entity")}: {node.Display}", () =>
                    {
                        _active = null;
                        _canvas.SelectByIds([node.Id]);
                        _canvas.RevealFirstSelected();
                    });
                if (Model().EntityOptions is not null)
                    foreach (var group in Model().Subgraphs)
                        Button("Subgraph: " + (group.Title ?? group.Id), () =>
                        {
                            _active = null;
                            _canvas.SelectEntityGroup(group);
                        });
                foreach (var edge in Model().Edges)
                    Button($"Relationship: {NodeName(edge.From)} → {NodeName(edge.To)}", () =>
                    {
                        _active = null;
                        _canvas.SelectEdge(edge);
                    });
                Button("Back to selection", CancelEditor);
                break;
        }
    }

    private void MemberEditor(Draft draft)
    {
        var node = (DiagramNode)draft.Owner;
        var member = node.Class!.Members.FirstOrDefault(m => m.Id == draft.MemberId);
        Heading((draft.MemberId is null ? "Add " : "Edit ") +
            (draft.MemberKind == ClassMemberKind.Attribute ? "attribute" : "method"));
        Text("Class: " + node.Display);
        Input(draft, "signature", "Signature", member?.Signature ?? "");
        Text(draft.MemberKind == ClassMemberKind.Attribute ? "For example: String owner" : "For example: save() bool");
        EnumInput(draft, "visibility", "Visibility", member?.Visibility ?? ClassVisibility.Unspecified);
        Check(draft, "static", "Static", member?.IsStatic ?? false);
        if (draft.MemberKind == ClassMemberKind.Operation)
            Check(draft, "abstract", "Abstract", member?.IsAbstract ?? false);
        Footer("Apply member", () =>
        {
            var next = new ClassMember(draft.MemberId ?? Guid.NewGuid().ToString("N"), draft.MemberKind,
                Value(draft, "signature"), draft.Get("visibility", ClassVisibility.Unspecified),
                draft.Get("static", false), draft.Get("abstract", false));
            Apply(draft.MemberId is null ? ClassEditing.AddMember(node, next) : ClassEditing.EditMember(node, next), node);
        });
        if (draft.MemberId is not null)
        {
            Button("Remove member", () => Apply(ClassEditing.DeleteMember(node, draft.MemberId), node));
            var index = node.Class!.Members.FindIndex(m => m.Id == draft.MemberId);
            Button("Move member up", () => MoveMember(-1)).IsEnabled = index > 0;
            Button("Move member down", () => MoveMember(1)).IsEnabled = index >= 0 && index < node.Class.Members.Count - 1;
        }
        void MoveMember(int direction)
        {
            var index = node.Class!.Members.FindIndex(m => m.Id == draft.MemberId);
            Apply(ClassEditing.MoveMember(node, draft.MemberId!, index + direction), node);
        }
    }

    private void ClassProperties(Draft draft)
    {
        var node = (DiagramNode)draft.Owner;
        Heading("Class");
        Input(draft, "label", "Class name", node.Display);
        PropertySection("Identity and type", () =>
        {
            Input(draft, "id", "Class identity", node.Id);
            Input(draft, "generic", "Generic type", node.Class!.GenericType ?? "");
            Input(draft, "annotations", "Annotations, comma separated", string.Join(", ", node.Class.Annotations));
            ChoiceInput(draft, "namespace", "Namespace", NamespaceChoices(), node.SubgraphId ?? "");
        });
        PropertySection("Size", () =>
        {
            Input(draft, "width", "Width (px)", node.Width.ToString(CultureInfo.InvariantCulture));
            Input(draft, "height", "Height (px)", node.Height.ToString(CultureInfo.InvariantCulture));
        });
        Footer("Apply class properties", () =>
        {
            var next = node.Class!.Clone();
            next.GenericType = Optional(draft, "generic");
            next.Annotations.Clear();
            next.Annotations.AddRange(Value(draft, "annotations").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            var commands = new List<IEditCommand>
            {
                new SetClassDefinitionCommand(node, next),
                new SetClassNamespaceCommand(node, Optional(draft, "namespace")),
            };
            if (draft.Get("label", node.Display) != node.Display) commands.Add(new RenameNodeCommand(node, Optional(draft, "label")));
            if (Number(draft, "width") != node.Width || Number(draft, "height") != node.Height)
                commands.Add(new ResizeClassNode(node, Number(draft, "width"), Number(draft, "height")));
            RenameClassIdentityCommand? rename = null;
            if (Value(draft, "id") != node.Id)
            {
                rename = new(node, Value(draft, "id"));
                commands.Add(rename);
            }
            Apply(new BatchCommand("Edit class properties", commands.ToArray()), node,
                after: () => { if (rename?.Result is { } result) _canvas.SelectByIds([result.Id]); });
        });
    }

    private void RelationshipEditor(Draft draft)
    {
        var edge = (DiagramEdge)draft.Owner;
        var relation = edge.ClassRelationship!;
        Heading("Relationship");
        Text(NodeName(edge.From) + " → " + NodeName(edge.To));
        var lollipop = relation.FromEnd == ClassRelationEnd.Lollipop || relation.ToEnd == ClassRelationEnd.Lollipop;
        ComboBox? sourceEnd = null;
        ComboBox? targetEnd = null;
        CheckBox? dashedLine = null;
        if (lollipop) Text("Interface connection: the occurrence belongs to this class.");
        else
        {
            var currentFrom = draft.Get("fromEnd", relation.FromEnd);
            var currentTo = draft.Get("toEnd", relation.ToEnd);
            var currentDashed = draft.Get("dashed", relation.Dashed);
            var presets = RelationshipTypes.ToList();
            var selected = presets.FirstOrDefault(p => p.From == currentFrom && p.To == currentTo && p.Dashed == currentDashed);
            selected ??= new("Custom / reversed ends", currentFrom, currentTo, currentDashed);
            if (!presets.Contains(selected)) presets.Add(selected);
            var box = new ComboBox { ItemsSource = presets, SelectedItem = selected };
            Field("Relationship type", box);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not RelationshipPreset preset) return;
                Changed(draft, "fromEnd", preset.From, commitOnChange: true); Changed(draft, "toEnd", preset.To, commitOnChange: true); Changed(draft, "dashed", preset.Dashed, commitOnChange: true);
                if (sourceEnd is not null) sourceEnd.SelectedItem = preset.From;
                if (targetEnd is not null) targetEnd.SelectedItem = preset.To;
                if (dashedLine is not null) dashedLine.IsChecked = preset.Dashed;
            };
        }
        Input(draft, "label", "Relationship label", edge.Label ?? "");
        Input(draft, "fromMultiplicity", $"Multiplicity at {NodeName(edge.From)}", relation.FromMultiplicity ?? "");
        Input(draft, "toMultiplicity", $"Multiplicity at {NodeName(edge.To)}", relation.ToMultiplicity ?? "");
        if (!lollipop)
        {
            var ends = new StackPanel { Spacing = 8 };
            var from = new ComboBox { ItemsSource = Ends, SelectedItem = draft.Get("fromEnd", relation.FromEnd) };
            var to = new ComboBox { ItemsSource = Ends, SelectedItem = draft.Get("toEnd", relation.ToEnd) };
            var dashed = new CheckBox { Content = "Dashed line", IsChecked = draft.Get("dashed", relation.Dashed) };
            sourceEnd = from; targetEnd = to; dashedLine = dashed;
            FieldIn(ends, "Source end", from); FieldIn(ends, "Target end", to); ends.Children.Add(dashed);
            ends.Children.Add(new TextBlock { Text = "These endpoint choices override the preset.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            from.SelectionChanged += (_, _) => { if (from.SelectedItem is ClassRelationEnd value) Changed(draft, "fromEnd", value, commitOnChange: true); };
            to.SelectionChanged += (_, _) => { if (to.SelectedItem is ClassRelationEnd value) Changed(draft, "toEnd", value, commitOnChange: true); };
            dashed.IsCheckedChanged += (_, _) => Changed(draft, "dashed", dashed.IsChecked == true, commitOnChange: true);
            Children.Add(new Expander { Header = "Advanced ends", Content = ends, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        Footer("Apply relationship", () => Apply(RelationshipChange(draft, edge), edge));
        if (!lollipop)
        {
            Button("Reverse direction", () =>
            {
                var reconnect = new ReconnectClassRelationshipCommand(edge, edge.To, edge.From);
                Apply(new BatchCommand("Reverse relationship", RelationshipChange(draft, edge), reconnect), edge,
                    after: () => _canvas.SelectEdge(reconnect.Result));
            });
            ActionMenu("Reconnect…", [
                ("Change source…", () => BeginReconnect(true)),
                ("Change target…", () => BeginReconnect(false)),
            ]);
        }
        Button("Delete relationship", () => Apply(new DeleteClassRelationshipCommand(edge), edge));
        void BeginReconnect(bool source)
        {
            _active = null;
            _canvas.BeginClassReconnect(edge, source);
        }
    }

    private SetClassRelationshipCommand RelationshipChange(Draft draft, DiagramEdge edge)
    {
        var label = Optional(draft, "label");
        if (label?.IndexOfAny(['\r', '\n', ';']) >= 0)
            throw new ArgumentException("Use a single-line relationship label without statement separators.");
        var before = edge.ClassRelationship!;
        var relation = new ClassRelationship(draft.Get("fromEnd", before.FromEnd), draft.Get("toEnd", before.ToEnd),
            draft.Get("dashed", before.Dashed), Optional(draft, "fromMultiplicity"), Optional(draft, "toMultiplicity"));
        return new(edge, relation, label);
    }

    private void NoteEditor(Draft draft)
    {
        var existing = draft.Task == TaskKind.Note ? (DiagramNode)draft.Owner : null;
        var suggestedTarget = draft.Task == TaskKind.CreateNote && draft.Owner is DiagramNode owner ? owner.Id : "";
        Heading(existing is null ? "New diagram note" : "Edit note");
        Input(draft, "text", "Note text", existing?.Display ?? "", multiline: true);
        ChoiceInput(draft, "target", "Attached class", new[] { new Choice("", "(general note)") }
            .Concat(Model().Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Class).Select(n => new Choice(n.Id, n.Display))),
            existing?.Class?.NoteFor ?? suggestedTarget);
        Footer(existing is null ? "Create note" : "Apply note", () =>
        {
            var text = draft.Get("text", "");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter note text.");
            if (existing is null)
            {
                var note = ClassEditing.CreateNote("__note_" + Guid.NewGuid().ToString("N"), text, Optional(draft, "target"));
                Place(note);
                Apply(new AddClassElementCommand(note), draft.Owner, note.Id);
            }
            else
            {
                var definition = existing.Class!.Clone();
                definition.NoteFor = Optional(draft, "target");
                Apply(new BatchCommand("Edit diagram note", new RenameNodeCommand(existing, text),
                    new SetClassDefinitionCommand(existing, definition)), existing);
            }
        });
    }

    private void NamespaceEditor(Draft draft)
    {
        Heading("Namespaces");
        if (draft.Owner is not DiagramSubgraph && !draft.Get("create", false))
        {
            Text("Select a namespace to edit it, or add a new one.");
            foreach (var group in Model().Subgraphs)
                Button(group.Title ?? group.Id, () => Open(new(TaskKind.Namespaces, group)));
            Button("Add namespace", () =>
            {
                draft.Values["create"] = true;
                RebuildEditor(draft);
            });
            Button("Back to selection", CancelEditor);
            return;
        }
        var existing = draft.Owner as DiagramSubgraph;
        Input(draft, "id", "Qualified namespace identity", existing?.Id ?? "");
        Input(draft, "title", "Namespace display name", existing?.Title ?? "");
        ChoiceInput(draft, "parent", "Parent / deletion destination", NamespaceChoices(), existing?.ParentId ?? "");
        Footer("Apply namespace", () =>
        {
            var parent = Optional(draft, "parent");
            if (existing is null)
                Apply(new AddClassNamespaceCommand(new() { Id = Value(draft, "id"), Title = Optional(draft, "title"), ParentId = parent }), draft.Owner);
            else
            {
                var command = new SetClassNamespaceDefinitionCommand(existing, Value(draft, "id"), Optional(draft, "title"), parent);
                Apply(command, draft.Owner, after: () => _canvas.SelectGroup(command.Result));
            }
        });
        if (existing is not null)
            Button("Remove namespace, keep classes", () => Apply(new DeleteClassNamespaceCommand(
                existing, Optional(draft, "parent")), draft.Owner));
    }

    private IEnumerable<Choice> NamespaceChoices() => new[] { new Choice("", "(root)") }
        .Concat(Model().Subgraphs.Select(g => new Choice(g.Id, g.Title ?? g.Id)));
    private string NodeName(string id) => Model().Node(id)?.Display ?? id;
    private void RebuildEditor(Draft draft)
    {
        if (!ReferenceEquals(_active, draft)) return;
        StartPanel(); draft.Fields.Clear(); BuildEditor(draft); OrganizeInlineFields(draft); CompactPrimaryField(draft); FinishPanel();
    }
    private static void FieldIn(StackPanel panel, string name, Control control)
    {
        var label = new TextBlock { Text = name };
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetLabeledBy(control, label);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(label); panel.Children.Add(control);
    }
}
