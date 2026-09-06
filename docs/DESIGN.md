# Why Merpeeps is built this way

The reasoning behind the design, and the things that were **measured rather than
assumed** — several of which turned out to contradict what the documentation
said, or what an earlier version of this file confidently claimed.

For how to *use* it, see the [user guide](GUIDE.md). For the file format, see
[SPEC.md](../SPEC.md).

---
## Why this shape

Verified against Mermaid **11.16.1** (see the conformance suite):

| Claim | Status |
|---|---|
| Unknown `@{}` node keys are ignored | ✅ but **flowchart only** — not available in C4/architecture |
| Unknown YAML frontmatter keys are ignored | ✅ works for every diagram type |
| `%%` comments are ignored | ✅ works everywhere (fallback carrier) |
| Extended file renders identically to baseline | ✅ 30/30 renderable diagram types |
| No metadata reaches the user-visible surface | ✅ zero leaks |

Because `@{}` is flowchart-only, the carrier is **frontmatter** (`x-merpeeps`), with a
`%% merpeeps1 {...}` fallback for pipelines that strip frontmatter.

## Layout

```
src/
  Merpeeps.Core/        format engine — parse / serialise / inject. No UI deps, AOT-clean.
  Merpeeps.Cli/         `merpeeps` — NativeAOT single binary
  Merpeeps.Mcp/         stdio MCP server so coding agents can edit metadata surgically
  Merpeeps.App/         Avalonia desktop app
  Merpeeps.CanvasSpike/ canvas performance benchmark
tests/
  Merpeeps.Core.Tests/  inner loop — 896 render-free invariant tests
oracle/            outer loop — real mermaid.js as ground truth
```

`Merpeeps.Core` is the single implementation of the format. The app, CLI and MCP
server are thin shells over it, so there is nothing to drift.

### On ELK

Mermaid offers ELK as a layout plugin, and the obvious question is whether the
editor can too. It cannot, and the reason is worth recording so nobody
re-investigates it: **there is no .NET port of the Eclipse Layout Kernel.** ELK
is Java, with a JavaScript transpile — which is what `@mermaid-js/layout-elk`
uses. The `Elk.NET` package on NuGet is unrelated; it is *"a simple Kibana
Exception logger"*.

The editor did once offer ELK's four algorithms anyway, by running them in the
reference pane and reading the coordinates back. That is gone. It worked, and it
was still wrong: picking a layout from a dropdown would open a browser pane in
the middle of an edit, which reads as a bug rather than a feature — the report
that ended it was "why is an upstream renderer popping up?". Removing it also
dropped 1.6 MB of embedded plugin from every build.

**Nothing broke in existing files.** A layout is stored as coordinates, not as an
instruction to re-run an engine, so a document recording `engine: elk` keeps its
arrangement exactly and renders as it always did. The dropdown just will not
offer to compute a new one, and leaves itself alone rather than misreporting what
produced what is on screen.

The genuine .NET options, if a heavier engine is ever wanted, are
[`Microsoft.Msagl`](https://github.com/Microsoft/automatic-graph-layout) (MIT,
netstandard2.0) and [`GraphShape`](https://github.com/KeRNeLith/GraphShape)
(MS-PL). Both would need checking against two hard requirements first:
determinism, and NativeAOT compatibility for the CLI. A native port is the only
way ELK-quality layout comes back to the dropdown.

These are implemented natively in C# rather than by calling out to JavaScript,
so the editor keeps working with no Node runtime present and no process
boundary in the middle of a redraw.

**Every engine is deterministic.** That is a correctness requirement, not a
nicety: the coordinates are written into the user's file, so an engine that
varied between runs would produce a diff every time the document was opened.
Force-directed layout normally starts from random positions; ours starts from a
golden-angle spiral and runs a fixed number of iterations.

The engine that produced a layout is recorded as `layout.engine`, so reopening
a document keeps arranging newly added nodes the same way. `manual` means a
person placed them, and nothing will move them.

Agents get the same choice over MCP — `merpeeps_layout_engines` lists them,
`merpeeps_layout` takes an `engine` argument. The CLI has both as
`merpeeps layout-engines` and `merpeeps layout --engine`.

### Two front ends, one engine

The CLI and the MCP server are the same capabilities addressed differently: a
script drives verbs, an agent drives tools. Keeping them equal is not
housekeeping. They drifted once — layout was reachable from an agent and not
from a script — and nothing failed, because each surface's tests passed on its
own terms. A gap *between* two components is invisible to tests that only look
at one of them.

`oracle/surface-parity-test.mjs` compares them, and requires a written reason
for any asymmetry rather than merely tolerating one. Two are declared:
`preview` needs the vendored mermaid bundle, megabytes the MCP server would
carry to duplicate what `svg` and `open` already cover; and `conformance` runs
the spec suite over a corpus, so it tests the tool rather than operating on a
diagram.

### Where automation stops

`merpeeps open` and `merpeeps_open` hand a file to the editor, and they exist
because of a limit worth being explicit about. Structure, colour and icons all
follow from what a diagram means, so a program can choose them well. Position
does not. A layout engine produces a defensible arrangement, but *which*
defensible arrangement is right depends on what you are trying to show, and no
engine has an opinion about that.

So an automated workflow should compose the diagram, optionally arrange it as a
starting point, and then stop — at a window, with a person in front of it. That
is not a gap in the tooling; it is the shape of the problem.

### Freezing a layout you can't compute: harvesting

An engine the editor cannot run is still available as an *answer*. Render the
diagram in a real browser, read back where the nodes landed, write those numbers
into the file, and the arrangement survives everywhere afterwards — including
where that engine is not installed, which is the whole point of storing positions
at all.

The editor no longer does this for you, for the reasons above. The scripted path
remains, and has none of the problem, because nothing pops up:

```
merpeeps preview diagram.mmd --out page.html      # render it in a browser
# ...call window.merpeepsHarvest() on the page...
merpeeps adopt diagram.mmd --positions harvest.json --write
```

`--layout` picks what mermaid should use; the shell supports `dagre` and
`merpeeps`. Install a mermaid layout plugin in your own page and you can harvest
whatever it produces — `merpeeps adopt` only cares about the numbers.

Two details that are easy to get wrong, and are pinned by tests because both
failed first:

- **Mermaid positions nodes by their centre; Merpeeps stores the top-left.** The
  conversion needs the node's own bounding box, and it is read in SVG user units
  from the `transform`, never from a bounding rect — a rect is viewport pixels
  and would change with the zoom level.
- **Mermaid renames every node.** The element id is
  `<renderId>-<diagramType>-<authorId>-<counter>` and there is no attribute
  carrying the original, so the id has to be reconstructed. It anchors on the
  render id, which we pass in and therefore know exactly; a pattern that merely
  guessed at the leading segments silently mangled every id, and no C# test
  could have caught it.

`oracle/harvest-test.mjs` proves the round trip in a real browser: harvest what
dagre decided, **shift every node by a fixed offset**, adopt that, re-render, and
check the nodes land on the shifted coordinates.

The offset is the part that matters. Harvesting dagre and replaying it under
dagre would pass whether or not the file was read at all, because the engine
produces that arrangement unaided — the gate would be measuring nothing. Moving
every node somewhere no engine would choose removes the escape. It used to get
this for free by running ELK, a genuinely different engine; with ELK gone the
control is constructed instead, which is stronger: the expected answer is known
exactly rather than being whatever some other engine happened to say.

### Constraints, not coordinates

Choosing an engine decides *how* a diagram is arranged. `layout.constraints`
decides *what matters* about the arrangement, without pinning pixels:

```yaml
layout:
  constraints:
    - same-rank: [api, cache]     # keep these on one layer
    - order: [web, api, db]       # sequence them within their layer
```

Both express layout intent. They differ on what happens when they go stale, and
that is the entire argument:

| | When the diagram changes underneath it |
|---|---|
| `x: 10, y: 20` | describes nothing; the node lands somewhere wrong while the picture still looks plausible — **silently wrong** |
| `same-rank: [a, b]` | stops applying; the engine lays out normally — **correct, just not hand-tuned** |

Wrong-but-plausible is a worse failure than correct-but-ordinary, and it is a
difference in kind rather than degree. Constraints are also sparse — you say
something about the two or three nodes you care about — where coordinates pull
towards an entry per node.

This is what every durable text diagram language converged on independently:
Graphviz, D2, TikZ and PlantUML all make relationship placement first-class and
quarantine absolute coordinates into a separate opt-in mode. Mermaid's own
most-upvoted layout request ([#3723](https://github.com/mermaid-js/mermaid/issues/3723),
82 👍, open since 2022) asks for a rank constraint, not a coordinate map.

Coordinates are not deprecated — nothing else reproduces a hand-dragged
arrangement. They are demoted to the escape hatch, written only when explicitly
asked for. See SPEC §6c.

## Running on Windows, Linux and macOS

One codebase, three platforms. The editor is Avalonia, the CLI is NativeAOT, and
CI builds and tests all three on every push.

| | Windows | Linux | macOS |
|---|---|---|---|
| Editor (Avalonia) | ✅ | ✅ | ✅ |
| CLI (NativeAOT single binary) | ✅ `win-x64` | ✅ `linux-x64` | ✅ `osx-arm64` |
| MCP server | ✅ | ✅ | ✅ |
| Test suite | ✅ | ✅ | ✅ |

Portability is a claim that has to be checked rather than asserted, and doing so
found two real bugs that only appear off a US-English Windows box:

- **Coordinates were destroyed under a comma-decimal locale.** A stored `x: 12.5`
  read back as `0` on a German machine and the position was silently dropped.
  The cause was subtle: the value arrives as a `double`, and `raw.ToString()`
  formats using the *current* culture, so it became `"12,5"` and then failed to
  parse as invariant. No error surfaced anywhere — the layout was simply gone.
- **Line endings were rewritten.** A CRLF file saved as LF, turning a one-node
  move into a whole-file diff for every Windows user. YamlDotNet compounded it
  by emitting `Environment.NewLine`, so the same edit produced different bytes
  on Windows and Linux.

Both are now covered by tests, and CI runs the suite a second time under a
`de_DE.UTF-8` locale so the decimal-separator guard cannot quietly regress.

## The test loop

Correctness here is **not** a matter of opinion: stock `mermaid.js` is an
executable oracle. That is what makes this project safe to iterate on
autonomously.

**Inner loop — milliseconds, no browser.** Six pure invariants across the 33
official Mermaid syntax samples: body byte-preservation, round-trip, idempotence,
single-frontmatter merge, unknown-key preservation, comment-carrier equivalence.

```bash
dotnet test tests/Merpeeps.Core.Tests
```

**Outer loop — real mermaid.js in real Chrome.** Proves graceful degradation and
no-leak, which only the real renderer can answer.

```bash
cd oracle && npm ci && npm test
```

**Negative test — proves the harness can fail.** A green suite is worthless if it
cannot go red. This induces real violations and asserts they are caught.

```bash
cd oracle && npm run test:negative
```

**UI loop — the real window, rendered to real pixels.** The window, its XAML and
its handlers run headlessly with Skia, so a test can capture the frame and assert
on what is actually on screen.

```bash
dotnet test tests/Merpeeps.App.Tests
```

This layer exists because of a specific failure mode. Two UI bugs shipped past a
fully green suite — icons still drawn with Merpeeps styling switched off, and
labels left unreadable in dark mode — and both were caught by a person looking at
a screenshot. Asserting that the drawing code *intends* to skip the icon is a
weaker claim than it sounds, because a bug can sit between the intent and the
pixels. `RenderedFrameTests` closes that gap:

- the assertions are coarse on purpose (ink present, ink absent, colours
  distinct) so they survive font hinting, GPU drivers and Skia upgrades, where a
  golden-image comparison would not;
- each one was verified by reintroducing the bug it covers and confirming it goes
  red. The styling test reports `303 colours vs 303 styled` when the icon
  suppression is removed.

The editor also takes `--theme light|dark|system`, so an automated check can pin
the appearance without clicking through a menu first. Following the operating
system is the right default for a person and the wrong one for a test runner.

```bash
dotnet run --project src/Merpeeps.App -- --theme light docs/styled-sample.mmd
```

### Two lessons already paid for

Both are encoded as permanent guards, because both nearly produced false
confidence:

- **Leak detection must scan the user-visible surface, not raw SVG.** Numeric
  tokens collide with float path geometry — `4242` appears inside
  `-0.2025895406114242`. The check is differential (extended vs baseline) and
  restricted to text content.
- **Some diagram types are non-deterministic.** `gitgraph` mints random commit
  hashes every render, so the suite renders each baseline *twice* to detect
  engine entropy before blaming a change.

The oracle also fails if fewer than `MERPEEPS_MIN_RENDERABLE` (default 30) diagram
types render, so the surface cannot silently shrink — a hidden zero-size
viewport once cut it from 30 to 24 while still reporting green.

### Checking what the user actually sees

`oracle/grab-screen.ps1` captures the whole desktop and crops to Merpeeps's window
plus a margin. That is deliberate: tooltips, dropdowns and menus are separate
top-level windows, so a per-window screenshot misses them entirely, and an
accessibility tree will happily report a control as present while it renders
somewhere nobody is looking.

```powershell
pwsh oracle/grab-screen.ps1 -Out shot.png -DelaySeconds 5
```

It earned its place immediately. The theme explanation passed its tests — the
tooltip was open, with the right text — while actually rendering in the corner of
the screen, roughly 1,300px from the button it described. Only pixels showed
that.

## Canvas performance

Measured on the Avalonia custom-draw surface (`docs/canvas-benchmark.json`),
p50 render cost per frame:

| Nodes | Navigating (culled) | Fully zoomed out (no culling) |
|---:|---:|---:|
| 1,000 | 1.3 ms | 0.3 ms |
| 5,000 | 1.3 ms | 1.9 ms |
| 10,000 | 1.8 ms | 3.6 ms |
| 25,000 | 1.9 ms | 22.9 ms |

Navigation stays flat regardless of graph size because of viewport culling. The
worst case — every node on screen at once — holds 60 fps to ~10,000 nodes and
remains interactive (~43 fps) at 25,000.



## What baking can and cannot carry

`merpeeps bake` rewrites Merpeeps styling as native `style` statements, so the
colours show up anywhere mermaid runs. It is verified by rendering three ways —
stripped, baked, and through the plugin — and asserting the baked fills match the
plugin exactly while differing from the unstyled control. A diagram that ignored
every `style` statement would pass a mere parse check.

Both losses are measured facts rather than assumptions:

- **Positions** — mermaid has no syntax for placing a node.
- **Icons** — the `icon` shapes need an icon pack registered by the *host* at
  runtime, and the one technique that does draw artwork, an `<img>` inside a
  label, dies under `htmlLabels: false`. See `oracle/svg-artwork-hardened-probe.mjs`.

  That configuration is a host's to choose, and this project does not claim to
  know who chooses it. An earlier note said hardened hosts set it; measuring the
  vendored bundle showed `securityLevel: 'strict'` leaves html labels on, so the
  claim was dropped rather than repeated. What is measured is the consequence:
  *if* a host sets it, the artwork is gone.

Baking also refuses to touch diagram types whose grammar rejects `style` —
sequence, C4, architecture, journey, mindmap — because emitting it there would
turn a working file into a parse error. That list comes from
`oracle/style-support-probe.mjs`, which derives it from a real renderer rather
than from documentation.

Because it mutates the body, baking is an explicit export and never something
saving does.
