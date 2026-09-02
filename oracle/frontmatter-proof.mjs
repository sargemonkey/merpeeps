// Does a stock mermaid renderer care about the `x-merpeeps` frontmatter block?
//
// The whole format rests on "mermaid ignores unknown frontmatter keys", which
// is a claim about someone else's software and therefore has to be measured
// rather than believed. This takes the real file from the editor, renders it
// twice in real mermaid — once as-is, once with the metadata stripped — and
// compares:
//
//   * the rendered SVG, byte for byte
//   * every console message mermaid emits (a silent warning still counts)
//   * whether any Merpeeps token reaches the user-visible text
//
// Usage: node oracle/frontmatter-proof.mjs [path-to.mmd]

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const TARGET = resolve(process.argv[2]
  ?? fileURLToPath(new URL('../docs/styled-sample.mmd', import.meta.url)));
const PORT = Number(process.env.MERPEEPS_PROOF_PORT ?? 8887);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

const source = await readFile(TARGET, 'utf8');

const server = await new Promise((r) => {
  const s = createServer(async (req, res) => {
    try {
      const rel = normalize(decodeURIComponent(new URL(req.url,
        `http://localhost:${PORT}`).pathname)).replace(/^([/\\])+/, '');
      const file = join(ROOT, rel);
      if (!file.startsWith(ROOT)) return res.writeHead(403).end();
      res.writeHead(200, { 'Content-Type': MIME[extname(file)] ?? 'text/plain',
                           'Cache-Control': 'no-store' }).end(await readFile(file));
    } catch { if (!res.headersSent) res.writeHead(404); res.end(); }
  });
  s.listen(PORT, '127.0.0.1', () => r(s));
});

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });

// Capture everything mermaid says, not just what it throws. A parser that
// "works" while logging "unknown key" on every render is not actually fine.
const console_ = [];
page.on('console', (m) => console_.push({ type: m.type(), text: m.text() }));
page.on('pageerror', (e) => console_.push({ type: 'pageerror', text: e.message }));

await page.goto(`http://127.0.0.1:${PORT}/plugin-page.html`, { waitUntil: 'load' });

const result = await page.evaluate(async (src) => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  const { strip, parse } = await import('./node_modules/merpeeps-mermaid-layout/src/index.js');

  // Turn logging all the way up: the question is whether mermaid notices the
  // block at all, and a default log level would hide a warning.
  mermaid.initialize({ startOnLoad: false, logLevel: 'debug' });

  const plain = strip(src);
  const { svg: withMeta } = await mermaid.render('withMeta', src);
  const { svg: without } = await mermaid.render('without', plain);

  // Control. Mermaid is not byte-deterministic between renders — some shapes
  // carry randomised bezier control points — so comparing two SVGs proves
  // nothing until you know what an unchanged input produces. Render the
  // stripped source a second time and diff that against the first.
  const { svg: withoutAgain } = await mermaid.render('withoutAgain', plain);

  // Compare on the user-visible surface. Raw SVG differs only by the id prefix
  // mermaid mints per render, which is noise, not leakage.
  const visible = (svg) => {
    const host = document.createElement('div');
    host.innerHTML = svg;
    document.body.appendChild(host);
    const text = host.innerText;
    const shapes = host.querySelectorAll('g.node').length;
    const edges = host.querySelectorAll('g.edgePaths path, path.flowchart-link').length;
    host.remove();
    return { text, shapes, edges };
  };

  const a = visible(withMeta);
  const b = visible(without);

  const TOKENS = ['x-merpeeps', 'merpeeps', 'spec', 'layout', 'engine',
                  'strokeWidth', 'dashed', 'DAE8FC'];

  // Normalise the per-render id prefix mermaid mints, then find the first real
  // difference so a failure says *what* differs rather than just "not equal".
  const norm = (svg, id) =>
    svg.replaceAll(id, 'ID').replace(/\s+/g, ' ');
  const na = norm(withMeta, 'withMeta');
  const nb = norm(without, 'without');
  const nc = norm(withoutAgain, 'withoutAgain');

  // If the same input renders differently twice, byte comparison is not a
  // meaningful test on this diagram and the visible-surface checks are what
  // count.
  const rendererIsDeterministic = nb === nc;

  let firstDiff = null;
  if (na !== nb) {
    let i = 0;
    while (i < na.length && i < nb.length && na[i] === nb[i]) i++;
    firstDiff = {
      at: i,
      withMeta: na.slice(Math.max(0, i - 70), i + 70),
      without: nb.slice(Math.max(0, i - 70), i + 70),
    };
  }

  return {
    metadataPresent: parse(src) !== null,
    strippedHasNoMetadata: parse(plain) === null,
    sameVisibleText: a.text === b.text,
    sameShapeCount: a.shapes === b.shapes,
    sameEdgeCount: a.edges === b.edges,
    shapes: a.shapes,
    edges: a.edges,
    leakedTokens: TOKENS.filter((t) =>
      a.text.toLowerCase().includes(t.toLowerCase())),
    rendererIsDeterministic,
    svgIdenticalIgnoringIds: na === nb,
    firstDiff,
    visibleText: a.text.replace(/\s+/g, ' ').trim().slice(0, 160),
  };
}, source);

await browser.close();
server.close();

// mermaid logs a lot at debug level; only complaints matter.
const complaints = console_.filter((m) =>
  m.type === 'error' || m.type === 'warning' || m.type === 'pageerror' ||
  /unknown|unrecognis|unrecogniz|unsupported|ignor|invalid|fail/i.test(m.text));

console.log(`file: ${TARGET}`);
console.log(JSON.stringify(result, null, 2));
console.log(`console complaints mentioning the metadata: ${
  complaints.filter(c => /merpeeps|frontmatter|yaml/i.test(c.text)).length}`);
for (const c of complaints.slice(0, 8)) {
  console.log(`  [${c.type}] ${c.text.slice(0, 140)}`);
}

const ok =
  result.metadataPresent &&
  result.strippedHasNoMetadata &&
  result.sameVisibleText &&
  result.sameShapeCount &&
  result.sameEdgeCount &&
  result.leakedTokens.length === 0 &&
  // Byte equality is only meaningful when the renderer is byte-deterministic
  // to begin with. Where it is not, the visible surface is the real test.
  (result.svgIdenticalIgnoringIds || !result.rendererIsDeterministic) &&
  complaints.filter(c => /merpeeps|frontmatter/i.test(c.text)).length === 0;

if (!result.rendererIsDeterministic) {
  console.log('\nnote: mermaid does not render this diagram byte-identically ' +
    'even for identical input (randomised curve control points), so the ' +
    'comparison is made on the user-visible surface instead.');
}

console.log(ok
  ? '\nSTOCK MERMAID IGNORES THE BLOCK — identical render, no leak, no complaint'
  : '\nTHE BLOCK IS NOT INERT — see the mismatch above');
process.exit(ok ? 0 : 1);
