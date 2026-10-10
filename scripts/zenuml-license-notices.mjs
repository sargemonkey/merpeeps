import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const oracle = join(repo, 'oracle');
const destination = join(repo, 'src', 'Merpeeps.Render', 'Assets', 'vendor', 'zenuml',
  'THIRD-PARTY-NOTICES.txt');

function packageDirectory(name, from) {
  for (let parent = from; parent.length >= oracle.length; parent = dirname(parent)) {
    const candidate = join(parent, 'node_modules', name);
    if (existsSync(join(candidate, 'package.json'))) return candidate;
    if (parent === oracle) break;
  }
  return null;
}

const root = packageDirectory('@zenuml/core', oracle);
if (!root) throw new Error('Install the pinned oracle dependencies before vendoring.');
const pending = [root];
const visited = new Set();
const notices = new Map();
const missingNotices = [];
const supplemental = new Map([
  ['antlr4@4.11.0', 'antlr4-4.11.0-LICENSE.txt'],
]);
while (pending.length) {
  const directory = pending.shift();
  if (visited.has(directory)) continue;
  visited.add(directory);
  const manifest = JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8'));
  const key = `${manifest.name}@${manifest.version}`;
  const files = readdirSync(directory, { withFileTypes: true })
    .filter(file => file.isFile() && /^(licen[cs]e|copying|notice)(?:$|[.-])/i.test(file.name))
    .map(file => file.name).sort();
  if (files.length) {
    notices.set(key, files.map(file =>
      `${file}\n\n${readFileSync(join(directory, file), 'utf8').replaceAll('\r\n', '\n').trimEnd()}`).join('\n\n'));
  } else if (supplemental.has(key)) {
    const file = supplemental.get(key);
    notices.set(key, `${file}\n\n${readFileSync(
      join(dirname(destination), 'upstream-notices', file), 'utf8').replaceAll('\r\n', '\n').trimEnd()}`);
  } else if (key === 'dlv@1.1.3') {
    const readme = readFileSync(join(directory, 'README.md'), 'utf8');
    const licenseSection = /### License\s+([\s\S]*?)(?=\n\[preact\]:|$)/.exec(readme);
    if (!licenseSection || manifest.license !== 'MIT')
      throw new Error('The pinned dlv publisher license declaration changed; review it.');
    notices.set(key, `Publisher declaration from package.json: ${manifest.license}\n`
      + `Author: ${manifest.author}\n\nPublisher notice from README.md:\n`
      + licenseSection[1].trim()
      + '\n\nThe upstream npm package and release tag provide a license reference, not a license file.');
  } else {
    missingNotices.push(key);
  }

  const dependencies = {
    ...manifest.dependencies,
    ...manifest.optionalDependencies,
    ...manifest.peerDependencies,
  };
  for (const name of Object.keys(dependencies)) {
    const resolved = packageDirectory(name, directory);
    if (resolved) pending.push(resolved);
    else if (manifest.dependencies?.[name] && !manifest.optionalDependencies?.[name])
      throw new Error(`Missing dependency ${name} of ${key}; cannot collect its notices.`);
  }
  if (missingNotices.length)
    throw new Error(`Review missing license notices before redistribution: ${missingNotices.join(', ')}`);
}

const header = `ZenUML standalone bundle dependency notices

The upstream UMD bundle is preserved verbatim. The notices below cover its
pinned package and installed production/peer dependency graph, which may
include dependencies not embedded in that bundle. No external runtime loading
or license grant beyond the individual notices is implied.
`;
writeFileSync(destination, header + [...notices].sort(([a], [b]) => a.localeCompare(b, 'en'))
  .map(([name, notice]) => `\n${'='.repeat(72)}\n${name}\n${'='.repeat(72)}\n\n${notice}\n`)
  .join(''), 'utf8');
console.log(`Preserved ${notices.size} dependency license/notice sets.`);
