#!/usr/bin/env node
/**
 * Where does mermaid's own `style` statement actually work?
 *
 * The question this answers: mermaid can already express `style B fill:#969` for
 * flowcharts, so does the Merpeeps style layer duplicate something that already
 * exists? The honest answer depends entirely on which diagram types accept it,
 * and that is a question for the renderer, not for the documentation.
 *
 * For each diagram type we render twice — once plain, once with a `style`
 * statement added — and compare the computed fill of the styled element. Three
 * outcomes:
 *   applied  : the style statement changed the paint    -> mermaid covers this
 *   ignored  : parsed fine but nothing changed          -> silently useless
 *   error    : the grammar rejected it outright         -> not expressible
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_517;

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

// Each case: a plain diagram, and the same diagram with a style statement.
const cases = [
  ['flowchart', 'flowchart TD\n  A[One] --> B[Two]\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['stateDiagram-v2', 'stateDiagram-v2\n  A --> B\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['classDiagram', 'classDiagram\n  class A\n  class B\n  A --> B\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['erDiagram', 'erDiagram\n  A ||--o{ B : has\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['sequenceDiagram', 'sequenceDiagram\n  participant A\n  participant B\n  A->>B: hi\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['C4Context', 'C4Context\n  Person(A, "A")\n  System(B, "B")\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['architecture-beta', 'architecture-beta\n  group api(cloud)[API]\n  service db(database)[Database] in api\n  service srv(server)[Server] in api\n  db:L -- R:srv\n', '  style db fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['journey', 'journey\n  title T\n  section S\n    Task: 5: A\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['mindmap', 'mindmap\n  root\n    B\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
  ['block-beta', 'block-beta\n  A B\n', '  style B fill:#996699,stroke:#333,stroke-width:4px\n'],
];

const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const rows = await page.evaluate(async (cases) => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  // Collect every resolved fill in the drawing. Comparing the whole multiset is
  // more robust than guessing which element represents "node B" in ten different
  // renderers, each with its own DOM shape.
  const fillsOf = (svg) => {
    stage.innerHTML = svg;
    return [...stage.querySelectorAll('rect, circle, ellipse, polygon, path')]
      .map((e) => getComputedStyle(e).fill)
      .filter((f) => f && f !== 'none')
      .sort()
      .join('|');
  };

  const out = [];
  let n = 0;
  for (const [name, plain, styleLine] of cases) {
    const row = { name, verdict: '', detail: '' };
    try {
      const { svg: a } = await m.render('p' + n++, plain);
      const before = fillsOf(a);
      try {
        const { svg: b } = await m.render('p' + n++, plain + styleLine);
        const after = fillsOf(b);
        if (before === after) {
          row.verdict = 'ignored';
          row.detail = 'parsed, but nothing repainted';
        } else {
          row.verdict = 'applied';
          row.detail = after.includes('rgb(153, 102, 153)')
            ? 'the requested colour appears'
            : 'paint changed';
        }
      } catch (e) {
        row.verdict = 'error';
        row.detail = String(e?.message ?? e).split('\n')[0].slice(0, 110);
      }
    } catch (e) {
      row.verdict = 'n/a';
      row.detail = 'plain diagram did not render: '
        + String(e?.message ?? e).split('\n')[0].slice(0, 80);
    }
    out.push(row);
  }
  return out;
}, cases);

await browser.close();
server.close();

const pad = (s, n) => String(s).padEnd(n);
console.log(`\nmermaid ${JSON.parse(readFileSync(join(here, 'node_modules/mermaid/package.json'))).version} — does \`style X fill:...\` work?\n`);
console.log(pad('diagram type', 20) + pad('verdict', 10) + 'detail');
console.log('-'.repeat(94));
for (const r of rows) console.log(pad(r.name, 20) + pad(r.verdict, 10) + r.detail);

const applied = rows.filter((r) => r.verdict === 'applied').map((r) => r.name);
console.log(`\nnative styling works on: ${applied.join(', ') || '(none)'}`);
console.log(`unavailable on:          ${rows.filter((r) => r.verdict !== 'applied').map((r) => r.name).join(', ')}`);
