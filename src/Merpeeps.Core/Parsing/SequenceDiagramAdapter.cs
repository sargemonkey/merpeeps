using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class SequenceDiagramAdapter : IDiagramAdapter
{
    private static readonly Regex Message = new(
        @"^(?<from>.+?)(?<centralFrom>\(\))?\s*(?<arrow>" +
        string.Join("|", SequenceArrows.All.OrderByDescending(a => a.Syntax.Length).Select(a => Regex.Escape(a.Syntax))) +
        @")\s*(?<effect>[+-])?(?<centralTo>\(\))?\s*(?<to>[^:]+):(?<text>.*)$");
    public string Kind => "sequenceDiagram";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;

    public DiagramModel Parse(string source)
    {
        var result = ReadSource(source);
        if (!result.CanEdit) throw new SequenceNotEditableException(result);
        return new DiagramModel { Kind = Kind, Sequence = result.Model, LineCount = MerpeepsDoc.Split(source).Body.Split('\n').Length };
    }

    public SequenceReadResult ReadSource(string source)
    {
        var model = Read(source).Model;
        var diagnostics = SequenceValidation.Validate(model).ToList();
        if (diagnostics.Count == 0)
        {
            try
            {
                var canonical = Write(model);
                if (!model.Participants.Select(p => p.Id).SequenceEqual(Read(canonical).Model.Participants.Select(p => p.Id)))
                    diagnostics.Add(new("participant-order", "", "This declaration/creation order cannot be rewritten without moving participant lanes.",
                        SequenceDiagnosticKind.NativeUnsupported));
            }
            catch (ArgumentException error)
            {
                diagnostics.Add(new("participant-order", "", "This declaration/creation order cannot be rewritten without moving participant lanes.",
                    SequenceDiagnosticKind.NativeUnsupported));
                diagnostics.Add(new("native-write-policy", "", error.Message, SequenceDiagnosticKind.NativeUnsupported));
            }
        }
        return new(model, diagnostics);
    }

    private static Document Read(string source)
    {
        var split = MerpeepsDoc.Split(source);
        var sequence = new SequenceDiagramModel { Options = ReadOptions(split.Frontmatter) };
        var document = new Document(sequence, split.Body);
        var identities = new IdentityReader(source);
        var blocks = new Stack<BlockBuilder>();
        var current = sequence.Statements;
        string? boxId = null;
        string? createId = null;
        string? destroyId = null;
        int? lifetimeStart = null;
        var header = false;
        foreach (var span in SplitStatements(split.Body))
        {
            var line = span.Text;
            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                current.Add(new SequenceComment(identities.Next("comment:" + line), line));
                continue;
            }
            if (!header)
            {
                if (line != "sequenceDiagram") throw Error(span, "Expected sequenceDiagram.");
                header = true;
                continue;
            }
            if (line == "end")
            {
                if (boxId is not null) { boxId = null; continue; }
                if (blocks.Count == 0) throw Error(span, "Unexpected end.");
                var builder = blocks.Pop();
                var block = new SequenceBlock(builder.Id, builder.Kind, builder.Text,
                    builder.Branches.Select(b => new SequenceBranch(b.Id, b.Text, b.Items.ToArray())).ToArray(), builder.Color);
                current = builder.Parent;
                current.Add(block);
                document.Spans[block.Id] = new(builder.Start, span.Start + span.Length - builder.Start);
                continue;
            }
            if (line.StartsWith("box ", StringComparison.Ordinal))
            {
                if (boxId is not null) throw Error(span, "Participant boxes cannot be nested.");
                var value = line[4..].Trim();
                var (color, title) = BoxData(value);
                color = SequenceColors.Source(color);
                title = SequenceText.Decode(title);
                boxId = identities.Next("box:" + color + "\0" + title);
                sequence.Boxes.Add(new(boxId, title, color));
                continue;
            }
            var declaration = Regex.Match(line, @"^(?<create>create\s+)?(?<kind>participant|actor)\s+(?<definition>.+)$");
            if (declaration.Success)
            {
                var participant = ParseParticipant(declaration.Groups["definition"].Value,
                    declaration.Groups["kind"].Value == "actor" ? SequenceParticipantKind.Actor : SequenceParticipantKind.Participant,
                    boxId);
                var index = sequence.Participants.FindIndex(p => p.Id == participant.Id);
                if (declaration.Groups["create"].Success)
                {
                    if (index >= 0 || createId is not null || boxId is not null)
                        throw Error(span, "A created participant must be new, outside a box, and paired with its receiving message.");
                    createId = participant.Id;
                    lifetimeStart ??= span.Start;
                }
                else
                {
                    var statement = new SequenceDeclaration(identities.Next("declaration:" + participant.Id), participant.Id);
                    current.Add(statement);
                    document.Spans[statement.Id] = new(span.Start, span.Length);
                }
                if (index < 0) sequence.Participants.Add(participant);
                else sequence.Participants[index] = participant;
                continue;
            }
            if (boxId is not null) throw Error(span, "A participant box may contain only participant declarations.");
            if (line.StartsWith("destroy ", StringComparison.Ordinal))
            {
                if (destroyId is not null) throw Error(span, "Pair destruction with a message before another destruction.");
                destroyId = SequenceText.Identifier(line[8..].Trim());
                lifetimeStart ??= span.Start;
                continue;
            }
            var message = Message.Match(line);
            if (message.Success)
            {
                var from = SequenceText.Identifier(message.Groups["from"].Value.Trim());
                var to = SequenceText.Identifier(message.Groups["to"].Value.Trim());
                Ensure(from); Ensure(to);
                var arrow = SequenceArrows.All.Single(a => a.Syntax == message.Groups["arrow"].Value).Id;
                var text = SequenceText.Decode(message.Groups["text"].Value.TrimStart());
                var next = new SequenceMessage("", from, to, text, arrow,
                    message.Groups["effect"].Value == "+", message.Groups["effect"].Value == "-",
                    message.Groups["centralFrom"].Success, message.Groups["centralTo"].Success, createId, destroyId);
                next = next with { Id = identities.Next(Signature(next)) };
                current.Add(next);
                document.Spans[next.Id] = new(lifetimeStart ?? span.Start, span.Start + span.Length - (lifetimeStart ?? span.Start));
                createId = destroyId = null; lifetimeStart = null;
                continue;
            }
            if (createId is not null || destroyId is not null) throw Error(span, "A lifecycle directive must be followed by its associated message.");
            if (line.StartsWith("activate ", StringComparison.Ordinal) || line.StartsWith("deactivate ", StringComparison.Ordinal))
            {
                var active = line.StartsWith("activate ", StringComparison.Ordinal);
                var participant = line[(active ? 9 : 11)..].Trim();
                Ensure(participant);
                Add(new SequenceActivation("", participant, active), span);
                continue;
            }
            var note = Regex.Match(line, @"^note\s+(?<placement>left\s+of|right\s+of|over)\s+(?<actors>[^:]+):(?<text>.*)$", RegexOptions.IgnoreCase);
            if (note.Success)
            {
                var actors = note.Groups["actors"].Value.Split(',', StringSplitOptions.TrimEntries);
                if (actors.Length is < 1 or > 2) throw Error(span, "A note needs one or two participants.");
                foreach (var actor in actors) Ensure(actor);
                var placement = note.Groups["placement"].Value.ToLowerInvariant() switch
                {
                    "left of" => SequenceNotePlacement.Left, "right of" => SequenceNotePlacement.Right, _ => SequenceNotePlacement.Over,
                };
                Add(new SequenceNote("", placement, actors[0], actors.Length == 2 ? actors[1] : null,
                    SequenceText.Decode(note.Groups["text"].Value.TrimStart())), span);
                continue;
            }
            var branch = Regex.Match(line, @"^(?<kind>else|and|option)(?:\s+(?<text>.*))?$");
            if (branch.Success)
            {
                if (blocks.Count == 0) throw Error(span, "A branch needs an enclosing fragment.");
                var block = blocks.Peek();
                var allowed = block.Kind switch { SequenceBlockKind.Alt => "else", SequenceBlockKind.Par or SequenceBlockKind.ParOver => "and", SequenceBlockKind.Critical => "option", _ => "" };
                if (branch.Groups["kind"].Value != allowed) throw Error(span, $"This fragment requires '{allowed}' branches.");
                var branchText = SequenceText.Decode(branch.Groups["text"].Value);
                var next = new BranchBuilder(identities.Next("branch:" + block.Id + "\0" + branchText), branchText);
                block.Branches.Add(next);
                current = next.Items;
                continue;
            }
            var opening = Regex.Match(line, @"^(?<kind>loop|alt|opt|par_over|par|critical|break|rect)(?:\s+(?<text>.*))?$");
            if (opening.Success)
            {
                var kind = opening.Groups["kind"].Value switch
                {
                    "loop" => SequenceBlockKind.Loop, "alt" => SequenceBlockKind.Alt, "opt" => SequenceBlockKind.Opt,
                    "par" => SequenceBlockKind.Par, "par_over" => SequenceBlockKind.ParOver,
                    "critical" => SequenceBlockKind.Critical, "break" => SequenceBlockKind.Break, _ => SequenceBlockKind.Rect,
                };
                var text = SequenceText.Decode(opening.Groups["text"].Value);
                var color = kind == SequenceBlockKind.Rect ? text : null;
                if (color is not null && !SequenceColors.TryParse(color, out _)) throw Error(span, "A background fragment needs a supported color.");
                if (color is not null) { color = SequenceColors.Source(color); text = color; }
                var id = identities.Next("block:" + kind + "\0" + text);
                var block = new BlockBuilder(id, kind, text, color, current, span.Start);
                var first = new BranchBuilder(identities.Next("branch:" + id + "\0"), "");
                block.Branches.Add(first);
                blocks.Push(block);
                current = first.Items;
                continue;
            }
            if (line.StartsWith("autonumber", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 3) throw Error(span, "Autonumber accepts a start and increment.");
                if (parts.Length == 2 && parts[1] == "off") Add(new SequenceNumbering("", false), span);
                else Add(new SequenceNumbering("", true, parts.Length > 1 ? Decimal(parts[1], span) : null, parts.Length > 2 ? Decimal(parts[2], span) : null), span);
                continue;
            }
            var metadata = Regex.Match(line, @"^(?<kind>links|link|properties|details)\s+(?<actor>[^:]+):(?<data>.*)$");
            if (metadata.Success)
            {
                var actor = metadata.Groups["actor"].Value.Trim();
                Ensure(actor);
                JsonObject values;
                var data = metadata.Groups["data"].Value.Trim();
                var directive = metadata.Groups["kind"].Value;
                if (directive == "link")
                {
                    var splitAt = data.IndexOf('@');
                    if (splitAt <= 0) throw Error(span, "A link needs a label @ URL.");
                    values = new() { [SequenceText.Decode(data[..splitAt].Trim())] = SequenceText.Decode(data[(splitAt + 1)..].Trim()) };
                    directive = "links";
                }
                else values = JsonNode.Parse(data) as JsonObject ?? throw Error(span, "Participant metadata must be a JSON object.");
                Add(new SequenceParticipantMetadata("", actor, directive, values), span);
                continue;
            }
            var documentText = Regex.Match(line, @"^(?<kind>title|accTitle|accDescr)\s*:?\s+(?<text>.*)$");
            if (documentText.Success)
            {
                Add(new SequenceDocumentText("", documentText.Groups["kind"].Value, SequenceText.Decode(documentText.Groups["text"].Value)), span);
                continue;
            }
            throw Error(span, "Unsupported sequence statement: " + line);
        }
        if (!header || blocks.Count > 0 || boxId is not null || createId is not null || destroyId is not null)
            throw new FormatException("The sequence diagram contains an incomplete directive or fragment.");
        return document;

        void Ensure(string id)
        {
            SequenceText.Identifier(id);
            if (sequence.Participants.All(p => p.Id != id)) sequence.Participants.Add(new(id));
        }
        void Add(SequenceStatement statement, Span span)
        {
            var id = identities.Next(Signature(statement));
            statement = statement switch
            {
                SequenceActivation activation => activation with { Id = id },
                SequenceNote annotation => annotation with { Id = id },
                SequenceNumbering numbering => numbering with { Id = id },
                SequenceParticipantMetadata info => info with { Id = id },
                SequenceDocumentText info => info with { Id = id },
                _ => throw new InvalidOperationException("Unrecognized sequence statement."),
            };
            current.Add(statement);
            document.Spans[id] = new(span.Start, span.Length);
        }
    }

    public string Write(DiagramModel model) => Write(model.Sequence ?? throw new ArgumentException("Sequence content is missing."));

    public static string Write(SequenceDiagramModel model)
    {
        var diagnostics = SequenceValidation.Validate(model);
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join("\n", diagnostics.Select(d => d.Message)));
        var builder = new StringBuilder("sequenceDiagram\n");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var created = SequenceDiagramModel.Walk(model.Statements).OfType<SequenceMessage>()
            .Where(m => m.CreateParticipantId is not null).Select(m => m.CreateParticipantId!).ToHashSet();
        WriteList(model.Statements, "");
        foreach (var participant in model.Participants)
            if (!created.Contains(participant.Id)) EmitParticipant(participant, "");
        foreach (var box in model.Boxes.Where(box => model.Participants.All(p => p.BoxId != box.Id)))
            builder.Append("box ").Append(SequenceColors.Source(box.Color)).Append(' ')
                .Append(SequenceText.Encode(box.Title)).Append("\nend\n");
        return builder.ToString();

        void EmitParticipant(SequenceParticipant participant, string indent)
        {
            if (seen.Contains(participant.Id)) return;
            if (participant.BoxId is { } boxId)
            {
                var box = model.Boxes.Single(b => b.Id == boxId);
                builder.Append(indent).Append("box ").Append(SequenceColors.Source(box.Color)).Append(' ').Append(SequenceText.Encode(box.Title)).Append('\n');
                foreach (var member in model.Participants.Where(p => p.BoxId == boxId))
                {
                    if (seen.Contains(member.Id)) throw new ArgumentException("Participant box members must be declared together.");
                    builder.Append(indent).Append("  ").Append(ParticipantSource(member)).Append('\n');
                    seen.Add(member.Id);
                }
                builder.Append(indent).Append("end\n");
            }
            else
            {
                builder.Append(indent).Append(ParticipantSource(participant)).Append('\n');
                seen.Add(participant.Id);
            }
        }

        void WriteList(IEnumerable<SequenceStatement> items, string indent)
        {
            foreach (var item in items)
            {
                if (item is SequenceDeclaration declaration)
                {
                    var participant = model.Participant(declaration.ParticipantId);
                    EmitParticipant(participant, indent);
                    continue;
                }
                if (item is SequenceComment comment)
                {
                    builder.Append(indent).Append(comment.Text).Append('\n');
                    continue;
                }
                if (item is SequenceMessage { CreateParticipantId: { } createdId })
                    foreach (var participant in model.Participants.TakeWhile(p => p.Id != createdId))
                    {
                        if (seen.Contains(participant.Id)) continue;
                        if (created.Contains(participant.Id)) throw new ArgumentException("Created participant lanes follow creation-message order.");
                        EmitParticipant(participant, indent);
                    }
                foreach (var id in item is SequenceMessage { CreateParticipantId: not null }
                    ? Array.Empty<string>() : SequenceValidation.References(item, includeChildren: false))
                {
                    if (seen.Contains(id) || created.Contains(id)) continue;
                    foreach (var participant in model.Participants.TakeWhile(p => p.Id != id).Append(model.Participant(id)))
                    {
                        if (seen.Contains(participant.Id) || created.Contains(participant.Id)) continue;
                        EmitParticipant(participant, indent);
                    }
                }
                if (item is SequenceBlock block)
                {
                    builder.Append(indent).Append(BlockKeyword(block.Kind)).Append(' ')
                        .Append(block.Kind == SequenceBlockKind.Rect ? SequenceColors.Source(block.Color ?? block.Text) : SequenceText.Encode(block.Text)).Append('\n');
                    for (var i = 0; i < block.Branches.Count; i++)
                    {
                        if (i > 0) builder.Append(indent).Append(BranchKeyword(block.Kind)).Append(' ').Append(SequenceText.Encode(block.Branches[i].Text)).Append('\n');
                        WriteList(block.Branches[i].Statements, indent + "  ");
                    }
                    builder.Append(indent).Append("end\n");
                }
                else
                {
                    builder.Append(indent).Append(StatementSource(model, item).Replace("\n", "\n" + indent)).Append('\n');
                    if (item is SequenceMessage { CreateParticipantId: { } id })
                    {
                        seen.Add(id);
                        foreach (var participantId in SequenceValidation.References(item, false))
                        {
                            if (seen.Contains(participantId) || created.Contains(participantId)) continue;
                            EmitParticipant(model.Participant(participantId), indent);
                        }
                    }
                }
            }
        }
    }

    internal string Apply(string source, DiagramModel edited)
    {
        var before = Read(source);
        var after = edited.Sequence ?? throw new ArgumentException("Sequence content is missing.");
        var diagnostics = SequenceValidation.Validate(after);
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join("\n", diagnostics.Select(d => d.Message)));
        var body = before.Body;
        if (Write(before.Model) != Write(after))
        {
            var oldItems = SequenceDiagramModel.Walk(before.Model.Statements).ToArray();
            var newItems = SequenceDiagramModel.Walk(after.Statements).ToArray();
            var sameShape = oldItems.Select(s => (s.Id, s.GetType())).SequenceEqual(newItems.Select(s => (s.Id, s.GetType())));
            var sameParticipants = before.Model.Participants.Select(ParticipantSource).SequenceEqual(after.Participants.Select(ParticipantSource));
            var sameBoxes = before.Model.Boxes.SequenceEqual(after.Boxes);
            var oldBlocks = oldItems.OfType<SequenceBlock>().ToArray();
            var newBlocks = newItems.OfType<SequenceBlock>().ToArray();
            var sameBranches = oldBlocks.Length == newBlocks.Length && oldBlocks.Zip(newBlocks).All(pair =>
                pair.First.Branches.Count == pair.Second.Branches.Count &&
                pair.First.Branches.Zip(pair.Second.Branches).All(branch =>
                    branch.First.Id == branch.Second.Id && branch.First.Text == branch.Second.Text &&
                    branch.First.Statements.Select(s => s.Id).SequenceEqual(branch.Second.Statements.Select(s => s.Id))));
            if (sameShape && sameParticipants && sameBoxes && sameBranches && oldBlocks.Select(Signature).SequenceEqual(newBlocks.Select(Signature)))
            {
                var edits = oldItems.Zip(newItems).Where(pair => Signature(pair.First) != Signature(pair.Second)).ToArray();
                if (edits.All(pair => before.Spans.ContainsKey(pair.First.Id) && pair.First is not SequenceBlock))
                {
                    foreach (var pair in edits.OrderByDescending(pair => before.Spans[pair.First.Id].Start))
                    {
                        var span = before.Spans[pair.First.Id];
                        var text = StatementSource(after, pair.Second);
                        if (body.Contains("\r\n", StringComparison.Ordinal)) text = text.Replace("\n", "\r\n");
                        body = body[..span.Start] + text + body[(span.Start + span.Length)..];
                    }
                }
                else body = Write(after);
            }
            else body = Write(after);
        }
        if (before.Body.Contains("\r\n", StringComparison.Ordinal) && !body.Contains("\r\n", StringComparison.Ordinal)) body = body.Replace("\n", "\r\n");
        var written = MerpeepsDoc.ReplaceBody(source, body);
        var fontsChanged = before.Model.Options.ActorFont != after.Options.ActorFont ||
            before.Model.Options.MessageFont != after.Options.MessageFont || before.Model.Options.NoteFont != after.Options.NoteFont;
        return before.Model.Options == after.Options ? written
            : MerpeepsDoc.WithDiagramConfiguration(written, "sequence", OptionsMap(after.Options), fontsChanged);
    }

    internal static string StatementSource(SequenceDiagramModel model, SequenceStatement item) => item switch
    {
        SequenceMessage message => (message.CreateParticipantId is { } create ? "create " + ParticipantSource(model.Participant(create)) + "\n" : "") +
            (message.DestroyParticipantId is { } destroy ? "destroy " + SequenceText.Identifier(destroy) + "\n" : "") +
            SequenceText.Identifier(message.From) + (message.CentralSource ? "()" : "") + SequenceArrows.Get(message.ArrowId).Syntax +
            (message.ActivateTarget ? "+" : message.DeactivateSource ? "-" : "") + (message.CentralTarget ? "()" : "") +
            SequenceText.Identifier(message.To) + ": " + SequenceText.Encode(message.Text),
        SequenceActivation activation => (activation.Active ? "activate " : "deactivate ") + SequenceText.Identifier(activation.ParticipantId),
        SequenceNote note => "Note " + (note.Placement switch { SequenceNotePlacement.Left => "left of ", SequenceNotePlacement.Right => "right of ", _ => "over " }) +
            note.ParticipantId + (note.SecondParticipantId is { } second ? "," + second : "") + ": " + SequenceText.Encode(note.Text),
        SequenceNumbering numbering => !numbering.Enabled ? "autonumber off" : "autonumber" +
            (numbering.Start is { } start ? " " + start.ToString(CultureInfo.InvariantCulture) : "") +
            (numbering.Step is { } step ? " " + step.ToString(CultureInfo.InvariantCulture) : ""),
        SequenceParticipantMetadata metadata => metadata.Directive + " " + metadata.ParticipantId + ": " + metadata.Values.ToJsonString(),
        SequenceDeclaration declaration => ParticipantSource(model.Participant(declaration.ParticipantId)),
        SequenceComment comment => comment.Text,
        SequenceDocumentText text => text.Directive + ": " + SequenceText.Encode(text.Text),
        _ => throw new ArgumentException("This sequence item requires its nested writer."),
    };

    internal static string ParticipantSource(SequenceParticipant participant)
    {
        var config = (JsonObject)participant.Configuration.DeepClone();
        if (participant.Shape == SequenceParticipantShape.Default) config.Remove("type");
        else config["type"] = participant.Shape == SequenceParticipantShape.Rectangle ? "participant" : participant.Shape.ToString().ToLowerInvariant();
        if (!participant.AliasIsExternal)
        {
            if (participant.Alias is null) config.Remove("alias");
            else config["alias"] = SequenceText.Encode(participant.Alias);
        }
        return (participant.Kind == SequenceParticipantKind.Actor ? "actor " : "participant ") +
            SequenceText.Identifier(participant.Id) + (config.Count > 0 ? "@" + config.ToJsonString() : "") +
            (participant.AliasIsExternal && participant.Alias is { } alias ? " as " + SequenceText.Encode(alias) : "");
    }

    internal static object Identity(string source, SequenceDiagramModel model)
    {
        var reader = new IdentityReader(source);
        var entries = new List<object?>();
        foreach (var box in model.Boxes) entries.Add(reader.Record(box.Id, "box:" + SequenceColors.Source(box.Color) + "\0" + box.Title));
        foreach (var item in SequenceDiagramModel.Walk(model.Statements))
        {
            entries.Add(reader.Record(item.Id, Signature(item)));
            if (item is SequenceBlock block)
                foreach (var branch in block.Branches) entries.Add(reader.Record(branch.Id, "branch:" + block.Id + "\0" + branch.Text));
        }
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
    }

    internal static string Signature(SequenceStatement statement) => statement switch
    {
        SequenceDeclaration declaration => "declaration:" + declaration.ParticipantId,
        SequenceMessage m => $"message:{m.From}\0{m.To}\0{m.Text}\0{m.ArrowId}\0{m.ActivateTarget}\0{m.DeactivateSource}\0{m.CentralSource}\0{m.CentralTarget}\0{m.CreateParticipantId}\0{m.DestroyParticipantId}",
        SequenceActivation a => $"activation:{a.ParticipantId}\0{a.Active}",
        SequenceNote n => $"note:{n.Placement}\0{n.ParticipantId}\0{n.SecondParticipantId}\0{n.Text}",
        SequenceBlock b => $"block:{b.Kind}\0{(b.Kind == SequenceBlockKind.Rect ? SequenceColors.Source(b.Color ?? b.Text) : b.Text)}",
        SequenceNumbering n => $"numbering:{n.Enabled}\0{n.Start?.ToString(CultureInfo.InvariantCulture)}\0{n.Step?.ToString(CultureInfo.InvariantCulture)}",
        SequenceParticipantMetadata m => $"metadata:{m.ParticipantId}\0{m.Directive}\0{m.Values.ToJsonString()}",
        SequenceComment c => "comment:" + c.Text,
        SequenceDocumentText t => "document:" + t.Directive + "\0" + t.Text,
        _ => throw new ArgumentException("Unknown sequence statement."),
    };

    private static SequenceParticipant ParseParticipant(string text, SequenceParticipantKind kind, string? boxId)
    {
        var match = Regex.Match(text, @"^(?<id>.+?)(?:@\s*(?<config>\{.*\}))?(?:\s+as\s+(?<alias>.+))?$", RegexOptions.Singleline);
        if (!match.Success) throw new FormatException("Malformed sequence participant.");
        var id = SequenceText.Identifier(match.Groups["id"].Value.Trim());
        var config = match.Groups["config"].Success ? JsonNode.Parse(match.Groups["config"].Value) as JsonObject
            ?? throw new FormatException("Participant options must be a JSON object.") : new JsonObject();
        var rawShape = config["type"]?.GetValue<string>();
        var shape = rawShape switch
        {
            null => SequenceParticipantShape.Default, "participant" => SequenceParticipantShape.Rectangle,
            "actor" => SequenceParticipantShape.Actor, "boundary" => SequenceParticipantShape.Boundary,
            "control" => SequenceParticipantShape.Control, "entity" => SequenceParticipantShape.Entity,
            "database" => SequenceParticipantShape.Database, "collections" => SequenceParticipantShape.Collections,
            "queue" => SequenceParticipantShape.Queue,
            _ => throw new FormatException($"Participant type '{rawShape}' has no native sequence interpretation."),
        };
        var external = match.Groups["alias"].Success;
        var alias = external ? match.Groups["alias"].Value : config["alias"]?.GetValue<string>();
        return new(id, kind, shape, alias is null ? null : SequenceText.Decode(alias), boxId)
        {
            Configuration = config, AliasIsExternal = external,
        };
    }

    private static (string Color, string Title) BoxData(string text)
    {
        if (text.TrimStart().StartsWith('#')) throw new FormatException("Sequence boxes do not accept hex colors in source; use rgb() or a named color.");
        var match = Regex.Match(text, @"^(?<color>(?:rgba?|hsla?)\s*\([^)]*\)|\w+)(?<title>.*)$", RegexOptions.IgnoreCase);
        if (match.Success && SequenceColors.TryParse(match.Groups["color"].Value, out _))
            return (match.Groups["color"].Value, match.Groups["title"].Value.Trim());
        return ("transparent", text);
    }

    internal static Dictionary<string, object?> OptionsMap(SequenceOptions options) => new()
    {
        ["actorMargin"] = options.ActorMargin, ["messageMargin"] = options.MessageMargin,
        ["width"] = options.ActorWidth, ["height"] = options.ActorHeight, ["noteMargin"] = options.NoteMargin,
        ["boxMargin"] = options.BoxMargin, ["mirrorActors"] = options.MirrorActors,
        ["showSequenceNumbers"] = options.ShowSequenceNumbers, ["noteAlign"] = options.NoteAlign,
        ["actorFontFamily"] = options.ActorFont.Family, ["actorFontSize"] = options.ActorFont.Size, ["actorFontWeight"] = options.ActorFont.Weight,
        ["messageFontFamily"] = options.MessageFont.Family, ["messageFontSize"] = options.MessageFont.Size, ["messageFontWeight"] = options.MessageFont.Weight,
        ["noteFontFamily"] = options.NoteFont.Family, ["noteFontSize"] = options.NoteFont.Size, ["noteFontWeight"] = options.NoteFont.Weight,
    };

    private static SequenceOptions ReadOptions(Dictionary<string, object?> frontmatter)
    {
        var config = frontmatter.GetValueOrDefault("config") as Dictionary<string, object?>;
        var settings = config?.GetValueOrDefault("sequence") as Dictionary<string, object?> ?? new();
        double Number(string key, double fallback) => settings.TryGetValue(key, out var value)
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
        SequenceFont Font(string role, double size)
        {
            var weightValue = config?.GetValueOrDefault("fontWeight") ?? settings.GetValueOrDefault(role + "FontWeight");
            var weight = weightValue is string text
                ? text switch { "normal" => 400, "bold" => 700, _ => int.Parse(text, CultureInfo.InvariantCulture) }
                : weightValue is null ? 400 : Convert.ToInt32(weightValue, CultureInfo.InvariantCulture);
            var sizeValue = config?.GetValueOrDefault("fontSize");
            return new(config?.GetValueOrDefault("fontFamily") as string ??
                settings.GetValueOrDefault(role + "FontFamily") as string ?? "Segoe UI",
                sizeValue is null ? Number(role + "FontSize", size) : Convert.ToDouble(sizeValue, CultureInfo.InvariantCulture), weight);
        }
        return new()
        {
            ActorMargin = Number("actorMargin", 80), MessageMargin = Number("messageMargin", 40),
            ActorWidth = Number("width", 140), ActorHeight = Number("height", 54),
            NoteMargin = Number("noteMargin", 12), BoxMargin = Number("boxMargin", 12),
            MirrorActors = settings.GetValueOrDefault("mirrorActors") is true,
            ShowSequenceNumbers = settings.GetValueOrDefault("showSequenceNumbers") is true,
            NoteAlign = settings.GetValueOrDefault("noteAlign") as string ?? "center",
            ActorFont = Font("actor", 14), MessageFont = Font("message", 16), NoteFont = Font("note", 14),
        };
    }

    internal static string BlockKeyword(SequenceBlockKind kind) => kind == SequenceBlockKind.ParOver ? "par_over" : kind.ToString().ToLowerInvariant();
    internal static string BranchKeyword(SequenceBlockKind kind) => kind switch
    {
        SequenceBlockKind.Alt => "else", SequenceBlockKind.Par or SequenceBlockKind.ParOver => "and",
        SequenceBlockKind.Critical => "option", _ => throw new ArgumentException("This fragment does not accept extra branches."),
    };
    private static decimal Decimal(string value, Span span) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 && decimal.Round(parsed, 2) == parsed
            ? parsed : throw Error(span, "Use a nonnegative number with at most two decimal places.");
    private static FormatException Error(Span span, string message) => new($"Sequence near character {span.Start}: {message}");
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("Sequence participants are not graph nodes.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("Sequence messages are ordered events, not graph edges.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    private sealed record Span(string Text, int Start, int Length);
    private sealed record SourceSpan(int Start, int Length);
    private sealed record Document(SequenceDiagramModel Model, string Body)
    {
        public Dictionary<string, SourceSpan> Spans { get; } = new(StringComparer.Ordinal);
    }
    private sealed record BranchBuilder(string Id, string Text)
    {
        public List<SequenceStatement> Items { get; } = new();
    }
    private sealed record BlockBuilder(string Id, SequenceBlockKind Kind, string Text, string? Color, List<SequenceStatement> Parent, int Start)
    {
        public List<BranchBuilder> Branches { get; } = new();
    }
    private sealed class IdentityReader
    {
        private readonly List<Dictionary<string, object?>> _records;
        private readonly Dictionary<string, int> _seen = new();
        public IdentityReader(string source)
        {
            var root = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
            var identity = (root?.GetValueOrDefault("identity") as Dictionary<string, object?>)?.GetValueOrDefault("sequenceDiagram") as Dictionary<string, object?>;
            if (identity?.GetValueOrDefault("spec") is { } version && version is not (1 or 1L))
                throw new FormatException("Unsupported sequence identity metadata.");
            if (identity?.GetValueOrDefault("entries") is { } entries && entries is not List<object?>)
                throw new FormatException("Sequence identities must be a list.");
            _records = (identity?.GetValueOrDefault("entries") as List<object?>)?.Select(e => e as Dictionary<string, object?>
                ?? throw new FormatException("Sequence identity entry is not an object.")).ToList() ?? new();
        }
        public string Next(string signature)
        {
            var hash = Hash(signature);
            var occurrence = _seen.GetValueOrDefault(hash);
            _seen[hash] = occurrence + 1;
            var matches = _records.Where(r => r.GetValueOrDefault("fingerprint") as string == hash).ToArray();
            return occurrence < matches.Length && matches[occurrence].GetValueOrDefault("id") is string { Length: > 0 } id
                ? id : "__sequence_" + hash[..24] + "_" + occurrence;
        }
        public Dictionary<string, object?> Record(string id, string signature)
        {
            var prior = _records.FirstOrDefault(r => r.GetValueOrDefault("id") as string == id);
            var value = prior is null ? new Dictionary<string, object?>() : new(prior);
            value["id"] = id; value["fingerprint"] = Hash(signature);
            return value;
        }
        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static IEnumerable<Span> SplitStatements(string source)
    {
        var start = 0;
        var braces = 0;
        var quoted = false;
        var comment = false;
        for (var i = 0; i <= source.Length; i++)
        {
            if (i < source.Length && !quoted && source[i] == '%' && i + 1 < source.Length && source[i + 1] == '%' &&
                source[start..i].Trim().Length == 0) comment = true;
            if (!comment && i < source.Length)
            {
                if (source[i] == '"' && (i == 0 || source[i - 1] != '\\')) quoted = !quoted;
                if (!quoted && source[i] == '{' && (braces > 0 || i > 0 && source[i - 1] == '@' ||
                    Regex.IsMatch(source[start..i], @"^\s*(links|properties|details)\b"))) braces++;
                else if (!quoted && source[i] == '}' && braces > 0) braces--;
                if (source[i] == '#' && Regex.Match(source[i..], @"^#[A-Za-z0-9]+;") is { Success: true } entity)
                {
                    i += entity.Length - 1;
                    continue;
                }
            }
            var delimiter = i == source.Length || source[i] == '\n' && (comment || braces == 0) ||
                source[i] == ';' && !quoted && !comment && braces == 0;
            if (!delimiter) continue;
            var raw = source[start..i];
            var text = raw.Trim();
            if (text.Length > 0) yield return new(text, start + raw.IndexOf(text, StringComparison.Ordinal), text.Length);
            start = i + 1; comment = false; quoted = false;
        }
        if (braces != 0) throw new FormatException("Unclosed sequence JSON configuration.");
    }
}
