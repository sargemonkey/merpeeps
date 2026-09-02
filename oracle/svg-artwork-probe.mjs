#!/usr/bin/env node
/**
 * Can a mermaid node display arbitrary SVG artwork, with nothing registered?
 *
 * This decides how much `merpeeps bake` can carry. Merpeeps draws icons; mermaid has
 * `icon`, `iconSquare`, `image` and `imageSquare` shapes. If any of them renders
 * artwork from the file alone — no host-side `registerIconPacks()` call — then
 * baked icons survive on GitHub. If they all need registration, icons can never
 * cross into a plugin-free file, and the bake must say so.
 *
 * Every positive is checked against a plain-node control, because "found an
 * <image> element" means nothing until you know a plain node has none.
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_520;

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
const page = await browser.newPage({ viewport: { width: 1000, height: 800 } });
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const rows = await page.evaluate(async () => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  // A tiny self-contained SVG as a data URI: no network, no registration.
  const dataUri =
    'data:image/svg+xml;base64,' +
    btoa('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40">' +
         '<circle cx="20" cy="20" r="18" fill="#e91e63"/></svg>');

  const variants = {
    'plain node (control)':
      `flowchart TD\n  A[Alpha] --> B[Beta]\n`,
    'shape: image with data URI':
      `flowchart TD\n  A@{ shape: image, img: "${dataUri}", label: "Art" }\n  A --> B[Beta]\n`,
    'shape: imageSquare with data URI':
      `flowchart TD\n  A@{ shape: imageSquare, img: "${dataUri}", label: "Art" }\n  A --> B[Beta]\n`,
    'shape: icon without a pack':
      `flowchart TD\n  A@{ shape: icon, icon: "fa:fa-user", label: "User" }\n  A --> B[Beta]\n`,
    'raw <img> in a label (htmlLabels)':
      `flowchart TD\n  A["<img src='${dataUri}' width='30'/>"] --> B[Beta]\n`,
  };

  const measure = (g) => ({
    image: g ? g.querySelectorAll('image').length : 0,
    img: g ? g.querySelectorAll('img').length : 0,
    svg: g ? g.querySelectorAll('svg').length : 0,
    use: g ? g.querySelectorAll('use').length : 0,
  });

  const out = [];
  let n = 0;
  for (const [name, src] of Object.entries(variants)) {
    const row = { name };
    try {
      const { svg } = await m.render('s' + n++, src);
      stage.innerHTML = svg;
      const nodes = [...stage.querySelectorAll('g.node')];
      const a = nodes.find((g) => /(^|[-_])A([-_]|$)/.test(g.id)) ?? nodes[0];
      row.counts = measure(a);
      row.rendered = true;
    } catch (e) {
      row.rendered = false;
      row.error = String(e?.message ?? e).split('\n')[0].slice(0, 100);
    }
    out.push(row);
  }
  return out;
});

await browser.close();
server.close();

const version = JSON.parse(readFileSync(join(here, 'node_modules/mermaid/package.json'))).version;
console.log(`\nmermaid ${version} — can a node show artwork with nothing registered?\n`);

const control = rows[0];
const fmt = (c) => `<image>=${c.image} <img>=${c.img} <svg>=${c.svg} <use>=${c.use}`;

for (const r of rows) {
  if (!r.rendered) {
    console.log(`  ${r.name.padEnd(36)} ERROR  ${r.error}`);
    continue;
  }
  const differs = control.rendered &&
    JSON.stringify(r.counts) !== JSON.stringify(control.counts);
  const verdict = r === control ? 'control' : (differs ? 'ARTWORK' : 'same as plain');
  console.log(`  ${r.name.padEnd(36)} ${verdict.padEnd(14)} ${fmt(r.counts)}`);
}

const winners = rows.slice(1).filter(
  (r) => r.rendered && control.rendered &&
    JSON.stringify(r.counts) !== JSON.stringify(control.counts));

console.log('');
console.log(winners.length
  ? `VERDICT: artwork renders unaided via — ${winners.map((w) => w.name).join('; ')}`
  : 'VERDICT: no variant produced artwork without host-side registration.');
