// Does the layout plugin actually place nodes where Merpeeps says?
//
// "It rendered without throwing" is not the claim. The claim is that a node
// stored at (x, y) is drawn at (x, y). That is only checkable by rendering in a
// real browser and reading the coordinates back out of the SVG, because node
// size depends on the rendered label and mermaid positions by centre.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.MERPEEPS_PLUGIN_PORT ?? 8891);
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
const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(e.message));
await page.goto(`http://127.0.0.1:${PORT}/plugin-page.html`, { waitUntil: 'load' });

const results = await page.evaluate(async () => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  const { registerMerpeepsLayout, renderWithLayout } =
    await import('./node_modules/merpeeps-mermaid-layout/src/mermaid-plugin.js');

  mermaid.initialize({ startOnLoad: false, suppressErrorRendering: true });
  registerMerpeepsLayout(mermaid);

  const stage = document.getElementById('stage');

  // Read back where each node was actually drawn.
  const drawn = (svg) => {
    stage.innerHTML = svg;
    const out = {};
    for (const g of stage.querySelectorAll('g.node')) {
      const m = /translate\(\s*([-\d.]+)[ ,]+([-\d.]+)\s*\)/.exec(
        g.getAttribute('transform') ?? '');
      if (!m) continue;
      const box = g.getBBox();
      out[g.id] = {
        cx: Number(m[1]), cy: Number(m[2]),
        w: box.width, h: box.height,
      };
    }
    return out;
  };

  const idFor = (nodes, want) =>
    Object.keys(nodes).find((k) => new RegExp(`(^|-)${want}(-|$)`).test(k));

  const checks = [];
  const check = (name, ok, detail) => checks.push({ name, ok, detail });

  // ---------------------------------------------------------- stored layout
  const positioned = `---
x-merpeeps:
  spec: 1
  layout:
    engine: manual
    nodes:
      a: { x: 100, y: 400 }
      b: { x: 600, y: 100 }
      c: { x: 600, y: 700 }
---
flowchart LR
    a[Alpha] --> b[Beta]
    a --> c[Gamma]
`;

  try {
    const { svg } = await renderWithLayout(mermaid, 'p1', positioned);
    const nodes = drawn(svg);
    const want = { a: [100, 400], b: [600, 100], c: [600, 700] };

    let placed = 0;
    const detail = {};
    for (const [id, [x, y]] of Object.entries(want)) {
      const key = idFor(nodes, id);
      if (!key) { detail[id] = 'not drawn'; continue; }
      const n = nodes[key];
      // Merpeeps stores the top-left; mermaid draws from the centre.
      const dx = Math.abs((n.cx - n.w / 2) - x);
      const dy = Math.abs((n.cy - n.h / 2) - y);
      detail[id] = { dx: +dx.toFixed(1), dy: +dy.toFixed(1) };
      if (dx <= 1.5 && dy <= 1.5) placed++;
    }
    check('nodes are drawn at their stored coordinates', placed === 3, detail);
    check('all three nodes rendered', Object.keys(nodes).length === 3,
      Object.keys(nodes));
  } catch (e) {
    check('stored layout renders', false, String(e?.message ?? e));
  }

  // A stored arrangement mermaid would never produce on its own: the whole
  // point is that auto-layout is overridden, not merely reproduced.
  try {
    const { svg } = await renderWithLayout(mermaid, 'p2', positioned);
    const nodes = drawn(svg);
    const a = nodes[idFor(nodes, 'a')];
    const b = nodes[idFor(nodes, 'b')];
    check('layout is genuinely overridden (a is left of and below b)',
      a.cx < b.cx && a.cy > b.cy, { a: [a.cx, a.cy], b: [b.cx, b.cy] });
  } catch (e) {
    check('layout is genuinely overridden', false, String(e?.message ?? e));
  }

  // ------------------------------------------------------------- edges drawn
  try {
    const { svg } = await renderWithLayout(mermaid, 'p3', positioned);
    stage.innerHTML = svg;
    const paths = stage.querySelectorAll('g.edgePaths path, path.flowchart-link');
    check('edges are drawn', paths.length >= 2, paths.length);

    // Control: does stock mermaid produce markers when measured the same way?
    // Without this, a zero could just as easily mean the measurement is wrong.
    // (It did: mermaid does not nest markers under <defs>.)
    const { strip } = await import('./node_modules/merpeeps-mermaid-layout/src/index.js');
    const { svg: control } = await mermaid.render('p3ctl', strip(positioned));
    stage.innerHTML = control;
    const controlMarkers = stage.querySelectorAll('marker').length;

    stage.innerHTML = svg;
    const markers = stage.querySelectorAll('marker');
    const livePaths = [...stage.querySelectorAll('g.edgePaths path, path.flowchart-link')];
    const arrowed = livePaths.filter((p) => p.getAttribute('marker-end'));

    // The reference an edge points at must actually resolve, or the arrowhead
    // silently does not draw.
    const resolved = arrowed.filter((p) => {
      const id = /url\(#(.+?)\)/.exec(p.getAttribute('marker-end') ?? '')?.[1];
      return id ? !!stage.querySelector(`#${CSS.escape(id)}`) : false;
    });

    check('stock mermaid defines markers (control)', controlMarkers > 0, controlMarkers);
    check('the plugin defines markers too', markers.length > 0, {
      plugin: markers.length, control: controlMarkers,
    });
    check('edges carry an arrowhead', arrowed.length >= 2, arrowed.length);
    check('every arrowhead reference resolves to a real marker',
      resolved.length === arrowed.length, {
        arrowed: arrowed.length,
        resolved: resolved.length,
        markerEnd: arrowed[0]?.getAttribute('marker-end') ?? null,
        ids: [...markers].map((m) => m.id).slice(0, 4),
      });

    // Regression: edges once ran centre-to-centre, so the arrowhead was drawn
    // under the target node and never seen. Every marker check above still
    // passed. The endpoint must land on the node's outline instead.
    const nodesNow = {};
    for (const g of stage.querySelectorAll('g.node')) {
      const m = /translate\(\s*([-\d.]+)[ ,]+([-\d.]+)\s*\)/.exec(
        g.getAttribute('transform') ?? '');
      if (m) nodesNow[g.id] = { cx: Number(m[1]), cy: Number(m[2]) };
    }
    const centres = Object.values(nodesNow);
    const tips = livePaths.map((p) => {
      const end = p.getPointAtLength(p.getTotalLength());
      const nearest = Math.min(...centres.map((c) =>
        Math.hypot(end.x - c.cx, end.y - c.cy)));
      return +nearest.toFixed(1);
    });
    check('edges stop at the node outline, not its centre',
      tips.length > 0 && tips.every((d) => d > 10), { distanceToNearestCentre: tips });
  } catch (e) {
    check('edges are drawn', false, String(e?.message ?? e));
  }

  // ------------------------------------------------------------ fall-through
  const plain = 'flowchart LR\n    a[Alpha] --> b[Beta]\n';
  try {
    const { svg } = await renderWithLayout(mermaid, 'p4', plain);
    check('a document with no stored positions still renders',
      Object.keys(drawn(svg)).length === 2);
  } catch (e) {
    check('a document with no stored positions still renders', false,
      String(e?.message ?? e));
  }

  const grouped = `---
x-merpeeps:
  spec: 1
  layout:
    nodes:
      a: { x: 0, y: 0 }
      b: { x: 200, y: 0 }
---
flowchart LR
    subgraph one [Group]
        a[Alpha] --> b[Beta]
    end
`;
  try {
    const { svg } = await renderWithLayout(mermaid, 'p5', grouped);
    check('subgraphs fall back to mermaid layout rather than breaking',
      Object.keys(drawn(svg)).length === 2);
  } catch (e) {
    check('subgraphs fall back to mermaid layout rather than breaking', false,
      String(e?.message ?? e));
  }

  // -------------------------------------------------- partial stored layout
  const partial = `---
x-merpeeps:
  spec: 1
  layout:
    nodes:
      a: { x: 50, y: 50 }
---
flowchart LR
    a[Alpha] --> b[Beta]
`;
  try {
    const { svg } = await renderWithLayout(mermaid, 'p6', partial);
    const nodes = drawn(svg);
    const a = nodes[idFor(nodes, 'a')];
    const b = nodes[idFor(nodes, 'b')];
    const overlap = Math.abs(a.cx - b.cx) < 2 && Math.abs(a.cy - b.cy) < 2;
    check('a partially positioned diagram places the rest without overlap',
      !!a && !!b && !overlap, { a: [a?.cx, a?.cy], b: [b?.cx, b?.cy] });
  } catch (e) {
    check('a partially positioned diagram places the rest without overlap',
      false, String(e?.message ?? e));
  }

  // ------------------------------------------------------------- styling
  const styledSrc = `---
x-merpeeps:
  spec: 1
  layout:
    nodes:
      a: { x: 40, y: 40 }
      b: { x: 360, y: 40 }
  style:
    nodes:
      a:
        fill: '#FFE6CC'
        stroke: '#D79B00'
        strokeWidth: 3
      b:
        fill: '#D5E8D4'
        stroke: '#82B366'
        dashed: true
---
flowchart LR
    a[Alpha] --> b[Beta]
`;

  // Colours are compared as computed values, because a browser normalises
  // '#FFE6CC' to 'rgb(255, 230, 204)' and a string match would miss it.
  const paintOf = (el) => {
    const shape = el.querySelector('rect, circle, ellipse, polygon, path');
    if (!shape) return null;
    const cs = getComputedStyle(shape);
    return {
      fill: cs.fill, stroke: cs.stroke,
      width: cs.strokeWidth, dash: cs.strokeDasharray,
    };
  };

  try {
    const { svg } = await renderWithLayout(mermaid, 'p8', styledSrc);
    stage.innerHTML = svg;
    const nodes = {};
    for (const g of stage.querySelectorAll('g.node')) nodes[g.id] = g;

    const aEl = nodes[Object.keys(nodes).find((k) => /(^|[-_])a([-_]|$)/.test(k))];
    const bEl = nodes[Object.keys(nodes).find((k) => /(^|[-_])b([-_]|$)/.test(k))];
    const a = aEl ? paintOf(aEl) : null;
    const b = bEl ? paintOf(bEl) : null;

    check('stored fill is applied', a?.fill === 'rgb(255, 230, 204)', a);
    check('stored stroke is applied', a?.stroke === 'rgb(215, 155, 0)', a);
    check('stored stroke width is applied', a?.width === '3px', a);
    check('a dashed outline is applied', !!b && b.dash !== 'none' && b.dash !== '', b);
    check('per-node styling is independent',
      a?.fill !== b?.fill, { a: a?.fill, b: b?.fill });
  } catch (e) {
    check('styling is applied', false, String(e?.message ?? e));
  }

  // Styling must survive the fall-through path too, where mermaid does the
  // layout and there is no plugin hook to paint through.
  const styledNoLayout = `---
x-merpeeps:
  spec: 1
  style:
    nodes:
      a: { fill: '#F8CECC', stroke: '#B85450' }
---
flowchart LR
    a[Alpha] --> b[Beta]
`;
  try {
    const { svg } = await renderWithLayout(mermaid, 'p9', styledNoLayout);
    stage.innerHTML = svg;
    const el = [...stage.querySelectorAll('g.node')]
      .find((g) => /(^|[-_])a([-_]|$)/.test(g.id));
    const paint = el ? paintOf(el) : null;
    check('styling applies even with no stored layout',
      paint?.fill === 'rgb(248, 206, 204)', paint);
  } catch (e) {
    check('styling applies even with no stored layout', false,
      String(e?.message ?? e));
  }

  // ------------------------------------------------- the file is not mutated
  try {
    const before = positioned;
    await renderWithLayout(mermaid, 'p7', before);
    check('rendering does not mutate the caller\'s source', before === positioned);
    check('layout selection is never written to the source',
      !positioned.includes('layout: merpeeps'));
  } catch (e) {
    check('rendering does not mutate the caller\'s source', false,
      String(e?.message ?? e));
  }

  return checks;
});

for (const c of results) {
  console.log(`${c.ok ? 'ok  ' : 'FAIL'}  ${c.name}` +
    (c.ok || c.detail === undefined ? '' : `\n        ${JSON.stringify(c.detail)}`));
}
if (pageErrors.length) console.error('page errors:', pageErrors);

await browser.close();
server.close();

const failed = results.filter((c) => !c.ok).length;
console.log(failed === 0
  ? `\nLAYOUT PLUGIN VERIFIED — ${results.length}/${results.length} checks, nodes drawn at stored coordinates`
  : `\nLAYOUT PLUGIN FAILED — ${failed} of ${results.length} checks`);
process.exit(failed === 0 ? 0 : 1);
