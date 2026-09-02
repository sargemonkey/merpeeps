# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing is published yet, so the format and the APIs remain free to change.
Version 0.1.0 is the first intended release of `merpeeps-mermaid-layout` (npm) and
`Merpeeps.Core` (NuGet).

### Added

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
- **A command-line tool** (`merpeeps`) — read, validate, strip, adopt, bake, svg,
  preview and conformance, publishable as a single NativeAOT binary.
- **An MCP server** — ten tools giving coding agents the same operations without
  a screen.
- **`merpeeps bake`** — rewrites Merpeeps styling as native `style` statements so
  colours survive on GitHub with no plugin, and reports what it could not carry
  rather than dropping it silently. Available from the editor's File menu and
  from the command line.
- **A conformance oracle** — real mermaid.js in a real browser as ground truth,
  plus twelve further gates covering the npm package, the MCP server over stdio,
  themes, previews and store-and-replay.

### Notes on scope

- **Layouts are computed in-process, in C#.** Layout engines that exist only as
  JavaScript are not offered from the editor's dropdown: running one means
  rendering in a browser and reading the coordinates back, and doing that behind
  a dropdown opens a browser pane in the middle of an edit. The scripted path is
  available for anyone who wants it — `merpeeps preview` writes a page exposing
  `window.merpeepsHarvest`, and `merpeeps adopt --positions` writes the result
  into a file.

- **Not built yet:** subgraph authoring, and bespoke editing UI for diagram types
  beyond flowchart. The format is type-agnostic, so the other 29 types already
  render and round-trip — they simply have no tailored editor.
