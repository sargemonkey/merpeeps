using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private SequenceDiagramModel Sequence => Model().Sequence ?? throw new InvalidOperationException("Open a sequence diagram.");
    private static string? SequenceId(Draft draft) => (draft.Owner as SequenceSelection)?.Id;
    public void EditSequenceSelection()
    {
        if (_canvas.SelectedSequenceElement is { Kind: SequenceElementKind.Participant } selection)
            Open(new(TaskKind.SequenceParticipant, selection) { Values = { ["$aliasOnly"] = true } });
        else if (_canvas.SelectedSequenceElement is { } item)
        {
            var statement = SequenceDiagramModel.Walk(Sequence.Statements).FirstOrDefault(s => s.Id == item.Id);
            var task = statement switch
            {
                SequenceMessage => TaskKind.SequenceMessage, SequenceNote => TaskKind.SequenceNote,
                SequenceBlock => TaskKind.SequenceBlock, SequenceActivation => TaskKind.SequenceActivation,
                _ => item.Kind == SequenceElementKind.ParticipantBox ? TaskKind.SequenceBox : TaskKind.SequenceBranch,
            };
            Open(new(task, item));
        }
    }

    private void ShowSequenceConnection()
    {
        StartPanel();
        Heading("Add message");
        Text("From: " + Sequence.Participant(_canvas.SequenceMessageSource!).Display);
        Text("Click the receiving participant or its lifeline. The message is added at the end; Escape cancels.");
        var picker = new ComboBox { ItemsSource = Sequence.Participants.Select(p => new Choice(p.Id, p.Display)).ToArray() };
        Field("Receiving participant", picker);
        Button("Add message to selected participant", () =>
        {
            if (picker.SelectedItem is not Choice target) throw new ArgumentException("Choose a participant.");
            var command = SequenceEditing.Insert(new SequenceMessage(SequenceEditing.NewId(), _canvas.SequenceMessageSource!, target.Id, ""));
            _canvas.CancelSequenceMessage();
            ApplySequence(command, Model());
        });
        Button("Cancel message", _canvas.CancelSequenceMessage);
        if (_canvas.SequenceActionError is { } error) Text(error);
        FinishPanel();
    }

    private void ShowSequenceSelection()
    {
        if (_canvas.SelectedSequenceElement is not { } selection)
        {
            Heading("Sequence diagram");
            Text("Add participants, then select one to send a message. Timeline order determines the interaction.");
            Button("Add participant", AddClass);
            Button("Timeline…", NavigateElements);
            Button("Add fragment…", () => Open(new(TaskKind.SequenceBlock, Model())));
            foreach (var draft in _drafts.Values.Where(d => d.Dirty).ToArray())
                Button("Resume unapplied sequence edit", () => Open(draft));
            return;
        }
        if (Sequence.Participants.FirstOrDefault(p => p.Id == selection.Id) is { } participant)
        {
            Open(new(TaskKind.SequenceParticipant, selection), false);
            Button("Add message", () => _canvas.BeginSequenceMessage(participant.Id));
            Button("Add note…", () => Open(new(TaskKind.SequenceNote, Model()) { Values = { ["participant"] = participant.Id } }));
            ActionMenu("More participant actions", [
                ("Create participant by message…", () => Open(new(TaskKind.SequenceCreation, Model()) { Values = { ["from"] = participant.Id } })),
                ("Move lane left", () => ApplySequence(SequenceEditing.MoveParticipant(participant.Id, Sequence.Participants.FindIndex(p => p.Id == participant.Id) - 1), selection)),
                ("Move lane right", () => ApplySequence(SequenceEditing.MoveParticipant(participant.Id, Sequence.Participants.FindIndex(p => p.Id == participant.Id) + 1), selection)),
                ("Participant links…", () => Open(new(TaskKind.SequenceLinks, selection))),
                ("Delete participant and interactions", () => ApplySequence(SequenceEditing.RemoveParticipant(participant.Id), selection)),
            ]);
            return;
        }
        if (Sequence.Boxes.Any(b => b.Id == selection.Id))
        {
            Open(new(TaskKind.SequenceBox, selection), false);
            return;
        }
        var statement = SequenceDiagramModel.Walk(Sequence.Statements).FirstOrDefault(s => s.Id == selection.Id);
        if (statement is SequenceBlock block)
        {
            Open(new(TaskKind.SequenceBlock, selection), false);
            foreach (var branch in block.Branches)
                Button(branch == block.Branches[0] ? "First branch" : branch.Text.Length == 0 ? "Unlabelled branch" : branch.Text,
                    () => _canvas.SelectSequenceId(branch.Id));
            if (block.Kind is SequenceBlockKind.Alt or SequenceBlockKind.Par or SequenceBlockKind.ParOver or SequenceBlockKind.Critical)
                Button("Add branch", () => ApplySequence(SequenceEditing.AddBranch(block.Id, ""), selection));
            OrderingActions(selection, block);
            return;
        }
        if (statement is not null)
        {
            var task = statement switch
            {
                SequenceMessage => TaskKind.SequenceMessage, SequenceNote => TaskKind.SequenceNote,
                SequenceActivation => TaskKind.SequenceActivation, SequenceNumbering => TaskKind.SequenceNumbering,
                _ => TaskKind.SequenceTimeline,
            };
            Open(new(task, selection), false);
            return;
        }
        var owner = SequenceDiagramModel.Walk(Sequence.Statements).OfType<SequenceBlock>().FirstOrDefault(b => b.Branches.Any(branch => branch.Id == selection.Id));
        if (owner is not null)
        {
            var branch = owner.Branches.Single(b => b.Id == selection.Id);
            Open(new(TaskKind.SequenceBranch, selection), false);
            Button("Add message…", () => NewAt(TaskKind.SequenceMessage, branch.Id));
            Button("Add note…", () => NewAt(TaskKind.SequenceNote, branch.Id));
            Button("Add activation…", () => NewAt(TaskKind.SequenceActivation, branch.Id));
            Button("Add nested fragment…", () => NewAt(TaskKind.SequenceBlock, branch.Id));
            foreach (var child in branch.Statements.Where(IsTimelineItem))
                Button(StatementLabel(child), () => _canvas.SelectSequenceId(child.Id));
            Button("Delete branch", () => ApplySequence(SequenceEditing.RemoveBranch(branch.Id), selection)).IsEnabled = owner.Branches.Count > 1;
        }
    }

    private void NewAt(TaskKind kind, string? branchId = null, int? index = null)
    {
        var draft = new Draft(kind, Model());
        draft.Values["$branch"] = branchId ?? "";
        if (index is { } value) draft.Values["$index"] = value;
        Open(draft);
    }

    private bool BuildSequenceEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.SequenceParticipant: ParticipantEditor(draft); return true;
            case TaskKind.SequenceMessage: MessageEditor(draft); return true;
            case TaskKind.SequenceCreation: CreationEditor(draft); return true;
            case TaskKind.SequenceNote: SequenceNoteEditor(draft); return true;
            case TaskKind.SequenceActivation: ActivationEditor(draft); return true;
            case TaskKind.SequenceBlock: FragmentEditor(draft); return true;
            case TaskKind.SequenceBranch: BranchEditor(draft); return true;
            case TaskKind.SequenceBox: BoxEditor(draft); return true;
            case TaskKind.SequenceNumbering: NumberingEditor(draft); return true;
            case TaskKind.SequenceOptions: SequenceOptionsEditor(draft); return true;
            case TaskKind.SequenceLinks: LinksEditor(draft); return true;
            case TaskKind.SequenceTimeline:
                Heading("Interaction timeline");
                Button("Add message…", () => NewAt(TaskKind.SequenceMessage));
                Button("Add note…", () => NewAt(TaskKind.SequenceNote));
                Button("Add activation…", () => NewAt(TaskKind.SequenceActivation));
                Button("Add fragment…", () => NewAt(TaskKind.SequenceBlock));
                Button("Number messages…", () => NewAt(TaskKind.SequenceNumbering));
                TimelineRows(Sequence.Statements, 0);
                Button("Back to selection", CancelEditor);
                return true;
            default: return false;
        }
    }

    private void ParticipantEditor(Draft draft)
    {
        var existing = SequenceId(draft) is { } id ? Remember(draft, () => Sequence.Participant(id)) : null;
        if (existing is not null && draft.Get("$aliasOnly", false))
        {
            Heading("Rename participant");
            Input(draft, "alias", "Participant label", existing.Display, multiline: true);
            Footer("Apply participant name", () => ApplySequence(SequenceEditing.SetParticipantAlias(existing.Id, EmptyToNull(draft.Get("alias", ""))), draft.Owner));
            return;
        }
        Heading(existing is null ? "New participant" : "Edit participant");
        Input(draft, "id", "Participant identity", existing?.Id ?? "");
        Input(draft, "alias", "Participant label", existing?.Alias ?? "", multiline: true);
        EnumInput(draft, "kind", "Declaration", existing?.Kind ?? SequenceParticipantKind.Participant);
        EnumInput(draft, "shape", "Appearance", existing?.Shape ?? SequenceParticipantShape.Default);
        ChoiceInput(draft, "box", "Participant box", new[] { new Choice("", "(none)") }.Concat(Sequence.Boxes.Select(b => new Choice(b.Id, b.Title))), existing?.BoxId ?? "");
        Footer("Apply participant", () =>
        {
            var next = new SequenceParticipant(Value(draft, "id"), draft.Get("kind", SequenceParticipantKind.Participant),
                draft.Get("shape", SequenceParticipantShape.Default), EmptyToNull(draft.Get("alias", "")), Optional(draft, "box"))
            {
                Configuration = existing is null ? new() : (JsonObject)existing.Configuration.DeepClone(),
                AliasIsExternal = true,
            };
            next.Configuration.Remove("alias");
            ApplySequence(existing is null ? SequenceEditing.AddParticipant(next) : SequenceEditing.UpdateParticipant(existing.Id, next), draft.Owner);
        });
    }

    private void MessageEditor(Draft draft)
    {
        var original = SequenceId(draft) is { } id ? Remember(draft, () => (SequenceMessage)Sequence.Statement(id)) : null;
        Heading(original is null ? "New message" : "Edit message");
        ParticipantChoice(draft, "from", "From participant", original?.From);
        ParticipantChoice(draft, "to", "To participant", original?.To);
        ChoiceInput(draft, "arrow", "Message style", SequenceArrows.All.Select(a => new Choice(a.Id, a.DisplayName)), original?.ArrowId ?? "arrow");
        Input(draft, "text", "Message text", original?.Text ?? "", multiline: true);
        var advancedStart = Children.Count;
        Check(draft, "activate", "Activate recipient after message", original?.ActivateTarget ?? false);
        Check(draft, "deactivate", "Deactivate sender after message", original?.DeactivateSource ?? false);
        Check(draft, "centralFrom", "Central connection at source", original?.CentralSource ?? false);
        Check(draft, "centralTo", "Central connection at target", original?.CentralTarget ?? false);
        var choices = new[] { new Choice("", "(no destruction)") }.Concat(Sequence.Participants.Select(p => new Choice(p.Id, p.Display)));
        ChoiceInput(draft, "destroy", "Destroyed participant", choices, original?.DestroyParticipantId ?? "");
        if (original?.CreateParticipantId is { } created) Text("This message creates " + Sequence.Participant(created).Display + ".");
        var advanced = new StackPanel { Spacing = 8 };
        foreach (var control in Children.Skip(advancedStart).ToArray())
        {
            Children.Remove(control);
            advanced.Children.Add(control);
        }
        var advancedOpen = draft.Get("activate", false) || draft.Get("deactivate", false) ||
            draft.Get("centralFrom", false) || draft.Get("centralTo", false) ||
            original?.CreateParticipantId is not null || Optional(draft, "destroy") is not null;
        Children.Add(new Expander
        {
            Header = "Activation and lifetime", Content = advanced,
            IsExpanded = advancedOpen, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        });
        Footer("Apply message", () =>
        {
            var message = new SequenceMessage(original?.Id ?? SequenceEditing.NewId(), Value(draft, "from"), Value(draft, "to"),
                draft.Get("text", ""), Value(draft, "arrow"), draft.Get("activate", false), draft.Get("deactivate", false),
                draft.Get("centralFrom", false), draft.Get("centralTo", false), original?.CreateParticipantId, Optional(draft, "destroy"));
            ApplySequence(original is null ? InsertAt(draft, message) : SequenceEditing.Update(original.Id, message), draft.Owner);
        });
        if (original is not null) OrderingActions((SequenceSelection)draft.Owner, original);
    }

    private void CreationEditor(Draft draft)
    {
        Heading("Create participant by message");
        ParticipantChoice(draft, "from", "Creating sender");
        Input(draft, "id", "New participant identity");
        Input(draft, "alias", "New participant label");
        EnumInput(draft, "kind", "Declaration", SequenceParticipantKind.Participant);
        EnumInput(draft, "shape", "Appearance", SequenceParticipantShape.Default);
        Input(draft, "text", "Creation message", multiline: true);
        Footer("Create participant and message", () =>
        {
            var participant = new SequenceParticipant(Value(draft, "id"), draft.Get("kind", SequenceParticipantKind.Participant),
                draft.Get("shape", SequenceParticipantShape.Default), Optional(draft, "alias"));
            var message = new SequenceMessage(SequenceEditing.NewId(), Value(draft, "from"), participant.Id, draft.Get("text", ""));
            ApplySequence(SequenceEditing.CreateParticipantOnMessage(participant, message), draft.Owner);
        });
    }

    private void SequenceNoteEditor(Draft draft)
    {
        var original = SequenceId(draft) is { } id ? Remember(draft, () => (SequenceNote)Sequence.Statement(id)) : null;
        Heading(original is null ? "New sequence note" : "Edit note");
        ParticipantChoice(draft, "participant", "Note participant", original?.ParticipantId);
        ChoiceInput(draft, "second", "Second participant (over only)", new[] { new Choice("", "(none)") }.Concat(Sequence.Participants.Select(p => new Choice(p.Id, p.Display))), original?.SecondParticipantId ?? "");
        EnumInput(draft, "placement", "Note placement", original?.Placement ?? SequenceNotePlacement.Over);
        Input(draft, "text", "Note text", original?.Text ?? "", multiline: true);
        Footer("Apply note", () =>
        {
            var next = new SequenceNote(original?.Id ?? SequenceEditing.NewId(), draft.Get("placement", SequenceNotePlacement.Over),
                Value(draft, "participant"), Optional(draft, "second"), draft.Get("text", ""));
            ApplySequence(original is null ? InsertAt(draft, next) : SequenceEditing.Update(next.Id, next), draft.Owner);
        });
        if (original is not null) OrderingActions((SequenceSelection)draft.Owner, original);
    }

    private void ActivationEditor(Draft draft)
    {
        var original = SequenceId(draft) is { } id ? Remember(draft, () => (SequenceActivation)Sequence.Statement(id)) : null;
        Heading("Activation");
        ParticipantChoice(draft, "participant", "Activation participant", original?.ParticipantId);
        Check(draft, "active", "Start activation (unchecked ends it)", original?.Active ?? true);
        Footer("Apply activation", () =>
        {
            var next = new SequenceActivation(original?.Id ?? SequenceEditing.NewId(), Value(draft, "participant"), draft.Get("active", true));
            ApplySequence(original is null ? InsertAt(draft, next) : SequenceEditing.Update(next.Id, next), draft.Owner);
        });
        if (original is not null) OrderingActions((SequenceSelection)draft.Owner, original);
    }

    private void FragmentEditor(Draft draft)
    {
        var original = SequenceId(draft) is { } id ? Remember(draft, () => (SequenceBlock)Sequence.Statement(id)) : null;
        Heading("Interaction fragment");
        EnumInput(draft, "kind", "Fragment type", original?.Kind ?? SequenceBlockKind.Loop);
        Input(draft, "text", "Condition / description", original?.Text ?? "", multiline: true);
        ColourInput(draft, "color", "Background color (rect)", original?.Color ?? "rgba(180,210,250,0.3)", "rgba(180,210,250,0.3)");
        Footer("Apply fragment", () =>
        {
            var kind = draft.Get("kind", SequenceBlockKind.Loop);
            var next = original is null ? SequenceEditing.NewBlock(kind, draft.Get("text", ""), kind == SequenceBlockKind.Rect ? Value(draft, "color") : null)
                : original with { Kind = kind, Text = draft.Get("text", ""), Color = kind == SequenceBlockKind.Rect ? Value(draft, "color") : null };
            ApplySequence(original is null ? InsertAt(draft, next) : SequenceEditing.Update(next.Id, next), draft.Owner);
        });
    }

    private void BranchEditor(Draft draft)
    {
        var branch = Remember(draft, () => SequenceEditing.LocateBranch(Sequence, SequenceId(draft)!));
        var block = (SequenceBlock)Sequence.Statement(branch.BlockId);
        Heading("Branch condition");
        Input(draft, "text", "Condition", branch.Index == 0 ? block.Text : branch.Branch.Text, multiline: true);
        Footer("Apply condition", () => ApplySequence(SequenceEditing.SetBranchText(branch.Branch.Id, draft.Get("text", "")), draft.Owner));
        Button("Move branch up", () => ApplySequence(SequenceEditing.MoveBranch(branch.Branch.Id, branch.Index - 1), draft.Owner)).IsEnabled = branch.Index > 0;
        Button("Move branch down", () => ApplySequence(SequenceEditing.MoveBranch(branch.Branch.Id, branch.Index + 1), draft.Owner)).IsEnabled = branch.Index + 1 < block.Branches.Count;
    }

    private void BoxEditor(Draft draft)
    {
        var existing = SequenceId(draft) is { } id ? Remember(draft, () => Sequence.Boxes.Single(b => b.Id == id)) : null;
        if (existing is null && !draft.Get("$create", false))
        {
            Heading("Participant boxes");
            foreach (var box in Sequence.Boxes) Button(box.Title.Length == 0 ? "Untitled box" : box.Title, () => _canvas.SelectSequenceId(box.Id));
            Button("Add participant box", () => { draft.Values["$create"] = true; RebuildEditor(draft); });
            Button("Back to selection", CancelEditor);
            return;
        }
        Heading("Participant box");
        Input(draft, "title", "Box title", existing?.Title ?? "");
        ColourInput(draft, "color", "Box color", existing?.Color ?? "transparent", "transparent");
        Footer("Apply participant box", () =>
        {
            var box = new SequenceParticipantBox(existing?.Id ?? SequenceEditing.NewId(), draft.Get("title", ""), Value(draft, "color"));
            ApplySequence(existing is null ? SequenceEditing.AddBox(box) : SequenceEditing.UpdateBox(box), draft.Owner);
        });
        if (existing is not null)
        {
            ParticipantChoice(draft, "member", "Participant to move into box");
            Button("Assign participant", () => ApplySequence(SequenceEditing.AssignBox(Value(draft, "member"), existing.Id), draft.Owner));
            Button("Remove box, keep participants", () => ApplySequence(SequenceEditing.RemoveBox(existing.Id), draft.Owner));
        }
    }

    private void NumberingEditor(Draft draft)
    {
        var original = SequenceId(draft) is { } id ? Remember(draft, () => (SequenceNumbering)Sequence.Statement(id)) : null;
        Heading("Message numbering");
        Check(draft, "enabled", "Show message numbers", original?.Enabled ?? true);
        Input(draft, "start", "Start value", (original?.Start ?? 1).ToString(CultureInfo.InvariantCulture));
        Input(draft, "step", "Increment", (original?.Step ?? 1).ToString(CultureInfo.InvariantCulture));
        Footer("Apply numbering", () =>
        {
            var next = new SequenceNumbering(original?.Id ?? SequenceEditing.NewId(), draft.Get("enabled", true),
                decimal.Parse(Value(draft, "start"), CultureInfo.InvariantCulture), decimal.Parse(Value(draft, "step"), CultureInfo.InvariantCulture));
            ApplySequence(original is null ? InsertAt(draft, next) : SequenceEditing.Update(next.Id, next), draft.Owner);
        });
    }

    private void SequenceOptionsEditor(Draft draft)
    {
        Heading("Sequence display");
        var original = draft.Get("$options", Sequence.Options);
        Check(draft, "mirror", "Mirror participant headers at bottom", original.MirrorActors);
        Input(draft, "spacing", "Participant spacing (px)", original.ActorMargin.ToString(CultureInfo.InvariantCulture));
        Input(draft, "messages", "Message spacing (px)", original.MessageMargin.ToString(CultureInfo.InvariantCulture));
        ChoiceInput(draft, "alignment", "Multiline note alignment", new[] { new Choice("left", "Left"), new Choice("center", "Center"), new Choice("right", "Right") }, original.NoteAlign);
        foreach (var (name, font) in new[] { ("Participant", original.ActorFont), ("Message", original.MessageFont), ("Note", original.NoteFont) })
        {
            Input(draft, name + "family", name + " font family", font.Family);
            Input(draft, name + "size", name + " font size (px)", font.Size.ToString(CultureInfo.InvariantCulture));
            Input(draft, name + "weight", name + " font weight (1–1000)", font.Weight.ToString(CultureInfo.InvariantCulture));
        }
        Footer("Apply sequence display", () => ApplySequence(SequenceEditing.SetOptions(original with
        {
            MirrorActors = draft.Get("mirror", false), ActorMargin = Margin("spacing"), MessageMargin = Margin("messages"),
            NoteAlign = Value(draft, "alignment"), ActorFont = Font("Participant"), MessageFont = Font("Message"), NoteFont = Font("Note"),
        }), draft.Owner));
        SequenceFont Font(string name) => new(Value(draft, name + "family"), Number(draft, name + "size"), int.Parse(Value(draft, name + "weight"), CultureInfo.InvariantCulture));
        double Margin(string key) => double.TryParse(Value(draft, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0
            ? value : throw new ArgumentException("Enter a finite nonnegative spacing.");
    }

    private void LinksEditor(Draft draft)
    {
        var participant = Sequence.Participant(SequenceId(draft)!);
        Heading("Links: " + participant.Display);
        foreach (var item in SequenceDiagramModel.Walk(Sequence.Statements).OfType<SequenceParticipantMetadata>().Where(m => m.ParticipantId == participant.Id && m.Directive == "links"))
            foreach (var pair in item.Values) Text(pair.Key + ": " + pair.Value?.ToJsonString());
        Text("Links are stored as data. This editor does not execute diagram callbacks.");
        Input(draft, "label", "Link label");
        Input(draft, "url", "Link URL");
        Footer("Add participant link", () =>
        {
            if (Value(draft, "label").Length == 0 || !Uri.TryCreate(Value(draft, "url"), UriKind.Absolute, out _)) throw new ArgumentException("Enter a label and an absolute URL.");
            ApplySequence(SequenceEditing.Insert(new SequenceParticipantMetadata(SequenceEditing.NewId(), participant.Id, "links",
                new JsonObject { [Value(draft, "label")] = Value(draft, "url") })), draft.Owner);
        });
    }

    private void OrderingActions(SequenceSelection selection, SequenceStatement statement)
    {
        var location = SequenceEditing.Locate(Sequence, statement.Id);
        IReadOnlyList<SequenceStatement> list = location.ParentBranchId is null ? Sequence.Statements : SequenceEditing.LocateBranch(Sequence, location.ParentBranchId).Branch.Statements;
        var earlier = list.Select((item, index) => (item, index)).LastOrDefault(pair => pair.index < location.Index && IsTimelineItem(pair.item));
        var later = list.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.index > location.Index && IsTimelineItem(pair.item));
        Button("Move earlier", () => ApplySequence(SequenceEditing.Move(statement.Id, location.ParentBranchId, earlier.index), selection)).IsEnabled = earlier.item is not null;
        Button("Move later", () => ApplySequence(SequenceEditing.Move(statement.Id, location.ParentBranchId, later.index), selection)).IsEnabled = later.item is not null;
        Button("Insert message after…", () => NewAt(TaskKind.SequenceMessage, location.ParentBranchId, location.Index + 1));
        Button("Delete sequence item", () => ApplySequence(SequenceEditing.Remove(statement.Id), selection));
    }

    private void ParticipantChoice(Draft draft, string key, string label, string? initial = null) =>
        ChoiceInput(draft, key, label, Sequence.Participants.Select(p => new Choice(p.Id, p.Display)), initial ?? Sequence.Participants.FirstOrDefault()?.Id ?? "");
    private T Remember<T>(Draft draft, Func<T> find) where T : class
    {
        if (draft.Values.TryGetValue("$original", out var cached) && cached is T value) return value;
        var original = find();
        draft.Values["$original"] = original;
        return original;
    }
    private SequenceEditCommand InsertAt(Draft draft, SequenceStatement statement) =>
        SequenceEditing.Insert(statement, Optional(draft, "$branch"), draft.Values.TryGetValue("$index", out var value) && value is int index ? index : null);
    private void ApplySequence(SequenceEditCommand command, object owner) =>
        Apply(command, owner, after: () => _canvas.SelectSequenceId(command.ResultId));
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string SequenceDiagramAdapterName(SequenceBlockKind kind) => kind.ToString();
    private static bool IsTimelineItem(SequenceStatement statement) => statement is not (SequenceDeclaration or SequenceComment or SequenceParticipantMetadata or SequenceDocumentText);
    private static string StatementLabel(SequenceStatement statement) => statement switch
    {
        SequenceMessage m => $"{m.From} → {m.To}: {m.Text}",
        SequenceNote n => "Note: " + n.Text,
        SequenceActivation a => (a.Active ? "Activate " : "Deactivate ") + a.ParticipantId,
        SequenceBlock b => b.Kind + ": " + b.Text,
        SequenceNumbering n => n.Enabled ? "Autonumber" : "Numbers off",
        _ => statement.GetType().Name,
    };
    private void TimelineRows(IEnumerable<SequenceStatement> statements, int depth)
    {
        foreach (var statement in statements.Where(IsTimelineItem))
        {
            Button(new string(' ', depth * 2) + StatementLabel(statement), () => _canvas.SelectSequenceId(statement.Id));
            if (statement is SequenceBlock block)
                foreach (var branch in block.Branches)
                {
                    Button(new string(' ', (depth + 1) * 2) + (branch == block.Branches[0] ? "First branch" : branch.Text), () => _canvas.SelectSequenceId(branch.Id));
                    TimelineRows(branch.Statements, depth + 2);
                }
        }
    }
}
