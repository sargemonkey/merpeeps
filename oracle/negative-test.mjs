// Negative test: deliberately break Merpeeps and prove BOTH loops catch it.
//
// A green suite is worthless unless it can go red. This induces two real
// violations and asserts the oracle reports them.
//
//   1. leak       — write metadata into the diagram body (user-visible)
//   2. bodyDamage — mutate the body while injecting
//
// Exits 0 when both violations are DETECTED (i.e. the harness has teeth).

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.MERPEEPS_NEG_PORT ?? 8898);
const MIME = { '.html': 'text/html', '.mjs': 'text/javascript',
               '.js': 'text/javascript', '.json': 'application/json' };

const server = await new Promise((resolve) => {
  const s = createServer(async (req, res) => {
    try {
      const rel = normalize(decodeURIComponent(new URL(req.url,
        `http://localhost:${PORT}`).pathname)).replace(/^([/\\])+/, '');
      const file = join(ROOT, rel);
      if (!file.startsWith(ROOT)) return res.writeHead(403).end();
      res.writeHead(200, { 'Content-Type': MIME[extname(file)] ?? 'text/plain',
                           'Cache-Control': 'no-store' })
         .end(await readFile(file));
    } catch { if (!res.headersSent) res.writeHead(404); res.end(); }
  });
  s.listen(PORT, '127.0.0.1', () => resolve(s));
});

const { chromium } = await import('playwright');
const browser = await chromium
  .launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' })
  .catch(() => chromium.launch());
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
await page.goto(`http://127.0.0.1:${PORT}/conformance.html`, { waitUntil: 'load' });
await page.waitForFunction(() => window.__CONF__, null, { timeout: 120_000 });

const findings = await page.evaluate(async () => {
  const mermaid = (await import('./node_modules/mermaid/dist/mermaid.esm.mjs')).default;
  const { inject, bodyOf } = await import('./merpeeps.mjs');
  const samples = await (await fetch('./samples.json')).json();
  mermaid.initialize({ startOnLoad: false, suppressErrorRendering: true });

  const base = samples.flowchart;
  const out = { mutationsWereReal: {} };

  // --- violation 1: metadata leaks into the rendered, user-visible surface.
  // Append rather than pattern-replace: a replace that silently matches
  // nothing would make this test vacuously "pass".
  const leaky = base.replace(/\s*$/, '') + '\n    LEAKCANARY[LEAKCANARY]\n';
  out.mutationsWereReal.leak = leaky !== base;

  const r = await mermaid.render('leak', leaky);
  const doc = new DOMParser().parseFromString(r.svg, 'image/svg+xml');
  // Mermaid emits labels into <foreignObject><span> when htmlLabels is on
  // (the default), so a <text>-only selector would miss real leaks.
  const text = [...doc.querySelectorAll('text,tspan,span,p,div,title')]
    .map(n => n.textContent).join(' ');
  out.renderedTextSample = text.slice(0, 120);
  out.leakDetected = text.includes('LEAKCANARY');

  // --- violation 2: body damaged during injection
  const good = inject(base, { spec: 1 });
  const damaged = good.replace(/\s*$/, '') + '\n    EXTRA_NODE[Damage]\n';
  out.mutationsWereReal.bodyDamage = damaged !== good;
  out.bodyDamageDetected = bodyOf(damaged) !== bodyOf(base);

  // --- control: an untouched injection must NOT be flagged
  out.cleanInjectionIsClean = bodyOf(good) === bodyOf(base);

  return out;
});

console.log(JSON.stringify(findings, null, 2));
await browser.close();
server.close();

const mutated = findings.mutationsWereReal.leak && findings.mutationsWereReal.bodyDamage;
const ok = mutated
  && findings.leakDetected
  && findings.bodyDamageDetected
  && findings.cleanInjectionIsClean;

if (!mutated) {
  console.error('NEGATIVE TEST INVALID — the induced mutations were no-ops, ' +
                'so nothing was actually tested.');
}
console.log(ok
  ? 'NEGATIVE TEST PASSED — harness detects induced violations and stays quiet on clean input'
  : 'NEGATIVE TEST FAILED — harness is blind to real violations');
process.exit(ok ? 0 : 1);
