using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

/// <summary>
/// A single declarative edit. Deliberately JSON-free so the core stays
/// AOT-clean; callers (the MCP server, the CLI) do their own deserialisation.
/// </summary>
public sealed record DiagramOperation
{
    public required string Op { get; init; }

    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Shape { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Kind { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public IReadOnlyList<string>? Ids { get; init; }
}

public sealed record OperationResult(
    bool Ok, IReadOnlyList<string> Applied, IReadOnlyList<string> Errors);

/// <summary>
/// Applies batches of <see cref="DiagramOperation"/> through the same command
/// layer the editor uses, so an agent's edits are undoable and behave
/// identically to a human's.
/// </summary>
public static class OperationApplier
{
    public static OperationResult Apply(
        DiagramModel model, EditHistory history,
        IEnumerable<DiagramOperation> operations)
    {
        var applied = new List<string>();
        var errors = new List<string>();

        foreach (var op in operations)
        {
            try
            {
                var description = ApplyOne(model, history, op);
                if (description is not null) applied.Add(description);
            }
            catch (Exception ex)
            {
                errors.Add($"{op.Op}: {ex.Message}");
            }
        }
        return new OperationResult(errors.Count == 0, applied, errors);
    }

    private static string? ApplyOne(
        DiagramModel model, EditHistory history, DiagramOperation op)
    {
        switch (op.Op.ToLowerInvariant())
        {
            case "addnode":
            {
                var requested = op.Id ?? throw new ArgumentException("addNode requires 'id'");

                // Mermaid rejects its own keywords as node ids in every form, so
                // an agent asking for `end` — the obvious id for a terminal
                // state — would otherwise produce a body that renders nowhere.
                // Escape minimally and avoid colliding with an existing node.
                var id = MermaidKeywords.MakeSafe(
                    requested, candidate => model.Node(candidate) is not null);

                if (model.Node(id) is not null)
                    throw new InvalidOperationException($"node '{id}' already exists");

                var node = new DiagramNode
                {
                    Id = id,
                    Label = op.Label,
                    Shape = ParseShape(op.Shape),
                };
                if (op.X is { } x && op.Y is { } y)
                {
                    node.X = x; node.Y = y; node.HasExplicitPosition = true;
                }
                history.Apply(new AddNodeCommand(node));
                return $"added node '{id}'";
            }

            case "connect":
            {
                // Resolve through the keyword escape: a caller who added `end`
                // got `end_`, and naming `end` here can only mean that node.
                // Without this the batch half-applies — node created, edge
                // silently missing.
                var from = MermaidKeywords.Resolve(
                    op.From ?? throw new ArgumentException("connect requires 'from'"),
                    id => model.Node(id) is not null);
                var to = MermaidKeywords.Resolve(
                    op.To ?? throw new ArgumentException("connect requires 'to'"),
                    id => model.Node(id) is not null);

                if (model.Node(from) is null)
                    throw new InvalidOperationException($"unknown node '{from}'");
                if (model.Node(to) is null)
                    throw new InvalidOperationException($"unknown node '{to}'");

                var ordinal = model.Edges.Count(e => e.From == from && e.To == to);
                history.Apply(new AddEdgeCommand(new DiagramEdge
                {
                    From = from, To = to, Label = op.Label,
                    Kind = ParseKind(op.Kind), Ordinal = ordinal,
                }));
                return $"connected '{from}' to '{to}'";
            }

            case "disconnect":
            {
                var from = op.From ?? throw new ArgumentException("disconnect requires 'from'");
                var to = op.To ?? throw new ArgumentException("disconnect requires 'to'");
                var edge = model.Edges.FirstOrDefault(e => e.From == from && e.To == to)
                    ?? throw new InvalidOperationException($"no link '{from}' -> '{to}'");
                history.Apply(new DeleteEdgeCommand(edge));
                return $"disconnected '{from}' from '{to}'";
            }

            case "rename":
            {
                var id = op.Id ?? throw new ArgumentException("rename requires 'id'");
                var node = model.Node(id)
                    ?? throw new InvalidOperationException($"unknown node '{id}'");
                history.Apply(new RenameNodeCommand(node, op.Label));
                return $"renamed '{id}'";
            }

            case "setshape":
            {
                var id = op.Id ?? throw new ArgumentException("setShape requires 'id'");
                var node = model.Node(id)
                    ?? throw new InvalidOperationException($"unknown node '{id}'");
                history.Apply(new ChangeShapeCommand(node, ParseShape(op.Shape)));
                return $"reshaped '{id}' to {ParseShape(op.Shape)}";
            }

            case "delete":
            {
                var ids = op.Ids ?? (op.Id is not null ? new[] { op.Id } : null)
                    ?? throw new ArgumentException("delete requires 'id' or 'ids'");
                var nodes = ids.Select(i => model.Node(i)
                        ?? throw new InvalidOperationException($"unknown node '{i}'"))
                    .ToList();
                history.Apply(new DeleteNodesCommand(nodes));
                return $"deleted {nodes.Count} node(s)";
            }

            case "move":
            {
                var id = op.Id ?? throw new ArgumentException("move requires 'id'");
                var node = model.Node(id)
                    ?? throw new InvalidOperationException($"unknown node '{id}'");
                var x = op.X ?? throw new ArgumentException("move requires 'x'");
                var y = op.Y ?? throw new ArgumentException("move requires 'y'");
                history.Apply(new MoveNodesCommand(
                    new[] { (node, node.X, node.Y, x, y) }));
                return $"moved '{id}'";
            }

            default:
                throw new ArgumentException($"unknown operation '{op.Op}'");
        }
    }

    public static NodeShape ParseShape(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "rect" or "rectangle" or "box" => NodeShape.Rectangle,
        "rounded" or "round" => NodeShape.RoundedRect,
        "stadium" or "pill" => NodeShape.Stadium,
        "subroutine" => NodeShape.Subroutine,
        "cylinder" or "database" or "db" => NodeShape.Cylinder,
        "circle" => NodeShape.Circle,
        "doublecircle" or "double-circle" => NodeShape.DoubleCircle,
        "diamond" or "rhombus" or "decision" => NodeShape.Rhombus,
        "hexagon" or "hex" => NodeShape.Hexagon,
        "parallelogram" => NodeShape.Parallelogram,
        "trapezoid" => NodeShape.Trapezoid,
        "asymmetric" => NodeShape.Asymmetric,
        _ => throw new ArgumentException($"unknown shape '{name}'"),
    };

    /// <summary>
    /// Map an agent-supplied link name to a kind.
    ///
    /// <para>"dotted" and "thick" resolve to the ARROWED variants, because that
    /// is what those words mean in practice and what mermaid users write. The
    /// arrowless forms need the explicit "dottedLine" / "thickLine" names —
    /// otherwise an agent asking for a dotted link silently gets `-.-` and a
    /// diagram that means something different.</para>
    /// </summary>
    public static EdgeKind ParseKind(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "arrow" => EdgeKind.Arrow,
        "open" or "line" => EdgeKind.Open,
        "dotted" or "dottedarrow" or "dotted-arrow" => EdgeKind.DottedArrow,
        "dottedline" or "dotted-line" => EdgeKind.Dotted,
        "thick" or "thickarrow" or "thick-arrow" => EdgeKind.ThickArrow,
        "thickline" or "thick-line" => EdgeKind.Thick,
        "invisible" => EdgeKind.Invisible,
        _ => throw new ArgumentException($"unknown link kind '{name}'"),
    };
}
