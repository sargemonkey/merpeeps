# Vendored third-party assets

Files here are **not ours**. They are copied verbatim from upstream so the editor
can render a reference diagram with no network access, and so the version it
compares against is pinned and explicit rather than whatever a CDN serves today.

## `mermaid/`

| | |
|---|---|
| Upstream | <https://github.com/mermaid-js/mermaid> |
| Package | `mermaid` on npm |
| Version | see `mermaid/.version` (currently **11.16.1**) |
| Licence | MIT — see `mermaid/LICENSE` |
| Copyright | 2014–2022 Knut Sveidqvist |

`mermaid.min.js` is the self-contained UMD bundle. It is the *smallest* offline
option: the ESM entry point is only 29 KB but dynamically imports the rest of the
12.8 MB `dist` tree, which would be far worse to ship.

**The minified bundle carries no licence banner** — esbuild strips it. MIT
requires the copyright and permission notice to travel with "all copies or
substantial portions", so `LICENSE` sits beside it and is embedded into the
assembly alongside the script. Do not remove it.

### Refreshing

```bash
cd oracle && npm ci
pwsh ../scripts/vendor-mermaid.ps1
```

Pin deliberately. This bundle is the reference the comparison pane renders
against, so bumping it changes what "correct" looks like — re-run the oracle
after any bump.

## Why vendor at all

Three reasons, in order of importance:

1. **Offline.** Previewing a diagram must not require a network round trip.
2. **Determinism.** A pinned bundle means a rendering difference is a real
   difference, not a silent upstream release.
3. **No CSP or CDN failure modes** inside the webview.
