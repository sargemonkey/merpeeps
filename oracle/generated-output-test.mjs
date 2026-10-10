// Outer-loop check for GENERATED output.
//
// The editor rewrites the diagram body after structural edits. Passing our own
// parser proves nothing — mermaid.js must accept the result. This renders
// editor-generated documents through stock mermaid and fails if any is invalid.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.MERPEEPS_GEN_PORT ?? 8897);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

const cases = JSON.parse(await readFile(
  process.env.MERPEEPS_GENERATED_CASES ?? join(ROOT, 'generated-cases.json'), 'utf8'));

const server = await new Promise((resolve) => {
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
  s.listen(PORT, '127.0.0.1', () => resolve(s));
});

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
await page.goto(`http://127.0.0.1:${PORT}/blank.html`, { waitUntil: 'load' });

const results = await page.evaluate(async (docs) => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  mermaid.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const out = [];
  let i = 0;
  for (const [name, text] of Object.entries(docs)) {
    i++;
    try {
      const { svg } = await mermaid.render('g' + i, text);
      const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');
      const nodes = doc.querySelectorAll('g.node').length;
      // A diagram that "renders" with no nodes is a silent failure: mermaid
      // returned an SVG, but nothing the user drew survived.
      out.push(nodes > 0
        ? { name, ok: true, nodes, bytes: svg.length }
        : { name, ok: false, error: 'rendered 0 nodes' });
    } catch (e) {
      out.push({ name, ok: false,
                 error: String(e && e.message || e).split('\n')[0].slice(0, 120) });
    }
  }
  return out;
}, cases);

console.log(JSON.stringify(results, null, 2));
await browser.close();
server.close();

const bad = results.filter(r => !r.ok);
console.log(bad.length === 0
  ? `GENERATED OUTPUT VALID — ${results.length}/${results.length} render in stock mermaid`
  : `GENERATED OUTPUT INVALID — ${bad.length} of ${results.length} failed`);
process.exit(bad.length === 0 ? 0 : 1);
