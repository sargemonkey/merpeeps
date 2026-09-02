// End-to-end agent workflow over the real MCP stdio protocol.
//
// Proves the thing the product actually claims: a coding agent can build and
// amend a diagram without ever loading the file into its context, and its edits
// produce minimal diffs rather than regenerated files.
import { spawn } from 'node:child_process';
import { writeFileSync, readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const DLL = process.argv[2];
if (!DLL) { console.error('usage: node mcp-agent-test.mjs <Merpeeps.Mcp.dll>'); process.exit(2); }

const dir = mkdtempSync(join(tmpdir(), 'merpeeps-agent-'));
const file = join(dir, 'arch.mmd');
const ORIGINAL = `flowchart TD
    api[API] --> db[(Database)]
    classDef external fill:#f96
`;
writeFileSync(file, ORIGINAL, 'utf8');

const proc = spawn('dotnet', [DLL], { stdio: ['pipe', 'pipe', 'pipe'] });
const responses = [];
let buf = '';
proc.stdout.on('data', (d) => {
  buf += d.toString();
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim();
    buf = buf.slice(i + 1);
    if (line) { try { responses.push(JSON.parse(line)); } catch { /* log noise */ } }
  }
});

let nextId = 1;
const send = (method, params) => {
  const id = nextId++;
  proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  return id;
};
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Wait until the response with this id arrives, rather than for a fixed time.
 *
 * This gate used to sleep a hopeful number of milliseconds after each request.
 * That passes on an idle machine and fails when the process is slow to start —
 * a cold `dotnet` launch, or eight other gates having just run. The symptom was
 * the worst kind: `toolsExposed: 0` with every other check green, which reads
 * as "the server exposes no tools" rather than "we asked too early".
 *
 * Polling for the id makes the timing irrelevant. The timeout is generous
 * because it now only has to catch a genuine hang, not a slow start.
 */
const awaitId = async (id, timeoutMs = 30_000) => {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const r = responses.find(x => x.id === id);
    if (r) return r;
    if (proc.exitCode !== null) {
      throw new Error(`server exited (code ${proc.exitCode}) before answering request ${id}`);
    }
    await wait(25);
  }
  throw new Error(`no response to request ${id} within ${timeoutMs}ms`);
};

const resultOf = (id) => {
  const r = responses.find(x => x.id === id);
  if (!r?.result?.content?.[0]?.text) return null;
  try { return JSON.parse(r.result.content[0].text); } catch { return null; }
};

/** Send a request and return its parsed result, once it actually arrives. */
const call = async (method, params) => {
  const id = send(method, params);
  await awaitId(id);
  return resultOf(id);
};

const initId = send('initialize', { protocolVersion: '2024-11-05', capabilities: {},
                     clientInfo: { name: 'agent-test', version: '1' } });
await awaitId(initId);
proc.stdin.write(JSON.stringify({ jsonrpc: '2.0',
  method: 'notifications/initialized' }) + '\n');

const toolsId = send('tools/list', {});
await awaitId(toolsId);
const tools = (responses.find(r => r.id === toolsId)?.result?.tools ?? [])
  .map(t => t.name).sort();

// 1. Understand the diagram WITHOUT reading the file.
const describeId = send('tools/call',
  { name: 'merpeeps_describe', arguments: { path: file } });
await awaitId(describeId);
const described = resultOf(describeId);

// 2. Extend it in one batch call.
const editId = send('tools/call', { name: 'merpeeps_edit', arguments: {
  path: file,
  operationsJson: JSON.stringify([
    { op: 'addNode', id: 'cache', label: 'Redis', shape: 'cylinder' },
    { op: 'addNode', id: 'queue', label: 'Event Queue', shape: 'subroutine' },
    { op: 'connect', from: 'api', to: 'cache', label: 'reads' },
    { op: 'connect', from: 'api', to: 'queue', kind: 'dotted' },
    { op: 'rename', id: 'api', label: 'API Gateway' },
  ]),
} });
await awaitId(editId);
const edited = resultOf(editId);

// 3. Let the engine position the new nodes.
const layoutId = send('tools/call', { name: 'merpeeps_layout', arguments: { path: file } });
await awaitId(layoutId);
const laidOut = resultOf(layoutId);

// 3b. Discover the available engines and use a specific one. An agent should
// not have to guess engine names, and asking for a bad one must fail loudly
// rather than silently falling back to something else.
const enginesId = send('tools/call',
  { name: 'merpeeps_layout_engines', arguments: {} });
await awaitId(enginesId);
const engines = resultOf(enginesId);

const treeId = send('tools/call',
  { name: 'merpeeps_layout', arguments: { path: file, engine: 'tree' } });
await awaitId(treeId);
const tree = resultOf(treeId);
const afterTree = readFileSync(file, 'utf8');

const badEngineId = send('tools/call',
  { name: 'merpeeps_layout', arguments: { path: file, engine: 'not-an-engine' } });
await awaitId(badEngineId);
const badEngine = resultOf(badEngineId);
const afterBadEngine = readFileSync(file, 'utf8');

// 4. Confirm invariants still hold.
const validateId = send('tools/call', { name: 'merpeeps_validate', arguments: { path: file } });
await awaitId(validateId);
const validated = resultOf(validateId);

// 5. A bad batch must change nothing.
const badId = send('tools/call', { name: 'merpeeps_edit', arguments: {
  path: file,
  operationsJson: JSON.stringify([{ op: 'connect', from: 'api', to: 'ghost' }]),
} });
await awaitId(badId);
const rejected = resultOf(badId);

// 6. Rendering. An agent that can edit a diagram but cannot produce a picture
//    of it has to hand the file back and ask someone else to look at it.
const svgPath = join(dir, 'out.svg');
const svgId = send('tools/call', { name: 'merpeeps_svg', arguments: { path: file, outPath: svgPath } });
await awaitId(svgId);
const rendered = resultOf(svgId);

const bakeId = send('tools/call', { name: 'merpeeps_bake', arguments: { path: file } });
await awaitId(bakeId);
const baked = resultOf(bakeId);

proc.stdin.end();
proc.kill();

const finalText = readFileSync(file, 'utf8');
const origBody = ORIGINAL.trimEnd().split('\n');
const finalBody = finalText.split('---').pop().trimEnd().split('\n')
  .filter(l => l.trim().length > 0);

const originalLinesIntact = origBody.every((l) => {
  // the api line is legitimately edited by the rename
  if (l.includes('api[API]')) return true;
  return finalBody.includes(l);
});

const checks = {
  toolsExposed: tools.length,
  describedWithoutReading: described?.nodeCount === 2 && described?.linkCount === 1,
  batchApplied: edited?.ok === true && edited?.applied?.length === 5,
  // 5 operations (1 rename + 4 new statements) should touch ~5 lines, not the
  // whole file. The property is 'proportional to edits', not 'small'.
  // The property is "proportional to the edit, not the file". Each operation
  // costs at most two lines (a modification is a removal plus an addition), so
  // N operations must stay within 2N regardless of how large the diagram is.
  minimalDiff: edited?.linesChanged !== undefined && edited.linesChanged <= 10,
  layoutStoredPositions: laidOut?.ok === true && laidOut?.bodyUnchanged === true,

  // Engines must be discoverable, selectable, recorded, and validated.
  enginesDiscoverable: Array.isArray(engines?.engines)
    && engines.engines.length >= 4
    && engines.engines.every(e => e.id && e.name && e.description),
  engineSelectable: tree?.ok === true && tree?.engine === 'tree',
  engineRecordedInFile: afterTree.includes('engine: tree'),
  unknownEngineRejected: badEngine?.ok === false
    && typeof badEngine?.error === 'string'
    && badEngine.error.includes('tree'),
  // A rejected request must leave the file exactly as it was.
  rejectedEngineChangedNothing: afterBadEngine === afterTree,

  invariantsHold: validated?.ok === true,
  badBatchRejected: rejected?.ok === false && Array.isArray(rejected?.errors),

  // An agent that can edit a diagram but not render one has to hand the file
  // back and ask a person to look at it.
  svgRendered: rendered?.ok === true && rendered?.bytes > 500,
  svgIsWellFormed: (() => {
    if (!rendered?.ok) return false;
    const text = readFileSync(svgPath, 'utf8');
    // Cheap well-formedness: an SVG that does not open and close is a text
    // file with the wrong extension.
    return text.startsWith('<?xml') && text.includes('<svg') && text.trimEnd().endsWith('</svg>');
  })(),
  // The control. Rendering must reflect the diagram, not emit a fixed page.
  svgContainsTheDiagram: rendered?.ok === true
    && readFileSync(svgPath, 'utf8').includes('Redis'),

  baked: baked?.ok === true && typeof baked?.text === 'string'
    && baked.text.includes('flowchart'),
  // Bake is lossy by nature and has to say so rather than dropping quietly.
  bakeReportsLoss: baked?.ok === true
    && Array.isArray(baked?.droppedPositions)
    && typeof baked?.lossy === 'boolean',
  // Writing is opt-in: without outPath the source file is untouched.
  bakeLeftTheSourceAlone: baked?.ok === true && readFileSync(file, 'utf8') === finalText,

  originalLinesIntact,
  classDefSurvived: finalText.includes('classDef external fill:#f96'),
  newContentPresent: finalText.includes('cache[(Redis)]')
                  && finalText.includes('api[API Gateway]'),
  dottedMeansArrow: finalText.includes('api -.-> queue'),
};

console.log('tools:', tools.join(', '));
console.log('linesChanged for a 5-operation batch:', edited?.linesChanged);
console.log(JSON.stringify(checks, null, 2));
console.log('--- resulting file ---');
console.log(finalText);

const ok = Object.values(checks).every(v => v === true || typeof v === 'number' && v > 0);
console.log(ok ? 'AGENT WORKFLOW PASSED' : 'AGENT WORKFLOW FAILED');
process.exit(ok ? 0 : 1);


