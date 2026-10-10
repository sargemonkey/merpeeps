# Diagram support in the Merpie preview

All 33 catalog families and 48 dialects have typed empty creation, native
authoring entry points, save/reopen and native SVG surfaces in `Merpeeps.Core`.
They ship in one package and one desktop download, not one plugin per family.
This table describes the release scope, **not exhaustive Mermaid parity**.

| Family | Dialects | Placement/editing model | Important limits |
|---|---:|---|---|
| Flowchart | 3 | Nodes/groups, graph layout, multi-select/align | `flowchart-elk` is a directive alias; no native ELK |
| Sequence | 1 | Ordered participants, events, fragments | Lifetime/box policies can diagnose source-only cases |
| Class | 2 | Class cards, members, namespaces, relations | Callbacks inert; SVG text metrics can differ |
| State | 2 | States, markers, composite groups | Start/end markers must have transitions |
| ER | 1 | Entities, attributes, keys, groups/relations | Some Markdown/style details differ from stock Mermaid |
| Journey | 1 | Sections, tasks, scores, actors | Semantic ordering, not arbitrary graph positioning |
| Gantt | 1 | Dates, durations, dependencies, sections | Requires explicit evaluation context; unsupported formats fail |
| Pie | 1 | Labels/weights, slices/donut | Percentages are relative weights; not free node dragging |
| Quadrant | 1 | Normalized points and labels | Drag changes data coordinates |
| Requirement | 1 | Typed elements and relationships | Typed rather than generic graph operations |
| Git | 1 | Ordered commit/branch operations | No operation exists for implicit main |
| C4 | 5 | Typed levels/containment/relationships | Extra source arguments retained; coalesced relations diagnosed |
| Mindmap | 1 | Hierarchy and supported position gestures | Not included in graph alignment tools |
| Timeline | 1 | Sections, periods and events | Ordering/layout owned |
| Sankey | 2 | Weighted flows | No arbitrary box movement |
| XY | 2 | Series, axes and values | Later bars can cover earlier line points |
| Block | 2 | Grid, spans, nested blocks | Not free graph positioning |
| Packet | 2 | Bit ranges and field labels | Range-owned layout |
| Kanban | 1 | Columns, cards and metadata | Ownership/order edits |
| Architecture | 1 | Groups, services, ports, row/column constraints | External icons/fcose-only options are not silently implemented |
| Radar | 1 | Axes, curves and values | Value dragging uses inverse data mapping |
| Treemap | 1 | Weighted hierarchy | Values on leaves |
| Venn | 1 | Sets, exact overlap membership, text | Circle fit reports area residuals |
| Wardley | 1 | Evolution/visibility, pipelines | Semantic coordinate axes, not graph alignment |
| Ishikawa | 1 | Cause tree and problem | Hierarchy-owned placement |
| Cynefin | 1 | Fixed domains, items, transitions | Confusion overflow/seeded-boundary policies diagnosed |
| Railroad | 4 | EBNF, ABNF, PEG, primitive grammar | Structured operators; unsupported syntax diagnosed |
| Swimlane | 1 | Responsibility lanes, steps, links | Nested lane directions retained with native diagnostic |
| Use Case | 1 | Actors, cases, boundaries, UML relations | Include/extend/generalization syntax fixes labels/markers |
| Agent Flow | 1 | Flows, tools, shared elements, links | External algorithms and animations retained/diagnosed |
| Event Modeling | 1 | Ordered frames, payloads/references | Payloads inert; declared order matters |
| Treeview | 1 | Directory-like hierarchy | Hierarchy-owned placement |
| ZenUML | 1 | Participants and nested calls | Some shapes/annotators diagnosed; reference validation is separate |

Graph alignment applies to Flowchart, State, Class and ER only. Other families
must not be coerced into graph nodes to expose an operation that does not match
their semantics. `CanLayout` reports generic graph layout capability, not whether
a family can be shown or edited.

Packed-package consumer tests exercise empty and populated fixtures for every
dialect. This proves package wiring and representative round trips, not every
shape, theme, input gesture or upstream grammar production.

Known preview limitations: imported fixed-color diagram labels can have poor
contrast with dark app chrome; transparent export is not yet exposed. Use light
app appearance for those imported documents. Static images do not automatically
recolor when their embedding page changes theme. Native accessibility, exact
PowerPoint fidelity and complete external viewer acceptance are not claimed.
