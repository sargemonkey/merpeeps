// The oracle's view of the Merpeeps reference implementation.
//
// Deliberately a thin re-export of the *installed package* rather than a second
// copy of the logic. The conformance suite is what proves Merpeeps degrades
// gracefully in real mermaid.js, so it must exercise the artefact that actually
// ships — otherwise the package and the thing we verified could drift apart and
// nobody would notice until a user hit it.
//
// `js-yaml` is a bare specifier inside the package: conformance.html resolves it
// with an import map, Node resolves it from node_modules.
export * from './node_modules/merpeeps-mermaid-layout/src/index.js';
