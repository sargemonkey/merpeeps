import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
  KEY,
  bodyOf,
  getPositions,
  getStyles,
  inject,
  parse,
  serialise,
  setPositions,
  setStyles,
  split,
  staleIds,
  strip,
  validate,
} from '../src/index.js';

const PLAIN = 'flowchart LR\n    a[Start] --> b[End]\n';

const TITLED = `---
title: Order fulfilment
---
flowchart LR
    a[Start] --> b[End]
`;

const POS = { a: { x: 0, y: 0 }, b: { x: 220, y: 40 } };

// ------------------------------------------------------------ the promise

test('writing positions never changes the diagram body', () => {
  const out = setPositions(PLAIN, POS);
  assert.equal(bodyOf(out), bodyOf(PLAIN));
});

test('stripping restores the original document exactly', () => {
  assert.equal(strip(setPositions(PLAIN, POS)), PLAIN);
});

test('positions survive a round trip', () => {
  assert.deepEqual(getPositions(setPositions(PLAIN, POS)), POS);
});

test('writing twice converges, so files do not churn in git', () => {
  const once = setPositions(PLAIN, POS);
  const twice = setPositions(once, getPositions(once));
  assert.equal(twice, once);
});

// --------------------------------------------------------- frontmatter care

test('an existing frontmatter key is preserved alongside the metadata', () => {
  const out = setPositions(TITLED, POS);
  assert.equal(split(out).fm.title, 'Order fulfilment');
  assert.deepEqual(getPositions(out), POS);
});

test('only one frontmatter block is ever emitted', () => {
  const out = setPositions(TITLED, POS);
  const fences = out.split('\n').filter((l) => l.trim() === '---').length;
  assert.equal(fences, 2, `expected one --- pair, saw ${fences} fence lines`);
});

test('removing the metadata leaves other frontmatter keys intact', () => {
  const cleaned = strip(setPositions(TITLED, POS));
  assert.equal(cleaned, TITLED);
});

test('a document with no metadata parses as null rather than throwing', () => {
  assert.equal(parse(PLAIN), null);
  assert.deepEqual(getPositions(PLAIN), {});
});

test('malformed frontmatter degrades to no metadata instead of throwing', () => {
  const broken = '---\n: : :\n---\nflowchart LR\n    a --> b\n';
  assert.equal(parse(broken), null);
});

// ---------------------------------------------------------------- carriers

test('the comment carrier round-trips without frontmatter', () => {
  const out = serialise(PLAIN, { spec: 1, layout: { nodes: POS } },
    { carrier: 'comment' });

  assert.match(out, /^%% merpeeps1 \{/m);
  assert.ok(!out.startsWith('---'));
  assert.deepEqual(getPositions(out), POS);
  assert.equal(bodyOf(out), bodyOf(PLAIN));
});

test('switching carriers does not leave two copies of the metadata', () => {
  const fm = setPositions(PLAIN, POS);
  const asComment = serialise(fm, parse(fm), { carrier: 'comment' });

  assert.ok(!asComment.includes(KEY), 'frontmatter copy should be gone');
  assert.deepEqual(getPositions(asComment), POS);
});

test('the comment carrier keeps the author\'s other frontmatter keys', () => {
  const out = serialise(TITLED, { spec: 1 }, { carrier: 'comment' });

  assert.ok(out.includes('title: Order fulfilment'), out);
  assert.ok(!out.includes(KEY), 'our own key should not be duplicated');
  assert.notEqual(parse(out), null);
  assert.equal(serialise(out, null, { carrier: 'comment' }), TITLED);
});

test('the comment carrier does not tidy away the author\'s blank lines', () => {
  const spaced = 'flowchart TD\n    a --> b\n\n\n\n    b --> c\n';
  const out = serialise(spaced, { spec: 1 }, { carrier: 'comment' });

  assert.equal(bodyOf(out), spaced);
  assert.equal(serialise(out, null, { carrier: 'comment' }), spaced);
});

// ----------------------------------------------------------------- merging

test('inject merges without discarding unrelated keys', () => {
  const withLayout = setPositions(PLAIN, POS);
  const merged = inject(withLayout, { semantics: { a: { kind: 'system' } } });

  assert.deepEqual(getPositions(merged), POS);
  assert.equal(parse(merged).semantics.a.kind, 'system');
});

test('setPositions replaces rather than merges, so deletions stick', () => {
  const two = setPositions(PLAIN, POS);
  const one = setPositions(two, { a: { x: 5, y: 5 } });

  assert.deepEqual(Object.keys(getPositions(one)), ['a']);
});

test('unknown keys inside the block are preserved on write', () => {
  const seeded = inject(PLAIN, { spec: 1, futureThing: { keep: true } });
  const out = setPositions(seeded, POS);

  assert.deepEqual(parse(out).futureThing, { keep: true });
});

// ------------------------------------------------------------------ styles

test('styles round-trip and stay out of the body', () => {
  const styles = { a: { fill: '#DAE8FC', stroke: '#6C8EBF', icon: 'browser' } };
  const out = setStyles(PLAIN, styles);

  assert.deepEqual(getStyles(out), styles);
  assert.equal(bodyOf(out), bodyOf(PLAIN));
});

test('layout and styles coexist', () => {
  const out = setStyles(setPositions(PLAIN, POS), { a: { fill: '#eee' } });

  assert.deepEqual(getPositions(out), POS);
  assert.equal(getStyles(out).a.fill, '#eee');
});

// ------------------------------------------------------------------- stale

test('a renamed node is reported stale, not silently dropped', () => {
  const out = setPositions(PLAIN, POS);

  assert.deepEqual(staleIds(out, ['a']), ['b']);
  assert.deepEqual(getPositions(out), POS, 'stale entries must be retained');
});

// ------------------------------------------------------------------ inputs

test('numeric strings are accepted, because YAML is not always typed', () => {
  const out = inject(PLAIN, {
    spec: 1,
    layout: { nodes: { a: { x: '10', y: '20' } } },
  });
  assert.deepEqual(getPositions(out).a, { x: 10, y: 20 });
});

test('non-finite and missing coordinates are rejected loudly', () => {
  assert.throws(() => setPositions(PLAIN, { a: { x: 1 } }), TypeError);
  assert.throws(() => setPositions(PLAIN, { a: { x: NaN, y: 0 } }), TypeError);
  assert.throws(() => setPositions(PLAIN, { a: null }), TypeError);
  assert.throws(() => setPositions(PLAIN, 'nope'), TypeError);
});

test('a non-string document is rejected rather than coerced', () => {
  assert.throws(() => parse(null), TypeError);
  assert.throws(() => parse(42), TypeError);
});

test('entries with unusable coordinates are skipped on read, not fatal', () => {
  const out = inject(PLAIN, {
    spec: 1,
    layout: { nodes: { a: { x: 1, y: 2 }, b: { x: 'abc', y: 0 } } },
  });
  assert.deepEqual(Object.keys(getPositions(out)), ['a']);
});

// -------------------------------------------------------------- validation

test('a clean document validates', () => {
  assert.equal(validate(setPositions(PLAIN, POS)).ok, true);
  assert.equal(validate(PLAIN).ok, true);
});

test('validation reports a bad layout shape', () => {
  const bad = inject(PLAIN, { spec: 1, layout: { nodes: { a: { x: 'zz' } } } });
  const r = validate(bad);

  assert.equal(r.ok, false);
  assert.ok(r.issues.some((i) => i.rule === 'layout'), JSON.stringify(r.issues));
});

test('validation reports a non-integer spec', () => {
  const r = validate(inject(PLAIN, { spec: 'one' }));
  assert.ok(r.issues.some((i) => i.rule === 'spec'), JSON.stringify(r.issues));
});

// ---------------------------------------------------- cross-implementation

test('metadata written by the C# implementation reads back identically', () => {
  // Captured verbatim from the C# writer, including its quoting choices —
  // js-yaml and YamlDotNet disagree about when a number-like string needs
  // quotes, and that divergence has bitten before.
  const fromCSharp = `---
title: Order fulfilment
x-merpeeps:
  spec: 1
  layout:
    nodes:
      gw:
        x: 60
        y: 130
      db:
        x: 300
        y: 130
    engine: manual
  style:
    nodes:
      gw:
        fill: '#FFE6CC'
        stroke: '#D79B00'
        strokeWidth: 3
        dashed: true
        icon: gateway
---
flowchart LR
    gw[Gateway] --> db[(Database)]
`;

  assert.deepEqual(getPositions(fromCSharp), {
    gw: { x: 60, y: 130 },
    db: { x: 300, y: 130 },
  });
  assert.deepEqual(getStyles(fromCSharp).gw, {
    fill: '#FFE6CC',
    stroke: '#D79B00',
    strokeWidth: 3,
    dashed: true,
    icon: 'gateway',
  });
  assert.equal(validate(fromCSharp).ok, true);
  assert.equal(
    strip(fromCSharp),
    '---\ntitle: Order fulfilment\n---\nflowchart LR\n    gw[Gateway] --> db[(Database)]\n',
  );
});

test('CRLF documents are handled', () => {
  const crlf = '---\r\ntitle: T\r\n---\r\nflowchart LR\r\n    a --> b\r\n';
  const out = setPositions(crlf, { a: { x: 1, y: 2 } });

  assert.deepEqual(getPositions(out).a, { x: 1, y: 2 });
  assert.equal(split(out).fm.title, 'T');
});
