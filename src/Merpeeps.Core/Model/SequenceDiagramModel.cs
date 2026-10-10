using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum SequenceParticipantKind { Participant, Actor }
public enum SequenceParticipantShape { Default, Rectangle, Actor, Boundary, Control, Entity, Database, Collections, Queue }
public enum SequenceBlockKind { Loop, Alt, Opt, Par, ParOver, Critical, Break, Rect }
public enum SequenceNotePlacement { Left, Right, Over }
public enum SequenceArrowHead { None, Filled, Open, Cross, FilledTop, FilledBottom, StickTop, StickBottom }

public sealed record SequenceArrow(
    string Id, string Syntax, string DisplayName, bool Dashed,
    SequenceArrowHead Head, bool Reverse = false, bool BothEnds = false);

/// <summary>Exact tokens from the pinned Mermaid 12 sequence grammar, not Markdown-table escaping.</summary>
public static class SequenceArrows
{
    public static IReadOnlyList<SequenceArrow> All { get; } = Array.AsReadOnly(new[]
    {
        new SequenceArrow("line", "->", "Solid line", false, SequenceArrowHead.None),
        new("dotted-line", "-->", "Dotted line", true, SequenceArrowHead.None),
        new("arrow", "->>", "Solid arrow", false, SequenceArrowHead.Filled),
        new("dotted-arrow", "-->>", "Dotted arrow", true, SequenceArrowHead.Filled),
        new("bidirectional", "<<->>", "Bidirectional arrow", false, SequenceArrowHead.Filled, BothEnds: true),
        new("dotted-bidirectional", "<<-->>", "Dotted bidirectional arrow", true, SequenceArrowHead.Filled, BothEnds: true),
        new("cross", "-x", "Cross", false, SequenceArrowHead.Cross),
        new("dotted-cross", "--x", "Dotted cross", true, SequenceArrowHead.Cross),
        new("async", "-)", "Asynchronous arrow", false, SequenceArrowHead.Open),
        new("dotted-async", "--)", "Dotted asynchronous arrow", true, SequenceArrowHead.Open),
        new("filled-top", "-|\\", "Top half arrow", false, SequenceArrowHead.FilledTop),
        new("filled-bottom", "-|/", "Bottom half arrow", false, SequenceArrowHead.FilledBottom),
        new("stick-top", "-\\\\", "Top stick arrow", false, SequenceArrowHead.StickTop),
        new("stick-bottom", "-//", "Bottom stick arrow", false, SequenceArrowHead.StickBottom),
        new("reverse-filled-top", "/|-", "Reverse top half arrow", false, SequenceArrowHead.FilledTop, true),
        new("reverse-filled-bottom", "\\|-", "Reverse bottom half arrow", false, SequenceArrowHead.FilledBottom, true),
        new("reverse-stick-top", "//-", "Reverse top stick arrow", false, SequenceArrowHead.StickTop, true),
        new("reverse-stick-bottom", "\\\\-", "Reverse bottom stick arrow", false, SequenceArrowHead.StickBottom, true),
        new("dotted-filled-top", "--|\\", "Dotted top half arrow", true, SequenceArrowHead.FilledTop),
        new("dotted-filled-bottom", "--|/", "Dotted bottom half arrow", true, SequenceArrowHead.FilledBottom),
        new("dotted-stick-top", "--\\\\", "Dotted top stick arrow", true, SequenceArrowHead.StickTop),
        new("dotted-stick-bottom", "--//", "Dotted bottom stick arrow", true, SequenceArrowHead.StickBottom),
        new("dotted-reverse-filled-top", "/|--", "Dotted reverse top half arrow", true, SequenceArrowHead.FilledTop, true),
        new("dotted-reverse-filled-bottom", "\\|--", "Dotted reverse bottom half arrow", true, SequenceArrowHead.FilledBottom, true),
        new("dotted-reverse-stick-top", "//--", "Dotted reverse top stick arrow", true, SequenceArrowHead.StickTop, true),
        new("dotted-reverse-stick-bottom", "\\\\--", "Dotted reverse bottom stick arrow", true, SequenceArrowHead.StickBottom, true),
    });

    public static SequenceArrow Get(string id) => All.SingleOrDefault(a => a.Id == id)
        ?? throw new ArgumentException($"Unknown sequence arrow '{id}'.", nameof(id));
}

public sealed record SequenceParticipant(
    string Id,
    SequenceParticipantKind Kind = SequenceParticipantKind.Participant,
    SequenceParticipantShape Shape = SequenceParticipantShape.Default,
    string? Alias = null,
    string? BoxId = null)
{
    /// <summary>Original inline configuration, including shadowed aliases and unknown fields.</summary>
    public JsonObject Configuration { get; init; } = new();
    public bool AliasIsExternal { get; init; } = true;
    public string Display => Alias ?? Id;
    public SequenceParticipant DeepCopy() => this with { Configuration = (JsonObject)Configuration.DeepClone() };
}

public sealed record SequenceParticipantBox(string Id, string Title, string Color = "transparent");

public abstract record SequenceStatement(string Id);
public sealed record SequenceDeclaration(string Id, string ParticipantId) : SequenceStatement(Id);
public sealed record SequenceMessage(
    string Id, string From, string To, string Text, string ArrowId = "arrow",
    bool ActivateTarget = false, bool DeactivateSource = false,
    bool CentralSource = false, bool CentralTarget = false,
    string? CreateParticipantId = null, string? DestroyParticipantId = null) : SequenceStatement(Id);
public sealed record SequenceActivation(string Id, string ParticipantId, bool Active) : SequenceStatement(Id);
public sealed record SequenceNote(
    string Id, SequenceNotePlacement Placement, string ParticipantId, string? SecondParticipantId, string Text) : SequenceStatement(Id);
public sealed record SequenceBranch(string Id, string Text, IReadOnlyList<SequenceStatement> Statements);
public sealed record SequenceBlock(
    string Id, SequenceBlockKind Kind, string Text, IReadOnlyList<SequenceBranch> Branches, string? Color = null) : SequenceStatement(Id);
public sealed record SequenceNumbering(string Id, bool Enabled = true, decimal? Start = null, decimal? Step = null) : SequenceStatement(Id);
public sealed record SequenceParticipantMetadata(string Id, string ParticipantId, string Directive, JsonObject Values) : SequenceStatement(Id);
public sealed record SequenceComment(string Id, string Text) : SequenceStatement(Id);
public sealed record SequenceDocumentText(string Id, string Directive, string Text) : SequenceStatement(Id);

public sealed record SequenceFont(string Family = "Segoe UI", double Size = 14, int Weight = 400);
public sealed record SequenceOptions
{
    public double ActorMargin { get; init; } = 80;
    public double MessageMargin { get; init; } = 40;
    public double ActorWidth { get; init; } = 140;
    public double ActorHeight { get; init; } = 54;
    public double NoteMargin { get; init; } = 12;
    public double BoxMargin { get; init; } = 12;
    public bool MirrorActors { get; init; }
    public bool ShowSequenceNumbers { get; init; }
    public string NoteAlign { get; init; } = "center";
    public SequenceFont ActorFont { get; init; } = new();
    public SequenceFont MessageFont { get; init; } = new(Size: 16);
    public SequenceFont NoteFont { get; init; } = new();
}

/// <summary>
/// Ordered sequence semantics. Declarations and interaction trees preserve
/// first-appearance order; there are no synthetic DiagramNodes or DiagramEdges.
/// </summary>
public sealed class SequenceDiagramModel
{
    public List<SequenceParticipant> Participants { get; } = new();
    public List<SequenceParticipantBox> Boxes { get; } = new();
    public List<SequenceStatement> Statements { get; } = new();
    public SequenceOptions Options { get; set; } = new();
    public SequenceParticipant Participant(string id) => Participants.SingleOrDefault(p => p.Id == id)
        ?? throw new ArgumentException($"Participant '{id}' does not exist.");
    public SequenceStatement Statement(string id) => Walk(Statements).SingleOrDefault(s => s.Id == id)
        ?? throw new ArgumentException($"Sequence statement '{id}' does not exist.");

    public static IEnumerable<SequenceStatement> Walk(IEnumerable<SequenceStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            if (statement is SequenceBlock block)
                foreach (var child in Walk(block.Branches.SelectMany(b => b.Statements))) yield return child;
        }
    }

    public SequenceDiagramModel Clone()
    {
        var copy = new SequenceDiagramModel { Options = Options };
        copy.Participants.AddRange(Participants.Select(p => p.DeepCopy()));
        copy.Boxes.AddRange(Boxes);
        copy.Statements.AddRange(CloneStatements(Statements));
        return copy;
    }
    internal static List<SequenceStatement> CloneStatements(IEnumerable<SequenceStatement> statements) =>
        statements.Select(s => s switch
        {
            SequenceBlock block => block with
            {
                Branches = block.Branches.Select(branch => branch with { Statements = CloneStatements(branch.Statements) }).ToArray(),
            },
            SequenceParticipantMetadata metadata => metadata with { Values = (JsonObject)metadata.Values.DeepClone() },
            _ => s,
        }).ToList();
}
