// Merpeeps v0.1 — store layout, styling and semantics inside ordinary Mermaid files.
//
// The whole design rests on one property, verified against Mermaid 11.16.1:
// an unknown key in YAML frontmatter is ignored by every diagram type, so the
// metadata rides along invisibly and a stock renderer draws exactly the same
// picture it would have drawn without it.
//
// Pure functions, no DOM, no renderer. Safe in Node and the browser.

import * as yaml from 'js-yaml';

/** The frontmatter key everything lives under. */
export const KEY = 'x-merpeeps';

/** Spec version written into new blocks. */
export const SPEC_VERSION = 1;

const FM = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/;
const COMMENT = /^%%[ \t]*merpeeps1[ \t]+(\{[\s\S]*?\})[ \t]*$/m;

// Removal must consume the comment's own line terminator. Matching only the
// line content leaves a blank line behind, so stripping metadata would not
// restore the original body byte-for-byte — which is the one promise the
// format cannot break.
const COMMENT_LINE = /^%%[ \t]*merpeeps1[ \t]+\{[\s\S]*?\}[ \t]*(?:\r?\n|$)/m;

// ---------------------------------------------------------------- carrier

/**
 * Split a Mermaid document into its frontmatter and body.
 * A document with no frontmatter yields `{ fmText: null, fm: {}, body: src }`.
 */
export function split(src) {
  assertString(src, 'src');
  const m = src.match(FM);
  if (!m) return { fmText: null, fm: {}, body: src };
  let fm = {};
  try {
    const loaded = yaml.load(m[1]);
    fm = loaded && typeof loaded === 'object' && !Array.isArray(loaded) ? loaded : {};
  } catch {
    fm = {};
  }
  return { fmText: m[1], fm, body: src.slice(m[0].length) };
}

/**
 * Read the Merpeeps block: frontmatter first, then the `%% merpeeps1 {...}` fallback.
 * Returns null when the document carries no metadata.
 */
export function parse(src) {
  const { fm, body } = split(src);
  if (fm && typeof fm === 'object' && fm[KEY] != null) return fm[KEY];

  const c = body.match(COMMENT) ?? src.match(COMMENT);
  if (c) {
    try {
      return JSON.parse(c[1]);
    } catch {
      /* malformed fallback: treat as absent rather than throwing */
    }
  }
  return null;
}

/**
 * Write an Merpeeps block into `src`, merging into any existing frontmatter.
 *
 * Never emits a second `---` block, and never alters the diagram body.
 * Pass `merpeeps = null` to remove the block entirely.
 */
export function serialise(src, merpeeps, { carrier = 'frontmatter' } = {}) {
  assertString(src, 'src');
  const { fm, body } = split(src);

  if (carrier === 'comment') {
    // The comment carrier is self-contained, so a frontmatter copy of the
    // block would be a second source of truth and is dropped — but the
    // author's other frontmatter keys are not ours to discard.
    const rest = { ...fm };
    delete rest[KEY];
    const head = withFrontmatter(rest, '');
    const base = stripComment(body);
    if (merpeeps == null) return `${head}${base}`;

    // Separate only when the body does not already end with a newline.
    // Trimming instead would delete trailing blank lines the author wrote.
    const sep = base.length === 0 || base.endsWith('\n') ? '' : '\n';
    return `${head}${base}${sep}%% merpeeps1 ${JSON.stringify(merpeeps)}\n`;
  }

  const next = { ...fm };
  if (merpeeps == null) delete next[KEY];
  else next[KEY] = merpeeps;
  return withFrontmatter(next, stripComment(body));
}

/** Deep-merge `patch` into the existing Merpeeps block and write it back. */
export function inject(src, patch) {
  const cur = parse(src) ?? { spec: SPEC_VERSION };
  return serialise(src, deepMerge(cur, patch));
}

/** The document with every Merpeeps carrier removed — plain Mermaid again. */
export function strip(src) {
  return serialise(src, null);
}

/** Body only, with any Merpeeps carrier removed. Used to prove byte-preservation. */
export function bodyOf(src) {
  return stripComment(split(src).body);
}

// ---------------------------------------------------------------- layout

/**
 * Node positions keyed by diagram node id, e.g. `{ a: { x: 0, y: 0 } }`.
 * Returns an empty object when the document carries none.
 */
export function getPositions(src) {
  const nodes = parse(src)?.layout?.nodes;
  if (!nodes || typeof nodes !== 'object') return {};

  const out = {};
  for (const [id, raw] of Object.entries(nodes)) {
    if (!raw || typeof raw !== 'object') continue;
    const x = num(raw.x);
    const y = num(raw.y);
    if (x === null || y === null) continue;
    const entry = { x, y };
    if (num(raw.w) !== null) entry.w = num(raw.w);
    if (num(raw.h) !== null) entry.h = num(raw.h);
    out[id] = entry;
  }
  return out;
}

/**
 * Write node positions, replacing any existing layout.
 *
 * Positions are *replaced* rather than merged, because merging would silently
 * resurrect nodes the caller had deleted.
 */
export function setPositions(src, positions, { engine = 'manual' } = {}) {
  assertPlainObject(positions, 'positions');

  const nodes = {};
  for (const [id, p] of Object.entries(positions)) {
    if (!p || typeof p !== 'object') {
      throw new TypeError(`position for "${id}" must be an object with x and y`);
    }
    const x = num(p.x);
    const y = num(p.y);
    if (x === null || y === null) {
      throw new TypeError(`position for "${id}" needs finite numeric x and y`);
    }
    const entry = { x, y };
    if (num(p.w) !== null) entry.w = num(p.w);
    if (num(p.h) !== null) entry.h = num(p.h);
    nodes[id] = entry;
  }

  const cur = parse(src) ?? {};
  return serialise(src, {
    ...cur,
    spec: cur.spec ?? SPEC_VERSION,
    layout: { ...cur.layout, nodes, engine },
  });
}

/** Per-node presentation (fill, stroke, icon…), keyed by node id. */
export function getStyles(src) {
  const nodes = parse(src)?.style?.nodes;
  return nodes && typeof nodes === 'object' ? { ...nodes } : {};
}

/** Write per-node presentation, replacing any existing style block. */
export function setStyles(src, styles) {
  assertPlainObject(styles, 'styles');
  const cur = parse(src) ?? {};
  return serialise(src, {
    ...cur,
    spec: cur.spec ?? SPEC_VERSION,
    style: { ...cur.style, nodes: { ...styles } },
  });
}

/**
 * Positions whose node id no longer appears in `knownIds`.
 *
 * Stale entries are reported, never auto-deleted: renaming a node in text
 * should not silently destroy the layout you spent time arranging.
 */
export function staleIds(src, knownIds) {
  const known = new Set(knownIds ?? []);
  return Object.keys(getPositions(src)).filter((id) => !known.has(id));
}

// ------------------------------------------------------------- validation

/**
 * Check the invariants that do not need a renderer.
 *
 * Returns `{ ok, issues }`. Rendering equivalence is the one invariant this
 * cannot check offline — for that, render both documents and compare.
 */
export function validate(src) {
  const issues = [];
  const push = (rule, message) => issues.push({ rule, message });

  let merpeeps = null;
  try {
    merpeeps = parse(src);
  } catch (e) {
    push('parse', `metadata could not be read: ${e.message}`);
    return { ok: false, issues };
  }

  if (merpeeps == null) return { ok: true, issues };

  if (typeof merpeeps !== 'object' || Array.isArray(merpeeps)) {
    push('shape', 'the Merpeeps block must be a mapping');
    return { ok: false, issues };
  }
  if (merpeeps.spec !== undefined && !Number.isInteger(merpeeps.spec)) {
    push('spec', '"spec" must be an integer when present');
  }

  const nodes = merpeeps.layout?.nodes;
  if (nodes !== undefined) {
    if (!nodes || typeof nodes !== 'object' || Array.isArray(nodes)) {
      push('layout', 'layout.nodes must be a mapping of id to position');
    } else {
      for (const [id, p] of Object.entries(nodes)) {
        if (!p || typeof p !== 'object') {
          push('layout', `layout.nodes["${id}"] must be an object`);
          continue;
        }
        if (num(p.x) === null || num(p.y) === null) {
          push('layout', `layout.nodes["${id}"] needs finite numeric x and y`);
        }
      }
    }
  }

  // Body preservation is the promise the whole format rests on.
  try {
    if (bodyOf(serialise(src, merpeeps)) !== bodyOf(src)) {
      push('body', 'rewriting the metadata would change the diagram body');
    }
  } catch (e) {
    push('body', `round-trip failed: ${e.message}`);
  }

  // Writing twice must converge, or files churn on every save.
  try {
    const once = serialise(src, merpeeps);
    if (serialise(once, parse(once)) !== once) {
      push('idempotent', 'writing the metadata twice does not converge');
    }
  } catch (e) {
    push('idempotent', `idempotence check failed: ${e.message}`);
  }

  return { ok: issues.length === 0, issues };
}

// ---------------------------------------------------------------- helpers

function stripComment(body) {
  // No blank-line collapsing here: rewriting the author's whitespace would be
  // a body modification, and COMMENT_LINE already removes cleanly.
  return body.replace(COMMENT_LINE, '');
}

function withFrontmatter(fm, body) {
  const keys = Object.keys(fm ?? {});
  if (keys.length === 0) return body;
  const text = yaml.dump(fm, { lineWidth: -1, noRefs: true }).replace(/\s*$/, '');
  return `---\n${text}\n---\n${body}`;
}

function deepMerge(a, b) {
  if (a === null || typeof a !== 'object' || Array.isArray(a)) return b;
  if (b === null || typeof b !== 'object' || Array.isArray(b)) return b;
  const out = { ...a };
  for (const [k, v] of Object.entries(b)) {
    out[k] = k in a ? deepMerge(a[k], v) : v;
  }
  return out;
}

function num(v) {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null;
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function assertString(v, name) {
  if (typeof v !== 'string') throw new TypeError(`${name} must be a string`);
}

function assertPlainObject(v, name) {
  if (!v || typeof v !== 'object' || Array.isArray(v)) {
    throw new TypeError(`${name} must be an object`);
  }
}
