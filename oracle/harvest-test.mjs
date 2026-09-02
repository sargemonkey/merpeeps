#!/usr/bin/env node
/**
 * Storing coordinates: can we capture what a real layout engine decided and
 * reproduce that arrangement later, from numbers alone?
 *
 * This is the whole argument for the format. If a rendered arrangement can be
 * captured as numbers and replayed by the plugin, then a layout engine is a
 * design-time tool rather than a runtime dependency.
 *
 * Two halves, and both need a real browser:
 *
 *   1. Is the harvest accurate? `window.merpeepsHarvest` claims to return the
 *      top-left of each node in SVG user units. Mermaid draws from the centre,
 *      so this involves a conversion that is easy to get wrong and impossible to
 *      check from C#. We verify it against the geometry independently.
 *
 *   2. Does the replay reproduce it? Harvest what dagre decided, shift every
 *      node by a fixed offset, write that in with `merpeeps adopt`, and re-render.
 *      The nodes must land on the shifted coordinates.
 *
 * The offset is what makes (2) mean anything. Harvesting dagre and replaying it
 * under dagre would pass whether or not the coordinates were read at all — the
 * engine would produce that arrangement unaided. Moving every node somewhere no
 * engine would put it removes that escape: the diagram can only come back
 * shifted if the stored numbers were used verbatim.
 *
 * The second control is the fixture's own hand-placed positions, which are
 * neither dagre's nor the shifted ones. Rendering it unmodified must differ from
 * the replay, or the replay was showing the original file all along.
 *
 * This gate used to run ELK here, which supplied its own control by being a
 * different engine from the default. ELK was removed from the project — it only
 * ran in a browser, and the editor opening one mid-edit to use it was worse than
 * the feature was worth — so the control is now constructed rather than borrowed.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const repo = resolve(import.meta.dirname, '..');
const cli = join(repo, 'src', 'Merpeeps.Cli', 'bin', 'Release', 'net10.0', 'merpeeps.dll');
// Reads the gate's own fixture, not docs/styled-sample.mmd. That file is what
// the editor opens by default, so anyone trying the app out edits it — which
// silently changed the diagram under three gates and failed them on code that
// was fine. A fixture has to be something no one is invited to edit.
const sample = join(repo, 'oracle', 'fixtures', 'styled.mmd');

if (!existsSync(cli)) {
  console.error(`FAIL: CLI not built at ${cli}`);
  process.exit(1);
}

const work = mkdtempSync(join(tmpdir(), 'merpeeps-harvest-'));
const fail = [];
const note = (ok, msg, detail) => {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${msg}${!ok && detail !== undefined ? ` — ${JSON.stringify(detail)}` : ''}`);
  if (!ok) fail.push(msg);
};

const run = (...a) =>
  execFileSync('dotnet', [cli, ...a], { encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'] });

/**
 * Run and capture both streams plus the exit code.
 *
 * Not execFileSync: it returns stdout only, so a check looking for a warning on
 * stderr sees an empty string and fails against working code.
 */
function tryRun(...a) {
  const r = spawnSync('dotnet', [cli, ...a], { encoding: 'utf8' });
  return { code: r.status ?? 1, err: String(r.stderr ?? ''), out: String(r.stdout ?? '') };
}

// ---------------------------------------------------------------------------
// 0. `adopt` refuses the cases where guessing would lose work.
//
// Run first because they are fast and need no browser: if the contract is
// broken there is no point starting Chrome.
// ---------------------------------------------------------------------------
{
  const stale = join(work, 'stale.json');
  writeFileSync(stale, JSON.stringify({ zzz: { x: 1, y: 2 } }));
  const none = tryRun('adopt', sample, '--positions', stale, '--out', join(work, 'n.mmd'));
  note(none.code !== 0 && /matched a node/.test(none.err),
    'adopt fails when no harvested id matches the diagram', none);

  const both = tryRun('adopt', sample, '--positions', stale, '--write', '--out', join(work, 'n.mmd'));
  note(both.code !== 0 && /alternatives/.test(both.err),
    'adopt refuses --write and --out together rather than picking one', both);

  const partial = join(work, 'partial.json');
  writeFileSync(partial, JSON.stringify({ web: { x: 1, y: 2 }, nope: { x: 3, y: 4 } }));
  const some = tryRun('adopt', sample, '--positions', partial, '--out', join(work, 'p.mmd'));
  note(some.code === 0 && /matched no node/.test(some.err),
    'adopt reports ids that matched nothing instead of absorbing them', some);
}

function emit(name, { plain, layout, file } = {}) {
  const out = join(work, name);
  const args = ['preview', file ?? sample, '--out', out];
  if (plain) args.push('--plain');
  if (layout) args.push('--layout', layout);
  run(...args);
  return `file://${out.replace(/\\/g, '/')}`;
}

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());

/** Load a page, wait for the render, and pull both the harvest and the raw geometry. */
async function inspect(url) {
  const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(() => window.merpeepsRendered === true, { timeout: 60_000 })
    .catch(() => {});

  const data = await page.evaluate(async () => {
    const svg = document.querySelector('#stage svg');
    if (!svg) return { error: 'nothing rendered' };

    // Read the geometry ourselves rather than trusting the harvest, so the two
    // can be compared. Same source (the transform) but the centre/top-left
    // conversion — the part under test — is computed independently here.
    const RENDER_ID = 'merpeeps-preview';
    const raw = {};
    for (const g of svg.querySelectorAll('g.node')) {
      const m = /translate\(\s*([-\d.]+)[ ,]+([-\d.]+)\s*\)/.exec(g.getAttribute('transform') ?? '');
      if (!m) continue;
      let id = g.id;
      if (id.startsWith(RENDER_ID + '-')) {
        id = id.slice(RENDER_ID.length + 1).replace(/^[a-zA-Z]+-/, '').replace(/-\d+$/, '');
      }
      if (!id) id = g.id;
      let b; try { b = g.getBBox(); } catch { b = { width: 0, height: 0 }; }
      raw[id] = { cx: Number(m[1]), cy: Number(m[2]), w: b.width, h: b.height };
    }

    const text = document.getElementById('src')?.textContent
      ?? window.merpeepsSource
      ?? null;

    let harvest = null, harvestError = null;
    try {
      harvest = JSON.parse(await window.merpeepsHarvest(text ?? undefined));
    } catch (e) {
      harvestError = String(e?.message ?? e);
    }
    return { raw, harvest, harvestError };
  });

  await page.close();
  return { ...data, errors };
}

// ---------------------------------------------------------------------------
// 1. Is the harvest accurate?
// ---------------------------------------------------------------------------
const stockUrl = emit('stock.html', { plain: true, layout: 'dagre' });
const stock = await inspect(stockUrl);

note(!stock.error && stock.errors.length === 0,
  'the stock page rendered without error', stock.error ?? stock.errors[0]);
note(stock.harvest && Object.keys(stock.harvest).length >= 6,
  'the harvest returned every node', stock.harvestError ?? Object.keys(stock.harvest ?? {}).length);

// Control for the id recovery. Mermaid stamps its own prefix and counter onto
// every node element and exposes no data-* attribute carrying the author's id,
// so the harvest has to reconstruct it. Without this check, a recovery bug would
// corrupt the test's own extractor in exactly the same way and every later check
// would pass on matching nonsense.
//
// The expected ids are written out rather than derived. Deriving them from the
// fixture's `style` block was the first attempt and it only worked by accident —
// every node in that file happened to be styled. The moment one was not, the
// control failed against correct code.
const FIXTURE_IDS = ['auth', 'bus', 'gw', 'inv', 'orders', 'web'];

{
  // Guard the oracle itself: if the fixture gains or loses a node, fail here
  // with a clear reason rather than misattributing it to the harvest.
  const body = readFileSync(sample, 'utf8').replace(/^---[\s\S]*?\n---\n/, '');
  const missing = FIXTURE_IDS.filter((id) => !new RegExp(`\\b${id}[[({]`).test(body));
  note(missing.length === 0,
    'the fixture still declares the ids this gate expects', { missing });
}

if (stock.harvest) {
  const got = Object.keys(stock.harvest).sort();
  note(FIXTURE_IDS.join(',') === got.join(','),
    "harvested ids are the author's ids, not mermaid's element ids",
    { expected: FIXTURE_IDS, got });
}

if (stock.harvest && stock.raw) {
  // The conversion under test: mermaid positions by centre, Merpeeps stores the
  // top-left. A sign error here would be invisible in C# and wrong on screen.
  const wrong = Object.entries(stock.harvest).filter(([id, h]) => {
    const r = stock.raw[id];
    if (!r) return true;
    return Math.abs(h.x - (r.cx - r.w / 2)) > 0.51
        || Math.abs(h.y - (r.cy - r.h / 2)) > 0.51;
  });
  note(wrong.length === 0,
    'the harvest reports the top-left, not the centre mermaid draws from',
    wrong.slice(0, 3));

  // A centre and a top-left only coincide for a zero-sized node, so if the
  // sample has any real width this distinguishes the two. Without it, the check
  // above would pass just as happily on an off-by-nothing bug.
  const sized = Object.values(stock.raw).filter((r) => r.w > 1 && r.h > 1);
  note(sized.length >= 6,
    'control: nodes have real size, so top-left and centre are distinguishable',
    sized.length);
}

// ---------------------------------------------------------------------------
// 2. Does the replay reproduce it?
// ---------------------------------------------------------------------------

// Somewhere no layout engine would independently choose. Large enough that no
// tolerance could absorb it, and not a round number, so a stray rounding or a
// half-applied offset shows up as a mismatch rather than blending in.
const SHIFT = { x: 137, y: 91 };

let adopted = null;
let expected = null;

if (stock.harvest && Object.keys(stock.harvest).length > 0) {
  expected = Object.fromEntries(Object.entries(stock.harvest).map(
    ([id, p]) => [id, { x: p.x + SHIFT.x, y: p.y + SHIFT.y }]));

  const posFile = join(work, 'harvest.json');
  writeFileSync(posFile, JSON.stringify(expected));
  adopted = join(work, 'adopted.mmd');
  run('adopt', sample, '--positions', posFile, '--out', adopted);

  const written = readFileSync(adopted, 'utf8');
  note(/x-merpeeps/.test(written) && /layout/.test(written),
    '`merpeeps adopt` wrote a layout block into the file');
  note(written.includes(readFileSync(sample, 'utf8').replace(/^---[\s\S]*?---\n/, '').trim().split('\n')[0]),
    'the diagram body survived adoption unchanged');

  // Size is derived from the rendered label and the plugin re-measures it, so
  // storing it would be bytes the renderer discards — and wrong the moment a
  // label changes. Positions are the only thing that cannot be recomputed.
  const entries = JSON.parse(run('read', adopted)).layout?.nodes ?? {};
  const withSize = Object.entries(entries).filter(([, v]) => 'w' in v || 'h' in v);
  note(Object.keys(entries).length >= 6 && withSize.length === 0,
    'adopt stores positions only, not the sizes the renderer recomputes',
    withSize.slice(0, 3));

  // The fixture ships with hand-placed positions of its own. Adoption has to
  // replace them, not merge with them, or the replay would be showing a blend of
  // two layouts and the comparison below would be meaningless.
  const stale = Object.entries(entries).filter(([id, v]) =>
    expected[id] && (Math.abs(v.x - expected[id].x) > 0.51 ||
                     Math.abs(v.y - expected[id].y) > 0.51));
  note(stale.length === 0,
    'the adopted file holds the shifted coordinates, not the fixture\'s own',
    stale.slice(0, 3));
}

if (adopted) {
  // The replay page: the Merpeeps plugin reading stored coordinates.
  const replayUrl = emit('replay.html', { file: adopted });
  const replay = await inspect(replayUrl);

  note(!replay.error && replay.errors.length === 0,
    'the replay page rendered without error', replay.error ?? replay.errors[0]);

  // No page should carry an ELK bundle any more, whatever it was asked for.
  // Checked on bytes rather than by looking for the word: mermaid's own source
  // mentions elk in a deprecation notice, so a text match would fire on a page
  // that is entirely clean.
  const replayHtml = readFileSync(join(work, 'replay.html'), 'utf8');
  note(replayHtml.length < 4_500_000,
    'the replay page does not carry a layout plugin bundle',
    `${(replayHtml.length / 1024 / 1024).toFixed(1)} MB`);

  if (replay.harvest) {
    // Against the coordinates we wrote, not against the earlier harvest. The
    // question is whether the file's numbers were honoured, and only the
    // written numbers can answer it.
    const drift = Object.entries(expected)
      .filter(([id]) => replay.harvest[id])
      .map(([id, want]) => {
        const got = replay.harvest[id];
        return { id, dx: +(got.x - want.x).toFixed(1), dy: +(got.y - want.y).toFixed(1) };
      });

    note(drift.length >= 6, 'every stored node came back on replay', drift.length);
    // 2px: labels can reflow by a hair between renders, which nudges the bbox
    // and therefore the top-left. Anything larger is a real placement change.
    const moved = drift.filter((d) => Math.abs(d.dx) > 2 || Math.abs(d.dy) > 2);
    note(moved.length === 0,
      'replayed nodes landed on the stored coordinates, offset and all',
      moved.slice(0, 4));
  } else {
    note(false, 'the replay page could be harvested', replay.harvestError);
  }

  // THE CONTROL. The same diagram, same plugin, same engine — but carrying the
  // fixture's own hand-placed positions instead of the adopted ones. If this
  // matches the replay, the stored coordinates were never read and every check
  // above passed for the wrong reason.
  const bareUrl = emit('bare.html', {});
  const bare = await inspect(bareUrl);

  if (bare.harvest && replay.harvest) {
    const differs = Object.keys(expected).some((id) => {
      const a = bare.harvest[id], b = replay.harvest[id];
      return a && b && (Math.abs(a.x - b.x) > 2 || Math.abs(a.y - b.y) > 2);
    });
    note(differs,
      'control: the unmodified fixture lays out differently from the replay '
      + '(so the replay really used the adopted coordinates)');
  } else {
    note(false, 'control: the unmodified page could be harvested', bare.harvestError);
  }
}

await browser.close();
rmSync(work, { recursive: true, force: true });

console.log('');
if (fail.length) {
  console.log(`HARVEST TEST FAILED — ${fail.length} check(s)`);
  process.exit(1);
}
console.log('HARVEST VERIFIED — a rendered layout was captured, shifted, and replayed from stored coordinates');
