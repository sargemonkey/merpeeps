# Merpeeps — format specification, v0.1 (draft)

A **type-agnostic** sidecar layer for layout, style and semantics that rides
inside ordinary Mermaid files and degrades gracefully in stock renderers.

## Design constraints (empirically established)

| Constraint | Evidence |
|---|---|
| `@{ }` per-node metadata is **flowchart-only** | mermaid `flowDb.ts` dispatch; not present in architecture/C4 grammars |
| Unknown YAML frontmatter keys are silently ignored | verified: parse + render clean, mermaid 11.16.1 |
| `%%` comments are always ignored | verified: parse + render clean |
| Extension must not alter rendered output | verified: zero leak, identical node/edge counts |

**Chosen carrier: YAML frontmatter under a single namespaced key.**
Rationale: structured (mermaid already runs `js-yaml` over it), one block rather
than scattered comments, trivially diffable, and type-agnostic — frontmatter is
handled by mermaid's common preprocessor, not per-diagram grammars.

`%%` comments are the **fallback carrier** for hosts that strip frontmatter
(see §6).

## 1. Shape

```yaml
---
title: Payment flow              # untouched official key
x-merpeeps:
  spec: 1
  layout:
    nodes:
      A: { x: 120, y: 80, w: 160, h: 60 }
      B: { x: 300, y: 240 }
    edges:
      "A->B#0": { waypoints: [[200,150],[260,200]], labelT: 0.5 }
    viewport: { x: 0, y: 0, zoom: 1 }
    engine: manual              # manual | dagre | elk | fcose
  style:
    nodes:
      A:
        fill: "#dae8fc"       # any CSS colour
        stroke: "#6c8ebf"
        text: "#1a1a1a"
        strokeWidth: 2
        dashed: false
        icon: gateway         # see §6b
    edges:
      "A->B#0": { stroke: "#6c8ebf", width: 2, dash: false }
  semantics:
    A: { kind: container, tech: "Node.js", team: platform, url: "…" }
  extras: {}                     # reserved; preserved verbatim
---
flowchart TD
  A[Start] --> B[End]
```

Every key is **optional**. A file carrying only `layout.nodes` is valid.

## 2. Identity (the hard part)

The spec joins to the diagram by **entity id**. Mermaid has no universal id
concept, so each diagram type needs a small *identity adapter* — far smaller
than a per-type editor.

| Type family | Node identity | Edge identity |
|---|---|---|
| flowchart, class, state, ER | declared id | `"<src>-><dst>#<ordinal>"` |
| sequence | participant id/alias | message ordinal `"#<n>"` |
| C4, architecture | declared alias | `"<src>-><dst>#<ordinal>"` |
| pie, venn, radar, xychart | label text (normalised) | n/a |
| gantt, kanban, timeline | task/item id, else ordinal | n/a |
| mindmap, treemap, treeView | path from root `"root/a/b"` | n/a |

**Ordinal suffix** (`#0`) disambiguates parallel edges between the same pair.
Ordinals are assigned in **source order** and are stable under edits that do not
reorder statements.

**Anonymous entities** get a synthetic, reserved id. A state diagram's `[*]`
carries no id yet needs a position, and it means *start* in the source position
but *end* in the target position — so it maps to two distinct ids, `__start` and
`__end`, rather than one. Reserved ids use a `__` prefix, which mermaid's own
grammars do not produce.

**Rule:** an identity that no longer resolves is *retained, not deleted*, and
flagged `stale` — so renaming a node in text never silently destroys its layout.

## 3. Invariants (all machine-checkable)

1. **Degradation** — stock Mermaid renders base and Merpeeps-extended source to the
   same visible text and the same node/edge counts.
2. **No leak** — no Merpeeps token appears in rendered output.
3. **Round-trip** — `serialise(parse(x))` preserves every byte outside the
   `x-merpeeps` block.
4. **Idempotence** — applying `serialise(parse(·))` twice equals once.
5. **Merge safety** — injecting into a file that already has frontmatter must
   merge, never emit a second `---` block.
6. **Unknown preservation** — unrecognised keys under `x-merpeeps` survive a
   round-trip verbatim (forward compatibility).
7. **Type agnosticism** — 1–6 hold for every Mermaid diagram type that renders.

## 4. Versioning

`spec` is an integer. A reader encountering `spec` greater than it understands
MUST still render, MUST preserve the block verbatim, and SHOULD warn.

## 5. Why `x-` prefix

Reserves the namespace against future official mermaid frontmatter keys, mirroring
the `x-` convention in OpenAPI/HTTP. If mermaid later blesses a layout key, Merpeeps
migrates rather than collides.

## 6. Fallback carrier

Where frontmatter is stripped (some wikis, some markdown pipelines), the same
document is emitted as a single comment line:

```
%% merpeeps1 {"layout":{"nodes":{"A":{"x":120,"y":80}}}}
```

Readers MUST accept either. Writers SHOULD prefer frontmatter.

## 6b. Icon vocabulary
`style.nodes.<id>.icon` names a glyph drawn in place of a plain box. This is among
the most-requested node features in mermaid (#1723, 122 👍, open since 2020) and
is not expressible in mermaid syntax, which is why it belongs here.

### Core names

Names with **no prefix** are the core vocabulary. They are stable, always
available, and no pack may claim one:

`server`, `database`, `storage`, `queue`, `cache`, `function`, `gateway`,
`user`, `browser`, `mobile`, `shield`, `cloud`, `cog`

The empty string means "no icon".

### Pack names

Everything else is `pack:name` — `aws:lambda`, `azure:sql`, `bpmn:gateway`. The
prefix is what stops two independent packs colliding on `gateway`.

```
icon-name  = core-name / pack-id ":" local-name
pack-id    = 1*( ALPHA / DIGIT / "-" )        ; lowercase
local-name = 1*( ALPHA / DIGIT / "-" / "_" )  ; lowercase
```

A name with more than one `:` is invalid; readers MUST treat it as unknown
rather than guessing at the intent.

### What an unrecognised name means

**A reader that does not recognise a name MUST draw the plain shape, MUST retain
the name unchanged, and MUST NOT fail.**

All three clauses carry weight:

- **Draw the plain shape** — a missing pack degrades the picture, never the
  document. This is also exactly what mermaid does with an unregistered icon
  pack: the node renders as an ordinary box.
- **Retain the name** — the same rule as unresolvable node ids in §2. Opening a
  diagram on a machine without the AWS pack and saving it must not silently
  strip every AWS icon from a colleague's work. Deletion is never the safe
  default when the tool is the thing that is missing, not the data.
- **Do not fail** — a document that will not open because of a decorative
  reference is a worse outcome than one that opens plainer than intended.

A reader SHOULD surface which packs are referenced but unavailable, so the
degradation is visible rather than mysterious.

### What packs may not do

A pack MUST NOT define a core name, and MUST NOT redefine another pack's names.
Where a pack is loaded that violates this, the core or incumbent definition wins
and the conflict SHOULD be reported. Otherwise installing a pack could silently
change how an existing diagram looks, which would make icons untrustworthy.

Pack distribution is deliberately out of scope here. This section fixes only
what a name *means*; how a pack reaches a reader is a tooling concern.

## 6c. Layout constraints

`layout.constraints` expresses arrangement as **relationships** rather than
coordinates:

```yaml
layout:
  constraints:
    - same-rank: [api, cache]     # keep these on one layer
    - order: [web, api, db]       # sequence them within their layer
```

### Why this and not coordinates

Both express layout intent. They differ on what happens when they go stale, and
that difference is the whole argument:

| | When the diagram changes underneath it |
|---|---|
| `x: 10, y: 20` | describes nothing; the node lands somewhere wrong while the picture still looks plausible — **silently wrong** |
| `same-rank: [a, b]` | stops applying; the engine lays out normally — **correct, just not hand-tuned** |

Wrong-but-plausible is a worse failure than correct-but-ordinary, and it is a
difference in kind rather than degree. A coordinate is presentational data that
can *disagree with the graph*; a constraint is semantic and can only become
irrelevant.

Constraints are also sparse by nature — you state something about the two or
three nodes you care about — where coordinates pull towards an entry per node,
so there is far less to keep in step in the first place.

This is what every durable text diagram language converged on independently:
Graphviz, D2, TikZ and PlantUML all make relationship placement first-class and
quarantine absolute coordinates into a separate opt-in mode. Mermaid's own
most-upvoted layout request (#3723, "specify that two nodes should be at the same
level", 82 👍, open since 2022) is asking for a rank constraint, not a coordinate
map.

### Tiers

| Tier | Mechanism | When |
|---|---|---|
| 0 | nothing | the default; the engine arranges everything |
| 1 | `layout.constraints` | **the recommended way to influence a layout** |
| 2 | `layout.nodes` coordinates | pixel-perfect escape hatch, written only on an explicit request |

Tier 2 is not deprecated — constraints cannot reproduce an arbitrary hand-dragged
arrangement, and nothing else can. It is demoted, not removed.

### Rules

- **Stale ids are retained and reported, never removed.** A constraint naming a
  node that no longer exists is inert. Deleting it would destroy intent the
  moment someone renamed a node on a branch.
- **A group with fewer than two surviving members does nothing.**
- **`same-rank` moves the group to the deepest member's layer**, never the
  shallowest. Pulling nodes upwards would place them above their own
  predecessors and turn forward edges into back edges, which reads as a broken
  diagram rather than an honoured constraint.
- **`order` only sequences members that share a layer.** It expresses
  left-to-right intent, not a demand to move nodes between layers.
- **Constraint kinds a reader does not model MUST be retained verbatim.**
  Partial knowledge is worse than none: a reader that parses the list and
  re-emits only what it understands destroys a newer tool's work merely by
  opening the file.

## 7. Open questions (need human decision)

- Should `semantics.kind` use a closed C4 vocabulary or be free-form?
- Should edge waypoints be absolute or relative to endpoints?
- Do we normalise colour to hex, or preserve author input?

