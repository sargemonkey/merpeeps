import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';

const [shell, casesFile] = process.argv.slice(2);
if (!shell || !casesFile) throw new Error('usage: er-authoring-test.mjs <validation-shell.html> <authored-er.json>');
const cases = JSON.parse(await readFile(casesFile, 'utf8'));
const browser = await chromium.launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' });
try {
  const page = await browser.newPage();
  await page.goto(pathToFileURL(resolve(shell)).href);
  await page.waitForFunction(() => window.merpeepsValidation?.ready);
  const results = await page.evaluate(async cases => {
    const engine = globalThis.mermaid ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid?.default
      ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid;
    const results = [];
    for (const [index, record] of cases.entries()) {
      const { svg } = await engine.render(`er-${index}`, record.source);
      const upstream = new DOMParser().parseFromString(svg, 'image/svg+xml');
      const native = new DOMParser().parseFromString(record.nativeSvg, 'image/svg+xml');
      results.push({
        upstream: [...upstream.querySelectorAll('text,foreignObject')].map(node => node.textContent).join(' '),
        native: native.documentElement.textContent,
        entities: native.querySelectorAll('[data-entity]').length,
        attributes: native.querySelectorAll('[data-attribute]').length,
        groups: native.querySelectorAll('[data-er-subgraph]').length,
        edges: native.querySelectorAll('[data-er-relationship]').length,
        circles: native.querySelectorAll('circle').length,
        self: [...native.querySelectorAll('[data-er-relationship] polyline')].some(line =>
          line.getAttribute('points').split(' ').length > 3),
      });
    }
    return results;
  }, cases);
  assert.ok(results.length > 0);
  for (const result of results) {
    for (const token of ['Customer', 'account', 'ORDER', 'email', 'string?', 'PK', 'UK', 'Primary email', 'places', 'refers'])
      assert.ok(result.upstream.includes(token) && result.native.includes(token),
        `An ER renderer lost ${token}: ${JSON.stringify({ upstream: result.upstream, native: result.native })}`);
    assert.equal(result.entities, 2);
    assert.equal(result.attributes, 2);
    assert.equal(result.groups, 2);
    assert.equal(result.edges, 3);
    assert.ok(result.circles >= 2);
    assert.ok(result.self, 'Self-relationship must have a visible nondegenerate route.');
  }
  console.log('Authored ER source renders in real Mermaid and native SVG with typed attributes, keys, groups, cardinalities and a self-loop.');
} finally {
  await browser.close();
}
