# Merpie 0.1.0-preview.1

First **draft prerelease** of the native Merpie visual editor. Runtime-bundled
portable downloads cover Windows x64, Intel macOS x64, Apple Silicon macOS arm64
and Linux x64; system native libraries and optional web engines still apply.

The preview includes typed authoring routes for 33 diagram families/48 dialects,
pictorial creation tools, inline properties, full-range colors, graph node/group
multi-select/alignment, reversible group gestures and explicit imported geometry.
See `docs/DIAGRAM-SUPPORT.md` for capability boundaries, not universal parity.

`x-merpeeps` saved data, the `merpeeps` CLI/MCP IDs and shared package names remain
compatible. MerDeck is a separate product and is not released by this workflow.

## Downloads and package artifacts

- Desktop packages include approved Merpie ICO/PNG/ICNS artwork and a hashed
  manifest. Archive `.sha256` files and `SHA256SUMS` identify the attached bytes.
- NuGet: `Merpeeps.Core 0.2.0-preview.1`, `Merpeeps.Render 0.2.0-preview.1`,
  `Merpeeps.Scene 0.1.0-preview.1`, validated as actual packed consumers.
- npm tarball: `merpeeps-mermaid-layout 0.2.0-preview.1`. This is a
  metadata/layout/render-replay companion, **not a JS port of the C# typed
  authoring engine**. No registry uploads occur automatically.

## Known limitations and release status

- Imported authored light/pastel surfaces can have unreadable inherited labels
  in dark application chrome. Use **light app appearance** for those documents.
  Transparent canvas/export controls and a unified contrast fix are pending.
- The desktop packages are unsigned/unnotarized previews, not signed installers
  or automatic updaters. Do not bypass platform trust/security warnings to
  install them. macOS public distribution remains gated on signing/notarization.
- Native-host CI compilation/tests do not replace manual Windows/macOS/Linux
  UX/accessibility acceptance. Native browser fonts/arrowhead/shape details and
  static image appearance can differ from the reference renderer.
- Unsupported grammar/appearance details are preserved or diagnosed. ZenUML
  parser validation is separate from Mermaid's built-in reference rendering.

The release remains a **draft** for review; attached libraries are prerelease
artifacts, not stable API commitments.
