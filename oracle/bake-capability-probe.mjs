#!/usr/bin/env node
/**
 * How much of the Merpeeps style vocabulary can be expressed in native mermaid?
 *
 * `merpeeps bake` is only worth building if the mapping is real, so this measures
 * it rather than assuming. Each probe renders a diagram and asks whether the
 * requested paint actually landed — parsing without error is not the same as
 * taking effect, and several of these parse happily and do nothing.
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_518;

const server = createServer((req, res) => {
  if (req.url.startsWith('/mermaid')) {
    res.writeHead(200, { 'content-type': 'text/javascript' });
    res.end(readFileSync(join(here, 'node_modules/mermaid/dist/mermaid.min.js')));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html' });
  res.end('<!DOCTYPE html><html><body><div id="stage"></div>'
    + '<script src="/mermaid.js"></script></body></html>');
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const probes = [
  {
    name: 'fill + stroke + width',
    src: 'flowchart TD\n  A[One] --> B[Two]\n  style B fill:#996699,stroke:#333333,stroke-width:4px\n',
    want: (s) => s.fill === 'rgb(153, 102, 153)' && s.strokeWidth === '4px',
  },
  {
    name: 'dashed outline',
    src: 'flowchart TD\n  A[One] --> B[Two]\n  style B stroke-dasharray:5 3\n',
    want: (s) => s.dash && s.dash !== 'none',
  },
  {
    name: 'text colour',
    src: 'flowchart TD\n  A[One] --> B[Two]\n  style B color:#ff0000\n',
    want: (_s, extra) => extra.labelColour === 'rgb(255, 0, 0)',
  },
  {
    name: 'edge colour via linkStyle',
    src: 'flowchart TD\n  A[One] --> B[Two]\n  linkStyle 0 stroke:#ff0000,stroke-width:3px\n',
    want: (_s, extra) => extra.edgeStroke === 'rgb(255, 0, 0)',
  },
  {
    name: 'native icon shape @{ icon: }',
    src: 'flowchart TD\n  A@{ icon: "fa:fa-user", form: "square", label: "User" }\n  A --> B[Two]\n',
    want: (_s, extra) => extra.hasIconMarkup,
  },
  {
    name: 'classDef + class (shared style)',
    src: 'flowchart TD\n  A[One] --> B[Two]\n  classDef hot fill:#996699\n  class B hot\n',
    want: (s) => s.fill === 'rgb(153, 102, 153)',
  },
  {
    name: 'state diagram: style',
    src: 'stateDiagram-v2\n  A --> B\n  style B fill:#996699,stroke-width:4px\n',
    want: (s) => s.fill === 'rgb(153, 102, 153)',
  },
  {
    name: 'state diagram: classDef + class',
    src: 'stateDiagram-v2\n  A --> B\n  classDef hot fill:#996699\n  class B hot\n',
    want: (s) => s.fill === 'rgb(153, 102, 153)',
  },
];

const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const rows = await page.evaluate(async (probes) => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  const out = [];
  let n = 0;
  for (const p of probes) {
    const row = { name: p.name, ok: false, detail: '' };
    try {
      const { svg } = await m.render('b' + n++, p.src);
      stage.innerHTML = svg;

      // Find the node whose label is "Two" (or state B) and read its paint.
      const nodes = [...stage.querySelectorAll('g.node')];
      const target = nodes.find((g) => /(^|[-_])B([-_]|$)/.test(g.id)) ?? nodes[nodes.length - 1];
      const shape = target?.querySelector('rect, circle, ellipse, polygon, path');
      const cs = shape ? getComputedStyle(shape) : null;

      const style = cs
        ? { fill: cs.fill, stroke: cs.stroke, strokeWidth: cs.strokeWidth, dash: cs.strokeDasharray }
        : {};

      const label = target?.querySelector('.nodeLabel, .label, foreignObject span, text');
      const edge = stage.querySelector('g.edgePaths path, path.flowchart-link, .edge-thickness-normal');

      const extra = {
        labelColour: label ? getComputedStyle(label).color : null,
        edgeStroke: edge ? getComputedStyle(edge).stroke : null,
        hasIconMarkup: !!stage.querySelector('g.node svg, g.node use, g.node image, .icon-shape'),
      };

      row.ok = !!p.want && new Function('return ' + p.want)()(style, extra);
      row.detail = JSON.stringify({ ...style, ...extra }).slice(0, 150);
    } catch (e) {
      row.detail = 'ERROR ' + String(e?.message ?? e).split('\n')[0].slice(0, 110);
    }
    out.push(row);
  }
  return out;
}, probes.map((p) => ({ ...p, want: p.want.toString() })));

await browser.close();
server.close();

const pad = (s, n) => String(s).padEnd(n);
const version = JSON.parse(readFileSync(join(here, 'node_modules/mermaid/package.json'))).version;
console.log(`\nmermaid ${version} — what can \`merpeeps bake\` emit natively?\n`);
console.log(pad('capability', 30) + pad('native?', 10) + 'observed');
console.log('-'.repeat(110));
for (const r of rows) console.log(pad(r.name, 30) + pad(r.ok ? 'YES' : 'no', 10) + r.detail);
console.log('');
