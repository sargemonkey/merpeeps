using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Merpeeps.Render;

/// <summary>
/// The JavaScript and HTML that let the editor render a diagram with the real
/// upstream <c>mermaid</c>, rather than with our own drawing code.
///
/// <para>This is a measuring instrument. Our canvas shows what Merpeeps thinks a
/// diagram looks like; this shows what a stock renderer actually draws, which is
/// the only thing that settles a disagreement. Keeping it honest means loading
/// genuine upstream artefacts — the pinned <c>mermaid</c> bundle, and the very
/// files we publish as <c>merpeeps-mermaid-layout</c> — never a reimplementation.</para>
///
/// <para>Everything here is pure string work with no windowing dependency, so it
/// is testable in headless CI even though the webview that consumes it is
/// not.</para>
/// </summary>
public static class RenderAssets
{
    /// <summary>How the reference render should be produced.</summary>
    public enum Mode
    {
        /// <summary>Stock mermaid alone — what GitHub, GitLab and Notion draw.</summary>
        Stock,

        /// <summary>
        /// Stock mermaid plus our published layout plugin, so stored positions
        /// and styling are applied. Doubles as end-to-end proof the npm artefact
        /// works, because it loads that package's real source.
        /// </summary>
        WithMerpeeps,
    }

    private const string MermaidRes = "Merpeeps.Render.Assets.mermaid.min.js";
    private const string YamlRes = "Merpeeps.Render.Assets.js-yaml.mjs";
    private const string IndexRes = "Merpeeps.Render.Assets.merpeeps.index.js";
    private const string PluginRes = "Merpeeps.Render.Assets.merpeeps.plugin.js";

    /// <summary>
    /// Layout algorithms the reference renderer can use.
    ///
    /// <para><c>dagre</c> is what mermaid does out of the box.</para>
    ///
    /// <para><c>merpeeps</c> is different in kind: it does not compute a layout at
    /// all, it replays the coordinates stored in the file. That distinction is
    /// the whole point of the format, and having both in one list makes it
    /// visible.</para>
    ///
    /// <para>The <c>elk.*</c> algorithms were here too, backed by a vendored copy
    /// of <c>@mermaid-js/layout-elk</c>. They were removed with the rest of the
    /// borrowed-layout machinery: the plugin cost 1.6 MB of embedded resource and
    /// only earned it while the editor could harvest an arrangement out of the
    /// browser, which it no longer does.</para>
    /// </summary>
    public static readonly string[] Layouts = ["dagre", "merpeeps"];

    private static readonly Assembly Owner = typeof(RenderAssets).Assembly;

    /// <summary>
    /// Bare module specifiers our published sources import, and the vendored
    /// resource that satisfies each.
    ///
    /// <para>A browser cannot resolve a bare specifier like <c>js-yaml</c>
    /// without a base URL, and the shell is loaded from a string, so there is no
    /// base URL to resolve against. Rather than bundling — which would mean
    /// testing a build artefact instead of the published source — the shell
    /// rewrites each specifier to a blob URL at runtime, leaving the published
    /// files byte-for-byte intact.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ModuleGraph = new(StringComparer.Ordinal)
    {
        ["js-yaml"] = YamlRes,
        ["./index.js"] = IndexRes,
        ["./rendered-geometry.js"] = "Merpeeps.Render.Assets.rendered-geometry.js",
    };

    public static string MermaidBundle => Text(MermaidRes);
    public static string MermaidVersion => Text("Merpeeps.Render.Assets.mermaid.version").Trim();
    public static string ZenUmlBundle => Text("Merpeeps.Render.Assets.zenuml.js");
    public static string ZenUmlVersion => Text("Merpeeps.Render.Assets.zenuml.version").Trim();
    public static string ZenUmlLicence => Text("Merpeeps.Render.Assets.zenuml.LICENSE");
    public static string ZenUmlThirdPartyNotices => Text("Merpeeps.Render.Assets.zenuml.NOTICES");
    internal static string ZenUmlValidationScript => Text("Merpeeps.Render.Assets.zenuml-validation.js");
    internal static string ValidationScript => Text("Merpeeps.Render.Assets.validation.js");
    public static string MermaidLicence => Text("Merpeeps.Render.Assets.mermaid.LICENSE");
    public static string JsYamlLicence => Text("Merpeeps.Render.Assets.js-yaml.LICENSE");
    public static string MerpeepsIndex => Text(IndexRes);
    public static string MerpeepsPlugin => Text(PluginRes);

    /// <summary>Read an embedded resource, naming it clearly when absent.</summary>
    private static string Text(string resource)
    {
        using var stream = Owner.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resource}' is missing. Available: " +
                string.Join(", ", Owner.GetManifestResourceNames()));

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Confirm every asset is present and every import the published package
    /// makes can be satisfied. Run at startup so a packaging mistake surfaces as
    /// a clear message instead of a silently blank pane.
    /// </summary>
    public static void Verify()
    {
        _ = MermaidBundle;
        _ = MermaidLicence;
        _ = JsYamlLicence;
        foreach (var resource in ModuleGraph.Values) _ = Text(resource);

        var unresolved = DiscoverImports(MerpeepsPlugin)
            .Concat(DiscoverImports(MerpeepsIndex))
            .Distinct(StringComparer.Ordinal)
            .Where(spec => !ModuleGraph.ContainsKey(spec))
            .ToArray();

        if (unresolved.Length > 0)
        {
            throw new InvalidOperationException(
                "merpeeps-mermaid-layout imports specifiers the reference shell cannot resolve: " +
                string.Join(", ", unresolved) +
                ". Vendor the dependency and add it to RenderAssets.ModuleGraph.");
        }
    }

    /// <summary>
    /// Every module specifier a source file imports.
    ///
    /// <para>Exists so that a new dependency in the published package fails
    /// loudly here rather than producing an empty pane at runtime. The package
    /// is developed independently of this app; without the check, the two drift
    /// silently.</para>
    /// </summary>
    internal static IEnumerable<string> DiscoverImports(string source) =>
        Regex.Matches(source, """^\s*(?:import|export)\b[^'"]*?from\s*['"]([^'"]+)['"]""",
                RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(source, """^\s*import\s*['"]([^'"]+)['"]""",
                    RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Build the page the webview loads. Navigation happens once; diagram text
    /// is pushed afterwards through <c>window.merpeepsRender</c>, so editing does
    /// not re-parse a three-megabyte bundle on every keystroke.
    /// </summary>
    /// <param name="autoRender">
    /// When supplied, the page renders this diagram as soon as it loads instead
    /// of waiting to be driven. Used for the standalone file <c>merpeeps preview</c>
    /// emits, which has no host to push text into it.
    /// </param>
    public static string BuildShell(Mode mode, bool dark, string? autoRender = null) =>
        BuildShell(mode, dark, autoRender, layout: null);

    /// <summary>
    /// As above, with an explicit layout algorithm.
    /// </summary>
    /// <param name="layout">
    /// A name from <see cref="Layouts"/>. Null leaves mermaid on its default.
    /// </param>
    public static string BuildShell(Mode mode, bool dark, string? autoRender, string? layout)
    {
        var sb = new StringBuilder(MermaidBundle.Length + 400_000);

        sb.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">\n<style>\n")
          .Append("html,body{margin:0;padding:0;height:100%;overflow:auto;")
          .Append("font-family:system-ui,-apple-system,'Segoe UI',sans-serif}\n")
          .Append("body{background:").Append(dark ? "#1e1e1e" : "#ffffff")
          .Append(";color:").Append(dark ? "#dddddd" : "#222222").Append("}\n")
          .Append("#stage{padding:16px;box-sizing:border-box}\n")
          .Append("#err{display:none;white-space:pre-wrap;padding:12px;margin:12px;")
          .Append("border-radius:6px;background:#5a1d1d;color:#ffd9d9;")
          .Append("font:12px ui-monospace,monospace}\n")
          .Append("</style></head>\n<body><div id=\"stage\"></div><pre id=\"err\"></pre>\n");

        // Classic script: the UMD bundle must define its global before any module runs.
        sb.Append("<script>\n").Append(MermaidBundle).Append("\n</script>\n");
        sb.Append("<script type=\"module\">\n").Append(Bootstrap(mode, dark, layout)).Append("\n</script>\n");

        if (autoRender is not null)
            sb.Append("<script type=\"module\">\n").Append(AutoRender(autoRender)).Append("\n</script>\n");

        sb.Append("</body></html>\n");

        return sb.ToString();
    }

    /// <summary>
    /// Resolve mermaid's global, optionally load our published plugin through
    /// rewritten blob URLs, and expose one render entry point.
    /// </summary>
    private static string Bootstrap(Mode mode, bool dark, string? layout)
    {
        var sb = new StringBuilder();

        sb.Append("""
            const stage = document.getElementById('stage');
            const errBox = document.getElementById('err');

            // The id we hand mermaid for every render. It is also the prefix mermaid stamps
            // onto each node element, so the harvest anchors on it to recover author ids.
            // One constant, because the two uses have to agree or the harvest returns junk.
            const RENDER_ID = 'merpeeps-preview';

            function fail(e) {
              errBox.style.display = 'block';
              errBox.textContent = String(e && e.stack ? e.stack : e);
              window.merpeepsError = errBox.textContent;
            }
            window.addEventListener('error', (e) => fail(e.error ?? e.message));
            window.addEventListener('unhandledrejection', (e) => fail(e.reason));

            // The bundle namespaces itself rather than exposing a bare global, so
            // probe the shapes it is known to take instead of assuming one.
            const mermaid =
              globalThis.mermaid
              ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid?.default
              ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid;
            if (!mermaid) throw new Error('mermaid bundle did not expose a usable global');

            """);

        // 'merpeeps' is not a mermaid layout name — the plugin registers and
        // selects it itself — so it is never passed through as config.
        var configLayout = layout is null || layout.Equals("merpeeps", StringComparison.OrdinalIgnoreCase)
            ? "dagre"
            : layout;

        sb.Append("mermaid.initialize({ startOnLoad: false, theme: ")
          .Append(dark ? "'dark'" : "'default'");
        sb.Append(", layout: ").Append(JsLiteral(configLayout));
        sb.Append(", look: 'classic' });\n\n");


        if (mode == Mode.WithMerpeeps)
        {
            sb.Append("const SOURCES = new Map();\n");
            foreach (var (specifier, resource) in ModuleGraph)
                sb.Append("SOURCES.set(").Append(JsLiteral(specifier)).Append(", ").Append(JsLiteral(Text(resource))).Append(");\n");
            sb.Append("SOURCES.set('__plugin', ").Append(JsLiteral(MerpeepsPlugin)).Append(");\n\n");

            sb.Append("""
                // Load the published merpeeps-mermaid-layout source unmodified, rewiring
                // only its bare import specifiers to blob URLs. Bundling would test a
                // build artefact; this tests what people actually install.
                const urls = new Map();
                const resolving = new Set();

                function moduleUrl(name) {
                  if (urls.has(name)) return urls.get(name);
                  if (resolving.has(name)) {
                    throw new Error('import cycle through ' + name);
                  }
                  resolving.add(name);

                  let src = SOURCES.get(name);
                  if (src === undefined) throw new Error('no source registered for ' + name);

                  for (const spec of SOURCES.keys()) {
                    if (spec === '__plugin' || spec === name) continue;
                    // Only recurse into a dependency this file genuinely imports.
                    // Walking every key unconditionally makes js-yaml and index.js
                    // resolve each other forever.
                    const q1 = `'${spec}'`, q2 = `"${spec}"`;
                    if (!src.includes(q1) && !src.includes(q2)) continue;
                    const u = moduleUrl(spec);
                    src = src.split(q1).join(`'${u}'`).split(q2).join(`"${u}"`);
                  }

                  const url = URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
                  urls.set(name, url);
                  resolving.delete(name);
                  return url;
                }

                const plugin = await import(moduleUrl('__plugin'));
                if (typeof plugin.renderWithLayout !== 'function') {
                  throw new Error('merpeeps-mermaid-layout did not export renderWithLayout');
                }

                // The layout has to be registered with mermaid before it can be
                // selected; without this the plugin falls back to auto-layout and
                // silently renders a correct-looking but unpositioned diagram.
                plugin.registerMerpeepsLayout(mermaid);

                window.merpeepsRender = async (text) => {
                  errBox.style.display = 'none';
                  stage.innerHTML = '';
                  window.merpeepsSource = text;
                  const { svg } = await plugin.renderWithLayout(mermaid, RENDER_ID, text);
                  stage.innerHTML = svg;
                  return svg;
                };
                """);
        }
        else
        {
            sb.Append("""
                window.merpeepsRender = async (text) => {
                  errBox.style.display = 'none';
                  stage.innerHTML = '';
                  window.merpeepsSource = text;
                  const { svg } = await mermaid.render(RENDER_ID, text);
                  stage.innerHTML = svg;
                  return svg;
                };
                """);
        }

        sb.Append("""

            // Read back where the renderer actually placed each node, so an
            // arrangement mermaid computed can be frozen into a file as Merpeeps
            // coordinates and replayed anywhere afterwards.
            //
            // The editor no longer calls this. It used to, behind the ELK and
            // dagre entries in the layout dropdown, and choosing one of those
            // opened a browser pane mid-edit — surprising enough to read as a
            // bug. The dropdown now offers only in-process engines.
            //
            // What remains is the scripted path, which has no such problem:
            // `merpeeps preview` writes a page exposing this function, and
            // `merpeeps adopt --positions` writes the result into a file. Nothing
            // pops up, and it stays available for anyone who wants a layout this
            // project cannot compute natively.
            //
            // translate() is in SVG user units and is what the layout engine
            // decided; a bounding rect would be viewport pixels and would change
            // with zoom. getBBox is likewise in user units.
            window.merpeepsHarvest = async (text) => {
              // Default to whatever is on screen. A page that baked its diagram
              // in at build time has no host holding the source, so requiring it
              // would make the harvest unusable exactly where it is most useful.
              const src = text ?? window.merpeepsSource;
              if (src == null) throw new Error('no source to harvest');
              await window.merpeepsRender(src);

              const svg = stage.querySelector('svg');
              if (!svg) throw new Error('nothing rendered');

              const out = {};
              for (const g of svg.querySelectorAll('g.node')) {
                const m = /translate\(\s*([-\d.]+)[ ,]+([-\d.]+)\s*\)/
                  .exec(g.getAttribute('transform') ?? '');
                if (!m) continue;

                // mermaid builds the element id as
                //   <renderId>-<diagramType>-<authorId>-<counter>
                // and there is no data-* attribute carrying the author's id, so
                // it has to be recovered. Anchoring on the render id — which we
                // pass ourselves and is therefore a known constant — is what
                // makes this safe: an id like "a-b-2" contains hyphens and
                // digits of its own, so a pattern that merely guessed at the
                // leading segments would mis-split it.
                let id = g.id;
                if (id.startsWith(RENDER_ID + '-')) {
                  id = id.slice(RENDER_ID.length + 1)   // drop the render id
                         .replace(/^[a-zA-Z]+-/, '')    // drop the diagram type
                         .replace(/-\d+$/, '');         // drop mermaid's counter
                }
                if (!id) id = g.id;

                let box;
                try { box = g.getBBox(); } catch { box = { width: 0, height: 0 }; }

                out[id] = {
                  // mermaid draws from the centre; Merpeeps stores the top-left.
                  x: Math.round((Number(m[1]) - box.width / 2) * 100) / 100,
                  y: Math.round((Number(m[2]) - box.height / 2) * 100) / 100,
                  w: Math.round(box.width * 100) / 100,
                  h: Math.round(box.height * 100) / 100,
                };
              }
              return JSON.stringify(out);
            };

            """);

        sb.Append("\nconst geometryModule = await import(");
        if (mode == Mode.WithMerpeeps) sb.Append("moduleUrl('./rendered-geometry.js')");
        else sb.Append("URL.createObjectURL(new Blob([").Append(JsLiteral(Text("Merpeeps.Render.Assets.rendered-geometry.js"))).Append("], {type:'text/javascript'}))");
        sb.Append(");\nwindow.merpeepsHarvestGeometry = async () => JSON.stringify(geometryModule.harvestRenderedGeometry(stage.querySelector('svg'), RENDER_ID));\n");
        sb.Append("\n\nwindow.merpeepsReady = true;\n");
        return sb.ToString();
    }

    /// <summary>
    /// A complete page that renders <paramref name="source"/> as soon as it
    /// loads, with no host driving it.
    ///
    /// <para>The editor's pane navigates once and then pushes text in, because
    /// re-parsing a three-megabyte bundle per keystroke would be absurd. A
    /// standalone file has no host to push anything, so the diagram is baked in.</para>
    /// </summary>
    public static string BuildPage(Mode mode, bool dark, string source) =>
        BuildShell(mode, dark, source);

    /// <summary>
    /// Script that renders a baked-in diagram once the shell has initialised.
    ///
    /// <para>This used to be spliced into a finished page by searching for
    /// <c>&lt;/body&gt;&lt;/html&gt;</c>. That was quietly catastrophic: the
    /// mermaid bundle embeds DOMPurify, whose source contains that exact string
    /// as a JavaScript literal, so the splice landed in the middle of a
    /// third-party library and corrupted it. Composing the page in one pass
    /// removes the whole class of bug — never pattern-match against a document
    /// carrying three megabytes of someone else's code.</para>
    /// </summary>
    private static string AutoRender(string source)
    {
        var sb = new StringBuilder();
        sb.Append("const source = ").Append(JsLiteral(source)).Append(";\n");
        sb.Append("""
            // Do not assume this module runs after the shell has finished. The
            // shell uses top-level await to import the plugin, and relying on
            // module ordering across separate <script> elements is exactly the
            // kind of assumption that works on one engine and not the next.
            // Wait for the entry point to appear, or for the shell to report why
            // it never will.
            async function whenReady(timeoutMs = 30000) {
              const started = Date.now();
              while (Date.now() - started < timeoutMs) {
                if (typeof window.merpeepsRender === 'function') return true;
                if (window.merpeepsError) return false;
                await new Promise((r) => setTimeout(r, 25));
              }
              return false;
            }

            const box = document.getElementById('err');
            function show(message) {
              box.style.display = 'block';
              box.textContent = message;
              window.merpeepsError = message;
            }

            if (!(await whenReady())) {
              show(window.merpeepsError ?? 'the render shell did not initialise in time');
            } else {
              try {
                await window.merpeepsRender(source);
                window.merpeepsRendered = true;
              } catch (e) {
                show(String(e && e.stack ? e.stack : e));
              }
            }
            """);
        return sb.ToString();
    }

    /// <summary>
    /// Embed JavaScript source as a JavaScript string literal, for a host that
    /// needs to build a call into an already-loaded page.
    /// </summary>
    public static string JsLiteralForHost(string source) => JsLiteral(source);

    /// <summary>
    /// Embed JavaScript source as a JavaScript string literal.
    ///
    /// <para>Hand-written rather than delegating to a JSON serialiser, because
    /// the serialiser is reflection-based and breaks NativeAOT — the CLI that
    /// emits these pages is AOT-compiled.</para>
    ///
    /// <para>Three escapes here are not optional, and two of them are easy to
    /// miss:</para>
    /// <list type="bullet">
    ///   <item><c>&lt;</c> and <c>&gt;</c>, because a literal <c>&lt;/script&gt;</c>
    ///   anywhere in the payload ends the enclosing HTML script block, no matter
    ///   that it sits inside a JavaScript string. The page would break at that
    ///   byte with a syntax error somewhere inside a three-megabyte line.</item>
    ///   <item>U+2028 and U+2029, which are literal line terminators in
    ///   JavaScript even though they are ordinary characters in JSON.</item>
    ///   <item>Control characters, which are invalid raw inside a string
    ///   literal.</item>
    /// </list>
    /// </summary>
    internal static string JsLiteral(string source)
    {
        var sb = new StringBuilder(source.Length + 64);
        sb.Append('"');

        foreach (var c in source)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;

                // Neutralise any '</script>' hiding in the payload.
                case '<': sb.Append("\\u003c"); break;
                case '>': sb.Append("\\u003e"); break;
                case '&': sb.Append("\\u0026"); break;

                // Line terminators in JavaScript, ordinary characters elsewhere.
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;

                default:
                    if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
