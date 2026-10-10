using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public enum SequenceDiagnosticKind { InvalidStructure, NativeUnsupported }
public sealed record SequenceDiagnostic(string Code, string ElementId, string Message,
    SequenceDiagnosticKind Kind = SequenceDiagnosticKind.InvalidStructure);
public sealed record SequenceReadResult(SequenceDiagramModel Model, IReadOnlyList<SequenceDiagnostic> Diagnostics)
{
    public bool CanEdit => Diagnostics.Count == 0;
}
public sealed class SequenceNotEditableException(SequenceReadResult result)
    : NotSupportedException(string.Join("\n", result.Diagnostics.Select(d => d.Message)))
{
    public SequenceReadResult Result { get; } = result;
}

public static class SequenceValidation
{
    public static IReadOnlyList<SequenceDiagnostic> Validate(SequenceDiagramModel model)
    {
        var diagnostics = new List<SequenceDiagnostic>();
        void Error(string code, string id, string message, SequenceDiagnosticKind kind = SequenceDiagnosticKind.InvalidStructure) =>
            diagnostics.Add(new(code, id, message, kind));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var participants = new Dictionary<string, SequenceParticipant>(StringComparer.Ordinal);
        foreach (var participant in model.Participants)
        {
            try { SequenceText.Identifier(participant.Id); }
            catch (ArgumentException error) { Error("participant-id", participant.Id, error.Message); }
            if (!participants.TryAdd(participant.Id, participant)) Error("duplicate-participant", participant.Id, "Participant identities must be unique.");
            ids.Add(participant.Id);
            if (!Enum.IsDefined(participant.Kind) || !Enum.IsDefined(participant.Shape))
                Error("participant-shape", participant.Id, "Unknown participant appearance.");
            if (participant.BoxId is { } box && !model.Boxes.Any(b => b.Id == box))
                Error("participant-box", participant.Id, "Participant box does not exist.");
        }
        foreach (var box in model.Boxes)
        {
            if (string.IsNullOrWhiteSpace(box.Id) || !ids.Add(box.Id)) Error("box-id", box.Id, "Box identity must be unique.");
            if (!SequenceColors.TryParse(box.Color, out _)) Error("box-color", box.Id, "Unsupported participant-box color.");
        }
        var items = SequenceDiagramModel.Walk(model.Statements).ToArray();
        var creations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in items.OfType<SequenceMessage>().Where(m => m.CreateParticipantId is not null))
            if (!creations.TryAdd(message.CreateParticipantId!, message.Id))
                Error("duplicate-creation", message.Id, "A participant cannot be created twice; use separate identities and aliases.");
        var alive = participants.Keys.ToDictionary(id => id, id => !creations.ContainsKey(id), StringComparer.Ordinal);
        var activations = participants.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var declared = new HashSet<string>();
        var born = new HashSet<string>();
        var destroyedParticipants = new HashSet<string>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) Error("statement-id", item.Id, "Statement identity must be unique.");
            foreach (var reference in References(item, false))
                if (!participants.ContainsKey(reference)) Error("missing-participant", item.Id, $"Participant '{reference}' does not exist.");
            if (item is SequenceDeclaration declaration)
            {
                if (creations.ContainsKey(declaration.ParticipantId) && !born.Contains(declaration.ParticipantId))
                    Error("declaration-before-creation", item.Id, "A created participant cannot be predeclared before its creation message.");
                declared.Add(declaration.ParticipantId);
                continue;
            }
            if (item is SequenceMessage message)
            {
                try { _ = SequenceArrows.Get(message.ArrowId); }
                catch (ArgumentException error) { Error("message-arrow", item.Id, error.Message); }
                if (message.ActivateTarget && message.DeactivateSource)
                    Error("message-activation", item.Id, "Use separate activation statements instead of two shorthand effects on one message.");
                if ((message.CentralSource || message.CentralTarget) && (message.ActivateTarget || message.DeactivateSource))
                    Error("central-activation", item.Id, "Central connection syntax and activation shorthand cannot be combined. Use an explicit activation statement.");
                if (message.CreateParticipantId is { } created)
                {
                    if (created != message.To) Error("create-target", item.Id, "Only a message's recipient can be created.");
                    if (declared.Contains(created) || alive.GetValueOrDefault(created))
                        Error("create-existing", item.Id, $"Participant '{created}' already existed before its creation.");
                    if (participants.GetValueOrDefault(created)?.BoxId is not null)
                        Error("created-box", item.Id, "A created participant cannot also be predeclared inside a participant box.");
                    if (alive.ContainsKey(created)) alive[created] = true;
                    born.Add(created);
                }
                foreach (var id in new[] { message.From, message.To })
                    if (alive.ContainsKey(id) && !alive[id]) Error("participant-lifetime", item.Id, $"Message uses '{id}' outside its lifetime.",
                        destroyedParticipants.Contains(id) ? SequenceDiagnosticKind.NativeUnsupported : SequenceDiagnosticKind.InvalidStructure);
                if (message.ActivateTarget && activations.ContainsKey(message.To)) activations[message.To]++;
                if (message.DeactivateSource) Deactivate(message.From, item.Id);
                if (message.DestroyParticipantId is { } destroyed)
                {
                    if (destroyed != message.From && destroyed != message.To)
                        Error("destroy-endpoint", item.Id, "The destroyed participant must send or receive the associated message.");
                    if (alive.ContainsKey(destroyed))
                    {
                        alive[destroyed] = false;
                        destroyedParticipants.Add(destroyed);
                        activations[destroyed] = 0;
                    }
                }
            }
            else if (item is SequenceActivation activation)
            {
                if (alive.ContainsKey(activation.ParticipantId) && !alive[activation.ParticipantId])
                    Error("activation-lifetime", item.Id, "Activation lies outside the participant's lifetime.");
                if (activation.Active && activations.ContainsKey(activation.ParticipantId)) activations[activation.ParticipantId]++;
                else if (!activation.Active) Deactivate(activation.ParticipantId, item.Id);
            }
            else if (item is SequenceNote note)
            {
                if (!Enum.IsDefined(note.Placement) || note.Placement != SequenceNotePlacement.Over && note.SecondParticipantId is not null)
                    Error("note-placement", item.Id, "Only an over note can span two participants.");
            }
            else if (item is SequenceBlock block)
            {
                if (!Enum.IsDefined(block.Kind) || block.Branches.Count == 0)
                    Error("fragment-kind", item.Id, "A fragment needs a known kind and its first branch.");
                if (block.Branches.Count > 0 && block.Branches[0].Text.Length != 0)
                    Error("first-branch-label", block.Id, "The first condition is Block.Text; the first branch's Text must be empty.");
                if (block.Kind is not (SequenceBlockKind.Alt or SequenceBlockKind.Par or SequenceBlockKind.ParOver or SequenceBlockKind.Critical) && block.Branches.Count > 1)
                    Error("fragment-branches", item.Id, "This fragment kind accepts only one branch.");
                if (block.Kind == SequenceBlockKind.Rect && !SequenceColors.TryParse(block.Color ?? block.Text, out _))
                    Error("fragment-color", item.Id, "A background fragment requires a valid color.");
                foreach (var branch in block.Branches)
                    if (string.IsNullOrWhiteSpace(branch.Id) || !ids.Add(branch.Id))
                        Error("branch-id", item.Id, "Fragment branch identities must be unique.");
            }
            else if (item is SequenceNumbering numbering)
            {
                if (numbering.Step is not null && numbering.Start is null)
                    Error("numbering-start", item.Id, "Specify a start value when setting a numbering increment.");
                foreach (var number in new[] { numbering.Start, numbering.Step })
                    if (number is { } value && (value < 0 || decimal.Round(value, 2) != value))
                        Error("numbering-value", item.Id, "Numbering uses nonnegative values with at most two decimal places.");
            }
            else if (item is SequenceParticipantMetadata metadata &&
                metadata.Directive is not ("links" or "properties" or "details"))
                Error("participant-metadata", item.Id, "Unknown participant metadata directive.");
        }
        var options = model.Options;
        foreach (var box in model.Boxes)
        {
            var indices = model.Participants.Select((p, i) => (p, i)).Where(p => p.p.BoxId == box.Id).Select(p => p.i).ToArray();
            if (indices.Length > 1 && indices[^1] - indices[0] + 1 != indices.Length)
                Error("box-order", box.Id, "Native participant boxes require adjacent member lanes.", SequenceDiagnosticKind.NativeUnsupported);
        }
        foreach (var number in new[] { options.ActorMargin, options.MessageMargin, options.NoteMargin, options.BoxMargin })
            if (!double.IsFinite(number) || number < 0) Error("layout-spacing", "", "Sequence margins must be finite and nonnegative.");
        foreach (var number in new[] { options.ActorWidth, options.ActorHeight })
            if (!double.IsFinite(number) || number <= 0) Error("participant-size", "", "Participant dimensions must be finite and positive.");
        foreach (var font in new[] { options.ActorFont, options.MessageFont, options.NoteFont })
            if (!double.IsFinite(font.Size) || font.Size <= 0 || string.IsNullOrWhiteSpace(font.Family) || font.Weight is < 1 or > 1000)
                Error("font", "", "Sequence fonts require a family, positive finite size and weight from 1 to 1000.");
        if (options.NoteAlign is not ("left" or "center" or "right")) Error("note-alignment", "", "Choose left, center or right note alignment.");
        return diagnostics;

        void Deactivate(string id, string statementId)
        {
            if (!activations.TryGetValue(id, out var count)) return;
            if (count == 0) Error("activation-underflow", statementId, $"Participant '{id}' is not active.");
            else activations[id]--;
        }
    }

    public static IEnumerable<string> References(SequenceStatement statement, bool includeChildren = true)
    {
        switch (statement)
        {
            case SequenceDeclaration declaration:
                yield return declaration.ParticipantId; break;
            case SequenceMessage message:
                if (message.CreateParticipantId is { } created) yield return created;
                yield return message.From; yield return message.To;
                if (message.DestroyParticipantId is { } destroyed) yield return destroyed;
                break;
            case SequenceActivation activation:
                yield return activation.ParticipantId; break;
            case SequenceNote note:
                yield return note.ParticipantId;
                if (note.SecondParticipantId is { } second) yield return second;
                break;
            case SequenceParticipantMetadata metadata:
                yield return metadata.ParticipantId; break;
            case SequenceBlock block when includeChildren:
                foreach (var id in block.Branches.SelectMany(b => b.Statements).SelectMany(s => References(s))) yield return id;
                break;
        }
    }
}
