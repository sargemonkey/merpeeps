namespace Merpeeps.Core.Model;

public enum NodeShape
{
    Rectangle, RoundedRect, Stadium, Subroutine, Cylinder, Circle,
    DoubleCircle, Rhombus, Hexagon, Parallelogram, ParallelogramAlt,
    Trapezoid, TrapezoidAlt, Asymmetric,
}

public enum EdgeKind { Arrow, Open, Dotted, DottedArrow, Thick, ThickArrow, Invisible }

public enum Direction { TB, BT, LR, RL }

public sealed class DiagramNode
{
    public required string Id { get; init; }
    public string? Label { get; set; }
    public NodeShape Shape { get; set; } = NodeShape.Rectangle;
    public string? SubgraphId { get; set; }
    public ClassNodeDefinition? Class { get; set; }
    public EntityNodeDefinition? Entity { get; set; }

    /// <summary>Index of the source line that declared this node.</summary>
    public int SourceLine { get; set; } = -1;

    /// <summary>
    /// Character span of the declaration token (for example <c>A[Label]</c>)
    /// within its source line. Enables surgical text edits instead of
    /// regenerating the whole document.
    /// </summary>
    public int DeclStart { get; set; } = -1;
    public int DeclLength { get; set; }

    // Presentation, populated from Merpeeps or by the layout engine.
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 56;
    public bool HasExplicitPosition { get; set; }
    public bool HasExplicitSize { get; set; }

    // Presentation carried in the Merpeeps style block. None of this is expressible
    // in mermaid syntax, which is exactly why it lives in the metadata layer:
    // a stock renderer ignores it and still draws a correct plain diagram.
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public string? TextColor { get; set; }
    public double? StrokeWidth { get; set; }
    public bool Dashed { get; set; }

    /// <summary>
    /// Icon name (for example <c>aws:lambda</c>, <c>azure:sql</c>). Rendering an
    /// icon in place of a box is among the most-requested node features in
    /// mermaid (issue #1723, 122 👍, open since 2020) and is unavailable in
    /// mermaid syntax.
    /// </summary>
    public string? Icon { get; set; }

    public string Display => Label ?? Class?.ReferenceName ?? Id;
}

public sealed class DiagramEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? Label { get; set; }
    public EdgeKind Kind { get; set; } = EdgeKind.Arrow;
    public ClassRelationship? ClassRelationship { get; set; }
    public EntityRelationship? EntityRelationship { get; set; }
    public int SourceLine { get; set; } = -1;

    /// <summary>
    /// Character span of the link operator — <c>--&gt;</c>, or <c>--&gt;|text|</c>
    /// when it carries a label — within its source line.
    /// </summary>
    /// <remarks>
    /// Lets a relabel or a style change rewrite one operator rather than
    /// regenerating the whole document. -1 means the span is unknown, and the
    /// writer falls back to regeneration rather than guessing.
    /// </remarks>
    public int OpStart { get; set; } = -1;
    public int OpLength { get; set; }

    /// <summary>Ordinal among edges sharing the same endpoints — part of Merpeeps identity.</summary>
    public int Ordinal { get; set; }

    public string Key => $"{From}->{To}#{Ordinal}";
}

public sealed class DiagramSubgraph
{
    public required string Id { get; init; }
    public string? Title { get; set; }
    public string? ParentId { get; set; }
    public bool IsExplicit { get; set; } = true;
    public Direction? Direction { get; set; }
    public List<string> NodeIds { get; } = new();
}

/// <summary>
/// A parsed mermaid diagram.
///
/// <para>Anything the parser does not understand is kept in
/// <see cref="Passthrough"/> keyed by source line, so serialising never
/// destroys syntax we did not model. That is what makes the editor safe to
/// point at a real file.</para>
/// </summary>
public sealed class DiagramModel
{
    public string Kind { get; set; } = "flowchart";
    public Direction Direction { get; set; } = Direction.TB;
    public ClassDiagramOptions? ClassOptions { get; set; }
    public EntityDiagramOptions? EntityOptions { get; set; }
    public SequenceDiagramModel? Sequence { get; set; }
    public ChartDocument? Chart { get; set; }
    public OutlineDocument? Outline { get; set; }
    public TemporalDocument? Temporal { get; set; }
    public PacketDocument? Packet { get; set; }
    public KanbanDocument? Kanban { get; set; }
    public RequirementDocument? Requirements { get; set; }
    public GitGraphDocument? GitDiagram { get; set; }
    public SankeyDocument? Sankey { get; set; }
    public TreemapDocument? Treemap { get; set; }
    public BlockDiagramDocument? Blocks { get; set; }
    public ArchitectureDiagramDocument? Architecture { get; set; }
    public C4DiagramDocument? C4 { get; set; }
    public GanttDiagramDocument? Gantt { get; set; }
    public IshikawaDocument? Ishikawa { get; set; }
    public CynefinDocument? Cynefin { get; set; }
    public WardleyDiagramDocument? Wardley { get; set; }
    public RailroadDiagramDocument? Railroad { get; set; }
    public VennDiagramDocument? Venn { get; set; }
    public SwimlaneDiagramDocument? Swimlane { get; set; }
    public UseCaseDiagramDocument? UseCase { get; set; }
    public AgentFlowDocument? AgentFlow { get; set; }
    public EventModelingDocument? EventModeling { get; set; }
    public ZenDiagramDocument? Zen { get; set; }

    /// <summary>
    /// Which layout engine produced the stored coordinates, or "manual" when a
    /// person placed them. Round-trips through <c>layout.engine</c> so a
    /// reopened document keeps arranging new nodes the same way.
    /// </summary>
    public string? LayoutEngine { get; set; }

    /// <summary>
    /// The mermaid theme, from <c>config: theme:</c> — one of
    /// <c>default</c>, <c>dark</c>, <c>forest</c>, <c>neutral</c>, <c>base</c>,
    /// or null when the file does not name one.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of the Merpeeps block. This is mermaid's own
    /// frontmatter key, so a themed diagram looks the same in every renderer
    /// with no plugin present. The editor reads it to colour its canvas the
    /// same way; it does not own it.
    /// </remarks>
    public string? Theme { get; set; }

    public List<DiagramNode> Nodes { get; } = new();
    public List<DiagramEdge> Edges { get; } = new();
    public List<DiagramSubgraph> Subgraphs { get; } = new();
    /// <summary>Explicit frames for empty graph containers; populated frames follow their members.</summary>
    public Dictionary<string, DiagramRect> GroupLayouts { get; } = new(StringComparer.Ordinal);
    public RenderedGraphGeometry? RenderedGeometry { get; set; }
    public Dictionary<string, IReadOnlyList<DiagramPoint>> EdgeWaypoints { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Layout expressed as relationships — "these share a rank", "this order" —
    /// rather than as coordinates. The low-coupling way to influence a layout;
    /// see <see cref="LayoutConstraints"/>.
    /// </summary>
    public LayoutConstraints Constraints { get; } = new();

    /// <summary>Source lines we did not model, by original line index.</summary>
    public SortedDictionary<int, string> Passthrough { get; } = new();

    /// <summary>Frontmatter block (including Merpeeps) carried alongside the body.</summary>
    public string? Frontmatter { get; set; }

    public int LineCount { get; set; }

    public DiagramNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public bool AnyExplicitPositions => Nodes.Any(n => n.HasExplicitPosition);
}
