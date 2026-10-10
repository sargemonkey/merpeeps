# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No changes recorded after the preview release candidate.

## [0.1.0-preview.1] - 2026-10-10

Draft release candidate; publishing the draft and package registries remain
separate operations. Desktop/CLI/MCP version `0.1.0-preview.1`; Core and Render
packages `0.2.0-preview.1`; Scene `0.1.0-preview.1`; npm companion `0.2.0-preview.1`.
Earlier local family builds remain frozen and are not overwritten.

### Preview highlights

- Merpie identity, locked sea-magpie mascot and native/platform icons. Technical
  CLI/MCP/library IDs and `x-merpeeps` remain compatible.
- Typed creation/authoring for all 33 catalog families and 48 dialects. Entry-point
  coverage is not complete upstream feature parity; see `docs/DIAGRAM-SUPPORT.md`.
- Compact pictorial tools, inline properties, full-range color dialogs and
  left-to-right Timeline layout.
- Node/group multi-select, six alignments and two distributions with one Undo
  step; nested/empty groups, cancellation and automatic-position Undo fixes.
- Internal relationship hit-priority fixes and persistent Sequence branch edits.
- Explicit rendered-geometry adoption with native/preview/SVG route and dimension
  persistence, retaining the legacy position-only path.
- Self-contained Windows, Intel/Apple Silicon macOS and Linux preview packages,
  hashed manifests/checksums, packed NuGet/npm consumers and draft-only release
  automation. No registry publication or installed-app changes are implied.

Known issues: fixed-color imported group/edge captions can be difficult to read
in dark app appearance; use light appearance. Transparent output, installer/
automatic updates and signed/notarized desktop distribution remain pending.
Native platform usability/accessibility and exact browser/export parity are
not fully accepted by automated suites.

### Added

- **All 33 catalog families have typed creation routes** — Venn set/overlap
  editing and four Railroad notations; ownership-aware Swimlanes and UML Use
  Cases; agent containment/metadata, ordered Event Modeling and independent
  nested ZenUML calls. This completes family entry points, not every documented
  feature, native accessibility gate or export-fidelity requirement.
- **Venn area diagnostics** — circle fitting uses requested sizes and analytic
  intersections, exact overlap hit tests, and explicit residual warnings when
  circle geometry cannot match all constraints.
- **Preserved final-family semantics** — ordered JSON table rows and duplicate
  replacement policy, implicit caller rebinding for ZenUML subtree moves,
  inert event payloads and metadata, and collapsed agent-flow endpoints.

- **C4 and Gantt authoring** — all five C4 dialects retain levels, containment,
  typed relationships and inert extra arguments. Gantt keeps raw temporal
  specifications, explicit host-captured clock/timezone, dependency and rendered
  end dates, calendar exclusions, and section-preserving edits.
- **Wardley, Ishikawa and Cynefin authoring** — semantic visibility/evolution
  maps, nested cause trees and fixed-domain items/transitions use their own
  shared models and layout. The all-family implementation and broader native
  acceptance remain in progress.

- **Block and Architecture authoring** — ordered nested Block grids, spans,
  spaces, composites, shape/arrow distinctions and exact dash patterns; typed
  Architecture services/junctions/groups, port connections, group boundaries
  and row/column constraints. Source/history safety and measured placements are
  shared with consumers. External Architecture icon packs and fcose-only layout
  controls are preserved/diagnosed rather than falsely presented as native.

- **Continuous multi-family authoring batches** — Pie/Quadrant/XY/Radar,
  Timeline/Journey/Mindmap/Tree View, and Packet/Kanban now have separate typed
  models and native authoring paths. Shared neutral drawing supports curves,
  holes, clips, text rotation and semantic hit regions without coercing data
  into graph nodes. The remaining families and full native/viewer acceptance
  remain open; no release-completion claim is implied.

- **Ordered sequence authoring** — participants/stereotypes, all 26 grammar arrow
  tokens, nested interaction fragments/branches, notes, activation spans,
  lifecycle-paired messages, boxes and decimal numbering. Typed snapshot commands
  validate before mutation; source saves retain identities. Native figure/label
  projection supports measured font weight/height and semantic selection.
  CLI/MCP summaries report sequence participants/events; graph operations reject
  sequences instead of inventing nodes. Remaining-family work is still open.
- **Entity-relationship authoring** — typed entities/attributes/key sets/comments,
  aliases and shared Markdown name runs; independent crow's-foot cardinalities,
  identifying lines, subgraph endpoints, self/parallel paths and reversible edits.
  Source-backed attribute identities survive edits. Native entity tables use real
  font measurement for explicit fit; native SVG font metrics remain approximate.
  This is one family in the still-incomplete all-family implementation, not a
  claim that every Mermaid type now has a visual editor.
- **Visual New diagram gallery** — startup without a file and File > New now
  show diagram-type previews rather than an automatic sample or type dropdown.
  Tile activation creates a genuinely empty supported document; alternate syntax
  is secondary and unavailable native types are named truthfully. Back/cancel
  preserves the current document and inspector drafts.
- **Type-specific palettes** — the editor no longer mixes stencil collections
  with diagram-type selection. Flowchart shapes, basic state tools and the class
  inspector follow the actual loaded document. State start/end markers use real
  pseudo-state identities and cannot be silently saved without a transition.
- **Selection-driven Class authoring** — simple creation when nothing is selected,
  class/member actions for a selected card, and relation-only properties for a
  selected edge. Add relationship chooses its target on the canvas with Escape
  cancellation and a keyboard alternative. Detailed tasks are transient and
  unapplied fields survive selection changes; diagram-wide settings live in menus.
- **Typed class authoring** — real class cards, members/modifiers, independent
  UML endpoint markers and multiplicities, namespace nesting, diagram notes and
  lollipop occurrences; reversible commands and identity-preserving source saves.
  The standalone New menu now chooses empty flowchart/state/class documents, with
  a class-specific inspector and native card/SVG rendering.
- **Frozen family catalog and empty drafts** — Core exposes Mermaid 12.0.0's
  33 documented families and dialects. Empty flowchart/state drafts contain no
  demo nodes; unsupported native creation remains an explicit error.
- **Offline syntax validation assets** — the separate Render package uses the
  real pinned Mermaid parser, with typed valid/invalid/unsupported/unavailable
  results, source-request isolation and parser-relative diagnostics. It does not
  treat a catalog entry or valid syntax as native editing support.
- **Real ZenUML grammar validation** — a validation-only bridge awaits the pinned
  standalone parser and reports its syntax diagnostics. A deliberate no-op-parser
  control verifies that malformed input would otherwise be wrongly accepted.
  This completes the validator's family inventory, not visual authoring coverage.
- **Pinned Mermaid 12 reference runtime** — classic/dagre reference defaults are
  retained explicitly; metadata conformance covers all 32 bundled families.
  ZenUML reference rendering remains separate from its validation integration.
  The removed native ELK layout controls are not restored.
- **Shared visual scenes** (`Merpeeps.Scene`) — renderer-independent text and
  diagram objects with scene-scoped IDs, content references, geometry and
  styling. The separate `x-merpeeps-scenes` envelope preserves unknown metadata
  and keeps unsupported versions/types read-only. Existing diagram `x-merpeeps`
  semantics are unchanged.
- **The `x-merpeeps` format** — node positions, styling, icons and layout
  constraints stored inside ordinary Mermaid files, under a frontmatter key stock
  renderers discard before parsing. Verified against real mermaid.js across 30
  diagram types.
- **A visual editor** (`Merpeeps.App`) — drag nodes, draw links, rename in place,
  colour from a palette or a full picker, apply icons, and six in-process layout
  engines: layered, tree, force, circular, radial and grid.
- **A mermaid layout plugin** (`merpeeps-mermaid-layout/mermaid`) — registers
  through mermaid's official layout interface so any mermaid-based viewer draws a
  saved arrangement instead of recomputing one.
- **A command-line tool** (`merpeeps`) — read, capabilities, describe, validate, strip, edit,
  layout, layout-engines, adopt, bake, svg, preview, open and conformance,
  publishable as a single NativeAOT binary.
- **An MCP server** — twelve tools giving coding agents the same operations
  without a screen.
- **`merpeeps open` / `merpeeps_open`** — hands a file to the visual editor.
  Structure, colour and icons follow from what a diagram means, so automation can
  choose them; node positions are a judgement, so the end of an automated
  workflow is a window rather than a guess.
- **A hand tool** — `H`, or **✋ Pan** in the toolbar, makes a left-drag move the
  view. Panning already worked on right-drag, middle-drag, `Ctrl`-drag and
  space-drag, and was still reported as missing: a capability with no visible
  control is one people conclude you do not have. The gestures are unchanged.
- **`merpeeps bake`** — rewrites Merpeeps styling as native `style` statements so
  colours survive on GitHub with no plugin, and reports what it could not carry
  rather than dropping it silently. Available from the editor's File menu and
  from the command line.
- **A conformance oracle** — real mermaid.js in a real browser as ground truth,
  plus further gates covering the npm package, the MCP server over stdio,
  themes, previews, store-and-replay, and CLI/MCP capability behavior and parity.

### Fixed

- New Block/Architecture workspaces retain useful canvas space at narrow
  widths by automatically hiding the optional source pane unless the user
  explicitly chooses it. Source/history stay unchanged, and the canvas refits
  after its actual size is available.

- Sankey outlined labels now carry a real glyph outline and central-layer
  placement; node renames retain label-keyed custom colors, with conflicting
  destination colors rejected rather than overwritten.
- Treemap labels and formatted values check both measured dimensions. Text that
  cannot fit is retained with a diagnostic instead of being painted outside its
  tile.

- Standalone New has an explicit native invoke provider. Reference preview pages
  use private content-addressed files rather than rewriting a shared page that
  another window's web engine may still have mapped.
- Geometry saves now explicitly overwrite default node dimensions. Previously,
  undoing a custom size to 160 by 56 omitted `w`/`h` from the patch, leaving the
  old size in merged metadata and restoring it on reopen. Unpositioned structural
  saves still leave layout optional.

### Notes on scope

- **Native support is explicit.** `GetCapabilities`, `merpeeps capabilities`
  and `merpeeps_capabilities` report family-level editing/layout/SVG support.
  Unsupported source is no longer guessed to be a flowchart. The desktop editor
  preserves it in a source-only view; metadata operations remain available.
  Metadata validation is not Mermaid syntax validation. Bake reports a missing
  native adapter separately from a grammar that cannot accept styles.

- **The CLI and the MCP server are held to the same capability set.** They are
  two front ends over one engine, and a gap between them is invisible to tests
  that only look at one — which is how layout came to be reachable from an agent
  and not from a script. `oracle/surface-parity-test.mjs` compares them and
  requires a written reason for any asymmetry. Two are declared: `preview` needs
  the vendored mermaid bundle, and `conformance` tests the tool rather than a
  diagram.

- **Layouts are computed in-process, in C#.** Layout engines that exist only as
  JavaScript are not offered from the editor's dropdown: running one means
  rendering in a browser and reading the coordinates back, and doing that behind
  a dropdown opens a browser pane in the middle of an edit. The scripted path is
  available for anyone who wants it — `merpeeps preview` writes a page exposing
  `window.merpeepsHarvest`, and `merpeeps adopt --positions` writes the result
  into a file.

- **Not built yet:** complete all-family structural authoring and native adapters
  beyond flowchart/graph, state and class diagrams. The metadata format is type-agnostic; other families can
  retain metadata and be previewed through the reference Mermaid renderer.
