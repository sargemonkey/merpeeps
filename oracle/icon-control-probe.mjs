#!/usr/bin/env node
/**
 * Does mermaid draw a real icon for `@{ icon: ... }` without an icon pack?
 *
 * The capability probe said yes, but it looked for nested <svg>/<use>/<image>
 * inside a node — and mermaid may produce those for ordinary nodes too. A
 * positive with no control is not evidence, which this project has already
 * learned the expensive way. So render three variants and compare.
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_519;

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

const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const rows = await page.evaluate(async () => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  const variants = {
    'plain node (control)': 'flowchart TD\n  A[Alpha] --> B[Beta]\n',
    'icon shape, no pack registered':
      'flowchart TD\n  A@{ icon: "fa:fa-user", form: "square", label: "User" }\n  A --> B[Beta]\n',
    'icon shape, nonsense icon name':
      'flowchart TD\n  A@{ icon: "totally:not-a-real-icon", form: "square", label: "User" }\n  A --> B[Beta]\n',
  };

  const out = [];
  let n = 0;
  for (const [name, src] of Object.entries(variants)) {
    const row = { name };
    try {
      const { svg } = await m.render('i' + n++, src);
      stage.innerHTML = svg;
      const nodes = [...stage.querySelectorAll('g.node')];
      const a = nodes.find((g) => /(^|[-_])A([-_]|$)/.test(g.id)) ?? nodes[0];

      row.nestedSvg = a ? a.querySelectorAll('svg').length : 0;
      row.useEls = a ? a.querySelectorAll('use').length : 0;
      row.imageEls = a ? a.querySelectorAll('image').length : 0;
      row.pathEls = a ? a.querySelectorAll('path').length : 0;
      row.iconShapeClass = a ? /icon/i.test(a.getAttribute('class') ?? '') : false;
      row.rendered = true;
    } catch (e) {
      row.rendered = false;
      row.error = String(e?.message ?? e).split('\n')[0].slice(0, 90);
    }
    out.push(row);
  }
  return out;
});

await browser.close();
server.close();

console.log(`\nDoes \`@{ icon: }\` draw a real icon with no pack registered?\n`);
for (const r of rows) {
  console.log(`  ${r.name}`);
  if (!r.rendered) { console.log(`    did not render: ${r.error}\n`); continue; }
  console.log(`    nested <svg>=${r.nestedSvg}  <use>=${r.useEls}  <image>=${r.imageEls}  `
    + `<path>=${r.pathEls}  icon-class=${r.iconShapeClass}\n`);
}

const control = rows[0];
const icon = rows[1];
if (control?.rendered && icon?.rendered) {
  const differs = control.nestedSvg !== icon.nestedSvg
    || control.useEls !== icon.useEls
    || control.imageEls !== icon.imageEls;
  console.log(differs
    ? 'VERDICT: the icon shape produces different markup from a plain node.'
    : 'VERDICT: identical markup to a plain node — no icon was drawn. '
      + 'The earlier "YES" was a false positive; the check matched markup every node has.');
}
