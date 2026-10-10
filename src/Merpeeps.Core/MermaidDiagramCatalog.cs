using Merpeeps.Core.Model;

namespace Merpeeps.Core;

/// <summary>A documented source dialect, not a claim of native editor support.</summary>
public sealed record MermaidDiagramDialect(string Id, string DisplayName, string Directive);

public sealed class MermaidDiagramFamily
{
    public string Id { get; }
    public string DisplayName { get; }
    public string DocumentationUrl { get; }
    public IReadOnlyList<MermaidDiagramDialect> Dialects { get; }
    public string Directive => Dialects[0].Directive;

    internal MermaidDiagramFamily(
        string id, string displayName, string documentationTopic,
        params MermaidDiagramDialect[] dialects)
    {
        Id = id;
        DisplayName = displayName;
        DocumentationUrl = $"https://github.com/mermaid-js/mermaid/blob/mermaid%40{MermaidDiagramCatalog.MermaidVersion}/packages/mermaid/src/docs/syntax/{documentationTopic}.md";
        Dialects = Array.AsReadOnly(dialects);
    }
}

/// <summary>An empty native editor model and its authoritative source, with no sample content.</summary>
public sealed record MermaidDiagramDraft(
    string Source, DiagramModel Model, DiagramCapabilities Capabilities);

/// <summary>
/// The frozen upstream authoring target. Recognition, syntax validity and native
/// editing support are separate: listing a family does not enable its editor.
/// </summary>
public static class MermaidDiagramCatalog
{
    public const string MermaidVersion = "12.0.0";

    public static IReadOnlyList<MermaidDiagramFamily> Families { get; } =
        Array.AsReadOnly<MermaidDiagramFamily>(
        [
            new("flowchart", "Flowchart", "flowchart",
                Dialect("flowchart", "Flowchart"),
                Dialect("graph", "Graph"),
                Dialect("flowchart-elk", "Flowchart (legacy ELK directive)")),
            new("sequence", "Sequence diagram", "sequenceDiagram",
                Dialect("sequenceDiagram", "Sequence diagram")),
            new("class", "Class diagram", "classDiagram",
                Dialect("classDiagram", "Class diagram"),
                Dialect("classDiagram-v2", "Class diagram (v2 directive)")),
            new("state", "State diagram", "stateDiagram",
                Dialect("stateDiagram-v2", "State diagram"),
                Dialect("stateDiagram", "State diagram (legacy directive)")),
            new("er", "Entity relationship", "entityRelationshipDiagram",
                Dialect("erDiagram", "Entity relationship")),
            new("journey", "User journey", "userJourney",
                Dialect("journey", "User journey")),
            new("gantt", "Gantt chart", "gantt",
                Dialect("gantt", "Gantt chart")),
            new("pie", "Pie chart", "pie",
                Dialect("pie", "Pie chart")),
            new("quadrant", "Quadrant chart", "quadrantChart",
                Dialect("quadrantChart", "Quadrant chart")),
            new("requirement", "Requirement diagram", "requirementDiagram",
                Dialect("requirementDiagram", "Requirement diagram")),
            new("git", "Git graph", "gitgraph",
                Dialect("gitGraph", "Git graph")),
            new("c4", "C4 architecture", "c4",
                Dialect("C4Context", "System context"),
                Dialect("C4Container", "Container"),
                Dialect("C4Component", "Component"),
                Dialect("C4Dynamic", "Dynamic"),
                Dialect("C4Deployment", "Deployment")),
            new("mindmap", "Mindmap", "mindmap",
                Dialect("mindmap", "Mindmap")),
            new("timeline", "Timeline", "timeline",
                Dialect("timeline", "Timeline")),
            new("sankey", "Sankey diagram", "sankey",
                Dialect("sankey", "Sankey diagram"),
                Dialect("sankey-beta", "Sankey (beta directive)")),
            new("xy", "XY chart", "xyChart",
                Dialect("xychart", "XY chart"),
                Dialect("xychart-beta", "XY chart (beta directive)")),
            new("block", "Block diagram", "block",
                Dialect("block", "Block diagram"),
                Dialect("block-beta", "Block (beta directive)")),
            new("packet", "Packet diagram", "packet",
                Dialect("packet", "Packet diagram"),
                Dialect("packet-beta", "Packet (beta directive)")),
            new("kanban", "Kanban", "kanban",
                Dialect("kanban", "Kanban")),
            new("architecture", "Architecture diagram", "architecture",
                Dialect("architecture-beta", "Architecture diagram")),
            new("radar", "Radar chart", "radar",
                Dialect("radar-beta", "Radar chart")),
            new("treemap", "Treemap", "treemap",
                Dialect("treemap-beta", "Treemap")),
            new("venn", "Venn diagram", "venn",
                Dialect("venn-beta", "Venn diagram")),
            new("wardley", "Wardley map", "wardley",
                Dialect("wardley-beta", "Wardley map")),
            new("ishikawa", "Ishikawa diagram", "ishikawa",
                Dialect("ishikawa-beta", "Ishikawa diagram")),
            new("cynefin", "Cynefin diagram", "cynefin",
                Dialect("cynefin-beta", "Cynefin diagram")),
            new("railroad", "Railroad diagram", "railroad",
                Dialect("railroad-ebnf-beta", "EBNF grammar"),
                Dialect("railroad-abnf-beta", "ABNF grammar"),
                Dialect("railroad-peg-beta", "PEG grammar"),
                Dialect("railroad-beta", "IR primitives")),
            new("swimlane", "Swimlane diagram", "swimlanes",
                Dialect("swimlane-beta", "Swimlane diagram")),
            new("usecase", "Use case diagram", "usecase",
                Dialect("usecase-beta", "Use case diagram")),
            new("agentflow", "Agent flow", "agentflow",
                Dialect("agentflow-beta", "Agent flow")),
            new("eventmodeling", "Event modeling", "eventmodeling",
                Dialect("eventmodeling", "Event modeling")),
            new("treeview", "Tree view", "treeView",
                Dialect("treeView-beta", "Tree view")),
            new("zenuml", "ZenUML", "zenuml",
                Dialect("zenuml", "ZenUML")),
        ]);

    public static MermaidDiagramFamily GetFamily(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Families.FirstOrDefault(f => f.Id == id)
            ?? throw new ArgumentException($"Unknown Mermaid family '{id}'.", nameof(id));
    }

    /// <summary>
    /// Create an empty source and native model without inserting example nodes.
    /// Unsupported families remain explicit errors; this is not a syntax validator.
    /// </summary>
    public static MermaidDiagramDraft CreateEmpty(string familyId, string? dialectId = null)
    {
        var family = GetFamily(familyId);
        var dialect = dialectId is null ? family.Dialects[0]
            : family.Dialects.FirstOrDefault(d => d.Id == dialectId)
              ?? throw new ArgumentException(
                  $"Unknown dialect '{dialectId}' for Mermaid family '{familyId}'.",
                  nameof(dialectId));
        var source = family.Id == "flowchart"
            ? $"{dialect.Directive} TD\n"
            : $"{dialect.Directive}\n";
        var capabilities = MerpeepsDocument.GetCapabilities(source);
        if (!capabilities.CanEdit)
            throw new UnsupportedDiagramException(capabilities);

        return new(source, MerpeepsDocument.Load(source), capabilities);
    }

    private static MermaidDiagramDialect Dialect(string directive, string displayName) =>
        new(directive, displayName, directive);
}
