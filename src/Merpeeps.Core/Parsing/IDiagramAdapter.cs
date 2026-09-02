using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>
/// Reads and writes one family of mermaid diagram.
///
/// <para>The canvas is type-agnostic: it draws boxes and connections and knows
/// nothing about mermaid syntax. Supporting a new diagram type therefore means
/// supplying a parser and a writer — a few hundred lines — rather than building
/// another editor. That is the difference between a layer that scales across
/// mermaid's 34 diagram types and a treadmill that never catches up.</para>
/// </summary>
public interface IDiagramAdapter
{
    /// <summary>Diagram kind as written in the source, e.g. "stateDiagram-v2".</summary>
    string Kind { get; }

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

/// <summary>Adapter lookup. Falls back to flowchart, which is the common case.</summary>
public static class DiagramAdapters
{
    private static readonly IDiagramAdapter[] All =
    {
        new StateAdapter(),
        new FlowchartAdapter(),   // last: its header check is the most permissive
    };

    public static IDiagramAdapter For(string source)
    {
        var body = MerpeepsDoc.Split(source).Body;
        foreach (var a in All)
            if (a.CanParse(body)) return a;
        return All[^1];
    }

    public static IDiagramAdapter ForModel(DiagramModel model)
    {
        foreach (var a in All)
            if (string.Equals(a.Kind, model.Kind, StringComparison.OrdinalIgnoreCase))
                return a;
        // stateDiagram and stateDiagram-v2 share an adapter
        foreach (var a in All)
            if (model.Kind.StartsWith(a.Kind.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                return a;
        return All[^1];
    }

    /// <summary>First non-blank, non-comment line — the diagram's opening keyword.</summary>
    internal static string FirstKeyword(string body)
    {
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;
            return line;
        }
        return "";
    }
}

public sealed class FlowchartAdapter : IDiagramAdapter
{
    public string Kind => "flowchart";

    public bool CanParse(string body)
    {
        var k = DiagramAdapters.FirstKeyword(body);
        return k.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("graph", StringComparison.OrdinalIgnoreCase);
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
