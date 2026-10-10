import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';

const [shell, report] = process.argv.slice(2);
if (!shell) throw new Error('usage: node validation-page-test.mjs <packaged-shell.html> [results.json]');
const samples = JSON.parse(await readFile(new URL('./samples.json', import.meta.url), 'utf8'));
const browser = await chromium.launch(
  process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' });
try {
  const page = await browser.newPage();
  const network = [];
  const errors = [];
  const responses = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**', route => {
    const url = route.request().url();
    if (url.startsWith('file:') || url.startsWith('data:') || url.startsWith('blob:'))
      return route.continue();
    network.push(url);
    return route.abort();
  });
  await page.goto(pathToFileURL(resolve(shell)).href, { waitUntil: 'load' });
  await page.waitForFunction(() => window.merpeepsValidation?.ready);
  assert.equal(await page.evaluate(() => window.merpeepsValidation.version), '12.0.0');
  assert.equal(await page.evaluate(() => window.merpeepsValidation.available), true);

  const take = async id => {
    const result = await (await page.waitForFunction(id => {
      const response = window.merpeepsValidation.take(id);
      return response.status === 'pending' ? false : response;
    }, id)).jsonValue();
    assert.equal(result.requestId, id);
    assert.equal(result.mermaidVersion, '12.0.0');
    responses.push(result);
    return result;
  };
  const validate = async (id, source, expected) => {
    await page.evaluate(([id, source]) => window.merpeepsValidation.validate(id, source), [id, source]);
    const result = await take(id);
    assert.equal(result.status, expected, `${id}: ${JSON.stringify(result)}`);
    if (expected === 'valid') {
      assert.ok(result.diagramType);
      assert.deepEqual(result.diagnostics, []);
    } else {
      assert.ok(result.diagnostics[0]?.message);
    }
    return result;
  };

  for (const [name, source] of Object.entries(samples)) {
    await validate(`fixture-${name}`, source, 'valid');
  }
  assert.equal(await page.evaluate(() => window.merpeepsValidation.zenUmlVersion), '3.47.9');
  for (const [name, source] of Object.entries({
    flowchart: 'flowchart TD\n a[Unclosed\n',
    sequence: 'sequenceDiagram\n Alice->>Bob:\n loop\n',
    class: 'classDiagram\n class Animal {\n',
    state: 'stateDiagram-v2\n state Outer {\n',
    pie: 'pie\n "Broken": not-a-number\n',
    zenuml: 'zenuml\n Alice.call() {\n',
  })) {
    await validate(`invalid-${name}`, source, 'invalid');
  }
  await validate('unknown', 'unknownFutureDiagram\n value\n', 'unsupported');
  await validate('blank-flowchart', 'flowchart TD\n', 'valid');
  await validate('blank-state', 'stateDiagram-v2\n', 'valid');
  await validate('frontmatter', '---\ntitle: Name\nconfig:\n  theme: dark\n---\nflowchart LR\n A-->B', 'valid');
  await validate('script-like-label', 'flowchart TD\n A["&lt;/script&gt;"] --> B["quoted label"]', 'valid');
  await validate('zenuml-nested', 'zenuml\nAlice.call() {\n if(ok) {\n Bob.check()\n }\n}', 'valid');
  await validate('zenuml-frontmatter', '---\ntitle: Example\n---\nzenuml\nAlice->Bob: Hello', 'valid');
  const zenError = await validate('zenuml-close', 'zenuml\nAlice.call()\n}', 'invalid');
  assert.ok(zenError.diagnostics[0].parserLine > 0);
  assert.ok(zenError.diagnostics[0].parserColumn > 0);
  await validate('zenuml-after-error', 'zenuml\nAlice->Bob: Recovered', 'valid');

  await page.evaluate(async () => {
    const engine = globalThis.mermaid
      ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid?.default
      ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid;
    const diagram = await engine.mermaidAPI.getDiagramFromText('zenuml\nAlice.call()');
    const parser = diagram.getParser();
    window.zenumlParserControl = { parser, parse: parser.parse };
    parser.parse = () => {};
  });
  try {
    await validate('control-dummy-zenuml-parser', 'zenuml\nAlice.call() {', 'valid');
  } finally {
    await page.evaluate(() => {
      const { parser, parse } = window.zenumlParserControl;
      parser.parse = parse;
      delete window.zenumlParserControl;
    });
  }
  await validate('control-restored-zenuml-parser', 'zenuml\nAlice.call() {', 'invalid');

  await page.evaluate(() => {
    window.merpeepsValidation.validate('cancelled', 'flowchart TD\nA-->B');
    window.merpeepsValidation.cancel('cancelled');
    window.merpeepsValidation.validate('queued-valid', 'sequenceDiagram\nA->>B: Message');
    window.merpeepsValidation.validate('queued-invalid', 'pie\n"Broken": text');
  });
  assert.equal((await take('queued-valid')).status, 'valid');
  assert.equal((await take('queued-invalid')).status, 'invalid');
  assert.equal(await page.evaluate(() => {
    try { window.merpeepsValidation.take('cancelled'); return false; }
    catch (error) { return error.message.includes('Unknown or consumed'); }
  }), true);
  assert.equal(await page.evaluate(() => {
    try { window.merpeepsValidation.take('queued-valid'); return false; }
    catch (error) { return error.message.includes('Unknown or consumed'); }
  }), true);
  assert.equal(await page.evaluate(() => {
    window.merpeepsValidation.validate('duplicate', 'flowchart TD');
    try { window.merpeepsValidation.validate('duplicate', 'stateDiagram-v2'); return false; }
    catch (error) { return error.message.includes('already in use'); }
    finally { window.merpeepsValidation.cancel('duplicate'); }
  }), true);
  const capacity = await page.evaluate(() => {
    for (let i = 0; i < 32; i++)
      window.merpeepsValidation.validate(`capacity-${i}`, 'flowchart TD');
    let rejected = false;
    try { window.merpeepsValidation.validate('capacity-overflow', 'flowchart TD'); }
    catch (error) { rejected = error.message.includes('Consume or cancel'); }
    finally {
      for (let i = 0; i < 32; i++) window.merpeepsValidation.cancel(`capacity-${i}`);
    }
    return rejected;
  });
  assert.equal(capacity, true, 'unconsumed requests must remain bounded');
  await validate('capacity-recovered', 'zenuml\nAlice.call()', 'valid');
  assert.deepEqual(network, [], 'offline validation must not fetch content');
  assert.deepEqual(errors, [], 'protocol errors must not escape as page exceptions');
  if (report) await writeFile(report, JSON.stringify(responses, null, 2));
  console.log(`Offline Mermaid 12 validation: ${responses.length} workflow results; all 33 fixtures, invalid syntax, dummy-parser negative control, bounded requests, cancellation and zero network verified.`);
} finally {
  await browser.close();
}
