#!/usr/bin/env node
/**
 * Round two: the correct `img:` syntax, and whether artwork survives hardened
 * renderer configs.
 *
 * Round one found that `shape: image` is not a shape name — mermaid derives
 * `imageSquare` from a node having an `img` property. It also found that an
 * `<img>` inside a label renders. Both matter for `merpeeps bake`, but only if
 * they survive the settings a cautious host actually uses: GitHub and friends
 * sanitise aggressively, and a capability that evaporates under
 * `securityLevel: 'strict'` or `htmlLabels: false` is not one a portable file
 * can rely on.
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_521;

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
  const stage = document.getElementById('stage');

  const dataUri =
    'data:image/svg+xml;base64,' +
    btoa('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40">' +
         '<circle cx="20" cy="20" r="18" fill="#e91e63"/></svg>');

  const sources = {
    control: `flowchart TD\n  A[Alpha] --> B[Beta]\n`,
    'img attribute': `flowchart TD\n  A@{ img: "${dataUri}", label: "Art" }\n  A --> B[Beta]\n`,
    'img in label': `flowchart TD\n  A["<img src='${dataUri}' width='30'/>"] --> B[Beta]\n`,
  };

  // The configurations a cautious host is likely to impose.
  const configs = {
    'default': {},
    "securityLevel:'strict'": { securityLevel: 'strict' },
    "securityLevel:'loose'": { securityLevel: 'loose' },
    'htmlLabels:false': { flowchart: { htmlLabels: false }, htmlLabels: false },
  };

  const measure = (g) => (g
    ? g.querySelectorAll('image').length * 100
      + g.querySelectorAll('img').length * 10
      + g.querySelectorAll('svg').length
    : -1);

  const out = [];
  let n = 0;

  for (const [cfgName, cfg] of Object.entries(configs)) {
    const row = { config: cfgName, results: {} };
    for (const [srcName, src] of Object.entries(sources)) {
      try {
        m.initialize({ startOnLoad: false, suppressErrorRendering: true, ...cfg });
        const { svg } = await m.render('v' + n++, src);
        stage.innerHTML = svg;
        const nodes = [...stage.querySelectorAll('g.node')];
        const a = nodes.find((g) => /(^|[-_])A([-_]|$)/.test(g.id)) ?? nodes[0];
        row.results[srcName] = { score: measure(a), ok: true };
      } catch (e) {
        row.results[srcName] = {
          score: -1, ok: false,
          err: String(e?.message ?? e).split('\n')[0].slice(0, 60),
        };
      }
    }
    out.push(row);
  }
  return out;
});

await browser.close();
server.close();

const version = JSON.parse(readFileSync(join(here, 'node_modules/mermaid/package.json'))).version;
console.log(`\nmermaid ${version} — does node artwork survive hardened configs?\n`);
console.log('  Each cell compares against that config\'s own plain-node control.\n');
console.log('  ' + 'config'.padEnd(26) + 'img attribute'.padEnd(20) + 'img in label');
console.log('  ' + '-'.repeat(66));

for (const row of rows) {
  const control = row.results.control;
  const cell = (r) => {
    if (!r.ok) return `ERROR(${r.err})`;
    if (!control.ok) return '?';
    return r.score !== control.score ? 'ARTWORK' : 'no artwork';
  };
  console.log('  ' + row.config.padEnd(26)
    + cell(row.results['img attribute']).padEnd(20)
    + cell(row.results['img in label']));
}

console.log('');
const always = ['img attribute', 'img in label'].filter((k) =>
  rows.every((r) => r.results[k].ok && r.results.control.ok
    && r.results[k].score !== r.results.control.score));

console.log(always.length
  ? `Survives every config tested: ${always.join(', ')}`
  : 'Nothing survived every config — artwork is not portable across hosts.');
