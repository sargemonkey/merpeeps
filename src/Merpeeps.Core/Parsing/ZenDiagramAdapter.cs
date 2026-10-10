using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public sealed class ZenDiagramAdapter : IDiagramAdapter
{
    public string Kind => "zenuml";
    public bool SupportsGraphLayout => false;
    public bool CanParse(string body) => DiagramAdapters.FirstKeyword(body) == Kind;
    public DiagramModel Parse(string source) => new Parser(source).Parse();
    internal static string Signature(ZenStatement statement) => statement.Kind + "\0" + statement.From + "\0" + statement.To + "\0" +
        statement.Text + "\0" + statement.Arguments + "\0" + statement.Assignment + "\0" + statement.Keyword;
    public string Write(DiagramModel model) => Write(model.Zen ?? throw new ArgumentException("ZenUML content is missing."));
    public static string Write(ZenDiagramDocument d)
    {
        ZenSemantics.Check(d); var b = new StringBuilder("zenuml\n");
        if (d.Title.Length > 0) b.Append("title ").Append(d.Title).Append('\n');
        foreach (var participant in d.Participants.Where(p => p.Declared || p.Alias is not null || p.Annotator is not null))
        {
            foreach (var comment in participant.Comments) b.Append("// ").Append(comment).Append('\n');
            if (participant.Annotator is not null) b.Append('@').Append(participant.Annotator).Append(' ');
            else if (participant.Kind != ZenParticipantKind.Participant) b.Append('@').Append(participant.Kind).Append(' ');
            b.Append(participant.SourceId);
            if (participant.Alias is not null) b.Append(" as ").Append(participant.Alias);
            b.Append('\n');
        }
        Statements(d.Statements, 0);
        return b.ToString();
        string Participant(string? id) => id is null ? "" : d.Participants.Single(p => p.Id == id).SourceId;
        void Statements(IEnumerable<ZenStatement> statements, int depth)
        {
            foreach (var statement in statements)
            {
                b.Append(' ', depth * 2);
                if (statement.Kind == ZenStatementKind.Comment)
                {
                    b.Append("// ").Append(statement.Text.Replace("\n", "\n" + new string(' ', depth * 2) + "// ")).Append('\n'); continue;
                }
                if (statement.Kind is ZenStatementKind.AsyncMessage or ZenStatementKind.ReplyMessage)
                {
                    if (statement.Kind == ZenStatementKind.ReplyMessage) b.Append('@').Append(statement.Keyword.Length == 0 ? "return" : statement.Keyword).Append('\n').Append(' ', depth * 2);
                    b.Append(Participant(statement.From)).Append("->").Append(Participant(statement.To)).Append(": ").Append(statement.Text).Append('\n'); continue;
                }
                if (statement.Kind == ZenStatementKind.Return) { b.Append("return ").Append(statement.Text).Append('\n'); continue; }
                if (statement.Kind is ZenStatementKind.Call or ZenStatementKind.Create)
                {
                    if (statement.Assignment is not null)
                        b.Append(statement.AssignmentType is null ? "" : statement.AssignmentType + " ").Append(statement.Assignment).Append(" = ");
                    if (statement.ExplicitSender && statement.From is not null) b.Append(Participant(statement.From)).Append("->");
                    if (statement.Kind == ZenStatementKind.Create) b.Append("new ").Append(Participant(statement.To));
                    else b.Append(Participant(statement.To)).Append('.').Append(statement.Text);
                    if (statement.Arguments is not null) b.Append('(').Append(statement.Arguments).Append(')');
                    if (statement.Children.Count > 0)
                    {
                        b.Append(" {\n"); Statements(statement.Children, depth + 1); b.Append(' ', depth * 2).Append("}\n");
                    }
                    else b.Append('\n');
                    continue;
                }
                var keyword = statement.Kind switch { ZenStatementKind.Conditional => "if", ZenStatementKind.Optional => "opt", ZenStatementKind.Parallel => "par", ZenStatementKind.Try => "try", _ => statement.Keyword.Length == 0 ? "loop" : statement.Keyword };
                b.Append(keyword);
                if (statement.Kind is ZenStatementKind.Loop or ZenStatementKind.Conditional) b.Append('(').Append(statement.Text).Append(')');
                b.Append(" {\n"); Statements(statement.Children, depth + 1); b.Append(' ', depth * 2).Append('}');
                foreach (var branch in statement.Branches)
                {
                    b.Append(' ').Append(branch.Keyword);
                    if (branch.Condition.Length > 0) b.Append('(').Append(branch.Condition).Append(')');
                    b.Append(" {\n"); Statements(branch.Statements, depth + 1); b.Append(' ', depth * 2).Append('}');
                }
                b.Append('\n');
            }
        }
    }
    internal string Apply(string source, DiagramModel model)
    {
        var d = model.Zen!; var before = Parse(source).Zen!;
        var result = Write(before) == Write(d) ? source : MerpeepsDoc.ReplaceBody(source, Write(d).Replace("\n", source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(before.Configuration, d.Configuration) || !System.Text.Json.Nodes.JsonNode.DeepEquals(before.ThemeVariables, d.ThemeVariables) ||
            !System.Text.Json.Nodes.JsonNode.DeepEquals(before.GlobalConfiguration, d.GlobalConfiguration))
            result = DataDiagramSupport.ApplyConfiguration(result, "zenuml", d.Configuration, d.ThemeVariables, d.GlobalConfiguration);
        return result;
    }
    internal static object Identity(string source, ZenDiagramDocument d)
    {
        var ids = new DataIdentity(source, "zenuml");
        var entries = d.Participants.Select(p => ids.Entry(p.Id, "participant:" + p.SourceId)).ToList();
        Add(d.Statements);
        return new Dictionary<string, object?> { ["spec"] = 1, ["entries"] = entries };
        void Add(IEnumerable<ZenStatement> statements)
        {
            foreach (var statement in statements)
            {
                entries.Add(ids.Entry(statement.Id, Signature(statement))); Add(statement.Children);
                foreach (var branch in statement.Branches) { entries.Add(ids.Entry(branch.Id, statement.Id + "\0" + branch.Keyword + "\0" + branch.Condition)); Add(branch.Statements); }
            }
        }
    }
    public string? RenderNodeStatement(DiagramNode node) => throw new NotSupportedException("ZenUML participants and calls have their own grammar.");
    public string RenderEdgeStatement(DiagramEdge edge) => throw new NotSupportedException("ZenUML messages are ordered nested statements.");
    public string? RenderLinkOperator(DiagramEdge edge) => null;
    private sealed class Parser
    {
        private readonly DataIdentity _ids; private readonly ZenDiagramDocument _document = new(); private readonly List<string> _lines = new(); private int _at;
        public Parser(string source)
        {
            _ids = new(source, "zenuml"); var config = DataDiagramSupport.Configuration(source, "zenuml");
            _document.Configuration = config.Configuration; _document.ThemeVariables = config.Theme; _document.GlobalConfiguration = config.Global;
            foreach (var raw in MerpeepsDoc.BodyOf(source).Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim(); if (line.Length == 0) continue;
                while (line.StartsWith('}')) { _lines.Add("}"); line = line[1..].Trim(); }
                if (line.Length > 0) _lines.Add(line);
            }
        }
        public DiagramModel Parse()
        {
            if (_lines.Count == 0 || _lines[0] != "zenuml") throw new FormatException("Expected zenuml.");
            _at = 1; var statements = Statements(null, null, false);
            _document.Statements.AddRange(statements.Select(Identify));
            ZenSemantics.Check(_document); return new() { Kind = "zenuml", Zen = _document };
        }
        private IReadOnlyList<ZenStatement> Statements(string? sender, string? replyTo, bool nested)
        {
            var statements = new List<ZenStatement>(); var comments = new List<string>(); string? replyKeyword = null;
            while (_at < _lines.Count)
            {
                var line = _lines[_at++];
                if (line == "}")
                {
                    if (!nested) throw new FormatException("Unexpected ZenUML close.");
                    FlushComments(); return statements;
                }
                if (line.StartsWith("//", StringComparison.Ordinal)) { comments.Add(line[2..].TrimStart()); continue; }
                if (line.StartsWith("%%", StringComparison.Ordinal)) { comments.Add(line[2..].TrimStart()); continue; }
                if (line.StartsWith("title ", StringComparison.Ordinal) && !nested) { _document.Title = line[6..]; continue; }
                if (line is "@return" or "@reply") { replyKeyword = line[1..]; continue; }
                var actor = Regex.Match(line, @"^(?:@(?<annotation>[A-Za-z0-9_]+)\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s+as\s+(?<alias>.+))?$");
                if (actor.Success && line is not ("opt" or "par" or "try" or "return"))
                {
                    var id = Participant(actor.Groups["name"].Value); var index = _document.Participants.FindIndex(p => p.Id == id);
                    var annotation = actor.Groups["annotation"].Success ? actor.Groups["annotation"].Value : null;
                    _document.Participants[index] = _document.Participants[index] with
                    {
                        Declared = true, Alias = actor.Groups["alias"].Success ? actor.Groups["alias"].Value : null, Annotator = annotation,
                        Kind = annotation switch { "Actor" => ZenParticipantKind.Actor, "Database" => ZenParticipantKind.Database, _ => ZenParticipantKind.Participant },
                        Comments = comments.ToArray(),
                    };
                    comments.Clear(); continue;
                }
                FlushComments();
                var async = Regex.Match(line, @"^(?<from>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*(?<to>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<text>.*)$");
                if (async.Success)
                {
                    statements.Add(new("", replyKeyword is null ? ZenStatementKind.AsyncMessage : ZenStatementKind.ReplyMessage)
                    {
                        From = Participant(async.Groups["from"].Value), To = Participant(async.Groups["to"].Value), Text = async.Groups["text"].Value,
                        Keyword = replyKeyword ?? "", ExplicitSender = true,
                    });
                    replyKeyword = null; continue;
                }
                if (line.StartsWith("return ", StringComparison.Ordinal))
                {
                    if (!nested || sender is null) throw new FormatException("A ZenUML return must be inside a synchronous call.");
                    statements.Add(new("", ZenStatementKind.Return) { From = sender, To = replyTo, Text = line[7..] }); continue;
                }
                var block = Regex.Match(line, @"^(?<kind>if|while|for|forEach|foreach|loop|opt|par|try)\s*(?:\((?<condition>.*)\))?\s*\{$");
                if (block.Success)
                {
                    var keyword = block.Groups["kind"].Value;
                    var kind = keyword switch { "if" => ZenStatementKind.Conditional, "opt" => ZenStatementKind.Optional, "par" => ZenStatementKind.Parallel, "try" => ZenStatementKind.Try, _ => ZenStatementKind.Loop };
                    var children = Statements(sender, replyTo, true); var branches = new List<ZenBranch>();
                    while (_at < _lines.Count && Regex.Match(_lines[_at], @"^(?<keyword>else if|else|catch|finally)\s*(?:\((?<condition>.*)\))?\s*\{$") is { Success: true } branch)
                    {
                        _at++; branches.Add(new("", branch.Groups["keyword"].Value, branch.Groups["condition"].Value, Statements(sender, replyTo, true)));
                    }
                    statements.Add(new("", kind) { Keyword = keyword, Text = block.Groups["condition"].Value, Children = children, Branches = branches }); continue;
                }
                var open = line.EndsWith('{'); var callText = open ? line[..^1].TrimEnd() : line;
                var call = Regex.Match(callText, @"^(?:(?:(?<type>[A-Za-z_][A-Za-z0-9_<>,.\[\]]*)\s+)?(?<assignment>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?:(?<from>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*)?(?:(?<new>new)\s+)?(?<to>[A-Za-z_][A-Za-z0-9_]*)(?:\.(?<method>[A-Za-z_][A-Za-z0-9_]*))?(?:\((?<arguments>.*)\))?$");
                if (!call.Success || !call.Groups["new"].Success && !call.Groups["method"].Success) throw new FormatException("Unsupported ZenUML statement: " + line);
                var target = Participant(call.Groups["to"].Value);
                var from = call.Groups["from"].Success ? Participant(call.Groups["from"].Value) : sender;
                var inner = open ? Statements(target, from, true) : [];
                statements.Add(new("", call.Groups["new"].Success ? ZenStatementKind.Create : ZenStatementKind.Call)
                {
                    From = from, To = target, ExplicitSender = call.Groups["from"].Success, Text = call.Groups["method"].Value,
                    Arguments = call.Groups["arguments"].Success ? call.Groups["arguments"].Value : null,
                    Assignment = call.Groups["assignment"].Success ? call.Groups["assignment"].Value : null,
                    AssignmentType = call.Groups["type"].Success ? call.Groups["type"].Value : null,
                    Children = inner,
                });
            }
            if (nested) throw new FormatException("Unclosed ZenUML call or fragment.");
            FlushComments(); return statements;
            void FlushComments()
            {
                if (comments.Count == 0) return;
                statements.Add(new("", ZenStatementKind.Comment) { Text = string.Join('\n', comments) }); comments.Clear();
            }
        }
        private string Participant(string sourceId)
        {
            var old = _document.Participants.FirstOrDefault(p => p.SourceId == sourceId); if (old is not null) return old.Id;
            var id = _ids.Next("participant:" + sourceId); _document.Participants.Add(new(id, sourceId)); return id;
        }
        private ZenStatement Identify(ZenStatement statement)
        {
            var id = _ids.Next(Signature(statement));
            return statement with { Id = id, Children = statement.Children.Select(Identify).ToArray(), Branches = statement.Branches.Select(branch =>
                branch with { Id = _ids.Next(id + "\0" + branch.Keyword + "\0" + branch.Condition), Statements = branch.Statements.Select(Identify).ToArray() }).ToArray() };
        }
    }
}
