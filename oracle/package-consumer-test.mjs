// Outer-loop check for the PUBLISHED ARTEFACT.
//
// The package's own tests import from `../src/`, so they pass even if the
// tarball is misconfigured — a wrong `files` list, a broken `exports` map or a
// missing dependency would all ship silently and only fail for the first person
// who installs it. This packs the package, installs the tarball into a scratch
// directory, and imports it by bare specifier exactly as a consumer would.

import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const PKG = fileURLToPath(new URL('../packages/merpeeps-mermaid-layout', import.meta.url));
const isWindows = process.platform === 'win32';
const npm = isWindows ? 'npm.cmd' : 'npm';

// Node 22+ on Windows refuses to spawn .cmd shims without an explicit shell,
// so npm has to be invoked through one there.
const run = (args, cwd) =>
  execFileSync(npm, args, {
    cwd,
    encoding: 'utf8',
    shell: isWindows,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

const scratch = mkdtempSync(join(tmpdir(), 'merpeeps-consumer-'));
let failures = 0;

try {
  run(['pack', '--pack-destination', scratch], PKG);
  const tarball = readdirSync(scratch).find((f) => f.endsWith('.tgz'));
  if (!tarball) throw new Error('npm pack produced no tarball');

  writeFileSync(
    join(scratch, 'package.json'),
    JSON.stringify({ name: 'consumer', version: '1.0.0', type: 'module', private: true }),
  );
  run(['install', `./${tarball}`, '--no-audit', '--no-fund'], scratch);

  const smoke = `
import { getPositions, setPositions, getStyles, setStyles,
         validate, strip, staleIds } from 'merpeeps-mermaid-layout';
import { registerMerpeepsLayout, renderWithLayout, LAYOUT_NAME } from 'merpeeps-mermaid-layout/mermaid';
import { harvestRenderedGeometry, replayRenderedGeometry, harvestTextGeometry } from 'merpeeps-mermaid-layout/geometry';

const src = \`---
title: Payments
---
flowchart LR
    gw[Gateway] --> db[(Database)]
    gw --> q[[Queue]]
\`;

let bad = 0;
const check = (n, c) => { console.log((c ? 'ok   ' : 'FAIL ') + n); if (!c) bad++; };

const positioned = setPositions(src, {
  gw: { x: 0, y: 0 }, db: { x: 240, y: 0 }, q: { x: 240, y: 120, w: 180 },
});
const styled = setStyles(positioned, {
  gw: { fill: '#FFE6CC', stroke: '#D79B00', icon: 'gateway' },
});

check('positions round-trip', getPositions(styled).db.x === 240);
check('optional width survives', getPositions(styled).q.w === 180);
check('styles round-trip', getStyles(styled).gw.icon === 'gateway');
check('diagram body untouched', strip(styled) === src);
check('title frontmatter preserved', styled.includes('title: Payments'));
check('single frontmatter block',
  styled.split('\\n').filter(l => l.trim() === '---').length === 2);
check('validates clean', validate(styled).ok === true);
check('stale ids reported', staleIds(styled, ['gw', 'db']).join() === 'q');
check('idempotent', setPositions(styled, getPositions(styled)) === styled);

// The mermaid subpath is a separate entry in the exports map, so it can be
// broken independently of the main one.
check('merpeeps-mermaid-layout/mermaid resolves', typeof registerMerpeepsLayout === 'function');
check('renderWithLayout is exported', typeof renderWithLayout === 'function');
check('layout name is stable', LAYOUT_NAME === 'merpeeps');
check('new geometry subpath resolves from the actual tarball',
  [harvestRenderedGeometry, replayRenderedGeometry, harvestTextGeometry].every(fn => typeof fn === 'function'));

// The plugin must not need mermaid at import time — the caller supplies it.
check('plugin registers against a stand-in mermaid', (() => {
  const calls = [];
  registerMerpeepsLayout({ registerLayoutLoaders: (l) => calls.push(l), render: async () => ({ svg: '' }) });
  return calls.length === 1 && calls[0][0].name === 'merpeeps';
})());

process.exit(bad === 0 ? 0 : 1);
`;
  writeFileSync(join(scratch, 'smoke.mjs'), smoke);

  const out = execFileSync(process.execPath, ['smoke.mjs'], {
    cwd: scratch, encoding: 'utf8',
  });
  process.stdout.write(out);
} catch (e) {
  failures++;
  process.stdout.write(e.stdout ?? '');
  process.stderr.write(`\n${e.stderr ?? e.message}\n`);
} finally {
  rmSync(scratch, { recursive: true, force: true });
}

console.log(failures === 0
  ? 'PACKAGE CONSUMER TEST PASSED — the tarball works when installed'
  : 'PACKAGE CONSUMER TEST FAILED — the tarball is not usable as published');
process.exit(failures === 0 ? 0 : 1);
