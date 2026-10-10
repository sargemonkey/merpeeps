using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public static class ZenSemantics
{
    public static void RebindContext(ZenDiagramDocument d)
    {
        var statements = Bind(d.Statements, null, null); d.Statements.Clear(); d.Statements.AddRange(statements);
        static IReadOnlyList<ZenStatement> Bind(IEnumerable<ZenStatement> statements, string? sender, string? replyTo) => statements.Select(s =>
        {
            var from = s.Kind is ZenStatementKind.Call or ZenStatementKind.Create && !s.ExplicitSender ? sender : s.From;
            if (s.Kind == ZenStatementKind.Return)
            {
                if (sender is null) throw new ArgumentException("A return must be inside a call or creation.");
                return s with { From = sender, To = replyTo };
            }
            var nestedSender = s.Kind is ZenStatementKind.Call or ZenStatementKind.Create ? s.To : sender;
            var nestedReply = s.Kind is ZenStatementKind.Call or ZenStatementKind.Create ? from : replyTo;
            return s with { From = from, Children = Bind(s.Children, nestedSender, nestedReply), Branches = s.Branches.Select(b => b with { Statements = Bind(b.Statements, sender, replyTo) }).ToArray() };
        }).ToArray();
    }
    public static IEnumerable<ZenStatement> Walk(IEnumerable<ZenStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            foreach (var child in Walk(statement.Children)) yield return child;
            foreach (var branch in statement.Branches) foreach (var child in Walk(branch.Statements)) yield return child;
        }
    }
    public static void Check(ZenDiagramDocument d)
    {
        var ids = new HashSet<string>(); var names = new HashSet<string>();
        foreach (var participant in d.Participants)
        {
            Id(participant.Id);
            if (!Regex.IsMatch(participant.SourceId, @"^[A-Za-z_][A-Za-z0-9_]*$") || !names.Add(participant.SourceId) || !Enum.IsDefined(participant.Kind)) throw new ArgumentException("ZenUML participants need unique source identifiers.");
            if (participant.Annotator is not (null or "Actor" or "Database")) throw new DataDiagramNotEditableException([new("zen-annotator", participant.Id, "This participant annotator is retained in source but has no native symbol: " + participant.Annotator, true)]);
            if (participant.Alias?.IndexOfAny(['\r', '\n', '{', '}']) >= 0) throw new ArgumentException("ZenUML aliases must be single-line text.");
        }
        foreach (var statement in Walk(d.Statements))
        {
            Id(statement.Id);
            if (!Enum.IsDefined(statement.Kind)) throw new ArgumentException("Unknown ZenUML statement.");
            if (statement.From is not null && d.Participants.All(p => p.Id != statement.From) || statement.To is not null && d.Participants.All(p => p.Id != statement.To))
                throw new ArgumentException("ZenUML participant reference does not exist.");
            if (statement.Kind is ZenStatementKind.Call or ZenStatementKind.Create or ZenStatementKind.AsyncMessage or ZenStatementKind.ReplyMessage && statement.To is null)
                throw new ArgumentException("ZenUML messages need a recipient.");
            if (statement.Kind is ZenStatementKind.AsyncMessage or ZenStatementKind.ReplyMessage && statement.From is null) throw new ArgumentException("Async/reply messages need a sender.");
            if (statement.Kind == ZenStatementKind.Call && !Regex.IsMatch(statement.Text, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new ArgumentException("ZenUML method names must be identifiers.");
            if (statement.Children.Count > 0 && statement.Kind is not (ZenStatementKind.Call or ZenStatementKind.Create or ZenStatementKind.Loop or ZenStatementKind.Conditional or ZenStatementKind.Optional or ZenStatementKind.Parallel or ZenStatementKind.Try))
                throw new ArgumentException("This ZenUML statement cannot contain child statements.");
            if (statement.Kind != ZenStatementKind.Comment && statement.Text.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("ZenUML messages and conditions must be one line.");
            foreach (var branch in statement.Branches)
            {
                Id(branch.Id);
                if (statement.Kind == ZenStatementKind.Conditional && branch.Keyword is not ("else" or "else if") ||
                    statement.Kind == ZenStatementKind.Try && branch.Keyword is not ("catch" or "finally") ||
                    statement.Kind is not (ZenStatementKind.Conditional or ZenStatementKind.Try))
                    throw new ArgumentException("Invalid ZenUML branch ownership.");
            }
            if (statement.Assignment is not null && !Regex.IsMatch(statement.Assignment, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new ArgumentException("Assignment variable must be an identifier.");
            if (statement.Arguments?.IndexOfAny(['\r', '\n', '{', '}']) >= 0) throw new ArgumentException("Arguments must remain an inert single-line parameter list.");
        }
        if (d.Title.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("ZenUML title must be one line.");
        void Id(string id) { if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("ZenUML identities must be unique."); }
    }
}
