// Produce a side-by-side image: the same diagram drawn by mermaid's own layout
// and by the Merpeeps layout plugin. Numbers in a test report are the proof; this is
// for humans deciding whether the result is any good.
//
//   node oracle/plugin-screenshot.mjs [outputPath]

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const OUT = resolve(process.argv[2]
  ?? fileURLToPath(new URL('../docs/layout-plugin-proof.png', import.meta.url)));
const PORT = Number(process.env.MERPEEPS_SHOT_PORT ?? 8890);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

const server = await new Promise((r) => {
  const s = createServer(async (req, res) => {
    try {
      const rel = normalize(decodeURIComponent(new URL(req.url,
        `http://localhost:${PORT}`).pathname)).replace(/^([/\\])+/, '');
      const file = join(ROOT, rel);
      if (!file.startsWith(ROOT)) return res.writeHead(403).end();
      res.writeHead(200, { 'Content-Type': MIME[extname(file)] ?? 'text/plain',
                           'Cache-Control': 'no-store' })
         .end(await readFile(file));
    } catch { if (!res.headersSent) res.writeHead(404); res.end(); }
  });
  s.listen(PORT, '127.0.0.1', () => r(s));
});

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({
  viewport: { width: 1500, height: 760 }, deviceScaleFactor: 2,
});
page.on('pageerror', (e) => console.error('PAGE ERROR:', e.message));
await page.goto(`http://127.0.0.1:${PORT}/plugin-page.html`, { waitUntil: 'load' });

await page.evaluate(async () => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  const { registerMerpeepsLayout, renderWithLayout } =
    await import('./node_modules/merpeeps-mermaid-layout/src/mermaid-plugin.js');
  const { strip } = await import('./node_modules/merpeeps-mermaid-layout/src/index.js');

  mermaid.initialize({ startOnLoad: false, suppressErrorRendering: true });
  registerMerpeepsLayout(mermaid);

  // A deliberate arrangement: the two stores side by side beneath their
  // services, and the queue parked out to the right. Auto-layout will not
  // produce this, which is the entire point.
  const src = `---
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
  style:
    nodes:
      web:    { fill: '#DAE8FC', stroke: '#6C8EBF' }
      api:    { fill: '#FFE6CC', stroke: '#D79B00', strokeWidth: 3 }
      auth:   { fill: '#E1D5E7', stroke: '#9673A6' }
      orders: { fill: '#D5E8D4', stroke: '#82B366' }
      db:     { fill: '#F8CECC', stroke: '#B85450' }
      cache:  { fill: '#F8CECC', stroke: '#B85450' }
      queue:  { fill: '#FFF2CC', stroke: '#D6B656', dashed: true }
---
flowchart LR
    web[Web App] --> api[API Gateway]
    api --> auth[Auth Service]
    api --> orders[Order Service]
    orders --> db[(Postgres)]
    orders --> cache[(Redis)]
    orders --> queue[[Event Bus]]
`;

  const { svg: auto } = await mermaid.render('shotAuto', strip(src));
  const { svg: merpeeps } = await renderWithLayout(mermaid, 'shotMerpeeps', src);

  const pane = (title, note, svg) => `
    <div style="flex:1;min-width:0;border:1px solid #d8dee4;border-radius:10px;
                overflow:hidden;background:#fff">
      <div style="padding:10px 14px;border-bottom:1px solid #d8dee4;background:#f6f8fa">
        <div style="font:600 15px system-ui">${title}</div>
        <div style="font:13px system-ui;color:#57606a;margin-top:2px">${note}</div>
      </div>
      <div style="padding:14px;height:470px;display:flex;align-items:center;
                  justify-content:center">${svg}</div>
    </div>`;

  document.body.style.cssText =
    'margin:0;padding:20px;background:#fff;font-family:system-ui';
  document.body.innerHTML = `
    <div style="display:flex;gap:20px;align-items:stretch">
      ${pane('Stock mermaid', 'Same file, metadata stripped — layout derived, no styling', auto)}
      ${pane('With the Merpeeps layout plugin',
             'Positions and styling read from the file and applied', merpeeps)}
    </div>`;

  for (const s of document.querySelectorAll('svg')) {
    s.style.maxWidth = '100%';
    s.style.maxHeight = '450px';
    s.style.height = 'auto';
  }
});

await page.waitForTimeout(400);
await page.screenshot({ path: OUT, fullPage: true });
console.log(`wrote ${OUT}`);

await browser.close();
server.close();
