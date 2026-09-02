# Merpeeps — Mermaid Metadata eXtension, v0.1 (draft)

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

`style.nodes.<id>.icon` names a glyph drawn in place of a plain box. This is the
most-requested node feature in mermaid (#1358, 143 reactions) and is not
expressible in mermaid syntax, which is why it belongs here.

Reserved names: `server`, `database`, `storage`, `queue`, `cache`, `function`,
`gateway`, `user`, `browser`, `mobile`, `shield`, `cloud`, `cog`.

A reader that does not recognise a name MUST fall back to the plain shape rather
than fail. Vendor-specific packs (`aws:lambda`, `azure:sql`) are reserved for a
future revision; the prefix form is deliberate so packs cannot collide.

## 7. Open questions (need human decision)

- Should `semantics.kind` use a closed C4 vocabulary or be free-form?
- Should edge waypoints be absolute or relative to endpoints?
- Do we normalise colour to hex, or preserve author input?

