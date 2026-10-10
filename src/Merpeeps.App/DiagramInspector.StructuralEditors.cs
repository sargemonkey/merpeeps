using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    public void EditStructuralSelection()
    {
        if (_canvas.SelectedStructuralTarget is not { } target) return;
        if (_canvas.IsWorkflowDiagram)
        {
            var task = WorkflowTask(target);
            if (task is not null) Open(new(task.Value, target)); return;
        }
        if (_canvas.IsOwnershipDiagram)
        {
            var task = OwnershipTask(target);
            if (task is not null) Open(new(task.Value, target));
            return;
        }
        if (_canvas.IsSetGrammarDiagram)
        {
            var task = SetGrammarTask(target);
            if (task is not null) Open(new(task.Value, target));
            return;
        }
        if (_canvas.IsStrategyDiagram)
        {
            var task = StrategyTask(target);
            if (task is not null) Open(new(task.Value, target));
            return;
        }
        if (Model().C4 is not null || Model().Gantt is not null)
        {
            Open(new(Model().C4 is not null ? target.Role == "C4Relationship" ? TaskKind.C4Relationship : TaskKind.C4Element
                : target.Role == "GanttSection" ? TaskKind.GanttSection : TaskKind.GanttTask, target));
            return;
        }
        if (Model().Blocks is not null || Model().Architecture is not null)
        {
            Open(new(Model().Blocks is not null ? target.Role == "BlockConnection" ? TaskKind.BlockLink : TaskKind.BlockItem
                : target.Role == "ArchitectureConnection" ? TaskKind.ArchitectureLink : target.Role == "ArchitectureGroup" ? TaskKind.ArchitectureGroup : TaskKind.ArchitectureNode, target));
            return;
        }
        if (Model().Sankey is not null || Model().Treemap is not null)
        {
            Open(new(Model().Treemap is not null ? TaskKind.TreemapNode : target.Role == "FlowNode" ? TaskKind.SankeyNode : TaskKind.SankeyFlow, target));
            return;
        }
        Open(new(Model().Requirements is not null ? target.Role == "Relationship" ? TaskKind.RequirementRelation : TaskKind.CreateRequirement : TaskKind.GitOperation, target));
    }
    private void ShowStructuralSelection()
    {
        if (ShowWorkflowSelection()) return;
        if (ShowOwnershipSelection()) return;
        if (ShowSetGrammarSelection()) return;
        if (ShowStrategySelection()) return;
        if (ShowC4GanttSelection()) return;
        if (ShowGridSelection()) return;
        if (ShowWeightedSelection()) return;
        if (_canvas.RequirementConnectionSource is { } source)
        {
            Heading("Connect requirement");
            Text("From: " + Model().Requirements!.Items.Single(i => i.Id == source).Name);
            Text("Click the destination requirement or element. Escape cancels.");
            Button("Cancel relationship", _canvas.CancelRequirementConnection); return;
        }
        if (Model().Requirements is { } requirements)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "Relationship" } relationship) { Open(new(TaskKind.RequirementRelation, relationship), false); return; }
            if (_canvas.SelectedStructuralTarget is { Role: "Requirement" } node)
            {
                var item = requirements.Items.Single(i => i.Id == node.Id);
                Open(new(TaskKind.CreateRequirement, node), false);
                Button("Add relationship", () => _canvas.BeginRequirementConnection(item.Id));
                Button("Element appearance…", () => Open(new(TaskKind.RequirementStyle, node)));
                Button("Delete element and relationships", () => ApplyRequirement(RequirementEditing.Remove(item.Id), node)); return;
            }
            Heading("Requirement diagram"); Button("Add requirement or element", AddClass);
            foreach (var item in requirements.Items) Button(item.Name, () => _canvas.SelectStructural(new("Requirement", item.Id)));
        }
        else if (Model().GitDiagram is { } git)
        {
            if (_canvas.SelectedStructuralTarget is { Role: "GitCommit" } selected) { Open(new(TaskKind.GitOperation, selected), false); return; }
            Heading("GitGraph operations"); Text("Diagram operations only. Repository branches and commits are never changed.");
            Text("Current diagram branch: " + git.Replay().CurrentBranch);
            Button("Add operation", AddClass);
            foreach (var op in git.Operations) Button(op.Kind + " " + (op.Name ?? op.CommitId ?? ""), () => Open(new(TaskKind.GitOperation, new StructuralSelection("GitCommit", op.Id))));
        }
        Button("Diagram options…", DisplayOptions);
    }
    private bool BuildStructuralEditor(Draft draft)
    {
        if (BuildWorkflowEditor(draft)) return true;
        if (BuildOwnershipEditor(draft)) return true;
        if (BuildSetGrammarEditor(draft)) return true;
        if (BuildStrategyEditor(draft)) return true;
        if (BuildC4GanttEditor(draft)) return true;
        if (BuildGridEditor(draft)) return true;
        if (BuildWeightedEditor(draft)) return true;
        switch (draft.Task)
        {
            case TaskKind.CreateRequirement: RequirementEditor(draft); return true;
            case TaskKind.RequirementRelation: RequirementRelationEditor(draft); return true;
            case TaskKind.RequirementStyle:
                var item = Remember(draft, () => Model().Requirements!.Items.Single(i => i.Id == ((StructuralSelection)draft.Owner).Id));
                Heading("Element appearance");
                foreach (var key in new[] { "fill", "stroke", "color", "stroke-width", "font-size", "font-family", "font-weight", "font-style" })
                    StyleInput(draft, key, item.Style[key]?.ToString() ?? "");
                Footer("Apply appearance", () =>
                {
                    var style = (JsonObject)item.Style.DeepClone();
                    foreach (var key in new[] { "fill", "stroke", "color", "stroke-width", "font-size", "font-family", "font-weight", "font-style" })
                        if (Optional(draft, key) is { } value) style[key] = value; else style.Remove(key);
                    ApplyRequirement(RequirementEditing.Update(item with { Style = style }), draft.Owner);
                });
                return true;
            case TaskKind.RequirementOptions:
                Heading("Requirement options"); EnumInput(draft, "direction", "Direction", Model().Requirements!.Direction);
                Footer("Apply direction", () => ApplyRequirement(RequirementEditing.SetDirection(draft.Get("direction", Direction.TB)), draft.Owner)); return true;
            case TaskKind.GitOperation: GitOperationEditor(draft); return true;
            case TaskKind.GitOptions: GitOptionsEditor(draft); return true;
            case TaskKind.StructuralItems: ShowStructuralSelection(); Button("Back to selection", CancelEditor); return true;
            default: return false;
        }
    }
    private void RequirementEditor(Draft draft)
    {
        var document = Model().Requirements!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.Items.Single(i => i.Id == selected.Id)) : null;
        Heading("Requirement or element");
        Input(draft, "name", "Name", original?.Name ?? "");
        var kindPicker = EnumInput(draft, "kind", "Requirement kind", original?.Kind ?? RequirementKind.Requirement,
            original is null ? Enum.GetValues<RequirementKind>() : original.Kind == RequirementKind.Element ? new[] { RequirementKind.Element } : Enum.GetValues<RequirementKind>().Where(k => k != RequirementKind.Element));
        kindPicker.SelectionChanged += (_, _) => RebuildEditor(draft);
        if (draft.Get("kind", original?.Kind ?? RequirementKind.Requirement) == RequirementKind.Element)
        {
            Input(draft, "type", "Element type", original?.ElementType ?? "");
            Input(draft, "docref", "Element document reference", original?.DocumentReference ?? "");
        }
        else
        {
            Input(draft, "displayId", "Displayed requirement ID", original?.DisplayId ?? "");
            Input(draft, "text", "Requirement text", original?.Text ?? "", multiline: true);
            EnumInput(draft, "risk", "Risk", original?.Risk ?? RequirementRisk.Unspecified);
            EnumInput(draft, "verify", "Verification", original?.Verification ?? RequirementVerification.Unspecified);
        }
        Input(draft, "classes", "Style classes", string.Join(",", original?.Classes ?? []));
        Footer("Apply requirement / element", () =>
        {
            var item = new RequirementItem(original?.Id ?? "r" + Guid.NewGuid().ToString("N"), Value(draft, "name"), draft.Get("kind", RequirementKind.Requirement))
            {
                DisplayId = Value(draft, "displayId"), Text = draft.Get("text", ""), Risk = draft.Get("risk", RequirementRisk.Unspecified),
                Verification = draft.Get("verify", RequirementVerification.Unspecified), ElementType = Value(draft, "type"), DocumentReference = Value(draft, "docref"),
                Classes = Value(draft, "classes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                Style = original is null ? new() : (JsonObject)original.Style.DeepClone(),
            };
            ApplyRequirement(original is null ? RequirementEditing.Add(item) : RequirementEditing.Update(item), draft.Owner);
        });
    }
    private void RequirementRelationEditor(Draft draft)
    {
        var document = Model().Requirements!;
        var original = Remember(draft, () => document.Relations.Single(r => r.Id == ((StructuralSelection)draft.Owner).Id));
        Heading("Requirement relationship");
        ChoiceInput(draft, "from", "Source element", document.Items.Select(i => new Choice(i.Id, i.Name)), original.From);
        ChoiceInput(draft, "to", "Destination element", document.Items.Select(i => new Choice(i.Id, i.Name)), original.To);
        EnumInput(draft, "kind", "Relationship kind", original.Kind);
        Footer("Apply relationship", () => ApplyRequirement(RequirementEditing.UpdateRelation(original with { From = Value(draft, "from"), To = Value(draft, "to"), Kind = draft.Get("kind", original.Kind) }), draft.Owner));
        Button("Delete relationship", () => ApplyRequirement(RequirementEditing.RemoveRelation(original.Id), draft.Owner));
    }
    private void GitOperationEditor(Draft draft)
    {
        var document = Model().GitDiagram!;
        var original = draft.Owner is StructuralSelection selected ? Remember(draft, () => document.Operations.Single(o => o.Id == selected.Id)) : null;
        Heading(original is null ? "Add diagram operation" : "Edit diagram operation");
        var kindPicker = EnumInput(draft, "kind", "Operation", original?.Kind ?? GitDiagramOperationKind.Commit);
        kindPicker.SelectionChanged += (_, _) => RebuildEditor(draft);
        var kind = draft.Get("kind", original?.Kind ?? GitDiagramOperationKind.Commit);
        if (kind != GitDiagramOperationKind.Commit) Input(draft, "name", "Branch / cherry-pick source", original?.Name ?? "");
        if (kind is GitDiagramOperationKind.Commit or GitDiagramOperationKind.Merge)
        {
            Input(draft, "commitId", "Custom commit ID", original?.CommitId ?? "");
            EnumInput(draft, "commitKind", "Commit appearance", original?.CommitKind ?? GitDiagramCommitKind.Normal);
        }
        if (kind is GitDiagramOperationKind.Commit or GitDiagramOperationKind.Merge or GitDiagramOperationKind.CherryPick)
            Input(draft, "tags", "Tags (one per line)", string.Join("\n", original?.Tags ?? []), multiline: true);
        if (kind == GitDiagramOperationKind.Branch) Input(draft, "order", "Branch order (optional)", original?.Order?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (kind == GitDiagramOperationKind.CherryPick) Input(draft, "parent", "Cherry-pick merge parent", original?.ParentCommitId ?? "");
        Footer("Apply diagram operation", () =>
        {
            var operation = new GitDiagramOperation(original?.Id ?? "g" + Guid.NewGuid().ToString("N"), draft.Get("kind", GitDiagramOperationKind.Commit))
            {
                Name = kind == GitDiagramOperationKind.Commit ? null : Optional(draft, "name"),
                CommitId = kind is GitDiagramOperationKind.Commit or GitDiagramOperationKind.Merge ? Optional(draft, "commitId") : null,
                CommitKind = kind is GitDiagramOperationKind.Commit or GitDiagramOperationKind.Merge ? draft.Get("commitKind", GitDiagramCommitKind.Normal) : GitDiagramCommitKind.Normal,
                Tags = kind is GitDiagramOperationKind.Branch or GitDiagramOperationKind.Checkout ? [] : draft.Get("tags", "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries),
                Order = kind == GitDiagramOperationKind.Branch && Optional(draft, "order") is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null,
                ParentCommitId = kind == GitDiagramOperationKind.CherryPick ? Optional(draft, "parent") : null,
            };
            ApplyGit(original is null ? GitDiagramEditing.Insert(operation) : GitDiagramEditing.Update(operation), draft.Owner);
        });
        if (original is not null)
        {
            var index = document.Operations.FindIndex(o => o.Id == original.Id);
            Button("Move operation earlier", () => ApplyGit(GitDiagramEditing.Move(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move operation later", () => ApplyGit(GitDiagramEditing.Move(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < document.Operations.Count;
            Button("Delete operation", () => ApplyGit(GitDiagramEditing.Remove(original.Id), draft.Owner));
        }
    }
    private void GitOptionsEditor(Draft draft)
    {
        var document = Model().GitDiagram!;
        Heading("GitGraph display");
        EnumInput(draft, "direction", "Direction", document.Direction);
        foreach (var key in new[] { "showBranches", "showCommitLabel", "rotateCommitLabel", "parallelCommits" })
            Check(draft, key, key, document.Configuration[key]?.GetValue<bool>() ?? key != "parallelCommits");
        Input(draft, "main", "Main branch name", document.Configuration["mainBranchName"]?.GetValue<string>() ?? "main");
        var themeKeys = Enumerable.Range(0, 8).SelectMany(i => new[] { "git" + i, "gitInv" + i, "gitBranchLabel" + i })
            .Concat(new[] { "commitLabelColor", "commitLabelBackground", "commitLabelFontSize", "tagLabelColor", "tagLabelBackground", "tagLabelBorder", "tagLabelFontSize" });
        ThemeValueInput(draft, themeKeys.ToArray(), "Theme value (blank keeps current)");
        Footer("Apply GitGraph display", () =>
        {
            var commands = new List<IEditCommand> { GitDiagramEditing.SetDirection(draft.Get("direction", GitDiagramDirection.LR)),
                GitDiagramEditing.SetOption("mainBranchName", JsonValue.Create(Value(draft, "main"))) };
            commands.AddRange(new[] { "showBranches", "showCommitLabel", "rotateCommitLabel", "parallelCommits" }.Select(key => GitDiagramEditing.SetOption(key, JsonValue.Create(draft.Get(key, key != "parallelCommits")))));
            if (Optional(draft, "themeValue") is { } value) commands.Add(GitDiagramEditing.SetThemeVariable(Value(draft, "themeKey"), JsonValue.Create(value)));
            Apply(new BatchCommand("Edit GitGraph options", commands.ToArray()), draft.Owner, after: () => _canvas.SelectStructural(null));
        });
    }
    private void ApplyRequirement(RequirementEditCommand command, object owner) => Apply(command, owner, after: () =>
        _canvas.SelectStructural(command.ResultId is { } id ? new(Model().Requirements!.Relations.Any(r => r.Id == id) ? "Relationship" : "Requirement", id) : null));
    private void ApplyGit(GitDiagramEditCommand command, object owner) => Apply(command, owner, after: () =>
        _canvas.SelectStructural(command.ResultId is { } id ? new("GitCommit", id) : null));
}
