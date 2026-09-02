#!/usr/bin/env node
/**
 * Does baked output render correctly with NO plugin?
 *
 * That is the entire promise of `merpeeps bake`: take a document whose styling
 * lives in metadata and produce plain mermaid that shows the same colours
 * anywhere mermaid runs — GitHub, GitLab, Notion — with nothing installed.
 *
 * The test renders three ways and compares:
 *   stripped : the plain body, no styling at all           (the "before")
 *   baked    : the output of `merpeeps bake`, stock mermaid  (the claim)
 *   merpeeps   : the original, through our layout plugin     (the reference)
 *
 * The claim holds only if baked matches merpeeps on colour while stripped does
 * not. Rendering without throwing proves nothing on its own — a diagram that
 * silently ignored every `style` statement would pass that.
 */
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

const repo = resolve(import.meta.dirname, '..');
const here = import.meta.dirname;
const cli = join(repo, 'src', 'Merpeeps.Cli', 'bin', 'Release', 'net10.0', 'merpeeps.dll');
// Reads the gate's own fixture, not docs/styled-sample.mmd. That file is what
// the editor opens by default, so anyone trying the app out edits it — which
// silently changed the diagram under three gates and failed them on code that
// was fine. A fixture has to be something no one is invited to edit.
const sample = join(repo, 'oracle', 'fixtures', 'styled.mmd');
const PORT = 39_522;

if (!existsSync(cli)) {
  console.error(`FAIL: CLI not built at ${cli}\n      run: dotnet build -c Release`);
  process.exit(1);
}

const run = (args) =>
  execFileSync('dotnet', [cli, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });

const original = readFileSync(sample, 'utf8');
const baked = run(['bake', sample]);
const stripped = run(['strip', sample]);

const fail = [];
const note = (ok, msg) => {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${msg}`);
  if (!ok) fail.push(msg);
};

note(baked.includes('style '), 'bake emitted style statements');
note(!baked.includes('x-merpeeps'), 'baked output carries no metadata block');
note(!stripped.includes('style '), 'the stripped control has no styling');

const server = createServer((req, res) => {
  if (req.url.startsWith('/mermaid.js')) {
    res.writeHead(200, { 'content-type': 'text/javascript' });
    res.end(readFileSync(join(here, 'node_modules/mermaid/dist/mermaid.min.js')));
    return;
  }

  // Serve the oracle directory so the plugin reference can be imported as a real
  // module graph — bare specifiers need a base URL, and the import map below
  // resolves js-yaml the same way the other harnesses do.
  if (req.url !== '/') {
    const rel = decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '');
    const file = join(here, rel);
    if (!file.startsWith(here)) { res.writeHead(403); res.end(); return; }

    // Read before writing headers: doing it the other way round means a missing
    // file throws after a 200 is already on the wire, which crashes the server
    // instead of producing a 404.
    let body;
    try {
      body = readFileSync(file);
    } catch {
      res.writeHead(404, { 'content-type': 'text/plain' });
      res.end('not found');
      return;
    }
    res.writeHead(200, { 'content-type': 'text/javascript' });
    res.end(body);
    return;
  }

  res.writeHead(200, { 'content-type': 'text/html' });
  res.end('<!DOCTYPE html><html><body><div id="stage"></div>'
    + '<script type="importmap">{"imports":{"js-yaml":"/node_modules/js-yaml/dist/js-yaml.mjs"}}</script>'
    + '<script src="/mermaid.js"></script></body></html>');
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 1200, height: 900 } });

const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(e.message));
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const out = await page.evaluate(async ({ baked, stripped, original }) => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  // Fills by node id, read from resolved style — the plugin paints via CSS, so
  // the `fill` attribute alone would miss it and look like a product bug.
  const paintOf = (svg) => {
    stage.innerHTML = svg;
    const out = {};
    for (const g of stage.querySelectorAll('g.node')) {
      const shape = g.querySelector('rect, circle, ellipse, polygon, path');
      if (!shape) continue;
      const cs = getComputedStyle(shape);
      const id = (g.id.match(/flowchart-([A-Za-z0-9_]+)-/) ?? [])[1] ?? g.id;
      out[id] = { fill: cs.fill, stroke: cs.stroke, dash: cs.strokeDasharray };
    }
    return out;
  };

  const result = {};
  try {
    const { svg } = await m.render('bk', baked);
    result.baked = paintOf(svg);
  } catch (e) { result.bakedError = String(e?.message ?? e).slice(0, 200); }

  try {
    const { svg } = await m.render('st', stripped);
    result.stripped = paintOf(svg);
  } catch (e) { result.strippedError = String(e?.message ?? e).slice(0, 200); }

  try {
    const plugin = await import('/node_modules/merpeeps-mermaid-layout/src/mermaid-plugin.js');
    plugin.registerMerpeepsLayout(m);
    const { svg } = await plugin.renderWithLayout(m, 'mm', original);
    result.merpeeps = paintOf(svg);
  } catch (e) { result.merpeepsError = String(e?.message ?? e).slice(0, 200); }

  return result;
}, { baked, stripped, original });

await browser.close();
server.close();

note(!out.bakedError, 'baked output renders in stock mermaid' + (out.bakedError ? ` — ${out.bakedError}` : ''));
note(!out.strippedError, 'stripped control renders' + (out.strippedError ? ` — ${out.strippedError}` : ''));
note(!out.merpeepsError, 'plugin reference renders' + (out.merpeepsError ? ` — ${out.merpeepsError}` : ''));
note(pageErrors.length === 0, 'no uncaught page errors' + (pageErrors.length ? ` — ${pageErrors[0]}` : ''));

if (out.baked && out.stripped && out.merpeeps) {
  const ids = Object.keys(out.merpeeps).filter((id) => out.baked[id] && out.stripped[id]);
  note(ids.length >= 5, `compared ${ids.length} nodes across all three renders`);

  const matched = ids.filter((id) => out.baked[id].fill === out.merpeeps[id].fill);
  note(matched.length === ids.length,
    `baked fills match the plugin reference on every node (${matched.length}/${ids.length})`);

  // Without this, "baked matches merpeeps" could just mean mermaid's default
  // palette happens to equal the stored colours and nothing was applied at all.
  const changed = ids.filter((id) => out.stripped[id].fill !== out.baked[id].fill);
  note(changed.length === ids.length,
    `baking genuinely changed the paint vs the unstyled control (${changed.length}/${ids.length})`);

  const dashed = ids.filter((id) => out.baked[id].dash && out.baked[id].dash !== 'none');
  note(dashed.length > 0, `dashed outlines survived the bake (${dashed.length} node(s))`);

  for (const id of ids) {
    const flag = out.baked[id].fill === out.merpeeps[id].fill ? ' ' : '!';
    console.log(`     ${flag} ${id.padEnd(8)} plain=${out.stripped[id].fill.padEnd(20)}`
      + ` baked=${out.baked[id].fill.padEnd(20)} plugin=${out.merpeeps[id].fill}`);
  }
} else {
  note(false, 'could not compare — at least one render produced no nodes');
}

console.log('');
if (fail.length) {
  console.log(`BAKE RENDER TEST FAILED — ${fail.length} check(s)`);
  process.exit(1);
}
console.log('BAKE VERIFIED — baked output renders unaided and matches the plugin reference');
