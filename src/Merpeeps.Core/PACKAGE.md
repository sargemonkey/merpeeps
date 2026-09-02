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

## What's included

- `MerpeepsDoc` — parse, serialise and inject the metadata block
- `MerpeepsDocument` — load into a positioned model, save positions or a full edit
- `IDiagramAdapter` — parser/writer per diagram family (flowchart, state)
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
