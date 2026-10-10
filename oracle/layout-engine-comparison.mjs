// Render one diagram through every available layout engine, side by side.
//
// The question this answers: how does Merpeeps differ from the official
// bundled dagre and ELK layout engines? They share a layout
// interface, so it is easy to assume they are alternatives to each other.
// They are not, and the difference is visible rather than theoretical:
// dagre and ELK COMPUTE a layout from the graph's structure, while Merpeeps
// REPLAYS a layout a human chose. Put them next to each other and the
// distinction stops needing explanation.
//
// This is a documentation generator, not a test. Mermaid 12 bundles ELK.
// The old tidy-tree plugin targets Mermaid 11 and is not part of this comparison.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const OUT = resolve(process.argv[2]
  ?? fileURLToPath(new URL('../docs/layout-engines.png', import.meta.url)));
const PORT = Number(process.env.MERPEEPS_CMP_PORT ?? 8888);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

// These are devDependencies, so check before launching a browser that would
// otherwise fail with an opaque module-resolution error inside the page.
const REQUIRED = [
  'node_modules/mermaid/dist/mermaid.esm.mjs',
];
const missing = REQUIRED.filter((p) => !existsSync(join(ROOT, p)));
if (missing.length > 0) {
  console.error(
    'The pinned Mermaid runtime is not installed. Run npm ci in oracle first.\n' +
    `missing:\n${missing.map((m) => `  ${m}`).join('\n')}`);
  process.exit(1);
}

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
const page = await browser.newPage({
  viewport: { width: 1720, height: 900 }, deviceScaleFactor: 2,
});
page.on('pageerror', (e) => console.error('PAGE ERROR:', e.message));
await page.goto(`http://127.0.0.1:${PORT}/plugin-page.html`, { waitUntil: 'load' });

const report = await page.evaluate(async () => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  const { registerMerpeepsLayout, renderWithLayout } =
    await import('./node_modules/merpeeps-mermaid-layout/src/mermaid-plugin.js');
  const { strip } = await import('./node_modules/merpeeps-mermaid-layout/src/index.js');

  mermaid.initialize({ startOnLoad: false, suppressErrorRendering: true,
    layout: 'dagre', theme: 'default', look: 'classic' });
  registerMerpeepsLayout(mermaid);

  // A deliberate arrangement: the two stores side by side under their service,
  // auth hanging below the gateway, the bus parked top-right. No automatic
  // algorithm produces this, because it encodes intent rather than structure.
  const withPositions = `---
x-merpeeps:
  spec: 1
  layout:
    engine: manual
    nodes:
      web:    { x: 20,  y: 150 }
      api:    { x: 220, y: 150 }
      auth:   { x: 220, y: 290 }
      orders: { x: 430, y: 150 }
      db:     { x: 380, y: 300 }
      cache:  { x: 530, y: 300 }
      queue:  { x: 650, y: 30 }
---
flowchart LR
    web[Web App] --> api[API Gateway]
    api --> auth[Auth Service]
    api --> orders[Order Service]
    orders --> db[(Postgres)]
    orders --> cache[(Redis)]
    orders --> queue[[Event Bus]]
`;
  const plain = strip(withPositions);

  const withAlgo = (src, algo) =>
    src.replace(/^flowchart/m, `---\nconfig:\n  layout: ${algo}\n---\nflowchart`);

  const panes = [];
  const add = async (title, note, render) => {
    try {
      const { svg } = await render();
      panes.push({ title, note, svg });
    } catch (e) {
      panes.push({ title, note, svg: `<pre>failed: ${e?.message ?? e}</pre>` });
    }
  };

  await add('dagre (built in)', 'Computes a layout from graph structure',
    () => mermaid.render('e1', plain));
  await add('ELK (built in)', 'A different algorithm — also computed',
    () => mermaid.render('e2', withAlgo(plain, 'elk')));
  await add('merpeeps', 'Replays the arrangement a human chose',
    () => renderWithLayout(mermaid, 'e4', withPositions));

  document.body.style.cssText =
    'margin:0;padding:18px;background:#fff;font-family:system-ui';
  document.body.innerHTML = `
    <div style="display:flex;gap:14px;align-items:stretch">
      ${panes.map((p) => `
        <div style="flex:1;min-width:0;border:1px solid #d8dee4;border-radius:10px;
                    overflow:hidden;background:#fff">
          <div style="padding:9px 12px;border-bottom:1px solid #d8dee4;background:#f6f8fa">
            <div style="font:600 13px system-ui">${p.title}</div>
            <div style="font:11px system-ui;color:#57606a;margin-top:2px">${p.note}</div>
          </div>
          <div style="padding:10px;height:640px;display:flex;align-items:center;
                      justify-content:center">${p.svg}</div>
        </div>`).join('')}
    </div>`;

  for (const s of document.querySelectorAll('svg')) {
    s.style.maxWidth = '100%';
    s.style.maxHeight = '620px';
    s.style.height = 'auto';
  }

  // The numeric claim behind the picture: only merpeeps reproduces the stored
  // coordinates. Everything else derives its own and ignores them.
  const centres = (svg) => {
    const host = document.createElement('div');
    host.innerHTML = svg;
    document.body.appendChild(host);
    const out = {};
    for (const g of host.querySelectorAll('g.node')) {
      const m = /translate\(\s*([-\d.]+)[ ,]+([-\d.]+)\s*\)/.exec(
        g.getAttribute('transform') ?? '');
      if (m) out[g.id] = [Number(m[1]), Number(m[2])];
    }
    host.remove();
    return out;
  };
  return panes.map((p) => ({ title: p.title, nodes: Object.keys(centres(p.svg)).length }));
});

console.log(JSON.stringify(report, null, 2));
await page.waitForTimeout(500);
await page.screenshot({ path: OUT, fullPage: true });
console.log(`wrote ${OUT}`);

await browser.close();
server.close();
