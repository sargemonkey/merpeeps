// Smoke-test the Merpeeps MCP server over stdio: initialize, list tools, and call
// one for real against a temp file.
import { spawn } from 'node:child_process';
import { writeFileSync, readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const DLL = process.argv[2];
if (!DLL) { console.error('usage: node mcp-smoke.mjs <path-to-Merpeeps.Mcp.dll>'); process.exit(2); }

const dir = mkdtempSync(join(tmpdir(), 'merpeeps-'));
const file = join(dir, 'demo.mmd');
writeFileSync(file, 'flowchart LR\n    A[Start] --> B[End]\n', 'utf8');

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

const send = (o) => proc.stdin.write(JSON.stringify(o) + '\n');
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Wait for the response with this id rather than for a fixed time.
 *
 * The sibling gate (mcp-agent-test) failed intermittently because it slept a
 * hopeful number of milliseconds after each request; a cold `dotnet` start
 * overran the sleep and the gate reported "no tools exposed" when the real
 * answer was "we asked too early". Same shape of bug here, same fix.
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

send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {
  protocolVersion: '2024-11-05', capabilities: {},
  clientInfo: { name: 'smoke', version: '1' } } });
await awaitId(1);

send({ jsonrpc: '2.0', method: 'notifications/initialized' });

send({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
await awaitId(2);

send({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: {
  name: 'merpeeps_set_positions',
  arguments: { path: file, positionsJson: '{"A":{"x":120,"y":80}}' } } });
await awaitId(3);

send({ jsonrpc: '2.0', id: 4, method: 'tools/call', params: {
  name: 'merpeeps_validate', arguments: { path: file } } });
await awaitId(4);

proc.stdin.end();
proc.kill();

const init = responses.find(r => r.id === 1);
const tools = responses.find(r => r.id === 2)?.result?.tools ?? [];
const setPos = responses.find(r => r.id === 3);
const validate = responses.find(r => r.id === 4);

console.log('initialize   :', init?.result?.serverInfo?.name ?? '(none)');
console.log('tools        :', tools.map(t => t.name).join(', ') || '(none)');
console.log('set_positions:', setPos?.result?.content?.[0]?.text ?? '(none)');
console.log('validate     :', validate?.result?.content?.[0]?.text ?? '(none)');
const finalText = readFileSync(file, 'utf8');
console.log('--- resulting file ---');
console.log(finalText);

// The values must actually survive — presence of "x-merpeeps" is not enough.
// (An earlier bug wrote `ValueKind: Object` here and still looked green.)
const positionsStored = /x:\s*120/.test(finalText) && /y:\s*80/.test(finalText);
const bodyIntact = finalText.includes('A[Start] --> B[End]');
const noJsonElementLeak = !finalText.includes('ValueKind');

console.log('positions stored :', positionsStored);
console.log('body intact      :', bodyIntact);
console.log('no JsonElement   :', noJsonElementLeak);

const ok = !!init && tools.length >= 4 && !!setPos && !!validate
  && positionsStored && bodyIntact && noJsonElementLeak;
console.log(ok ? 'MCP SMOKE PASSED' : 'MCP SMOKE FAILED');
process.exit(ok ? 0 : 1);
