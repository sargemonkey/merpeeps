using System.Text.Json;
using System.Text.Json.Serialization;
using Merpeeps.Core;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Merpeeps.Render;

// merpeeps — NativeAOT CLI over the Merpeeps engine.
//   merpeeps read      <file>
//   merpeeps validate  <file> [more...]     exit 1 if any invariant fails
//   merpeeps strip     <file>
//   merpeeps conformance <samples.json>     inner-loop suite (render-free)

if (args.Length == 0) { Usage(); return 2; }

var cmd = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return cmd switch
    {
        "read" => Read(rest),
        "validate" => Validate(rest),
        "strip" => Strip(rest),
        "adopt" => Adopt(rest),
        "svg" => Svg(rest),
        "bake" => Bake(rest),
        "preview" => Preview(rest),
        "conformance" => Conformance(rest),
        "-h" or "--help" or "help" => Usage(),
        _ => Fail($"unknown command '{cmd}'"),
    };
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

          merpeeps read <file>                 print the Merpeeps block as JSON
          merpeeps validate <file>...          check invariants (exit 1 on failure)
          merpeeps strip <file>                print the plain mermaid to stdout
          merpeeps strip <file> --write        remove the metadata from the file
          merpeeps adopt <file> --positions <json>  freeze harvested coordinates in
          merpeeps adopt <file> --positions <json> --write   ... editing in place
          merpeeps bake <file>                plain mermaid with the styling baked in
          merpeeps svg <file>                 render to SVG — styling and icons included
          merpeeps preview <file>              self-contained HTML, rendered by real mermaid
          merpeeps preview <file> --plain      as a stock renderer draws it, ignoring Merpeeps
          merpeeps conformance <samples.json>  run the render-free invariant suite

        `bake` rewrites Merpeeps styling as native `style` statements, so the
        colours show up anywhere mermaid runs — GitHub, GitLab, Notion — with no
        plugin. It is lossy by nature: icons and stored positions have no
        equivalent in mermaid syntax, and it says so rather than dropping them
        quietly.

        `preview` writes one HTML file with mermaid embedded — it opens in any
        browser with no network access, and is how the editor's reference pane
        renders. Use --out to write it somewhere instead of stdout.
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
    var svg = SvgExporter.ToSvg(MerpeepsDocument.Load(source), theme);

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
    var posIndex = Array.FindIndex(a, x => x is "--positions" or "-p");
    if (posIndex < 0) return Fail("adopt requires --positions <file|-> ");
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
    var harvest = HarvestReader.Parse(json);
    if (harvest.Count == 0) return Fail("no usable positions in the harvest");

    var src = File.ReadAllText(files[0]);
    var model = MerpeepsDocument.Load(src);

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
    var result = MerpeepsDocument.SavePositions(src, model);

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

    // Collect diagnostics first and emit them after the document. Writing them
    // as we go lets stderr interleave into the middle of stdout when both go to
    // a terminal, which shredded a `style` line in testing.
    var notes = new List<string>();

    if (result.UnsupportedKind is { } kind)
    {
        notes.Add($"'{kind}' has no `style` statement in its grammar, so nothing could be baked. " +
                  "The plain diagram is unchanged.");
    }
    else if (result.StyledNodes.Count > 0)
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

        // SPEC §6b: a reader SHOULD surface packs it cannot resolve. This is
        // reported, never counted as a failure — a document that names a pack
        // this machine happens to lack is perfectly valid, and treating it as
        // broken would punish the author for the reader's missing tooling.
        var wanted = IconVocabulary.PacksReferencedBy(MerpeepsDocument.Load(src));
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

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CliJson : JsonSerializerContext;
