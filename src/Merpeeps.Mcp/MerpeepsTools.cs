using System.ComponentModel;
using System.Text.Json;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using ModelContextProtocol.Server;

namespace Merpeeps.Mcp;

/// <summary>
/// Agent-facing surface over the Merpeeps engine.
///
/// <para>Two properties matter here. First, token economy: an agent can inspect
/// or amend a diagram without pulling the whole file into context. Second,
/// minimal diffs: every mutation goes through the surgical writer and reports
/// how many lines actually changed, so adding one node produces a two-line diff
/// rather than a regenerated file.</para>
/// </summary>
[McpServerToolType]
public static class MerpeepsTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions Lenient =
        new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------- reading

    [McpServerTool(Name = "merpeeps_describe")]
    [Description("Summarise a diagram's structure (nodes, links, counts) WITHOUT " +
                 "returning the file contents. Use this instead of reading the " +
                 "file when you only need to know what the diagram contains.")]
    public static string Describe(
        [Description("Absolute path to a .mmd or .md file")] string path,
        [Description("Include x/y positions. Defaults to false to save tokens.")]
        bool includePositions = false)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");
        var model = MerpeepsDocument.Load(File.ReadAllText(path));

        var nodes = model.Nodes.Select(n => includePositions
            ? (object)new { id = n.Id, label = n.Display, shape = n.Shape.ToString(),
                            x = Math.Round(n.X), y = Math.Round(n.Y) }
            : new { id = n.Id, label = n.Display, shape = n.Shape.ToString() });

        return JsonSerializer.Serialize(new
        {
            ok = true,
            kind = model.Kind,
            direction = model.Direction.ToString(),
            nodeCount = model.Nodes.Count,
            linkCount = model.Edges.Count,
            nodes,
            links = model.Edges.Select(e => new
            {
                from = e.From, to = e.To, label = e.Label, kind = e.Kind.ToString(),
            }),
            subgraphs = model.Subgraphs.Select(s => new
            {
                id = s.Id, title = s.Title, members = s.NodeIds,
            }),
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_read_metadata")]
    [Description("Read the Merpeeps metadata block (layout, style, semantics) attached " +
                 "to a diagram, without returning the diagram body.")]
    public static string ReadMetadata(
        [Description("Absolute path to the file")] string path)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");
        var merpeeps = MerpeepsDoc.Parse(File.ReadAllText(path));
        return merpeeps is null
            ? JsonSerializer.Serialize(new { ok = true, present = false }, Json)
            : JsonSerializer.Serialize(
                new { ok = true, present = true, merpeeps = MerpeepsDoc.ToJson(merpeeps) }, Json);
    }

    // ------------------------------------------------------------- editing

    [McpServerTool(Name = "merpeeps_edit")]
    [Description("""
        Apply a batch of edits to a diagram in one atomic call. Edits are written
        surgically, so untouched lines stay byte-identical.

        operationsJson is a JSON array. Supported operations:
          {"op":"addNode","id":"cache","label":"Redis","shape":"cylinder"}
          {"op":"connect","from":"api","to":"cache","label":"reads","kind":"dotted"}
          {"op":"disconnect","from":"api","to":"cache"}
          {"op":"rename","id":"api","label":"API Gateway"}
          {"op":"setShape","id":"api","shape":"hexagon"}
          {"op":"delete","ids":["old1","old2"]}
          {"op":"move","id":"api","x":120,"y":80}

        Shapes: rect, rounded, stadium, subroutine, cylinder, circle, doubleCircle,
        diamond, hexagon, parallelogram, trapezoid, asymmetric.
        Link kinds: arrow (default), open, dotted, thick, invisible.
        "dotted" and "thick" include an arrowhead; use "dottedLine" / "thickLine"
        for the arrowless variants.

        If any operation fails, nothing is written.
        """)]
    public static string Edit(
        [Description("Absolute path to the file")] string path,
        [Description("JSON array of operations (see the tool description)")]
        string operationsJson,
        [Description("Preview only: report what would change without writing.")]
        bool dryRun = false)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");

        List<OperationDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<OperationDto>>(operationsJson, Lenient);
        }
        catch (Exception ex) { return Err($"invalid operationsJson: {ex.Message}"); }
        if (dtos is null || dtos.Count == 0) return Err("no operations supplied");

        var source = File.ReadAllText(path);
        var model = MerpeepsDocument.Load(source);
        var history = new EditHistory(model);

        var result = OperationApplier.Apply(model, history,
            dtos.Select(d => d.ToOperation()));

        if (!result.Ok)
            return JsonSerializer.Serialize(new
            {
                ok = false,
                applied = result.Applied,
                errors = result.Errors,
                note = "nothing was written; fix the errors and retry",
            }, Json);

        var updated = MerpeepsDocument.Save(source, model);
        var changed = ChangedLines(source, updated);

        if (!dryRun) File.WriteAllText(path, updated);

        return JsonSerializer.Serialize(new
        {
            ok = true,
            dryRun,
            applied = result.Applied,
            linesChanged = changed,
            nodeCount = model.Nodes.Count,
            linkCount = model.Edges.Count,
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_set_positions")]
    [Description("Set x/y positions for one or more nodes. The diagram body is " +
                 "guaranteed byte-for-byte unchanged.")]
    public static string SetPositions(
        [Description("Absolute path to the file")] string path,
        [Description("JSON object mapping node id to {\"x\":number,\"y\":number}")]
        string positionsJson)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");

        Dictionary<string, object?>? nodes;
        try
        {
            nodes = JsonSerializer.Deserialize<Dictionary<string, object?>>(positionsJson);
        }
        catch (Exception ex) { return Err($"invalid positionsJson: {ex.Message}"); }
        if (nodes is null) return Err("positionsJson deserialised to null");

        var source = File.ReadAllText(path);
        var patch = new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["layout"] = new Dictionary<string, object?> { ["nodes"] = nodes },
        };
        var updated = MerpeepsDoc.Inject(source, patch);

        if (MerpeepsDoc.BodyOf(updated) != MerpeepsDoc.BodyOf(source))
            return Err("refused: edit would have altered the diagram body");

        File.WriteAllText(path, updated);
        return JsonSerializer.Serialize(
            new { ok = true, updated = nodes.Count, bodyUnchanged = true }, Json);
    }

    [McpServerTool(Name = "merpeeps_layout")]
    [Description("Re-run automatic layout and store the resulting positions, so an " +
                 "agent never has to invent coordinates after adding nodes. " +
                 "Engines: layered (flows, default), tree (hierarchies), " +
                 "force (networks), grid (predictable rows).")]
    public static string Layout(
        [Description("Absolute path to the file")] string path,
        [Description("Layout engine: layered, tree, force or grid. " +
                     "Defaults to whatever the file last used, else layered.")]
        string? engine = null)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");
        if (engine is not null && !Core.Layout.LayoutEngines.IsKnown(engine))
            return Err($"unknown engine '{engine}'. Available: " +
                       string.Join(", ", Core.Layout.LayoutEngines.Available.Select(e => e.Id)));

        var source = File.ReadAllText(path);
        var model = MerpeepsDocument.Load(source);

        var chosen = Core.Layout.LayoutEngines.ById(
            engine ?? (Core.Layout.LayoutEngines.IsKnown(model.LayoutEngine)
                ? model.LayoutEngine : null));

        foreach (var n in model.Nodes) n.HasExplicitPosition = false;
        chosen.Apply(model, Core.Layout.LayoutOptions.Default);
        foreach (var n in model.Nodes) n.HasExplicitPosition = true;
        model.LayoutEngine = chosen.Id;

        var updated = MerpeepsDocument.SavePositions(source, model);
        File.WriteAllText(path, updated);

        return JsonSerializer.Serialize(new
        {
            ok = true, positioned = model.Nodes.Count, engine = chosen.Id,
            bodyUnchanged = true,
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_layout_engines")]
    [Description("List the available layout engines and what each is good for.")]
    public static string LayoutEnginesList() =>
        JsonSerializer.Serialize(new
        {
            ok = true,
            engines = Core.Layout.LayoutEngines.Available
                .Select(e => new { id = e.Id, name = e.Name, description = e.Description })
                .ToArray(),
            @default = Core.Layout.LayoutEngines.Default.Id,
        }, Json);

    // ------------------------------------------------------------ hygiene

    [McpServerTool(Name = "merpeeps_svg")]
    [Description("Render the diagram to a standalone SVG file — stored positions, " +
                 "Merpeeps styling and icons included. Needs no browser.")]
    public static string Svg(
        [Description("Absolute path to the .mmd file")] string path,
        [Description("Absolute path for the .svg to write. Defaults to the input path with a .svg extension.")]
        string? outPath = null,
        [Description("Optional mermaid theme name (default, dark, forest, neutral, base, neo, neo-dark, redux, redux-dark). Omit to use the theme in the file.")]
        string? theme = null)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");

        var model = MerpeepsDocument.Load(File.ReadAllText(path));
        var svg = SvgExporter.ToSvg(model, theme);

        var target = outPath ?? Path.ChangeExtension(path, ".svg");
        File.WriteAllText(target, svg);

        return JsonSerializer.Serialize(new
        {
            ok = true,
            path = target,
            bytes = svg.Length,
            nodes = model.Nodes.Count,
            edges = model.Edges.Count,

            // Said plainly, because an agent asking for "a picture of this
            // diagram" may well mean the one mermaid draws. This renders what
            // the editor shows — which is the only way to get the styling and
            // icons, since mermaid syntax cannot express them.
            note = "Renders the Merpeeps view: stored positions, styling and icons. "
                 + "For what a stock renderer draws, use merpeeps_bake first.",
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_bake")]
    [Description("Rewrite Merpeeps styling as native mermaid `style` statements, so " +
                 "the colours survive in any renderer with no plugin. Lossy by " +
                 "nature: icons and stored positions have no mermaid equivalent.")]
    public static string Bake(
        [Description("Absolute path to the .mmd file")] string path,
        [Description("Absolute path to write the baked file to. Omit to return the text without writing.")]
        string? outPath = null)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");

        var result = MermaidBake.Bake(File.ReadAllText(path));
        if (outPath is not null) File.WriteAllText(outPath, result.Output);

        // Loss is reported node by node rather than as a count, so an agent can
        // decide what to do about it instead of merely noticing it happened.
        return JsonSerializer.Serialize(new
        {
            ok = true,
            path = outPath,
            text = outPath is null ? result.Output : null,
            lossy = result.Lossy,
            styledNodes = result.StyledNodes,
            droppedIcons = result.DroppedIcons,
            droppedPositions = result.DroppedPositions,
            unsupportedKind = result.UnsupportedKind,
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_strip")]
    [Description("Remove all Merpeeps metadata, restoring a plain mermaid file.")]
    public static string Strip(
        [Description("Absolute path to the file")] string path)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");
        var source = File.ReadAllText(path);
        var stripped = MerpeepsDoc.Serialise(source, null);
        File.WriteAllText(path, stripped);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            bodyUnchanged = MerpeepsDoc.BodyOf(stripped) == MerpeepsDoc.BodyOf(source),
        }, Json);
    }

    [McpServerTool(Name = "merpeeps_validate")]
    [Description("Check Merpeeps invariants for a file: round-trip fidelity, " +
                 "idempotence, and that the diagram body is preserved.")]
    public static string Validate(
        [Description("Absolute path to the file")] string path)
    {
        if (!File.Exists(path)) return Err($"file not found: {path}");
        var source = File.ReadAllText(path);
        var merpeeps = MerpeepsDoc.Parse(source);
        var reserialised = MerpeepsDoc.Serialise(source, merpeeps);

        var checks = new Dictionary<string, bool>
        {
            ["hasMerpeeps"] = merpeeps is not null,
            ["bodyPreserved"] = MerpeepsDoc.BodyOf(reserialised) == MerpeepsDoc.BodyOf(source),
            ["idempotent"] = MerpeepsDoc.Serialise(reserialised, MerpeepsDoc.Parse(reserialised))
                             == reserialised,
            ["roundTrip"] = MerpeepsDoc.ToJson(MerpeepsDoc.Parse(reserialised)) == MerpeepsDoc.ToJson(merpeeps),
        };
        return JsonSerializer.Serialize(new
        {
            ok = checks.Where(c => c.Key != "hasMerpeeps").All(c => c.Value),
            checks,
        }, Json);
    }

    // ------------------------------------------------------------- helpers

    private static int ChangedLines(string before, string after) =>
        TextDiff.CountChangedLines(MerpeepsDoc.BodyOf(before), MerpeepsDoc.BodyOf(after));

    private static string Err(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message }, Json);

    /// <summary>Wire format for an operation; kept out of the core.</summary>
    public sealed class OperationDto
    {
        public string Op { get; set; } = "";
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Shape { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Kind { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public List<string>? Ids { get; set; }

        public DiagramOperation ToOperation() => new()
        {
            Op = Op, Id = Id, Label = Label, Shape = Shape,
            From = From, To = To, Kind = Kind, X = X, Y = Y, Ids = Ids,
        };
    }
}

