# Merpeeps.Core

Store diagram **layout positions, styling and metadata inside ordinary Mermaid
files** — without changing what they render.

Mermaid has no coordinate system, so there is nowhere in its syntax to record
where a node should sit. Merpeeps puts that information under an unknown YAML
frontmatter key, which every Mermaid diagram type ignores. Your tool reads the
positions; every other tool sees a perfectly ordinary diagram.

```csharp
using Merpeeps.Core;

var src = "flowchart LR\n    a[Start] --> b[End]\n";

// Load, arrange, save. The diagram body is returned byte-for-byte unchanged.
var model = MerpeepsDocument.Load(src);
model.Node("b")!.X = 220;
var saved = MerpeepsDocument.SavePositions(src, model);

MerpeepsDoc.BodyOf(saved) == MerpeepsDoc.BodyOf(src);   // true, or SavePositions throws
```

`SavePositions` enforces body preservation at the boundary rather than trusting
the caller: if the write would alter the diagram text, it throws instead of
silently corrupting the file.

Explicit geometry saves retain node width and height even when they return to
the model defaults (160 by 56). Omitting those values would leave an earlier
custom size in the recursively merged metadata, so undoing a resize would be
lost on reopen. Ordinary structural saves of unpositioned documents still do not
introduce a layout block.

## Check native support before loading

```csharp
var capabilities = MerpeepsDocument.GetCapabilities(src);
if (!capabilities.CanEdit)
    Console.WriteLine(capabilities.Diagnostic);
else
    model = MerpeepsDocument.Load(src);
```

Native typed editing and SVG entry points cover all **33 catalog families and
48 dialects**, including charts, sequences, ownership diagrams and grammar
structures. `CanLayout` means generic graph layout, not family availability:
data/time/grid/ownership-driven families use their own typed layout.
`flowchart-elk` is a legacy directive alias, not an ELK implementation.
`GetCapabilities` does **not** validate syntax or promise every grammar construct.

`Load` throws `UnsupportedDiagramException` for unknown families instead of
inventing flowchart nodes. `MerpeepsDoc.Parse`, `Inject`, `Serialise` and
`BodyOf` remain type-agnostic, so callers can preserve or change metadata on
other diagram families without loading a native model.

## Discover families and create an empty diagram

`MermaidDiagramCatalog` describes the frozen **Mermaid 12.0.0 authoring target**:
33 documented families, with dialects such as the five C4 views and four railroad
notations. Its versioned documentation links do not depend on changing upstream
default documentation. The catalog is shared by consumers rather than copied
into each app.

```csharp
var families = MermaidDiagramCatalog.Families;
var family = MermaidDiagramCatalog.GetFamily("flowchart");
var draft = MermaidDiagramCatalog.CreateEmpty(family.Id);
// draft.Source is "flowchart TD\n"; draft.Model has no nodes or edges.
```

`CreateEmpty(familyId, dialectId)` returns a new `MermaidDiagramDraft` containing
`Source`, `Model` and `Capabilities`. Omit `dialectId` for the family's default.
Use `Merpeeps.Core.Editing` commands to author content in the returned model.
There are no demonstration nodes or automatically inserted examples.

**Catalog membership is not complete feature parity or syntax validation.** All
catalog family/dialect empty factories are implemented without substitute
flowcharts or demonstration content. Unknown family/dialect IDs throw
`ArgumentException`. Use `GetCapabilities(dialect.Directive)` to
communicate current native availability. Do not treat `CanEdit` or a successful
permissive `Load` as proof that an imported source is syntactically valid.

Core does not bundle the upstream browser renderer. `Merpeeps.Render` is optional
and carries Mermaid 12.0.0 plus the separate ZenUML validation integration.
See the [family capability and limitations table](https://github.com/sargemonkey/merpeeps/blob/main/docs/DIAGRAM-SUPPORT.md).
This prerelease is not a promise of full browser/native visual parity.

## Typed class diagrams

Class cards use `DiagramNode.Class` rather than flattening members into a label.
`ClassNodeDefinition` distinguishes classes, notes and individual lollipop
occurrences. Node identity/label/geometry remain on the existing node.
`ClassMember` preserves attribute/operation kind, visibility, signature and
static/abstract flags. `DiagramEdge.ClassRelationship` has independent endpoint
markers, stroke type, two multiplicities and the existing edge label/identity.

`ClassEditing` and the commands in `Merpeeps.Core.Editing` support members,
annotations, classes, notes, namespaces, relationships and atomic interface
insertion. `RenameClassIdentityCommand.Result` and
`ReconnectClassRelationshipCommand.Result` expose replacement selections after
`Do`; undo restores the original objects. Use `AddClassInterfaceCommand` for a
lollipop and its one relationship, never save an orphan interface.
`SetClassNamespaceDefinitionCommand` changes a namespace's qualified identity,
label and parent with reference repair; `DeleteClassNamespaceCommand` moves its
classes to an explicit destination instead of deleting their content.

`ClassDiagramText.Content` interprets class-card rows; notes and lollipop labels
are already decoded plain text. `ClassDiagramStyle.Resolve` resolves source
styles, then optional Merpeeps overrides, returning typed colors and pixel
lengths. Diagnostics identify unsupported style properties rather than pretending
they were rendered. Theme colors fill values that remain null in the host.
`ClassDiagramGeometry` shares card regions, endpoint markers and namespace scope.
Lollipop nodes own the single circle; their relationship marker adds no second
circle. Namespace frames are derived from direct class membership and parents,
not independently authored rectangles.

Class identity metadata under `x-merpeeps.identity.classDiagram` contains IDs and
fingerprints for members, notes and interface occurrences, never a duplicate of
their content. The Mermaid body remains authoritative. Geometry-only saves
preserve the body. Member/relation changes patch existing source spans; topology
changes regenerate known structure while retaining comments. Unsupported
structural syntax fails explicitly instead of being rendered as a flowchart.
Callbacks are preserved as inert source and are never executed.

Hosts must render/edit/export these typed fields before enabling class editing;
an older generic-node renderer cannot consume the new class capability safely.
Family recognition still does not promise every upstream rendering behavior or
CSS property is implemented.

## Typed entity-relationship diagrams

ER entities use `DiagramNode.Entity`, not a Class facet or a flowchart node label.
`EntityAttribute` preserves a stable ID, free-form data type, name, PK/FK/UK key
set and comment. `EffectiveKeys` includes Mermaid's `*name` primary-key notation;
`WithKeys` can deliberately clear that notation. `EntityDiagramText.Content`
supplies typed rows and interpreted bold/italic entity-name runs.

`DiagramEdge.EntityRelationship` keeps each endpoint's cardinality independent
and distinguishes identifying (solid) from non-identifying (dashed) relationships.
The existing label is the relationship's role, not an endpoint multiplicity.
`EntityDiagramGeometry.Endpoint` resolves an actual entity **or subgraph**.
`Route` includes self-loops and separate parallel paths; `Marker` exposes
individual circle/bar/fork primitives. A group endpoint must not be invented as
an entity table.

`EntityDiagramGeometry.Columns` has a measurement-callback overload for native
font metrics. The default SVG approximation is not an exact typography promise.
Native hosts should use their real font measurement for column sizing and
explicit fit actions. `EntityDiagramStyle.Resolve` reuses the existing paint
value structure without reinterpreting ER data as Class semantics.

`EntityEditing` attribute operations and `AddEntityCommand`,
`SetEntityAliasCommand`, `RenameEntityIdentityCommand`,
`AddEntityRelationshipCommand`, `SetEntityRelationshipCommand` and
`ReconnectEntityRelationshipCommand` use the existing single `IEditCommand`
history. Rename/reconnect expose replacement `Result` objects for selection.
Groups use `AddEntityGroupCommand`, `SetEntityGroupCommand`,
`DeleteEntityGroupCommand` and `SetEntityGroupMembershipCommand`. Group deletion
retains contents and reparents child groups; only relationships incident to the
removed group are deleted. Group IDs are global, not qualified Class namespaces.

Nested subgraph direction is respected during automatic grouped layout.
Explicit entity positions take precedence. Empty groups have a derived visible
frame; this is not an independently persisted user-resizable group rectangle.
Attributes use identity-only metadata under `x-merpeeps.identity.erDiagram`;
entity/group content remains in Mermaid source. Local attribute/relationship
changes preserve unaffected source, while structural regeneration retains comments.

## Ordered sequence diagrams

`DiagramModel.Sequence` is an independent `SequenceDiagramModel`; its participants,
declarations, nested fragments/branches, messages, activations, notes, numbering,
participant boxes and inert metadata are not graph nodes or edges. Generic graph
operations/layout engines are refused. Participant lane order is maintained with
declaration and creation order, and unsupported rearrangements fail before mutation.

`SequenceEditing` returns one `SequenceEditCommand` for each atomic operation.
It validates a detached candidate before replacing the live sequence; `ResultId`,
`Locate` and `LocateBranch` provide stable selection and insertion locations.
Creation is paired with its receiving message. Branch zero has empty `Text`; the
initial condition belongs to the enclosing block, and `SetBranchText` handles
that distinction. Commands preserve participant configuration JSON and stable
statement/branch identities across source saves.

`SequenceDiagramAdapter.ReadSource` returns the parsed tree plus structured
`SequenceDiagnostic` codes and `InvalidStructure`/`NativeUnsupported` kinds.
`SequenceNotEditableException.Result` retains that result for a source-only host.
This is separate from validation by the real Mermaid grammar.

`SequenceDiagramLayout.Create(model, measure, themeOverride)` derives ordered
figures, labels and selection regions without changing the model. The optional
measurement callback receives full `SequenceFont` family/size/integer weight and
returns both width and height. Consumers must preserve figure layer order and
text alignment; mirrored headers have distinct occurrence IDs but share their
participant owner. Lifetimes and stacked open activations are derived from events.
The default SVG measurement is approximate, and reports that fact.

Current native policy rejects messages outside a destroyed participant's
lifetime and non-adjacent participant-box lanes even where Mermaid's parser is
more permissive. Source/tree/diagnostics remain recoverable; hosts must not
mislabel those limits as a Mermaid syntax failure.

## Numeric charts: Pie, Quadrant, XY and Radar

`DiagramModel.Chart` holds one of four distinct `ChartDocument` types.
They do not populate graph nodes/edges or a Sequence model. `ChartEditing`
operations use validated snapshots; `Result` is a `ChartTarget` with a
role-scoped identity, not an ambiguous display label.

`PieChart` keeps ordered slices and `showData`. Zero values are retained without
inventing area; later duplicate labels are native-unsupported because Mermaid
silently ignores them. Donut regions use a true annular cubic path, not a white
circle painted over the center. Hover highlight is view state only.

`QuadrantChart` keeps normalized point data, four region labels, paired axis
labels, point classes and direct styles. Direct point styles override class and
theme styles. Dragging uses `ChartCoordinateMap`, not graph coordinates.

`XyChart` keeps categorical/range/auto axes, explicit-vs-config orientation,
ordered mixed bar/line series and optional point labels. Numeric X values are
distributed across the range per series; the first nonempty series seeds auto
X. Bars start at the plot's lower bound, matching the pinned renderer rather
than assuming a zero baseline. Extra values past named categories remain a
native-policy rejection, not silently truncated. Per-point bar labels are
preserved but warned as not rendered, matching upstream.

`RadarChart` uses role-scoped axis and curve IDs (the same ID may exist in both).
Values are tied to axis IDs even for positional source. Reordering/renaming axes
preserves that mapping; adding an axis requires values for every existing curve.
Circle grids use the pinned cubic tension formula, and polygon grids retain
straight curves. Axis scale and label factors are independent of curve scale.

`ChartConfiguration.Definitions/Value/Set/Validate` supplies known typed options,
explicit unsetting and diagnostics while preserving unknown JSON fields.
`ChartDiagramLayout.Create` returns a neutral `DrawingScene`, semantic hit
regions, data transforms and diagnostics. Drawing paths support Move/Line/Cubic/
Close; fills/strokes are nullable RGBA, and clips/layers are explicit. Label
rotation is about `Bounds.Center`. Width/height measurement can be injected;
omission is marked approximate. `DrawingSceneSvg.Write` preserves the same
geometry. Hosts must implement clipping or reject exports that cannot preserve it.

Drawing owner roles are `ChartElementRole` names. XY point IDs are value IDs with
the parent series ID in `DetailId`; radar curve-value owners use curve ID plus
axis ID in `DetailId`. Shared hit testing respects donut holes and clips, and
prioritizes data-value handles over a whole series.

## Timelines, journeys and outlines

`DiagramModel.Temporal` represents Timeline periods/events or User Journey
tasks/scores/actors. Time-period labels are text, not dates to sort. Sections and
item order are authoritative, and continuation events retain their own IDs.
`TemporalEditing` uses atomic snapshots and preserves actor lists and 1-5 scores.

`DiagramModel.Outline` represents a Mindmap or Tree View. `OutlineNode.Children`
is the hierarchy; opaque IDs are separate from optional Mindmap source IDs.
Mindmap shapes/Markdown/icons/classes and Tree View directories/descriptions/
annotations are distinct fields. `OutlineEditing` provides subtree operations,
cycle-safe reparenting, sibling ordering and explicit Mindmap position overrides.
Tree View does not use graph coordinates. Source indentation and Unicode
box-drawing trees are both read; source regenerates without inventing a root.

`DataDiagramLayouts.Create` reuses only neutral drawing primitives and real
font measurement. It supplies wrapped labels, text styles, selection owners,
diagnostics, and an origin offset/unshifted node bounds for Mindmap position
edits. Default Mindmap layout balances root branches; `tidy-tree` uses one side.
Tree View uses ordered rows; Timeline supports LR/TD; Journey draws actor
markers and score faces. External icon/CSS dependencies remain in source and
are explicitly diagnosed; unknown icon packs are not silently assumed installed.
Built-in file/folder icons and explicit icon hiding are handled locally.

## Packet fields and Kanban boards

`DiagramModel.Packet` keeps ordered fields with positive widths and optional
explicit starts. `Resolve` requires contiguous fields starting at zero; relative
width declarations remain relative. Rendering splits a field into row segments
without creating multiple source fields. `PacketEditing` validates changes;
move/delete operations explicitly reflow field positions and remain one undo.

`DiagramModel.Kanban` keeps ordered columns and cards. Opaque editor identities
are distinct from optional source IDs, so duplicate card source IDs in different
columns do not collapse records. YAML task metadata and unknown keys round-trip;
assignee, ticket and priority are displayed as data. No diagram ticket action is
executed automatically. `KanbanEditing` provides column/card edits and transfers.
`BoardPacketLayout.Create` returns the same neutral measured drawing primitives.

## Requirement and GitGraph data

`DiagramModel.Requirements` has typed requirements/elements and seven relationship
types. Opaque identity is separate from the requirement name and displayed ID.
Class/default/direct style resolution validates before a snapshot edit is
committed. `RequirementEditing` and `RequirementDiagramLayout` share these
semantics without treating requirement text as a generic node label.

`DiagramModel.GitDiagram` is a diagram operation log, never repository state.
`Replay` derives branch heads, commits, parents and the current branch, and
validates branch/merge/cherry-pick dependencies before mutations. Duplicate
explicit commit IDs, which upstream overwrites, are rejected with source-policy
diagnostics rather than guessed. `GitDiagramEditing` provides atomic operations
and ordering; deleting an operation that breaks later dependencies fails.
Automatic native display IDs are not source-addressable cherry-pick targets.

`PacketEditing.UpdateAndReflow` explicitly updates following absolute ranges;
the original strict `Update` remains unchanged.

## Weighted flows and treemaps

`DiagramModel.Sankey` stores CSV-derived nodes and weighted directed links;
quoted commas/quotes and blank/comment lines are retained semantically.
Cycles and negative/non-finite weights are refused before edits commit.
`SankeyEditing.AddFlow` creates missing endpoints atomically and deleting flows
removes source-unrepresentable orphan nodes.

`DiagramModel.Treemap` is a weighted hierarchy with values only on leaves.
`TreemapEditing` preserves stable identities during subtree moves and rejects
cycles. Parent totals are derived, not copied into a second value field.
`WeightedDiagramLayout` produces weighted ribbons/rectangles and semantic hits.
Small tile labels remain in the model and receive a diagnostic rather than
being shrunk or discarded. Supported value-format strings are explicit; other
D3 formatters fail rather than guessing.

`DrawingFigure.Gradient` is an optional two-stop linear gradient with RGBA
colors and an angle. Consumers must preserve it or refuse an unsupported
export; silently treating it as a flat fill is not correct.

`DrawingLabel.Outline` and `OutlineWidth` describe a glyph outline painted
behind the foreground text, not a rectangular background. Defaults are null/0.
Sankey `labelStyle: outlined` uses a 4-DIP outline and positions labels by the
layer of the first maximum-value node; `legacy` uses horizontal position.
Unsupported style values are rejected before an edit commits.

Renaming a Sankey node moves its label-keyed custom color with it. An existing,
different destination color is a conflict, not permission to overwrite data.
Undo restores both the old label and map. Treemap label and value annotations
must fit both measured dimensions, including padding; otherwise the annotation
is omitted with `tile-label`/`tile-value` diagnostics and the data is retained.

## Block grids and Architecture ports

`DiagramModel.Blocks` is an ordered, nested grid. `BlockItem` keeps scoped
column counts, spans, spaces, composites, shapes and block-arrow directions.
Connections retain independent endpoint decorations. `BlockDiagramEditing`
changes placement order/parentage rather than inventing free X/Y coordinates;
`BlockDiagramLayout` returns measured placements and native drawing primitives.

`DiagramModel.Architecture` separates services, junctions and nested groups.
Connections name member nodes with explicit ports and optional group-boundary
ownership. Ordered row/column alignment participates in the native constraint
layout, and contradictory constraints fail before an edit commits.
`ArchitectureDiagramEditing.SetOption` supports native icon/spacing controls;
fcose-only options remain in source with diagnostics and cannot be presented
as implemented native controls. External icon packs are not silently assumed
available. Deleting a group reparents members and repairs boundary endpoints.

`DrawingFigure.DashPattern` optionally carries exact dash/gap lengths in DIPs,
independent of stroke width. It takes precedence over the legacy `Dashed` flag.

## C4 declarations and containment

`DiagramModel.C4` keeps five dialects and ordered typed declarations, separate
from generic graph nodes. Stable identities are distinct from source aliases.
`C4DiagramEditing` provides cloned, validated edits and relationship ordering;
`C4DiagramLayout` returns measured cards, containment, routes and diagnostics.
The fixed per-type C4 palette is overridden by explicit configuration/styles,
not silently replaced by an app theme. Dynamic relation numbers follow order;
the `RelIndex` source argument remains preserved.

Repeated ordered endpoint pairs, which upstream coalesces, are preserved
source-only rather than invented as parallel relationships. Extra arguments
such as tags/links are retained as inert data and reported separately from
implemented visual properties. Offsets use the upstream integer truncation
rule.

## Gantt temporal evaluation

`DiagramModel.Gantt` preserves source start/end specifications rather than
overwriting them with resolved dates. `GanttScheduleResolver.Resolve` and
`GanttDiagramLayout.Create` require an explicit `GanttEvaluationContext`
(reference instant, timezone and optional culture). Capture it once in the
host and reuse it for visible edits and snapshot/export measurement. Core never
reads the system clock. `SvgExporter.ToSvg` accepts this context for Gantt;
omitting it is an explicit error.

Dependency end and rendered end remain separate across excluded days. Calendar
month/year truncation and day/week rounding follow the pinned runtime.
Numeric `X` start tokens use milliseconds while `X` end tokens use seconds,
matching the observed upstream asymmetry; raw source tokens remain visible.
Missing references, first-task implicit starts, invalid durations, date
fallbacks and cycles are not guessed. Arbitrary CSS and unsupported tick/date
formats preserve source but refuse native editing/output.

`GanttDiagramEditing` accepts the evaluation context and validates a detached
candidate before assignment. Removing a section explicitly reparents and moves
its tasks to the destination section. Callbacks/links are inert retained data.

## Strategy maps, cause trees and complexity domains

`DiagramModel.Wardley`, `Ishikawa` and `Cynefin` retain separate typed data.
`WardleyEditing`, `IshikawaEditing` and `CynefinEditing` apply validated
snapshots; `StrategyDiagramLayouts` returns measured neutral scenes and
diagnostics. Wardley layout also exposes forward/inverse normalized coordinates.
Component source coordinates are visibility/evolution; the returned point's
X/Y are evolution/visibility. Pipeline variants inherit their parent's
visibility. Source annotation coordinates follow the pinned parser's same
visibility/evolution order, even where the documentation calls them x/y.

Ishikawa retains the problem independently of its nested cause tree. Cynefin
retains all five domains and all items; only the first three confusion items
are drawn, followed by an overflow badge and diagnostic. Self transitions
that upstream ignores are explicitly refused. `Cynefin` layout accepts a
stable `renderId` for seed-zero boundary variation; callers should reuse the
same value for preview/export. The default is stable, not a hidden random seed.
Confusion hit-testing follows the actual ellipse instead of treating the empty
corners of its bounding rectangle as part of that domain. Automatic Wardley
stage boundaries cannot be changed without explicitly selecting width syntax.

## Venn and Railroad

`DiagramModel.Venn` keeps set identifiers, optional source sizes/labels,
overlaps and their attached text independently. The source word `union` denotes
the common overlap. `VennGeometry` computes circle intersection areas and fits
circles deterministically; `VennDiagramLayout` reports requested versus actual
areas and warns when a circle arrangement cannot match the data. Higher-arity
regions add implied pairwise constraints only to layout, not to saved content.
Negative/nonfinite sizes and overlaps larger than their sets are refused.

Venn overlap hit regions use `DrawingHitKind.CircleIntersection` with exact
`DrawingRegion.Circles` constraints; callers must retain those constraints
rather than treating their bounding box as the overlap.

`DiagramModel.Railroad` is a typed grammar expression tree with stable rule and
expression IDs. The four notation adapters preserve EBNF exceptions, PEG ordered
choice/lookahead, ABNF bounded repetitions/numeric literals and the documented
IR constructors. Grouping punctuation is derived. `RailroadEditing` changes the
tree transactionally, and `RailroadDiagramLayout` draws actual branches,
bypasses and backward repetition paths without fabricating graph nodes.

## What's included

- `MerpeepsDoc` — parse, serialise and inject the metadata block
- `MerpeepsDocument` — load into a positioned model, save positions or a full edit
- `MermaidDiagramCatalog` — versioned family/dialect discovery and genuine empty native drafts
- `IDiagramAdapter` — parser/writer per diagram family (flowchart, state, class, ER)
- `LayeredLayout` — deterministic Sugiyama-style layout, cycle-safe
- `EditCommands` — undoable add/delete/rename/connect/style operations
- `SurgicalWriter` — minimal diffs; a rename changes one token, not the file

AOT-clean and trim-clean, with no UI dependencies.

## Verification

The claim that Mermaid ignores the metadata is measured, not assumed: a
conformance suite renders every diagram type twice in a real browser — plain and
extended — and compares. Against Mermaid 11.16.1 that is 30/30 renderable types
identical, with zero leaks into the rendered output.

A JavaScript implementation (`merpeeps-mermaid-layout` on npm) is held to the same shared
conformance suite, so the two cannot drift apart.

## Licence and names

MIT.

**Not affiliated with, endorsed by, or sponsored by Mermaid Chart Inc. or the
Mermaid open-source project.** "Mermaid" is used here only to describe the file
format this library reads and writes.
