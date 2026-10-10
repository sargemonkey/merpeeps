using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum AgentFlowElementKind { Node, Flow, Connector }
public enum AgentFlowLinkKind { Sequence, Reference, Failure }
public sealed record AgentFlowElement(string Id, string SourceId, AgentFlowElementKind Kind, string Label)
{
    public string? ParentId { get; init; }
    public bool Global { get; init; }
    public JsonObject Metadata { get; init; } = new();
    public string Shape => Metadata["shape"]?.ToString() ?? "rect";
    public string Display => Metadata["label"]?.ToString() ?? Label;
    public bool Markdown => Metadata["labelType"]?.ToString() == "markdown";
    public bool Collapsed => Kind == AgentFlowElementKind.Flow && Metadata["view"]?.ToString() == "collapsed";
    public AgentFlowElement DeepCopy() => this with { Metadata = (JsonObject)Metadata.DeepClone() };
}
public sealed record AgentFlowLink(string Id, string From, string To, AgentFlowLinkKind Kind = AgentFlowLinkKind.Sequence, string Label = "")
{
    public string? SourceId { get; init; }
    public JsonObject Metadata { get; init; } = new();
    public AgentFlowLink DeepCopy() => this with { Metadata = (JsonObject)Metadata.DeepClone() };
}
public sealed class AgentFlowDocument
{
    public Direction Direction { get; set; } = Direction.TB;
    public List<AgentFlowElement> Elements { get; } = new();
    public List<AgentFlowLink> Links { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public AgentFlowDocument Clone()
    {
        var copy = new AgentFlowDocument
        {
            Direction = Direction, Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Elements.AddRange(Elements.Select(e => e.DeepCopy())); copy.Links.AddRange(Links.Select(l => l.DeepCopy())); copy.Comments.AddRange(Comments); return copy;
    }
}
public enum EventEntityKind { UserInterface, Processor, Command, ReadModel, Event }
public enum EventDataKind { Json, JsObject, Figma, Salt, Uri, Markdown, Html, Text }
public sealed record EventDataValue(EventDataKind Kind, string Source, bool ExplicitKind = false);
public sealed record EventDataBlock(string Id, string Name, EventDataValue Data);
public sealed record EventFrame(string Id, string Number, EventEntityKind Kind, string EntityName)
{
    public bool Reset { get; init; }
    public bool RelaxedSyntax { get; init; }
    public EventDataValue? InlineData { get; init; }
    public string? DataBlockId { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    public string? Namespace => EntityName.Contains('.') ? EntityName[..EntityName.IndexOf('.')] : null;
    public EventFrame DeepCopy() => this with { References = References.ToArray() };
}
public sealed record EventRelation(string From, string To, bool Inferred);
public sealed class EventModelingDocument
{
    public List<EventFrame> Frames { get; } = new();
    public List<EventDataBlock> DataBlocks { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public EventModelingDocument Clone()
    {
        var copy = new EventModelingDocument
        {
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Frames.AddRange(Frames.Select(f => f.DeepCopy())); copy.DataBlocks.AddRange(DataBlocks); copy.Comments.AddRange(Comments); return copy;
    }
}
public enum ZenParticipantKind { Participant, Actor, Database }
public enum ZenStatementKind { Call, AsyncMessage, Create, Return, ReplyMessage, Loop, Conditional, Optional, Parallel, Try, Comment }
public sealed record ZenParticipant(string Id, string SourceId, string? Alias = null, ZenParticipantKind Kind = ZenParticipantKind.Participant)
{
    public bool Declared { get; init; }
    public string? Annotator { get; init; }
    public IReadOnlyList<string> Comments { get; init; } = [];
}
public sealed record ZenBranch(string Id, string Keyword, string Condition, IReadOnlyList<ZenStatement> Statements)
{
    public ZenBranch DeepCopy() => this with { Statements = Statements.Select(s => s.DeepCopy()).ToArray() };
}
public sealed record ZenStatement(string Id, ZenStatementKind Kind)
{
    public string? From { get; init; }
    public string? To { get; init; }
    public string Text { get; init; } = "";
    public string? Arguments { get; init; }
    public string? Assignment { get; init; }
    public string? AssignmentType { get; init; }
    public string Keyword { get; init; } = "";
    public bool ExplicitSender { get; init; }
    public IReadOnlyList<ZenStatement> Children { get; init; } = [];
    public IReadOnlyList<ZenBranch> Branches { get; init; } = [];
    public ZenStatement DeepCopy() => this with { Children = Children.Select(c => c.DeepCopy()).ToArray(), Branches = Branches.Select(b => b.DeepCopy()).ToArray() };
}
public sealed class ZenDiagramDocument
{
    public string Title { get; set; } = "";
    public List<ZenParticipant> Participants { get; } = new();
    public List<ZenStatement> Statements { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public ZenDiagramDocument Clone()
    {
        var copy = new ZenDiagramDocument
        {
            Title = Title, Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Participants.AddRange(Participants.Select(p => p with { Comments = p.Comments.ToArray() })); copy.Statements.AddRange(Statements.Select(s => s.DeepCopy())); return copy;
    }
}
