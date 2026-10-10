# Merpie

<img src="src/Merpeeps.App/Assets/Brand/merpie-mark.svg" width="96" alt="Merpie sea-magpie icon">

**Create, edit, enhance and export Mermaid diagrams.**

Merpie is a visual desktop editor for **33 diagram families and 48 dialects**.
Work with flowcharts, UML, entity relationships, sequences, charts, system
architecture, schedules, workflows and more, using tools suited to each type.

Start a new diagram or open an existing `.mmd` file, edit its structure and
content, enhance its appearance, refine its layout, and export it for sharing.
Mermaid source remains editable throughout the workflow.

The **Merpeeps** libraries, CLI and MCP server provide the underlying diagram
editing, enhancement, layout and rendering tools for applications, scripts and
coding agents.

![Merpie diagram gallery](docs/merpie-gallery.png)

## Features

- **Broad diagram support:** native models and authoring tools for graphs,
  sequences, charts, hierarchies, timelines and other structured diagrams.
- **Create and edit `.mmd` files:** pictorial tools, selection-driven properties,
  relationship editing and Undo/Redo, with access to the underlying source.
- **Enhance diagrams:** colors, icons, labels and type-specific presentation
  settings, with light/dark app appearance.
- **Export and share:** editable Mermaid source, plain or style-baked Mermaid,
  standalone SVG and offline HTML previews.
- **Control layout:** move supported nodes and groups, apply automatic layouts,
  and multi-select, align or distribute elements in Flowchart, State, Class and
  ER diagrams. Other families use their own data, ordering or placement rules.
- **Automation:** a CLI and stdio MCP server for supported edits, layout,
  metadata operations and SVG rendering.
- **Local files:** no account is required. Diagrams remain editable Mermaid
  source rather than a proprietary project format.

## Supported diagrams

| Area | Diagram families |
|---|---|
| Systems and architecture | Flowchart, C4, Architecture, Block |
| UML and data modeling | State, Class, Entity Relationship, Requirement, Use Case |
| Interactions and workflows | Sequence, User Journey, Swimlane, Agent Flow, Event Modeling, ZenUML |
| Charts and visualization | Pie, Quadrant, XY, Radar, Sankey, Treemap, Venn |
| Hierarchies | Mindmap, Treeview |
| Planning and time | Gantt, Timeline, Kanban |
| Technical structures | Packet, GitGraph, Railroad |
| Analysis and reasoning | Wardley, Ishikawa, Cynefin |

Editing follows the diagram's structure: participant messages, entity
attributes, chart values, dates, ownership, nested calls or grammar expressions.
These are not all treated as interchangeable flowchart nodes. See
[diagram support](docs/DIAGRAM-SUPPORT.md) for dialects, editing models and limits.

## Edit, enhance and export

Open a Mermaid file or create an empty diagram from the gallery. Add and edit
content with the family's tools, adjust appearance and supported placement,
then save the editable source or export a shareable result.

| Output | Purpose |
|---|---|
| Mermaid `.mmd` | Editable diagram source with Merpeeps enhancements and saved presentation |
| Plain or style-baked Mermaid `.mmd` | Source for standard Mermaid tooling; baking converts supported colors to native styling and reports dropped enhancements |
| SVG `.svg` | Standalone vector output with diagram styling, icons and saved placement |
| Offline HTML `.html` | CLI-generated, self-contained Mermaid reference preview, with metadata-aware rendering where supported |

SVG export offers document/light/dark appearance and an independent transparent
outer canvas. Intentional node/group fills and label backplates remain.
Explicit authored colors stay unchanged, including low-contrast choices.
Static images do not recolor when their embedding page changes theme.

## Preview status

Merpie is an early preview. Native authoring entry points cover all catalog
families, but this is **not complete parity with every Mermaid feature**.
Unsupported constructs are preserved or diagnosed, and rendering details can
differ from the upstream renderer.

The desktop targets Windows x64, Linux x64, Intel macOS and Apple Silicon macOS.
Public desktop downloads and installers are not yet available; build from
source using the instructions below. Follow [Releases](https://github.com/sargemonkey/merpeeps/releases)
for packaged distributions.

Signing/notarization and human usability/accessibility acceptance are still in
progress. See [diagram support and limitations](docs/DIAGRAM-SUPPORT.md) for
family-specific capabilities.

## Build and run

Install the **.NET 10 SDK**, then run:

```text
git clone https://github.com/sargemonkey/merpeeps.git merpie
cd merpie
cd src
dotnet run --project Merpeeps.App
```

The app opens the diagram gallery. Choose a diagram type to create an empty
document, or open an existing `.mmd` file. The source project is named
`Merpeeps.App`; the desktop executable is `Merpie.App`.

Node.js is not required to build or run the desktop editor. The optional
reference-rendering pane uses a platform web engine, and Linux builds require
platform native dependencies. See the [user guide](docs/GUIDE.md) for setup,
editing controls and platform requirements.

## Editable Mermaid source

Diagrams stay in `.mmd` files rather than a separate proprietary project format.
Structural edits update the Mermaid source. Presentation enhancements can be
stored in an `x-merpeeps` metadata block without changing the diagram body.

<details>
<summary>Example: a Flowchart with saved presentation metadata</summary>

```yaml
---
x-merpeeps:
  spec: 1
  layout:
    nodes:
      web: {x: 60, y: 0}
  style:
    nodes:
      web: {fill: '#DAE8FC', icon: browser}
---
flowchart LR
    web[Web client] --> api[API gateway]
```

</details>

Renderers that support the diagram's Mermaid syntax can display it without
Merpie. They ignore the extra metadata, so manual positions and metadata-based
styling require a compatible renderer. Layout-only saves preserve the diagram
body; structural edits update the Mermaid source.

![Stock Mermaid and the same diagram rendered with saved layout](docs/layout-plugin-proof.png)

## CLI and MCP

The CLI is named `merpeeps`. From the repository's `src` directory:

```text
dotnet run --project Merpeeps.Cli -- capabilities diagram.mmd
dotnet run --project Merpeeps.Cli -- svg diagram.mmd --out diagram.svg
dotnet run --project Merpeeps.Cli -- svg diagram.mmd --theme dark --transparent --out diagram.svg
dotnet run --project Merpeeps.Cli -- preview diagram.mmd --out diagram.html
```

`capabilities` reports native support. `validate` checks metadata invariants,
not Mermaid grammar. The separate reference validator uses the bundled
upstream parser.

The MCP server exposes supported operations to coding agents over stdio.
See the [user guide](docs/GUIDE.md) for server configuration, edit operations,
layout engines and command options.

## Libraries and integrations

The editor is built on reusable components:

| Component | Purpose |
|---|---|
| [`Merpeeps.Core`](src/Merpeeps.Core/PACKAGE.md) | C# diagram models, parsing, reversible edits, layout, metadata and SVG export |
| [`Merpeeps.Render`](src/Merpeeps.Render/PACKAGE.md) | Bundled offline Mermaid reference rendering and syntax-validation assets |
| [`Merpeeps.Scene`](docs/SCENES.md) | A separate visual-scene contract for geometry, styling and host-owned content references |
| [`merpeeps-mermaid-layout`](packages/merpeeps-mermaid-layout) | JavaScript metadata utilities and a Mermaid saved-layout renderer |

The JavaScript companion is **not a port of the C# editor or its all-family
authoring engine**. The libraries and companion have separate version lines;
the repository's preview package versions are not yet published to registries.

In a browser application with the JavaScript companion available:

```js
import mermaid from 'mermaid';
import {
  registerMerpeepsLayout,
  renderWithLayout,
} from 'merpeeps-mermaid-layout/mermaid';

registerMerpeepsLayout(mermaid);
const { svg } = await renderWithLayout(mermaid, 'diagram', source);
```

The companion replays saved graph positions rather than computing a layout.
The explicit Flowchart geometry path also preserves captured group frames,
label placement and sampled routes. Documents without applicable stored layout
use Mermaid's normal renderer.

The `Merpeeps` package names, `merpeeps` commands and `x-merpeeps` metadata key
are retained technical identifiers. The desktop product is Merpie.

## Documentation and contributing

- [User guide](docs/GUIDE.md): setup, editing, sharing and exports.
- [Diagram support](docs/DIAGRAM-SUPPORT.md): family capabilities and limits.
- [File format](SPEC.md): metadata and interoperability requirements.
- [Design notes](docs/DESIGN.md): architecture and implementation decisions.
- [Contributing](CONTRIBUTING.md): build instructions and test suites.
- [Changelog](CHANGELOG.md): project changes.

Report bugs and feature requests through
[GitHub Issues](https://github.com/sargemonkey/merpeeps/issues).

## License and naming

Merpie is free and open source under the [MIT License](LICENSE).

Not affiliated with, endorsed by, or sponsored by Mermaid Chart Inc., the
Mermaid open-source project, or The Walt Disney Company. "Mermaid" is used
descriptively to identify the diagram syntax and rendering ecosystem.
