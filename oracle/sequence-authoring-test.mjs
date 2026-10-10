import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';

const [shell, file] = process.argv.slice(2);
if (!shell || !file) throw new Error('usage: sequence-authoring-test.mjs <validation-shell.html> <authored-sequence.json>');
const cases = JSON.parse(await readFile(file, 'utf8'));
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
      const { svg } = await engine.render(`sequence-${index}`, record.source);
      const upstream = new DOMParser().parseFromString(svg, 'image/svg+xml');
      const native = new DOMParser().parseFromString(record.nativeSvg, 'image/svg+xml');
      results.push({
        text: [...upstream.querySelectorAll('text,foreignObject')].map(n => n.textContent).join(' '),
        nativeText: [...native.querySelectorAll('text')].map(n => n.textContent).join(' '),
        kinds: [...new Set([...native.querySelectorAll('[data-sequence-kind]')].map(n => n.getAttribute('data-sequence-kind')))],
        selfSegments: [...native.querySelectorAll('[data-sequence-owner="self"]')].some(n =>
          n.tagName === 'polyline' && n.getAttribute('points').split(' ').length === 4),
        noGraphNodes: native.querySelectorAll('[data-entity],[data-class-kind]').length === 0,
      });
    }
    return results;
  }, cases);
  for (const result of results) {
    for (const text of ['Client', 'Public API', 'DB', 'Request', 'Lookup', 'Allowed', 'Denied', 'No access', 'Try again', 'Response', 'Finished']) {
      assert.ok(result.text.includes(text), `Upstream sequence lost ${text}`);
      assert.ok(result.nativeText.includes(text), `Native sequence lost ${text}`);
    }
    for (const kind of ['Participant', 'Message', 'Activation', 'Fragment', 'Branch', 'Note'])
      assert.ok(result.kinds.includes(kind), `Native sequence lacks ${kind}`);
    assert.ok(result.selfSegments, 'Self-message lost its multi-segment route.');
    assert.ok(result.noGraphNodes, 'Sequence must not be flattened into entity/class graph nodes.');
  }
  console.log('Actual Mermaid and native SVG retain authored participants, ordered messages, activation, branching, notes and self-connections.');
} finally {
  await browser.close();
}
