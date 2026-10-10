using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Adapter for <c>stateDiagram</c> / <c>stateDiagram-v2</c>.
///
/// <para>State diagrams are the same shape as flowcharts — things and
/// transitions — so they reuse the whole canvas, the command layer and Merpeeps.
/// Only the grammar differs, which is the entire argument for the adapter
/// pattern.</para>
///
/// <para>Same safety rule as the flowchart parser: anything not modelled
/// (notes, forks, concurrency markers) is preserved verbatim.</para>
/// </summary>
public sealed class StateAdapter : IDiagramAdapter
{
    /// <summary>Synthetic ids for the <c>[*]</c> pseudo-states.</summary>
    public const string StartId = "__start";
    public const string EndId = "__end";

    private static readonly Regex Header = new(
        @"^\s*(?<kind>stateDiagram(?:-v2)?)\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DirectionLine = new(
        @"^\s*direction\s+(?<dir>TB|TD|BT|LR|RL)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Still --> Moving : event
    private static readonly Regex Transition = new(
        @"^\s*(?<from>\[\*\]|[A-Za-z0-9_\-\.]+)\s*-->\s*" +
        @"(?<to>\[\*\]|[A-Za-z0-9_\-\.]+)\s*(?::\s*(?<label>.*?))?\s*$",
        RegexOptions.Compiled);

    // state "Long description" as id
    private static readonly Regex NamedState = new(
        @"^\s*state\s+""(?<desc>[^""]*)""\s+as\s+(?<id>[A-Za-z0-9_\-\.]+)\s*$",
        RegexOptions.Compiled);

    // state Composite {
    private static readonly Regex CompositeOpen = new(
        @"^\s*state\s+(?<id>[A-Za-z0-9_\-\.]+)\s*\{\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BlockEnd = new(@"^\s*\}\s*$", RegexOptions.Compiled);

    // A bare state declaration on its own line
    private static readonly Regex BareState = new(
        @"^\s*(?<id>[A-Za-z0-9_\-\.]+)\s*$", RegexOptions.Compiled);

    public string Kind => "stateDiagram-v2";

    public bool CanParse(string body) =>
        DiagramAdapters.FirstKeyword(body).ToLowerInvariant() is "statediagram" or "statediagram-v2";

    public DiagramModel Parse(string source)
    {
        var model = new DiagramModel { Kind = "stateDiagram-v2" };
        var body = MerpeepsDoc.Split(source).Body.Replace("\r\n", "\n");
        var lines = body.Split('\n');
        model.LineCount = lines.Length;

        var composites = new Stack<DiagramSubgraph>();
        var headerSeen = false;
        var inDirective = false;
        var ordinals = new Dictionary<string, int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (DiagramAdapters.IsCommentLine(raw.Trim(), ref inDirective))
            {
                model.Passthrough[i] = raw;
                continue;
            }
            var line = StripComment(raw).Trim();
            if (line.Length == 0) { model.Passthrough[i] = raw; continue; }

            if (!headerSeen)
            {
                var h = Header.Match(line);
                if (h.Success)
                {
                    headerSeen = true;
                    model.Kind = h.Groups["kind"].Value;
                    continue;
                }
            }

            if (DirectionLine.IsMatch(line))
            {
                model.Direction = DirectionLine.Match(line).Groups["dir"].Value
                    .ToUpperInvariant() switch
                {
                    "LR" => Direction.LR, "RL" => Direction.RL,
                    "BT" => Direction.BT, _ => Direction.TB,
                };
                continue;
            }

            var comp = CompositeOpen.Match(line);
            if (comp.Success)
            {
                var sg = new DiagramSubgraph
                {
                    Id = comp.Groups["id"].Value,
                    ParentId = composites.TryPeek(out var parent) ? parent.Id : null,
                };
                model.Subgraphs.Add(sg);
                composites.Push(sg);
                continue;
            }

            if (BlockEnd.IsMatch(line) && composites.Count > 0)
            {
                composites.Pop();
                continue;
            }

            var named = NamedState.Match(line);
            if (named.Success)
            {
                var n = Upsert(model, composites, named.Groups["id"].Value, i);
                n.Label = named.Groups["desc"].Value;
                continue;
            }

            var t = Transition.Match(line);
            if (t.Success)
            {
                var from = Upsert(model, composites, MapId(t.Groups["from"].Value, true), i);
                var to = Upsert(model, composites, MapId(t.Groups["to"].Value, false), i);

                var key = $"{from.Id}->{to.Id}";
                ordinals.TryGetValue(key, out var ord);
                ordinals[key] = ord + 1;

                model.Edges.Add(new DiagramEdge
                {
                    From = from.Id,
                    To = to.Id,
                    Label = t.Groups["label"].Success && t.Groups["label"].Value.Length > 0
                        ? t.Groups["label"].Value.Trim() : null,
                    Kind = EdgeKind.Arrow,
                    SourceLine = i,
                    Ordinal = ord,
                });
                continue;
            }

            var bare = BareState.Match(line);
            if (bare.Success && !IsKeyword(bare.Groups["id"].Value))
            {
                Upsert(model, composites, bare.Groups["id"].Value, i);
                continue;
            }

            model.Passthrough[i] = raw;
        }

        return model;
    }

    public string Write(DiagramModel model)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrEmpty(model.Kind) ? "stateDiagram-v2" : model.Kind)
          .Append('\n');

        if (model.Direction != Direction.TB)
            sb.Append("    direction ").Append(model.Direction).Append('\n');

        // Descriptions only need declaring when they differ from the id.
        foreach (var n in model.Nodes)
        {
            if (IsPseudo(n.Id)) continue;
            if (n.Label is null || n.Label == n.Id) continue;
            sb.Append("    state \"").Append(n.Label).Append("\" as ")
              .Append(n.Id).Append('\n');
        }

        foreach (var e in model.Edges)
        {
            sb.Append("    ").Append(Render(e.From)).Append(" --> ").Append(Render(e.To));
            if (!string.IsNullOrEmpty(e.Label)) sb.Append(" : ").Append(e.Label);
            sb.Append('\n');
        }

        // States with no transitions would otherwise vanish.
        foreach (var n in model.Nodes)
        {
            if (IsPseudo(n.Id)) continue;
            if (model.Edges.Any(e => e.From == n.Id || e.To == n.Id)) continue;
            if (n.Label is not null && n.Label != n.Id) continue;   // already declared
            sb.Append("    ").Append(n.Id).Append('\n');
        }

        foreach (var (_, text) in model.Passthrough)
        {
            if (text.Trim().Length == 0) continue;
            sb.Append(text.TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------- helpers

    public string? RenderNodeStatement(DiagramNode node)
    {
        if (IsPseudo(node.Id)) return null;
        return node.Label is not null && node.Label != node.Id
            ? $"state \"{node.Label}\" as {node.Id}"
            : node.Id;
    }

    public string RenderEdgeStatement(DiagramEdge edge)
    {
        var s = $"{Render(edge.From)} --> {Render(edge.To)}";
        return string.IsNullOrEmpty(edge.Label) ? s : $"{s} : {edge.Label}";
    }

    /// <summary>
    /// Not supported for state diagrams.
    /// </summary>
    /// <remarks>
    /// A state transition's label is written after the arrow as <c>: text</c>
    /// rather than inside the operator, so there is no single span to swap and
    /// this parser does not record one. Returning null makes the writer
    /// regenerate — correct, just not minimal.
    /// </remarks>
    public string? RenderLinkOperator(DiagramEdge edge) => null;

    public static bool IsPseudo(string id) => id is StartId or EndId;

    private static string Render(string id) => id switch
    {
        StartId or EndId => "[*]",
        _ => id,
    };

    /// <summary>
    /// <c>[*]</c> means "start" in the source position and "end" in the target
    /// position, so it maps to two distinct synthetic nodes.
    /// </summary>
    private static string MapId(string token, bool isSource) =>
        token == "[*]" ? (isSource ? StartId : EndId) : token;

    private static DiagramNode Upsert(
        DiagramModel model, Stack<DiagramSubgraph> composites, string id, int line)
    {
        var existing = model.Node(id);
        if (existing is not null) return existing;

        var node = new DiagramNode
        {
            Id = id,
            SourceLine = line,
            Shape = id switch
            {
                StartId => NodeShape.Circle,
                EndId => NodeShape.DoubleCircle,
                _ => NodeShape.RoundedRect,
            },
            Label = IsPseudo(id) ? "" : null,
        };
        model.Nodes.Add(node);

        if (composites.Count > 0)
        {
            var sg = composites.Peek();
            node.SubgraphId = sg.Id;
            sg.NodeIds.Add(id);
        }
        return node;
    }

    private static bool IsKeyword(string s) =>
        s is "state" or "note" or "end" or "direction" or "class";

    private static string StripComment(string line)
    {
        var i = line.IndexOf("%%", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }
}
