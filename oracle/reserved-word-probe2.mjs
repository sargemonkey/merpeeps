#!/usr/bin/env node
/**
 * Round two on reserved words — the forms that actually bite.
 *
 * The first probe used a labelled id (`end[Done]`) and everything parsed, which
 * would have made it easy to declare the benchmark wrong. That would have been
 * sloppy: mermaid's known `end` problem is narrower than "reserved word as id",
 * and testing the easy form and stopping is how a real defect gets waved away.
 *
 * So this tries the forms where it is reported to break: a bare unlabelled id,
 * and an id inside a subgraph (where `end` also terminates the block).
 */
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const here = import.meta.dirname;
const PORT = 39_524;

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

const cases = {
  'bare, unlabelled':        'flowchart TD\n    start --> end\n',
  'bare, unlabelled (class)': 'flowchart TD\n    start --> class\n',
  'labelled':                'flowchart TD\n    start --> end[Done]\n',
  'inside a subgraph':       'flowchart TD\n    subgraph g [Group]\n        a --> end[Done]\n    end\n',
  'subgraph, unlabelled':    'flowchart TD\n    subgraph g [Group]\n        a --> end\n    end\n',
  'as edge source':          'flowchart TD\n    end[Done] --> after[After]\n',
  'quoted with label':       'flowchart TD\n    start --> end["Done"]\n',

  // Candidate escapes that would let a writer keep an id recognisably intact.
  'escape: trailing _':      'flowchart TD\n    start --> end_[Done]\n',
  'escape: uppercase':       'flowchart TD\n    start --> END[Done]\n',
  'escape: capitalised':     'flowchart TD\n    start --> End[Done]\n',
  'escape: prefix n_':       'flowchart TD\n    start --> n_end[Done]\n',

  'control: normal id':      'flowchart TD\n    start --> finish[Done]\n',
};

const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });

const rows = await page.evaluate(async (cases) => {
  const m = globalThis.mermaid;
  m.initialize({ startOnLoad: false, suppressErrorRendering: true });
  const stage = document.getElementById('stage');

  const out = [];
  let n = 0;
  for (const [name, src] of Object.entries(cases)) {
    const row = { name };
    try {
      await m.parse(src);
      row.parse = 'ok';
      // Parsing is not the whole story: a diagram can parse and still draw the
      // wrong thing, which is a worse outcome than a clean failure.
      const { svg } = await m.render('r' + n++, src);
      stage.innerHTML = svg;
      const nodes = [...stage.querySelectorAll('g.node')];
      row.nodes = nodes.length;
      row.ids = nodes.map((g) => (g.id.match(/flowchart-([^-]+)-/) ?? [])[1] ?? g.id).join(',');
    } catch (e) {
      row.parse = 'FAILS';
      row.nodes = 0;
      row.ids = String(e?.message ?? e).split('\n')[0].slice(0, 60);
    }
    out.push(row);
  }
  return out;
}, cases);

await browser.close();
server.close();

const version = JSON.parse(readFileSync(join(here, 'node_modules/mermaid/package.json'))).version;
console.log(`\nmermaid ${version} — where does a reserved-word id actually break?\n`);
console.log('  ' + 'case'.padEnd(26) + 'parse'.padEnd(8) + 'nodes'.padEnd(7) + 'ids drawn / error');
console.log('  ' + '-'.repeat(100));
for (const r of rows) {
  console.log('  ' + r.name.padEnd(26) + r.parse.padEnd(8)
    + String(r.nodes).padEnd(7) + r.ids);
}
console.log('');
