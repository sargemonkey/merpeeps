using System.Text.Json;
using System.Text.Json.Serialization;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Tooling;
using Merpeeps.Render;

// merpeeps — NativeAOT CLI over the Merpeeps engine.
//
// Every capability the MCP server exposes to an agent is reachable here too,
// so a script and an agent can do the same work. The two surfaces are held
// together by oracle/surface-parity-test.mjs, which fails if either grows a
// capability the other lacks.
//
// Two commands are deliberately CLI-only. `preview` needs the vendored mermaid
// bundle — three and a half megabytes that would otherwise be linked into the
// MCP server to duplicate what `svg` and `open` already cover. `conformance`
// runs the spec suite over a corpus of samples; it tests the tool rather than
// operating on a diagram, so it has no meaning as an agent tool.

if (args.Length == 0) { Usage(); return 2; }

var cmd = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return cmd switch
    {
        "read" => Read(rest),
        "capabilities" => Capabilities(rest),
        "describe" => Describe(rest),
        "validate" => Validate(rest),
        "strip" => Strip(rest),
        "edit" => Edit(rest),
        "layout" => Layout(rest),
        "layout-engines" or "engines" => ListEngines(),
        "adopt" => Adopt(rest),
        "svg" => Svg(rest),
        "bake" => Bake(rest),
        "preview" => Preview(rest),
        "open" => Open(rest),
        "conformance" => Conformance(rest),
        "-h" or "--help" or "help" => Usage(),
        _ => Fail($"unknown command '{cmd}'"),
    };
}
catch (UnsupportedDiagramException ex)
{
    return Fail(ex.Message);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"merpeeps: {ex.Message}");
    return 3;
}

static int Usage()
{
    Console.WriteLine("""
        merpeeps — layout and styling for Mermaid diagrams

        inspect
          merpeeps read <file>                 print the Merpeeps block as JSON
          merpeeps capabilities <file>         report native support without parsing the body
          merpeeps describe <file>             structure as JSON, without the body
          merpeeps describe <file> --positions   ... including stored coordinates
          merpeeps validate <file>...          check metadata invariants (not Mermaid syntax)

        change
          merpeeps edit <file> --ops <json|file|->   apply operations
          merpeeps layout <file> [--engine <id>]     arrange, and store the result
          merpeeps adopt <file> --positions <json>   freeze supplied coordinates in
          merpeeps adopt <file> --geometry <json>    adopt rendered Flowchart dimensions, groups, labels and routes
          merpeeps layout-engines              list the engines, as JSON

        `edit`, `layout`, `adopt` and `strip` print the result to stdout and
        leave your file alone. Add --write to edit it in place. `edit`, `layout`
        and `adopt` also support --out <path> to write elsewhere.

        export
          merpeeps strip <file>                print the plain mermaid to stdout
          merpeeps bake <file>                 plain mermaid with the styling baked in
          merpeeps svg <file>                  render to SVG — styling and icons included
          merpeeps preview <file>              self-contained HTML, rendered by real mermaid
          merpeeps preview <file> --plain      as a stock renderer draws it, ignoring Merpeeps

        hand over
          merpeeps open <file>                 open it in the Merpeeps editor

        development
          merpeeps conformance <samples.json>  run the render-free invariant suite

        `layout` computes positions; `open` is for the ones only a person can
        choose. An agent can compose a diagram and arrange it plausibly, but
        where a node belongs on the page is a judgement, so the useful last step
        is to hand the file to someone with a canvas.

        `bake` rewrites Merpeeps styling as native `style` statements, so the
        colours show up anywhere mermaid runs — GitHub, GitLab, Notion — with no
        plugin. It is lossy by nature: icons and stored positions have no
        equivalent in mermaid syntax, and it says so rather than dropping them
        quietly.

        `preview` writes one HTML file with mermaid embedded — it opens in any
        browser with no network access, and is how the editor's reference pane
        renders. Use --out to write it somewhere instead of stdout.

        Set MERPEEPS_APP to point `open` at a specific editor build.
        """);
    return 0;
}

static int Fail(string msg) { Console.Error.WriteLine($"merpeeps: {msg}"); return 2; }

static int Read(string[] a)
{
    if (a.Length < 1) return Fail("read requires a file");
    var merpeeps = MerpeepsDoc.Parse(File.ReadAllText(a[0]));
    Console.WriteLine(merpeeps is null ? "null" : MerpeepsDoc.ToJson(merpeeps));
    return 0;
}

static int Capabilities(string[] a)
{
    if (a.Length != 1) return Fail("capabilities requires one file");
    var capabilities = MerpeepsDocument.GetCapabilities(File.ReadAllText(a[0]));
    Console.WriteLine(JsonSerializer.Serialize(capabilities, CliJson.Default.DiagramCapabilities));
    return 0;
}

/// <summary>
/// Report a diagram's structure without reproducing the diagram.
///
/// <para>The counterpart to <c>read</c>, which returns the metadata: this
/// returns what the metadata is <em>about</em>. Together they let a caller
/// answer "what is in this file" without loading the file, which matters for an
/// agent paying by the token and for a script that wants to branch on the node
/// count without writing a mermaid parser.</para>
///
/// <para>Positions are opt-in. They roughly double the output and are noise to
/// anyone asking a structural question.</para>
/// </summary>
static int Describe(string[] a)
{
    var positions = a.Contains("--positions") || a.Contains("-p");
    var files = a.Where(x => x is not ("--positions" or "-p")).ToArray();
    if (files.Length < 1) return Fail("describe requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var model = MerpeepsDocument.Load(File.ReadAllText(files[0]));
    if (DiagramStructure.DescribeData(model) is { } data)
    {
        Console.WriteLine(MerpeepsDoc.ToJson(data));
        return 0;
    }
    if (model.Sequence is { } sequence)
    {
        var statements = SequenceDiagramModel.Walk(sequence.Statements).ToArray();
        var report = new SequenceDescribeDto(
            model.Kind, sequence.Participants.Count, statements.OfType<SequenceMessage>().Count(),
            sequence.Participants.Select(p => new SequenceParticipantDto(p.Id, p.Display, p.Kind.ToString(), p.Shape.ToString(), p.BoxId)).ToArray(),
            statements.Select(s =>
            {
                var location = SequenceEditing.Locate(sequence, s.Id);
                return new SequenceStatementDto(s.Id, s.GetType().Name, location.ParentBranchId, location.Index,
                    s is SequenceMessage m ? m.From : null, s is SequenceMessage n ? n.To : null,
                    s switch { SequenceMessage message => message.Text, SequenceNote note => note.Text, SequenceBlock block => block.Text, _ => null });
            }).ToArray());
        Console.WriteLine(JsonSerializer.Serialize(report, CliJson.Default.SequenceDescribeDto));
        return 0;
    }

    var summary = new DescribeDto(
        Kind: model.Kind,
        Direction: model.Direction.ToString(),
        NodeCount: model.Nodes.Count,
        LinkCount: model.Edges.Count,
        Nodes: model.Nodes.Select(n => new DescribeNodeDto(
            n.Id, n.Display, n.Shape.ToString(),
            positions ? Math.Round(n.X) : null,
            positions ? Math.Round(n.Y) : null)).ToArray(),
        Links: model.Edges.Select(e => new DescribeLinkDto(
            e.From, e.To, e.Label, e.Kind.ToString())).ToArray(),
        Subgraphs: model.Subgraphs.Select(s => new DescribeSubgraphDto(
            s.Id, s.Title, s.NodeIds.ToArray())).ToArray());

    Console.WriteLine(JsonSerializer.Serialize(summary, CliJson.Default.DescribeDto));
    return 0;
}

/// <summary>
/// Apply a batch of declarative edits.
///
/// <para>Goes through the same command layer the editor's own tools use, so an
/// automated edit is indistinguishable from a hand one — and, more usefully,
/// the writer is surgical: adding a node changes the lines for that node and
/// leaves every other byte alone. A regenerated file would be correct and
/// unreviewable.</para>
///
/// <para>The batch is all-or-nothing. A half-applied edit — node created, edge
/// silently missing because the id was wrong — is the failure mode worth
/// designing out, because it produces a diagram that is wrong rather than one
/// that is obviously broken.</para>
///
/// <para>Prints the result and leaves the file alone unless asked. The MCP tool
/// writes by default and offers <c>dryRun</c>; here the default <em>is</em> the
/// dry run, which matches every other command in this CLI. Same capability,
/// each surface keeping its own conventions.</para>
/// </summary>
static int Edit(string[] a)
{
    var opsIndex = Array.FindIndex(a, x => x is "--ops" or "-e");
    if (opsIndex < 0) return Fail("edit requires --ops <json|file|->");
    if (opsIndex + 1 >= a.Length) return Fail("--ops requires JSON, a path, or - for stdin");
    var opsArg = a[opsIndex + 1];

    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    var inPlace = a.Contains("--write") || a.Contains("-w");
    if (inPlace && outPath is not null)
        return Fail("--write and --out are alternatives; pick one");

    var consumed = new HashSet<int> { opsIndex, opsIndex + 1 };
    if (outIndex >= 0) { consumed.Add(outIndex); consumed.Add(outIndex + 1); }
    var files = a.Where((x, i) =>
        !consumed.Contains(i) && x is not ("--write" or "-w")).ToArray();

    if (files.Length < 1) return Fail("edit requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    List<DiagramOperation>? operations;
    try
    {
        operations = JsonSerializer.Deserialize(
            ReadJsonArgument(opsArg), CliJson.Default.ListDiagramOperation);
    }
    catch (JsonException ex) { return Fail($"invalid --ops JSON: {ex.Message}"); }

    if (operations is null || operations.Count == 0)
        return Fail("no operations supplied");

    var source = File.ReadAllText(files[0]);
    var model = MerpeepsDocument.Load(source);
    var result = OperationApplier.Apply(model, new EditHistory(model), operations);

    // Report every failure, not just the first: an agent fixing one id at a
    // time costs a round trip per mistake.
    if (!result.Ok)
    {
        foreach (var error in result.Errors) Console.Error.WriteLine($"merpeeps: {error}");
        Console.Error.WriteLine("merpeeps: nothing was written; fix the errors and retry");
        return 1;
    }

    var updated = MerpeepsDocument.Save(source, model);

    if (inPlace) File.WriteAllText(files[0], updated);
    else if (outPath is null) { Console.Out.Write(updated); Console.Out.Flush(); }
    else File.WriteAllText(outPath, updated);

    foreach (var applied in result.Applied) Console.Error.WriteLine($"merpeeps: {applied}");
    if (!inPlace && outPath is null)
        Console.Error.WriteLine("merpeeps: printed to stdout — add --write to edit the file");
    return 0;
}

/// <summary>
/// Arrange the diagram and store the coordinates.
///
/// <para>The point of storing them is that a computed layout stops being
/// recomputed. Mermaid re-runs its own algorithm on every render, so the same
/// file can be drawn differently by two viewers, and a diagram someone reasoned
/// about yesterday can rearrange itself today. Freezing the answer makes the
/// picture a property of the document.</para>
///
/// <para>Existing hand-placed nodes are deliberately overridden here. Layout is
/// normally polite about them — <see cref="ILayoutEngine"/> only moves nodes
/// without an explicit position — but someone asking for a layout is asking for
/// the whole diagram to be arranged, and honouring old pins would produce a
/// half-tidied result that looks like a bug. This is also why the command
/// prints rather than writes unless asked: it discards work, so it should be
/// the thing you asked for.</para>
/// </summary>
static int Layout(string[] a)
{
    var engineIndex = Array.FindIndex(a, x => x is "--engine" or "-e");
    string? engine = null;
    if (engineIndex >= 0)
    {
        if (engineIndex + 1 >= a.Length) return Fail("--engine requires a name");
        engine = a[engineIndex + 1];
        if (!LayoutEngines.IsKnown(engine))
            return Fail($"unknown engine '{engine}'; try: " +
                        string.Join(", ", LayoutEngines.Available.Select(e => e.Id)));
    }

    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    var inPlace = a.Contains("--write") || a.Contains("-w");
    if (inPlace && outPath is not null)
        return Fail("--write and --out are alternatives; pick one");

    var consumed = new HashSet<int>();
    if (engineIndex >= 0) { consumed.Add(engineIndex); consumed.Add(engineIndex + 1); }
    if (outIndex >= 0) { consumed.Add(outIndex); consumed.Add(outIndex + 1); }
    var files = a.Where((x, i) =>
        !consumed.Contains(i) && x is not ("--write" or "-w")).ToArray();

    if (files.Length < 1) return Fail("layout requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var source = File.ReadAllText(files[0]);
    if (MerpeepsDocument.GetCapabilities(source) is { CanEdit: true, CanLayout: false })
        return Fail("This diagram uses ordered semantic layout, not graph layout engines.");
    var model = MerpeepsDocument.Load(source);

    // Fall back to the engine the file last used, so re-running the command
    // without an argument reproduces the arrangement rather than switching
    // algorithms underneath the author.
    var chosen = LayoutEngines.ById(
        engine ?? (LayoutEngines.IsKnown(model.LayoutEngine) ? model.LayoutEngine : null));

    foreach (var n in model.Nodes) n.HasExplicitPosition = false;
    chosen.Apply(model, LayoutOptions.Default);
    foreach (var n in model.Nodes) n.HasExplicitPosition = true;
    model.LayoutEngine = chosen.Id;

    var updated = MerpeepsDocument.SavePositions(source, model);

    if (MerpeepsDoc.BodyOf(updated) != MerpeepsDoc.BodyOf(source))
        return Fail("refusing to write — laying out altered the diagram body");

    if (inPlace) File.WriteAllText(files[0], updated);
    else if (outPath is null) { Console.Out.Write(updated); Console.Out.Flush(); }
    else File.WriteAllText(outPath, updated);

    Console.Error.WriteLine(
        $"merpeeps: positioned {model.Nodes.Count} node(s) with '{chosen.Id}' (body unchanged)");
    if (!inPlace && outPath is null)
        Console.Error.WriteLine("merpeeps: printed to stdout — add --write to edit the file");
    return 0;
}

static int ListEngines()
{
    var engines = LayoutEngines.Available
        .Select(e => new EngineDto(e.Id, e.Name, e.Description))
        .ToArray();
    Console.WriteLine(JsonSerializer.Serialize(
        new EngineListDto(engines, LayoutEngines.Default.Id),
        CliJson.Default.EngineListDto));
    return 0;
}

/// <summary>
/// Hand the file to the editor.
///
/// <para>Where the automated path stops. Structure, colour and icons all follow
/// from what a diagram means, so a program can choose them; position does not,
/// and a layout engine's answer is a starting point rather than a decision. The
/// honest end of an automated workflow is therefore a window.</para>
///
/// <para>Does not wait for the editor to close. The person may spend twenty
/// minutes arranging, and a caller blocked on that is a caller that looks
/// hung.</para>
/// </summary>
static int Open(string[] a)
{
    if (a.Length < 1) return Fail("open requires a file");
    var target = a[0];
    if (!File.Exists(target)) return Fail($"no such file: {target}");

    var located = EditorLauncher.Locate();
    if (!located.Found)
    {
        Console.Error.WriteLine("merpeeps: could not find the editor. Looked in:");
        foreach (var candidate in located.Searched)
            Console.Error.WriteLine($"  {candidate}");
        Console.Error.WriteLine(
            $"merpeeps: set {EditorLauncher.OverrideVariable} to the editor's path, " +
            "or build it with `dotnet build src/Merpeeps.App`");
        return 2;
    }

    var full = Path.GetFullPath(target);
    var pid = EditorLauncher.Launch(located.Path!, full);
    Console.Error.WriteLine($"merpeeps: opened {full} in the editor (pid {pid})");
    return 0;
}

/// <summary>
/// Read a JSON argument given inline, as a path, or on stdin.
/// </summary>
/// <remarks>
/// Inline is worth supporting because these payloads are usually one short
/// array, and forcing a temporary file for a two-operation edit turns a
/// one-liner into a script. The leading bracket or brace disambiguates without
/// a flag, since neither begins a usable filename.
/// </remarks>
static string ReadJsonArgument(string argument)
{
    if (argument == "-") return Console.In.ReadToEnd();

    var trimmed = argument.TrimStart();
    if (trimmed.StartsWith('[') || trimmed.StartsWith('{')) return argument;

    return File.ReadAllText(argument);
}

/// <summary>
/// Remove the metadata. Writes to stdout by default so the command composes
/// and cannot surprise anyone; <c>--write</c> is required to modify the file.
///
/// <para>It used to overwrite in place unconditionally, printing only
/// "stripped (body unchanged)". That reads like a report, not a warning, and
/// it destroyed a file during development — the discarded metadata is
/// unrecoverable, so the destructive path should be the one you ask for.</para>
/// </summary>
static int Strip(string[] a)
{
    var write = a.Contains("--write") || a.Contains("-w");
    var files = a.Where(x => x is not ("--write" or "-w")).ToArray();
    if (files.Length < 1) return Fail("strip requires a file");

    var src = File.ReadAllText(files[0]);
    var stripped = MerpeepsDoc.Serialise(src, null);

    if (MerpeepsDoc.BodyOf(stripped) != MerpeepsDoc.BodyOf(src))
    {
        Console.Error.WriteLine("merpeeps: refusing to strip — the body would change");
        return 1;
    }

    if (!write)
    {
        Console.Out.Write(stripped);
        return 0;
    }

    File.WriteAllText(files[0], stripped);
    var had = MerpeepsDoc.Parse(src) is not null;
    Console.Error.WriteLine(had
        ? $"merpeeps: removed the metadata from {files[0]} (body unchanged)"
        : $"merpeeps: {files[0]} had no metadata; nothing changed");
    return 0;
}

/// <summary>
/// Render a diagram to SVG, with no browser involved.
/// </summary>
/// <remarks>
/// Draws what the editor draws — stored positions, Merpeeps styling, icons — so
/// a build can produce diagram images without a Node runtime or a headless
/// Chrome. Styling and icons are exactly what mermaid cannot express, so this
/// is the only export that carries them as a picture.
/// </remarks>
static int Svg(string[] a)
{
    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    var themeIndex = Array.FindIndex(a, x => x is "--theme" or "-t");
    string? theme = null;
    if (themeIndex >= 0)
    {
        if (themeIndex + 1 >= a.Length) return Fail("--theme requires a name");
        theme = a[themeIndex + 1];
    }

    var consumed = new HashSet<int>();
    if (outIndex >= 0) { consumed.Add(outIndex); consumed.Add(outIndex + 1); }
    if (themeIndex >= 0) { consumed.Add(themeIndex); consumed.Add(themeIndex + 1); }
    var files = a.Where((_, i) => !consumed.Contains(i)).ToArray();

    if (files.Length < 1) return Fail("svg requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var source = File.ReadAllText(files[0]);
    var model = MerpeepsDocument.Load(source);
    var svg = SvgExporter.ToSvg(model, theme, model.Gantt is null ? null : new GanttEvaluationContext(DateTimeOffset.Now, TimeZoneInfo.Local, System.Globalization.CultureInfo.CurrentCulture));

    if (outPath is null) Console.Out.Write(svg);
    else
    {
        File.WriteAllText(outPath, svg);
        Console.Error.WriteLine($"merpeeps: wrote {outPath}");
    }
    return 0;
}

/// <summary>
/// Freeze a harvested layout into the file.
///
/// <para>The point is "compute once, replay forever": a layout engine this
/// project cannot run — anything that exists only as JavaScript — still produces
/// an answer that is just numbers. Once those numbers are in the Merpeeps block,
/// any renderer with the layout plugin reproduces the arrangement with no engine
/// present.</para>
///
/// <para>This was once also reachable from the editor, as "Adopt reference
/// layout". That went when the borrowed layouts did: it needed a browser pane
/// open, and opening one mid-edit was worse than the feature was worth. Here it
/// costs nothing, because a CLI has no window to disturb.</para>
///
/// <para>Positions are matched by node id, so ids absent from the harvest keep
/// whatever they had and unknown ids are reported rather than silently dropped —
/// a harvest from a stale render should be visible, not absorbed.</para>
///
/// <para>Positions only. The harvest also reports each node's measured size, but
/// the layout plugin re-measures every node from its rendered label and
/// overwrites whatever was stored, so a size in the file is bytes the renderer
/// discards — and one that silently becomes wrong the moment a label changes.
/// Position is the thing that genuinely cannot be recomputed; size is derived,
/// and derived data does not belong in the document.</para>
/// </summary>
static int Adopt(string[] a)
{
    var geometryIndex = Array.IndexOf(a, "--geometry");
    var posIndex = Array.FindIndex(a, x => x is "--positions" or "-p");
    if (geometryIndex >= 0 && posIndex >= 0) return Fail("--geometry and --positions are alternatives");
    var fullGeometry = geometryIndex >= 0;
    if (fullGeometry) posIndex = geometryIndex;
    if (posIndex < 0) return Fail("adopt requires --positions <file|-> or --geometry <file|->");
    if (posIndex + 1 >= a.Length) return Fail("--positions requires a path, or - for stdin");
    var posArg = a[posIndex + 1];

    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    // --write mirrors `strip --write`: edit in place. Spelt out rather than
    // implied, because silently rewriting the file someone passed in is the
    // kind of default that loses work.
    var inPlace = a.Contains("--write") || a.Contains("-w");
    if (inPlace && outPath is not null)
        return Fail("--write and --out are alternatives; pick one");

    var consumed = new HashSet<int> { posIndex, posIndex + 1 };
    if (outIndex >= 0) { consumed.Add(outIndex); consumed.Add(outIndex + 1); }
    var files = a.Where((x, i) =>
        !consumed.Contains(i) && x is not ("--write" or "-w")).ToArray();

    if (files.Length < 1) return Fail("adopt requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var json = posArg == "-" ? Console.In.ReadToEnd() : File.ReadAllText(posArg);
    if (fullGeometry)
    {
        var source = File.ReadAllText(files[0]);
        var graph = MerpeepsDocument.Load(source);
        var command = new AdoptRenderedGeometryCommand(graph, RenderedGraphGeometry.Parse(json));
        command.Do(graph);
        var written = MerpeepsDocument.SavePositions(source, graph);
        if (inPlace) File.WriteAllText(files[0], written);
        else if (outPath is null) Console.Out.Write(written);
        else File.WriteAllText(outPath, written);
        Console.Error.WriteLine($"merpeeps: adopted rendered geometry for {command.NodeCount} nodes");
        return 0;
    }
    var harvest = HarvestReader.Parse(json);
    if (harvest.Count == 0) return Fail("no usable positions in the harvest");

    var src = File.ReadAllText(files[0]);
    var model = MerpeepsDocument.Load(src);
    if (model.Sequence is not null || model.Chart is not null || model.Outline is not null || model.Temporal is not null || model.Packet is not null || model.Kanban is not null)
        return Fail("This diagram uses family-specific geometry; graph coordinate adoption does not apply.");

    var applied = 0;
    foreach (var node in model.Nodes)
    {
        if (!harvest.TryGetValue(node.Id, out var p)) continue;
        node.X = p.X;
        node.Y = p.Y;
        node.HasExplicitPosition = true;
        applied++;
    }

    if (applied == 0) return Fail("none of the harvested ids matched a node in the diagram");

    // SavePositions, not Save: naming the method is the request. `Save` only
    // writes a layout block if one was already there, which is right for a
    // structural edit and wrong here — freezing coordinates is the whole point.
    var result = MerpeepsDocument.SavePositions(src, model, writeDimensions: false);

    if (inPlace) File.WriteAllText(files[0], result);
    else if (outPath is null) Console.Out.Write(result);
    else File.WriteAllText(outPath, result);

    var unmatched = harvest.Keys.Where(k => model.Nodes.All(n => n.Id != k)).ToArray();
    Console.Error.WriteLine($"merpeeps: adopted {applied} position(s)");
    if (unmatched.Length > 0)
    {
        Console.Error.WriteLine(
            $"merpeeps: {unmatched.Length} harvested id(s) matched no node ({string.Join(", ", unmatched.Take(6))}"
            + (unmatched.Length > 6 ? ", …" : "") + ") — is the harvest from this diagram?");
    }
    return 0;
}

/// <summary>
/// Write a self-contained HTML page that renders the diagram with the real
/// upstream mermaid.
///
/// <para>Two audiences, one artefact. For a person it is a preview that needs no
/// network and no toolchain. For the test suite it is the exact page the
/// editor's reference pane loads, which is what lets CI verify that pane on
/// machines with no OS web engine — the webview itself cannot run headless, but
/// the page it displays can.</para>
/// </summary>
static int Preview(string[] a)
{
    var plain = a.Contains("--plain") || a.Contains("-p");
    var dark = a.Contains("--dark");

    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    // Which layout algorithm mermaid should use. Validated against the shell's
    // own list, so an unsupported name fails here with the supported ones named
    // rather than silently falling back to dagre in the browser.
    var layoutIndex = Array.FindIndex(a, x => x is "--layout" or "-l");
    string? layoutName = null;
    if (layoutIndex >= 0)
    {
        if (layoutIndex + 1 >= a.Length) return Fail("--layout requires a name");
        layoutName = a[layoutIndex + 1];
        if (!RenderAssets.Layouts.Contains(layoutName, StringComparer.OrdinalIgnoreCase))
            return Fail($"unknown layout '{layoutName}'; try: {string.Join(", ", RenderAssets.Layouts)}");
    }

    var files = a.Where((x, i) =>
        x is not ("--plain" or "-p" or "--dark" or "--out" or "-o" or "--layout" or "-l")
        && (outIndex < 0 || i != outIndex + 1)
        && (layoutIndex < 0 || i != layoutIndex + 1)).ToArray();

    if (files.Length < 1) return Fail("preview requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var source = File.ReadAllText(files[0]);

    // --plain shows what a stock renderer draws, which is the comparison that
    // matters: it is what every platform embedding mermaid will show.
    var mode = plain ? RenderAssets.Mode.Stock : RenderAssets.Mode.WithMerpeeps;
    var html = RenderAssets.BuildShell(mode, dark, source, layoutName);

    if (outPath is null) { Console.Out.Write(html); return 0; }

    File.WriteAllText(outPath, html);
    Console.Error.WriteLine(
        $"merpeeps: wrote {outPath} ({html.Length / 1024:N0} KB, renders offline)");
    return 0;
}

/// <summary>
/// Rewrite Merpeeps styling as native mermaid, for somewhere no plugin exists.
///
/// <para>Lossy by nature, and it says so. Icons cannot cross: mermaid's icon
/// shapes need an icon pack registered by the host at runtime, and the one
/// technique that does draw artwork — an <c>&lt;img&gt;</c> inside a label —
/// dies under <c>htmlLabels: false</c>. Positions cannot cross either, because
/// mermaid has no syntax for placing a node. Reporting that is the point; a
/// bake that quietly discarded half a diagram's design would be worse than no
/// bake at all.</para>
/// </summary>
static int Bake(string[] a)
{
    var outIndex = Array.FindIndex(a, x => x is "--out" or "-o");
    string? outPath = null;
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= a.Length) return Fail("--out requires a path");
        outPath = a[outIndex + 1];
    }

    var files = a.Where((x, i) =>
        x is not ("--out" or "-o") && (outIndex < 0 || i != outIndex + 1)).ToArray();

    if (files.Length < 1) return Fail("bake requires a file");
    if (!File.Exists(files[0])) return Fail($"no such file: {files[0]}");

    var result = MermaidBake.Bake(File.ReadAllText(files[0]));
    if (result.UnsupportedKind is { } unsupportedKind)
        return Fail(result.Diagnostic ?? $"This build cannot bake '{unsupportedKind}'.");

    // Collect diagnostics first and emit them after the document. Writing them
    // as we go lets stderr interleave into the middle of stdout when both go to
    // a terminal, which shredded a `style` line in testing.
    var notes = new List<string>();

    if (result.StyledNodes.Count > 0)
    {
        notes.Add($"baked styling for {result.StyledNodes.Count} node(s) into the body");
    }

    // Loss is named node by node so it can be acted on rather than merely noticed.
    if (result.DroppedIcons.Count > 0)
    {
        notes.Add($"icons dropped ({string.Join(", ", result.DroppedIcons)}) — mermaid has no " +
                  "syntax for artwork that renders without host-side setup");
    }

    if (result.DroppedPositions.Count > 0)
    {
        notes.Add($"{result.DroppedPositions.Count} stored position(s) dropped — mermaid will " +
                  "lay the diagram out itself");
    }

    if (result.Lossy)
    {
        notes.Add("keep the original; it is the one that still carries everything");
    }

    if (outPath is null)
    {
        Console.Out.Write(result.Output);
        Console.Out.Flush();
    }
    else
    {
        File.WriteAllText(outPath, result.Output);
        notes.Add($"wrote {outPath}");
    }

    foreach (var note in notes) Console.Error.WriteLine($"merpeeps: {note}");
    return 0;
}

static int Validate(string[] files)
{
    if (files.Length == 0) return Fail("validate requires at least one file");
    var bad = 0;
    foreach (var f in files)
    {
        var src = File.ReadAllText(f);
        var merpeeps = MerpeepsDoc.Parse(src);
        var re = MerpeepsDoc.Serialise(src, merpeeps);
        var body = MerpeepsDoc.BodyOf(re) == MerpeepsDoc.BodyOf(src);
        var idem = MerpeepsDoc.Serialise(re, MerpeepsDoc.Parse(re)) == re;
        var trip = MerpeepsDoc.ToJson(MerpeepsDoc.Parse(re)) == MerpeepsDoc.ToJson(merpeeps);
        var ok = body && idem && trip;
        if (!ok) bad++;
        Console.WriteLine(
            $"{(ok ? "PASS" : "FAIL")}  {f}  body={body} idempotent={idem} roundTrip={trip}");
        var capabilities = MerpeepsDocument.GetCapabilities(src);
        Console.WriteLine("      metadata invariants only; Mermaid syntax was not checked.");
        Console.WriteLine(capabilities.CanRenderNatively
            ? $"      native editing/layout/SVG supported for family '{capabilities.Kind}' (parser subset)."
            : $"      {capabilities.Diagnostic}");

        // SPEC §6b: a reader SHOULD surface packs it cannot resolve. This is
        // reported, never counted as a failure — a document that names a pack
        // this machine happens to lack is perfectly valid, and treating it as
        // broken would punish the author for the reader's missing tooling.
        var wanted = IconVocabulary.PacksReferencedByMetadata(merpeeps);
        if (wanted.Count > 0)
        {
            Console.WriteLine(
                $"      icon packs referenced: {string.Join(", ", wanted)} " +
                "(icons render as plain shapes without them)");
        }
    }
    return bad == 0 ? 0 : 1;
}

static int Conformance(string[] a)
{
    var path = a.Length > 0 ? a[0] : "samples.json";
    if (!File.Exists(path)) return Fail($"samples not found: {path}");

    var samples = JsonSerializer.Deserialize(
        File.ReadAllText(path), CliJson.Default.DictionaryStringString)!;

    var patch = new Dictionary<string, object?>
    {
        ["spec"] = 1,
        ["layout"] = new Dictionary<string, object?>
        {
            ["nodes"] = new Dictionary<string, object?>
            {
                ["ZZTOP"] = new Dictionary<string, object?>
                    { ["x"] = 4242, ["y"] = 8484 },
            },
        },
        ["futureUnknownKey"] = new Dictionary<string, object?>
            { ["nested"] = "PRESERVEME" },
    };
    var expected = MerpeepsDoc.ToJson(patch);

    int pass = 0, fail = 0;
    foreach (var (name, src) in samples)
    {
        var ext = MerpeepsDoc.Inject(src, patch);
        var ok = MerpeepsDoc.BodyOf(ext) == MerpeepsDoc.BodyOf(src)
              && MerpeepsDoc.ToJson(MerpeepsDoc.Parse(ext)) == expected
              && MerpeepsDoc.Inject(ext, patch) == ext
              && ext.Split('\n').Count(l => l.TrimEnd('\r') == "---") <= 2;
        if (ok) pass++; else { fail++; Console.Error.WriteLine($"FAIL {name}"); }
    }
    Console.WriteLine($"{{\"passing\":{pass},\"failing\":{fail}," +
                      $"\"verdict\":\"{(fail == 0 ? "CONFORMANT" : "VIOLATIONS")}\"}}");
    return fail == 0 ? 0 : 1;
}

/// <summary>
/// Wire shapes for the JSON commands.
/// </summary>
/// <remarks>
/// <para>Written out as records rather than the anonymous objects the MCP
/// server uses, because this binary is NativeAOT: reflection-based
/// serialisation is trimmed away, so every shape has to be known to the source
/// generator at build time. The upside is that the output schema is now
/// something you can read in one place.</para>
/// <para>Nulls are omitted, which is what makes <c>--positions</c> free: the
/// coordinates simply are not there when they were not asked for, rather than
/// appearing as a column of <c>null</c>.</para>
/// </remarks>
internal sealed record DescribeNodeDto(
    string Id, string Label, string Shape, double? X, double? Y);

internal sealed record DescribeLinkDto(
    string From, string To, string? Label, string Kind);

internal sealed record DescribeSubgraphDto(
    string Id, string? Title, IReadOnlyList<string> Members);

internal sealed record DescribeDto(
    string Kind, string Direction, int NodeCount, int LinkCount,
    IReadOnlyList<DescribeNodeDto> Nodes,
    IReadOnlyList<DescribeLinkDto> Links,
    IReadOnlyList<DescribeSubgraphDto> Subgraphs);

internal sealed record SequenceParticipantDto(string Id, string Label, string Kind, string Shape, string? BoxId);
internal sealed record SequenceStatementDto(string Id, string Kind, string? ParentBranchId, int Index, string? From, string? To, string? Text);
internal sealed record SequenceDescribeDto(
    string Kind, int ParticipantCount, int MessageCount,
    IReadOnlyList<SequenceParticipantDto> Participants, IReadOnlyList<SequenceStatementDto> Statements);

internal sealed record EngineDto(string Id, string Name, string Description);

internal sealed record EngineListDto(
    IReadOnlyList<EngineDto> Engines, string Default);

// DiagramOperation is registered directly rather than through a local copy, so
// the operations this CLI accepts cannot drift from the ones the MCP server and
// the editor accept — they are all deserialising into the same type.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DescribeDto))]
[JsonSerializable(typeof(SequenceDescribeDto))]
[JsonSerializable(typeof(EngineListDto))]
[JsonSerializable(typeof(DiagramCapabilities))]
[JsonSerializable(typeof(List<DiagramOperation>))]
internal partial class CliJson : JsonSerializerContext;
