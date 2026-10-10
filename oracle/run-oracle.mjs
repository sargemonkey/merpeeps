// Outer loop: run the Merpeeps conformance suite against REAL mermaid.js.
//
// mermaid.js is the oracle. Only it can answer the question the whole format
// thesis rests on: "does a stock renderer still produce the same output?"
// A browser is required because mermaid needs real SVG layout (getBBox) and
// constructable stylesheets — jsdom cannot provide either.
//
// Emits JSON on stdout and exits non-zero on any violation.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.MERPEEPS_ORACLE_PORT ?? 8899);

const MIME = {
  '.html': 'text/html',
  '.mjs': 'text/javascript',
  '.js': 'text/javascript',
  '.json': 'application/json',
  '.map': 'application/json',
};

function serve() {
  const server = createServer(async (req, res) => {
    try {
      const url = new URL(req.url, `http://localhost:${PORT}`);
      const rel = normalize(decodeURIComponent(url.pathname)).replace(/^([/\\])+/, '');
      const file = join(ROOT, rel);
      if (!file.startsWith(ROOT)) { res.writeHead(403).end('forbidden'); return; }
      const body = await readFile(file);
      res.writeHead(200, {
        'Content-Type': MIME[extname(file)] ?? 'application/octet-stream',
        'Cache-Control': 'no-store',
      }).end(body);
    } catch {
      res.writeHead(404).end('not found');
    }
  });
  return new Promise((resolve) =>
    server.listen(PORT, '127.0.0.1', () => resolve(server)));
}

async function main() {
  const server = await serve();
  let browser;
  let exitCode = 1;

  try {
    const { chromium } = await import('playwright');
    // Prefer an already-installed Chrome so CI need not download a browser.
    const launch = process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled'
      ? {}
      : { channel: process.env.MERPEEPS_ORACLE_CHANNEL || 'chrome' };

    browser = await chromium.launch(launch).catch(() => chromium.launch());

    // A real viewport matters: zero-size contexts silently reduce how many
    // diagram types can lay out, which would quietly shrink the test surface.
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));

    await page.goto(`http://127.0.0.1:${PORT}/conformance.html`,
                    { waitUntil: 'load' });

    const result = await page.waitForFunction(() => window.__CONF__, null,
      { timeout: 120_000 }).then((h) => h.jsonValue());

    // Guard against silent shrinkage of the tested surface.
    const MIN_RENDERABLE = Number(process.env.MERPEEPS_MIN_RENDERABLE ?? 32);
    const shrunk = result.renderable < MIN_RENDERABLE;
    const unexpectedUnavailable = result.notRenderable.filter(type => type !== 'zenuml');

    const report = {
      ...result,
      pageErrors: errors,
      minRenderableExpected: MIN_RENDERABLE,
      surfaceShrunk: shrunk,
      unexpectedUnavailable,
    };
    console.log(JSON.stringify(report, null, 2));

    exitCode = (result.verdict === 'CONFORMANT' && !shrunk &&
      unexpectedUnavailable.length === 0) ? 0 : 1;
    if (shrunk) {
      console.error(
        `oracle: only ${result.renderable} diagram types rendered, expected ` +
        `>= ${MIN_RENDERABLE}. The suite is testing less than it should.`);
    }
  } catch (err) {
    console.error('oracle: ' + (err?.stack || err));
    exitCode = 1;
  } finally {
    await browser?.close();
    server.close();
  }
  process.exit(exitCode);
}

main();
