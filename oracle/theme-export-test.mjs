import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';
import { inject, parse } from '../packages/merpeeps-mermaid-layout/src/index.js';

const repo = resolve(import.meta.dirname, '..');
const cli = process.env.MERPEEPS_TEST_CLI ?? join(repo, 'src', 'Merpeeps.Cli', 'bin', 'Release', 'net10.0', 'merpeeps.dll');
assert.ok(existsSync(cli), 'Build the CLI before running theme/export acceptance.');
const work = mkdtempSync(join(tmpdir(), 'merpie-theme-export-'));
const run = (...args) => execFileSync('dotnet', [cli, ...args], { stdio: ['ignore', 'ignore', 'pipe'] });
const browser = await chromium.launch(process.env.MERPEEPS_ORACLE_CHANNEL === 'bundled' ? {} : { channel: 'chrome' });
const errors = [], network = [];
const page = await browser.newPage({ viewport: { width: 1800, height: 1200 } });
page.on('pageerror', error => errors.push(error.message));
await page.route('**', route => {
  if (/^(file|blob|data):/.test(route.request().url())) return route.continue();
  network.push(route.request().url());
  return route.abort();
});

async function preview(file, dark, transparent) {
  const html = join(work, 'preview.html');
  run('preview', file, '--out', html, ...(dark ? ['--dark'] : []), ...(transparent ? ['--transparent'] : []));
  await page.goto(pathToFileURL(html).href);
  await page.waitForFunction(() => window.merpeepsRendered === true, { timeout: 60000 });
  assert.equal(await page.evaluate(() => window.merpeepsError ?? null), null);
}

async function alphaFromPng(png) {
  return page.evaluate(async base64 => {
    const image = await createImageBitmap(await (await fetch('data:image/png;base64,' + base64)).blob());
    const canvas = document.createElement('canvas');
    canvas.width = image.width; canvas.height = image.height;
    const context = canvas.getContext('2d');
    context.drawImage(image, 0, 0);
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
    const transparent = pixels.filter((v, i) => i % 4 === 3 && v === 0).length;
    return { corner: pixels[3], transparent, opaque: pixels.filter((v, i) => i % 4 === 3 && v === 255).length };
  }, png.toString('base64'));
}

try {
  const seed = join(work, 'seed.mmd');
  writeFileSync(seed, '---\ntitle: Theme acceptance\n---\nflowchart LR\nsubgraph Outer[Outer]\nsubgraph Inner[Inner]\nA[Alpha] -->|Reads| B[Beta]\nend\nend\nB -.->|Returns| C[Gamma]\n');
  await preview(seed, false, false);
  const geometry = JSON.parse(await page.evaluate(() => window.merpeepsHarvestGeometry()));
  geometry.groups.Outer.fill = '#FFFFFF';
  geometry.groups.Inner.fill = '#00000010';
  const styles = { A: { fill: '#FFFFFF80' }, B: { fill: '#EFF6FF' }, C: { fill: '#222222', text: '#FAFAFA' } };
  const source = readFileSync(seed, 'utf8');
  const cases = [{ offset: 0, fill: '#00000010' }, { offset: 700, fill: '#00000010' }, { offset: 0, fill: '#7D7D7D' }].map(({ offset, fill }, index) => {
    const file = join(work, 'synthetic-' + index + '.mmd');
    const rendered = structuredClone(geometry);
    rendered.groups.Inner.fill = fill;
    writeFileSync(file, inject(source, {
      spec: 1,
      layout: { rendered, nodes: Object.fromEntries(Object.entries(geometry.nodes)
        .map(([id, n]) => [id, { x: n.x + offset, y: n.y + offset, w: n.w, h: n.h }])) },
      style: { nodes: styles },
    }));
    return file;
  });
  cases.push(...process.argv.slice(2).map(file => resolve(file)));

  for (const file of cases) {
    const before = readFileSync(file, 'utf8'), metadata = parse(before), snapshot = metadata.layout.rendered;
    for (const dark of [false, true]) for (const transparent of [false, true]) {
      await preview(file, dark, transparent);
      const contrast = await page.evaluate(({ snapshot, styles }) => {
        const svg = document.querySelector('#stage svg');
        const rgba = css => {
          const span = document.createElement('span');
          span.style.color = css; document.body.append(span);
          const c = getComputedStyle(span).color.match(/rgba?\(([^)]+)\)/)[1].split(',').map(Number);
          span.remove(); return [c[0], c[1], c[2], c[3] ?? 1];
        };
        const composite = (a, b) => a.slice(0, 3).map((v, i) => Math.round(v * a[3] + b[i] * (1 - a[3]))).concat(1);
        const luminance = color => color.slice(0, 3).map(v => v / 255)
          .map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4)
          .reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
        const ratio = (a, b) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05);
        const groups = [...svg.querySelectorAll('g.cluster')].map(group => {
          const rect = group.querySelector(':scope > rect');
          return { bounds: rect.getBBox(), fill: rgba(getComputedStyle(rect).fill) };
        }).sort((a, b) => b.bounds.width * b.bounds.height - a.bounds.width * a.bounds.height);
        const surfaceAt = (x, y) => groups.filter(({ bounds: r }) =>
          x >= r.x && y >= r.y && x <= r.x + r.width && y <= r.y + r.height)
          .reduce((surface, group) => composite(group.fill, surface), rgba(window.merpeepsCanvasColor));
        const inherited = [...Object.values(snapshot.groups).map(g => g.label), ...Object.values(snapshot.edges).map(e => e.label), snapshot.caption?.label]
          .filter(Boolean).flatMap(label => label.lines.map(line => ({ text: line.text, background: label.background })));
        const ratios = [];
        for (const label of inherited) {
          const text = [...svg.querySelectorAll('text')].find(t => t.textContent === label.text);
          if (!text) throw new Error('Missing inherited label: ' + label.text);
          const bounds = text.getBBox();
          let surface = surfaceAt(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
          if (label.background) surface = composite(rgba(label.background), surface);
          ratios.push(ratio(composite(rgba(getComputedStyle(text).fill), surface), surface));
        }
        for (const [id, node] of Object.entries(snapshot.nodes)) {
          const group = [...svg.querySelectorAll('g.node')].find(g =>
            g.id.replace(/^merpeeps-preview-flowchart-/, '').replace(/-\d+$/, '') === id);
          if (!group) throw new Error('Missing node: ' + id);
          const fill = rgba(getComputedStyle(group.querySelector('rect, circle, ellipse, polygon, path')).fill);
          for (const line of node.label?.lines ?? []) {
            const text = [...svg.querySelectorAll('text')].find(t => t.textContent === line.text);
            if (!text) throw new Error('Missing node label: ' + line.text);
            const ink = rgba(getComputedStyle(text).fill);
            if (styles[id]?.text) {
              if (JSON.stringify(ink) !== JSON.stringify(rgba(styles[id].text))) throw new Error('Authored node ink changed: ' + id);
              continue;
            }
            const bounds = text.getBBox();
            let surface = composite(fill, surfaceAt(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2));
            if (node.label.background) surface = composite(rgba(node.label.background), surface);
            ratios.push(ratio(composite(ink, surface), surface));
          }
        }
        for (const path of svg.querySelectorAll('path[data-edge="true"]')) {
          const halo = path.previousElementSibling?.hasAttribute('data-contrast-halo') ? path.previousElementSibling : null;
          if (halo && getComputedStyle(halo).strokeDasharray !== getComputedStyle(path).strokeDasharray)
            throw new Error('Contrast halo changed the connector dash pattern.');
          for (const attribute of ['marker-start', 'marker-end']) {
            const id = path.getAttribute(attribute)?.match(/#([^)]*)/)?.[1];
            if (!id) continue;
            const endpoint = path.getPointAtLength(attribute === 'marker-start' ? 0 : path.getTotalLength());
            const surface = surfaceAt(endpoint.x, endpoint.y);
            for (const shape of svg.querySelector(`[id="${id}"]`).querySelectorAll('path,polygon,circle')) {
              const style = getComputedStyle(shape), ink = style.fill === 'none' ? style.stroke : style.fill;
              if (ink !== 'none' && ratio(composite(rgba(ink), surface), surface) < 3)
                throw new Error('Inherited arrowhead is unreadable at its endpoint.');
            }
          }
        }
        return { labels: ratios.length, minimum: Math.min(...ratios), edges: svg.querySelectorAll('path[data-edge="true"]').length };
      }, { snapshot, styles: metadata.style?.nodes ?? {} });
      assert.ok(contrast.labels > 0 && contrast.minimum >= 4.5, JSON.stringify({ file: basename(file), dark, contrast }));
      assert.equal(contrast.edges, Object.keys(snapshot.edges).length, 'Halos must not duplicate semantic routes.');
      const harvested = JSON.parse(await page.evaluate(() => window.merpeepsHarvestGeometry()));
      assert.equal(Object.keys(harvested.edges).length, Object.keys(snapshot.edges).length);
      for (const [id, node] of Object.entries(snapshot.nodes)) {
        const position = metadata.layout.nodes[id] ?? node;
        for (const key of ['x', 'y', 'w', 'h'])
          assert.ok(Math.abs(harvested.nodes[id][key] - position[key]) <= .02, `Geometry drift ${id}.${key}`);
      }
      const alpha = await alphaFromPng(await page.screenshot({ omitBackground: true }));
      assert.equal(alpha.corner, transparent ? 0 : 255, 'Preview outer canvas alpha');
      assert.ok(alpha.opaque > 0, 'Authored diagram fills must remain visible.');
      if (transparent) assert.ok(alpha.transparent > 0);

      const svgFile = join(work, 'static.svg');
      run('svg', file, '--theme', dark ? 'dark' : 'default', '--out', svgFile, ...(transparent ? ['--transparent'] : []));
      const svg = readFileSync(svgFile, 'utf8');
      const raster = await page.evaluate(async source => {
        const image = new Image();
        image.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(source)));
        await image.decode();
        const canvas = document.createElement('canvas');
        canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
        const context = canvas.getContext('2d');
        context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        return { corner: pixels[(5 * canvas.width + 5) * 4 + 3], opaque: pixels.filter((v, i) => i % 4 === 3 && v === 255).length };
      }, svg);
      assert.equal(raster.corner, transparent ? 0 : 255, 'Static SVG outer canvas alpha');
      assert.ok(raster.opaque > 0, 'Static SVG preserves authored fills.');
      console.log(`ok ${basename(file)} ${dark ? 'dark' : 'light'} ${transparent ? 'transparent' : 'filled'}: ${contrast.labels} labels, min ${contrast.minimum.toFixed(3)}:1, alpha ${alpha.corner}/${raster.corner}`);
    }
    assert.equal(readFileSync(file, 'utf8'), before, 'Appearance/output choices must not change source.');
  }
  assert.deepEqual(errors, []);
  assert.deepEqual(network, []);
  console.log('THEME/EXPORT ACCEPTANCE PASSED: actual browser ink, translated groups, preserved routes/fills, preview PNG and static SVG alpha, offline.');
} finally {
  await browser.close();
  rmSync(work, { recursive: true, force: true });
}
