// Probe: what does a registered mermaid layout plugin actually receive?
//
// The plugin contract gives the algorithm a LayoutData, not the source text.
// Whether Merpeeps metadata reaches it — via config, an index signature, or not at
// all — determines how the viewer integration has to be built. Reading the type
// definitions cannot answer this; only running it can.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.MERPEEPS_PROBE_PORT ?? 8893);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

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
page.on('pageerror', (e) => console.error('PAGE ERROR:', e.message));
await page.goto(`http://127.0.0.1:${PORT}/blank.html`, { waitUntil: 'load' });

const result = await page.evaluate(async () => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;

  const seen = {};
  mermaid.registerLayoutLoaders([{
    name: 'merpeepsprobe',
    loader: async () => ({
      async render(layoutData, svg, helpers, options) {
        seen.topLevelKeys = Object.keys(layoutData);
        seen.configKeys = Object.keys(layoutData.config ?? {});
        seen.configHasMerpeeps = 'x-merpeeps' in (layoutData.config ?? {});
        seen.layoutDataHasMerpeeps = 'x-merpeeps' in layoutData;
        seen.configLayout = layoutData.config?.layout;
        seen.diagramId = layoutData.diagramId;
        seen.nodeIds = (layoutData.nodes ?? []).map((n) => n.id);
        seen.firstNode = layoutData.nodes?.[0]
          ? Object.keys(layoutData.nodes[0]) : null;
        seen.edgeKeys = (layoutData.edges ?? [])[0]
          ? Object.keys(layoutData.edges[0]) : null;
        seen.helperKeys = Object.keys(helpers ?? {});
        seen.options = options;
        // Anything not thrown here would try to draw; we only want the shape.
        throw new Error('PROBE_OK');
      },
    }),
  }]);

  const text = `---
title: Probe
config:
  layout: merpeepsprobe
x-merpeeps:
  spec: 1
  layout:
    nodes:
      a: { x: 11, y: 22 }
      b: { x: 33, y: 44 }
---
flowchart LR
    a[Alpha] --> b[Beta]
`;

  let error = null;
  try {
    await mermaid.render('probe1', text);
  } catch (e) {
    error = String(e?.message ?? e);
  }
  return { seen, error };
});

console.log(JSON.stringify(result, null, 2));
await browser.close();
server.close();

const reached = result.error?.includes('PROBE_OK');
console.log(reached
  ? 'PLUGIN REACHED — a custom layout algorithm can be registered and invoked'
  : 'PLUGIN NOT REACHED — registration or selection did not work');
process.exit(reached ? 0 : 1);
