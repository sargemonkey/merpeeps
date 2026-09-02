#!/usr/bin/env node
/**
 * What colours does each mermaid theme actually use?
 *
 * The editor draws its own canvas, so to make a diagram theme mean anything it
 * has to know the fill, stroke and text colour each theme gives an unstyled
 * node. Those values are not documented anywhere stable, and guessing them
 * would produce a canvas that disagreed with the render — the one thing the
 * reference pane exists to prevent.
 *
 * So measure them, from the exact mermaid build we ship.
 */
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const repo = resolve(import.meta.dirname, '..');
const cli = join(repo, 'src', 'Merpeeps.Cli', 'bin', 'Release', 'net10.0', 'merpeeps.dll');
if (!existsSync(cli)) { console.error(`FAIL: CLI not built at ${cli}`); process.exit(1); }

const THEMES = [
  // The five long-standing ones.
  'default', 'dark', 'forest', 'neutral', 'base',
  // Newer additions. Listed here as candidates, not facts: mermaid silently
  // falls back to `default` for a name it does not know, so a theme that is not
  // supported looks exactly like one that is until the colours are compared.
  'neo', 'neo-dark', 'redux', 'redux-dark', 'redux-color', 'redux-dark-color',
  'mc', 'mermaid-chart',
];
const work = mkdtempSync(join(tmpdir(), 'merpeeps-theme-'));

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());

const found = {};

for (const theme of THEMES) {
  const src = join(work, `${theme}.mmd`);
  writeFileSync(src, `---\nconfig:\n  theme: ${theme}\n---\nflowchart TD\n    a[Alpha] --> b[Beta]\n`);

  const out = join(work, `${theme}.html`);
  execFileSync('dotnet', [cli, 'preview', src, '--plain', '--out', out],
    { stdio: ['ignore', 'ignore', 'pipe'] });

  const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
  await page.goto(`file://${out.replace(/\\/g, '/')}`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.merpeepsRendered === true, { timeout: 60_000 })
    .catch(() => {});

  found[theme] = await page.evaluate(() => {
    const g = document.querySelector('#stage svg g.node');
    if (!g) return null;
    // The shape carries the fill and stroke; the label carries the text colour.
    const shape = g.querySelector('rect, polygon, path, circle, ellipse');
    const label = g.querySelector('.nodeLabel, foreignObject span, text');
    const cs = shape ? getComputedStyle(shape) : null;
    const ls = label ? getComputedStyle(label) : null;
    return {
      fill: cs?.fill ?? null,
      stroke: cs?.stroke ?? null,
      text: ls?.color ?? null,
      background: getComputedStyle(document.querySelector('#stage svg')).backgroundColor,
    };
  });

  await page.close();
}

await browser.close();
rmSync(work, { recursive: true, force: true });

const hex = (rgb) => {
  const m = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)/.exec(rgb ?? '');
  if (!m) return rgb;
  return '#' + [1, 2, 3].map((i) => Math.round(+m[i]).toString(16).padStart(2, '0')).join('').toUpperCase();
};

const table = Object.fromEntries(Object.entries(found).map(([k, v]) => [k, v && {
  fill: hex(v.fill), stroke: hex(v.stroke), text: hex(v.text),
}]));

// The control that makes this mean anything. An unsupported name renders
// exactly like `default`, so "it rendered" proves nothing; only a difference in
// the resulting colours shows the theme was actually applied.
const baseline = JSON.stringify(table.default);
for (const [name, v] of Object.entries(table)) {
  if (name === 'default') continue;
  table[name] = { ...v, supported: JSON.stringify(v) !== baseline };
}

console.log(JSON.stringify(table, null, 2));

const unsupported = Object.entries(table)
  .filter(([n, v]) => n !== 'default' && !v.supported).map(([n]) => n);
console.log('\nnot supported by this mermaid build:', unsupported.join(', ') || '(none)');

// ---------------------------------------------------------------------------
// Gate: does the editor's palette still match what mermaid draws?
//
// The canvas exists to predict the render. If a mermaid upgrade changes a
// theme's colours the editor would go on drawing the old ones, quietly
// disagreeing with the reference pane sitting next to it — the one failure that
// pane was built to rule out. Cheaper to fail here.
// ---------------------------------------------------------------------------
const palette = readFileSync(
  join(repo, 'src', 'Merpeeps.App', 'DiagramPalette.cs'), 'utf8');

const declared = {};
for (const m of palette.matchAll(
  /new\("([\w-]+)",\s*"[^"]*",\s*C\("(#[0-9A-Fa-f]{6})"\),\s*C\("(#[0-9A-Fa-f]{6})"\),\s*C\("(#[0-9A-Fa-f]{6})"\)\)/g)) {
  declared[m[1]] = {
    fill: m[2].toUpperCase(), stroke: m[3].toUpperCase(), text: m[4].toUpperCase(),
  };
}

const fail = [];
const note = (ok, msg) => {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${msg}`);
  if (!ok) fail.push(msg);
};

console.log('');
note(Object.keys(declared).length >= 5,
  `read ${Object.keys(declared).length} themes out of DiagramPalette.cs`);

for (const [name, want] of Object.entries(declared)) {
  const got = table[name];
  if (!got) {
    note(false, `${name}: the editor offers a theme this probe never measured`);
    continue;
  }
  const same = got.fill === want.fill && got.stroke === want.stroke && got.text === want.text;
  note(same, `${name}: editor palette matches the render`
    + (same ? '' : ` — editor ${JSON.stringify(want)}, mermaid ${JSON.stringify({
        fill: got.fill, stroke: got.stroke, text: got.text })}`));
}

// The other direction: a theme mermaid supports that the editor does not offer
// is a missed feature rather than a bug, but it should be visible not silent.
const missing = Object.entries(table)
  .filter(([n, v]) => (n === 'default' || v.supported) && !declared[n])
  .map(([n]) => n);
if (missing.length) console.log(`note: mermaid also supports ${missing.join(', ')}`);

console.log('');
if (fail.length) {
  console.log(`THEME PALETTE FAILED — ${fail.length} check(s)`);
  process.exit(1);
}
console.log('THEME PALETTE VERIFIED — the editor draws what mermaid draws');

