import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';

const [shell, casesFile] = process.argv.slice(2);
if (!shell || !casesFile) throw new Error('usage: class-authoring-test.mjs <validation-shell.html> <authored-cases.json>');
const cases = JSON.parse(await readFile(casesFile, 'utf8'));
assert.equal(cases.length, 2);
const browser = await chromium.launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' });
try {
  const page = await browser.newPage();
  await page.goto(pathToFileURL(resolve(shell)).href);
  await page.waitForFunction(() => window.merpeepsValidation?.ready);
  const results = await page.evaluate(async cases => {
    const engine = globalThis.mermaid ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid?.default
      ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid;
    const records = [];
    for (const [index, value] of cases.entries()) {
      const { svg } = await engine.render(`class-case-${index}`, value.source);
      const document = new DOMParser().parseFromString(svg, 'image/svg+xml');
      const native = new DOMParser().parseFromString(value.nativeSvg, 'image/svg+xml');
      records.push({
        name: value.name,
        text: document.documentElement.textContent,
        nativeText: native.documentElement.textContent,
        nativeCards: native.querySelectorAll('[data-class-kind="Class"]').length,
        nativeInterfaces: native.querySelectorAll('[data-class-kind="Lollipop"] circle').length,
        nativeNotes: native.querySelectorAll('[data-class-kind="Note"]').length,
        nativeRelationships: native.querySelectorAll('[data-class-relationship]').length,
        nativeNamespaces: native.querySelectorAll('[data-namespace]').length,
      });
    }
    return records;
  }, cases);
  for (const result of results) {
    for (const token of ['Customer account', 'String owner', 'save()', 'contains', 'Storage', 'First line', 'Second line']) {
      assert.ok(result.text.includes(token), `${result.name}: upstream lost ${token}`);
      assert.ok(result.nativeText.includes(token), `${result.name}: native SVG lost ${token}`);
    }
    assert.equal(result.nativeCards, 2);
    assert.equal(result.nativeInterfaces, 1);
    assert.equal(result.nativeNotes, 1);
    assert.equal(result.nativeRelationships, 2);
    assert.equal(result.nativeNamespaces, 1);
  }
  console.log('Real Mermaid and native SVG retain authored class cards, members, namespace, relation labels, note and interface for both dialects.');
} finally {
  await browser.close();
}
