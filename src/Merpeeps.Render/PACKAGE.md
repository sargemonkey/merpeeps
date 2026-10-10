# Merpeeps.Render

Offline reference rendering and **real Mermaid 12.0.0 grammar validation** for
native applications. This local package carries the upstream runtime and licenses;
it does not require Node.js, a CDN or an account at runtime. A host supplies an
existing system web engine, such as Avalonia's NativeWebView.

## Validation protocol

Write `MermaidValidationPage.BuildShell()` to a private temporary HTML file and
navigate a dedicated web-engine host to its file URI. Do not pass the multi-megabyte
page through WebView2's size-limited NavigateToString API. Never open an external
browser or a surprise user-facing window for validation.

1. Wait for successful navigation and evaluate `ReadyScript`.
2. Issue `StartScript(uniqueRequestId, exactSource)`.
3. Evaluate `ReadScript(requestId)` until its JSON has a non-pending status.
   Unwrap the web engine's JavaScript-string representation before passing that
   JSON to `ParseResult(json, requestId)`.
4. Use `CancelScript(requestId)` when the source changes or the operation is cancelled.
   A request's completed result is consumed once. Do not reuse request IDs.

The page serializes parser calls because Mermaid maintains shared parser/config
state. It reinitializes between sources, keeps at most 32 unconsumed requests and
does not render or insert source into HTML. Its CSP disallows network requests.
Hosts must debounce editing, bound waiting, handle navigation/engine failures
explicitly, and discard stale results by request ID/source revision.

`MermaidSyntaxResult.Status` is `Pending`, `Valid`, `Invalid`, `Unsupported` or
`Unavailable`. `Valid` identifies the upstream parser's `DiagramType`; it is **not**
permission to load that source with a native diagram adapter. Check Core family
and structural support independently. A valid sequence diagram is still not a
native graph model.

Diagnostics have codes/messages and optional **parser-relative** line/column
positions. Frontmatter/directive preprocessing may change offsets: do not
highlight those positions as original-document coordinates without mapping them.
An absent position is unknown, not line one. Protocol/version/missing-field
errors throw rather than synthesizing valid results.

The validator covers the 32 built-in families plus ZenUML using the pinned
standalone `@zenuml/core` 3.47.9 **real async parser**. The published Mermaid
ZenUML adapter's parser is a no-op, so registering that adapter alone would
incorrectly accept malformed source. This package instead checks the standalone
parser's `pass` and structured `errorDetails` result; missing diagnostics on
failure are reported unavailable, not valid. The adapter is validation-only.

Initialization is asynchronous and `ReadyScript` becomes true after both parser
integrations are ready or a concrete startup failure is available. The private
host needs ordinary file-origin storage access for the standalone parser; do not
load it in a sandboxed opaque-origin document or weaken browser security flags.
It makes no network requests and needs no user account or SSO.

The validation integration does not itself implement editing. Core and Merpie
provide separate typed native ZenUML authoring; the browser reference preview
does not gain an equivalent standalone ZenUML renderer from this package.
Standalone bundle/license and dependency notices are
embedded and also available through `RenderAssets`. The supplemental ANTLR notice
comes from its exact upstream 4.11.0 release. `dlv` publishes an MIT declaration
and README license reference rather than a license file; that publisher notice
is retained as a reference, not replaced by an invented copyright statement.

## Reference rendering

`RenderAssets.BuildPage` / `BuildShell` retain the existing plain and Merpeeps
reference rendering modes. Defaults explicitly preserve classic appearance and
dagre layout rather than adopting Mermaid 12's new defaults. Source frontmatter
may explicitly override them. This does not add ELK to the native editor.

The package is separate from the lightweight Core metadata/model library.
No native UI controls, window lifecycle or installation behavior are included.

The explicit Flowchart geometry path uses `window.merpeepsHarvestGeometry()` and
Core's `RenderedGraphGeometry` / `AdoptRenderedGeometryCommand`. It preserves
dimensions, wrapped label placement, groups and sampled routes in the source.
Legacy position-only harvest/adoption remains available. Geometry replay is not
a claim of identical native/browser pixels; changed content or incompatible
endpoint arrangements require re-adoption. Application chrome, document colors
and static export appearance are distinct; see prerelease known limitations.
