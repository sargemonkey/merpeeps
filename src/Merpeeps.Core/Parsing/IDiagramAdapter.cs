using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Reads and writes one family of mermaid diagram.
///
/// <para>This adapter models graph-like nodes and connections. Families such as
/// sequence diagrams and data charts need their own structural editing models;
/// they must not be flattened into graph nodes to claim visual support.</para>
/// </summary>
public interface IDiagramAdapter
{
    /// <summary>Diagram kind as written in the source, e.g. "stateDiagram-v2".</summary>
    string Kind { get; }
    bool SupportsGraphLayout => true;

    /// <summary>True when this adapter recognises the diagram's opening keyword.</summary>
    bool CanParse(string body);

    DiagramModel Parse(string source);

    /// <summary>Regenerate the whole body. Used when a surgical patch is not possible.</summary>
    string Write(DiagramModel model);

    /// <summary>
    /// Render a single node declaration in this diagram's syntax, for appending
    /// during a surgical edit. Returns null when the node needs no declaration
    /// line of its own.
    /// </summary>
    string? RenderNodeStatement(DiagramNode node);

    /// <summary>Render a single connection in this diagram's syntax.</summary>
    string RenderEdgeStatement(DiagramEdge edge);

    /// <summary>
    /// Render just the link operator — <c>--&gt;</c>, or <c>--&gt;|text|</c> when
    /// it carries a label — for patching one in place.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RenderEdgeStatement"/> so a relabel rewrites the
    /// arrow and leaves both node declarations exactly as the author wrote them.
    /// Returns null when this syntax has no single operator to swap, in which
    /// case the writer regenerates instead.
    /// </remarks>
    string? RenderLinkOperator(DiagramEdge edge);
}

/// <summary>Adapter lookup. Unknown families are never guessed to be flowcharts.</summary>
public static class DiagramAdapters
{
    private static readonly IDiagramAdapter[] All =
    {
        new ClassDiagramAdapter(),
        new EntityDiagramAdapter(),
        new SequenceDiagramAdapter(),
        new ChartDiagramAdapter(ChartFamily.Pie),
        new ChartDiagramAdapter(ChartFamily.Quadrant),
        new ChartDiagramAdapter(ChartFamily.Xy),
        new ChartDiagramAdapter(ChartFamily.Radar),
        new TemporalDiagramAdapter(TemporalFamily.Timeline),
        new TemporalDiagramAdapter(TemporalFamily.Journey),
        new OutlineDiagramAdapter(OutlineFamily.Mindmap),
        new OutlineDiagramAdapter(OutlineFamily.TreeView),
        new PacketDiagramAdapter(),
        new KanbanDiagramAdapter(),
        new RequirementDiagramAdapter(),
        new GitGraphAdapter(),
        new SankeyDiagramAdapter(),
        new TreemapDiagramAdapter(),
        new BlockDiagramAdapter(),
        new ArchitectureDiagramAdapter(),
        new C4DiagramAdapter(),
        new GanttDiagramAdapter(),
        new WardleyDiagramAdapter(),
        new IshikawaDiagramAdapter(),
        new CynefinDiagramAdapter(),
        new VennDiagramAdapter(),
        new RailroadDiagramAdapter(),
        new SwimlaneDiagramAdapter(),
        new UseCaseDiagramAdapter(),
        new AgentFlowDiagramAdapter(),
        new EventModelingAdapter(),
        new ZenDiagramAdapter(),
        new StateAdapter(),
        new FlowchartAdapter(),
    };

    public static DiagramCapabilities GetCapabilities(string source) =>
        CapabilitiesFor(FirstKeyword(MerpeepsDoc.Split(source).Body));

    private static DiagramCapabilities CapabilitiesFor(string kind)
    {
        var adapter = All.FirstOrDefault(a => a.CanParse(kind));
        var supported = adapter is not null;
        var diagnostic = supported ? null
            : (kind.Length == 0 ? "No Mermaid diagram directive was found."
                : $"Native editing, layout and SVG rendering are not supported for '{kind}'.")
              + " Supported families: " + string.Join(", ", All.Select(a => a.Kind))
              + " (flowchart-elk is a legacy flowchart alias)."
              + " Metadata operations remain available; use a Mermaid renderer to preview the source.";
        return new DiagramCapabilities(kind, true, supported, adapter?.SupportsGraphLayout ?? false, supported, diagnostic);
    }

    public static IDiagramAdapter For(string source)
    {
        var body = MerpeepsDoc.Split(source).Body;
        foreach (var a in All)
            if (a.CanParse(body)) return a;
        throw new UnsupportedDiagramException(CapabilitiesFor(FirstKeyword(body)));
    }

    public static IDiagramAdapter ForModel(DiagramModel model)
    {
        if ((model.AgentFlow is not null || model.EventModeling is not null || model.Zen is not null) &&
            (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0 ||
             model.AgentFlow is not null && model.Kind != "agentflow-beta" || model.EventModeling is not null && model.Kind != "eventmodeling" ||
             model.Zen is not null && model.Kind != "zenuml"))
            throw new ArgumentException("Agentflow, Event Modeling and ZenUML require their own typed models, not generic graph objects.");
        if ((model.Swimlane is not null || model.UseCase is not null) && (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0 ||
            model.Swimlane is not null && model.Kind != "swimlane-beta" || model.UseCase is not null && model.Kind != "usecase-beta"))
            throw new ArgumentException("Swimlane ownership and UML use-case models must not be replaced by generic graph objects.");
        if ((model.Venn is not null || model.Railroad is not null) && (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0 ||
            model.Venn is not null && model.Kind != "venn-beta" || model.Railroad is not null && model.Kind != model.Railroad.Directive))
            throw new ArgumentException("Venn and Railroad require their matching non-graph data model.");
        if ((model.Wardley is not null || model.Ishikawa is not null || model.Cynefin is not null) &&
            (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0 ||
             model.Wardley is not null && model.Kind != "wardley-beta" || model.Ishikawa is not null && model.Kind != "ishikawa-beta" ||
             model.Cynefin is not null && model.Kind != "cynefin-beta"))
            throw new ArgumentException("Strategy diagrams require their own typed models, not generic graph objects.");
        if (model.Gantt is not null && (model.Kind != "gantt" || model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0))
            throw new ArgumentException("Gantt tasks retain their own temporal model, not graph nodes or edges.");
        if (model.C4 is not null && (model.Kind != model.C4.Directive || model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0))
            throw new ArgumentException("C4 diagram content must match its dialect and cannot be stored as generic graph objects.");
        if (model.Blocks is not null && (model.Kind is not ("block" or "block-beta") || model.Blocks.Directive != model.Kind) ||
            model.Architecture is not null && model.Kind != "architecture-beta")
            throw new ArgumentException("The Block/Architecture model does not match its diagram directive.");
        if ((model.Blocks is not null || model.Architecture is not null) &&
            (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Subgraphs.Count > 0 ||
             model.Sequence is not null || model.Chart is not null || model.Outline is not null || model.Temporal is not null ||
             model.Packet is not null || model.Kanban is not null || model.Requirements is not null || model.GitDiagram is not null ||
             model.Sankey is not null || model.Treemap is not null || model.Blocks is not null && model.Architecture is not null))
            throw new ArgumentException("Block grids and Architecture port graphs have their own canonical models.");
        if ((model.Packet is not null || model.Kanban is not null) && (model.Nodes.Count > 0 || model.Edges.Count > 0 ||
            model.Sequence is not null || model.Chart is not null || model.Outline is not null || model.Temporal is not null))
            throw new ArgumentException("Packet and Kanban data cannot be represented by graph/other diagram content.");
        if ((model.Outline is not null || model.Temporal is not null) &&
            (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Sequence is not null || model.Chart is not null || model.Outline is not null && model.Temporal is not null))
            throw new ArgumentException("Outline and temporal structures cannot be represented by graph or chart elements.");
        if (model.Chart is not null && (model.Nodes.Count > 0 || model.Edges.Count > 0 || model.Sequence is not null))
            throw new ArgumentException("Chart data cannot be represented by graph nodes, edges or sequence events.");
        if (model.Kind == "sequenceDiagram" && (model.Sequence is null || model.Nodes.Count > 0 || model.Edges.Count > 0) ||
            model.Sequence is not null && model.Kind != "sequenceDiagram")
            throw new ArgumentException("Sequence participants and messages cannot be represented by graph nodes or edges.");
        foreach (var a in All)
            if (FirstKeyword(model.Kind) == model.Kind && a.CanParse(model.Kind))
            {
                if (a is ChartDiagramAdapter chart && model.Chart?.Family != chart.Family)
                    throw new ArgumentException("The chart kind requires its own typed chart data.");
                if (a is TemporalDiagramAdapter temporal && model.Temporal?.Family != temporal.Family ||
                    a is OutlineDiagramAdapter outline && model.Outline?.Family != outline.Family)
                    throw new ArgumentException("The diagram kind requires its own typed temporal or hierarchy content.");
                if (a is PacketDiagramAdapter && model.Packet is null || a is KanbanDiagramAdapter && model.Kanban is null)
                    throw new ArgumentException("The diagram kind requires typed packet or board content.");
                if (a is RequirementDiagramAdapter && model.Requirements is null)
                    throw new ArgumentException("The diagram kind requires typed requirement content.");
                if (a is GitGraphAdapter && model.GitDiagram is null)
                    throw new ArgumentException("The diagram kind requires a typed GitGraph operation log.");
                if (a is SankeyDiagramAdapter && model.Sankey is null || a is TreemapDiagramAdapter && model.Treemap is null)
                    throw new ArgumentException("The diagram requires typed weighted data.");
                if (a is BlockDiagramAdapter && (model.Blocks is null || model.Blocks.Directive != model.Kind) ||
                    a is ArchitectureDiagramAdapter && model.Architecture is null)
                    throw new ArgumentException("The diagram requires its matching Block grid or Architecture model.");
                if (a is C4DiagramAdapter && model.C4 is null)
                    throw new ArgumentException("The diagram requires its typed C4 content.");
                if (a is GanttDiagramAdapter && model.Gantt is null)
                    throw new ArgumentException("The diagram requires its typed Gantt content.");
                if (a is WardleyDiagramAdapter && model.Wardley is null || a is IshikawaDiagramAdapter && model.Ishikawa is null ||
                    a is CynefinDiagramAdapter && model.Cynefin is null)
                    throw new ArgumentException("The diagram requires its matching Wardley, Ishikawa or Cynefin content.");
                if (a is VennDiagramAdapter && model.Venn is null || a is RailroadDiagramAdapter && model.Railroad is null)
                    throw new ArgumentException("The diagram requires its matching Venn or Railroad content.");
                if (a is SwimlaneDiagramAdapter && model.Swimlane is null || a is UseCaseDiagramAdapter && model.UseCase is null)
                    throw new ArgumentException("The diagram requires its matching Swimlane or Use Case content.");
                if (a is AgentFlowDiagramAdapter && model.AgentFlow is null || a is EventModelingAdapter && model.EventModeling is null || a is ZenDiagramAdapter && model.Zen is null)
                    throw new ArgumentException("The diagram requires its matching agent, event or ZenUML content.");
                return a;
            }
        throw new UnsupportedDiagramException(CapabilitiesFor(model.Kind) with
        {
            CanEdit = false, CanLayout = false, CanRenderNatively = false,
            Diagnostic = $"No native adapter for model kind '{model.Kind}'.",
        });
    }

    /// <summary>The opening keyword, excluding comments and initialization directives.</summary>
    internal static string FirstKeyword(string body)
    {
        var inDirective = false;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || IsCommentLine(line, ref inDirective)) continue;
            var end = line.IndexOfAny([' ', '\t', ';']);
            return end < 0 ? line : line[..end];
        }
        return "";
    }

    internal static bool IsCommentLine(string line, ref bool inDirective)
    {
        if (inDirective)
        {
            if (line.Contains("}%%", StringComparison.Ordinal)) inDirective = false;
            return true;
        }
        if (line.StartsWith("%%{", StringComparison.Ordinal))
        {
            inDirective = !line.Contains("}%%", StringComparison.Ordinal);
            return true;
        }
        return line.StartsWith("%%", StringComparison.Ordinal);
    }
}

public sealed class FlowchartAdapter : IDiagramAdapter
{
    public string Kind => "flowchart";

    public bool CanParse(string body)
    {
        var k = DiagramAdapters.FirstKeyword(body).ToLowerInvariant();
        return k is "flowchart" or "graph" or "flowchart-elk";
    }

    public DiagramModel Parse(string source) => FlowchartParser.Parse(source);
    public string Write(DiagramModel model) => FlowchartWriter.Write(model);

    public string? RenderNodeStatement(DiagramNode node) =>
        FlowchartWriter.Declare(node);

    public string RenderEdgeStatement(DiagramEdge edge) =>
        FlowchartWriter.EdgeStatement(edge);

    public string? RenderLinkOperator(DiagramEdge edge) =>
        FlowchartWriter.LinkOperator(edge);
}
