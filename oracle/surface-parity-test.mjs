// Holds the CLI and the MCP server to the same capability set.
//
// The two surfaces drifted once already: the MCP server could lay a diagram
// out and the CLI could not, so an agent driving `merpeeps` had no way to
// position anything while an agent driving MCP did. Nothing failed — each
// surface's own tests passed, because each was complete on its own terms. A
// gap between two components is invisible to tests that only look at one.
//
// So this compares them. Every capability below must be reachable from both,
// and — the half that actually prevents recurrence — anything present on one
// side and absent from the other must be listed as a deliberate exception with
// a reason. Adding a tool to only one surface fails here.
import { spawn, spawnSync } from 'node:child_process';

const CLI = process.argv[2];
const DLL = process.argv[3];
if (!CLI || !DLL) {
  console.error('usage: node surface-parity-test.mjs <merpeeps-exe> <Merpeeps.Mcp.dll>');
  process.exit(2);
}

// The contract. `cli` is the verb as it appears in `merpeeps help`; `mcp` is
// the tool name as advertised over stdio.
const CAPABILITIES = [
  { what: 'read the metadata',    cli: 'read',           mcp: 'merpeeps_read_metadata' },
  { what: 'inspect capabilities', cli: 'capabilities',   mcp: 'merpeeps_capabilities' },
  { what: 'describe structure',   cli: 'describe',       mcp: 'merpeeps_describe' },
  { what: 'check invariants',     cli: 'validate',       mcp: 'merpeeps_validate' },
  { what: 'remove the metadata',  cli: 'strip',          mcp: 'merpeeps_strip' },
  { what: 'apply edits',          cli: 'edit',           mcp: 'merpeeps_edit' },
  { what: 'arrange the diagram',  cli: 'layout',         mcp: 'merpeeps_layout' },
  { what: 'list layout engines',  cli: 'layout-engines', mcp: 'merpeeps_layout_engines' },
  { what: 'store positions',      cli: 'adopt',          mcp: 'merpeeps_set_positions' },
  { what: 'render to SVG',        cli: 'svg',            mcp: 'merpeeps_svg' },
  { what: 'bake styling in',      cli: 'bake',           mcp: 'merpeeps_bake' },
  { what: 'open the editor',      cli: 'open',           mcp: 'merpeeps_open' },
];

// Asymmetries we mean. Anything not listed here is a gap, not a decision.
const CLI_ONLY = {
  preview: 'needs the vendored mermaid bundle — megabytes the MCP server would '
         + 'carry to duplicate what svg and open already cover',
  conformance: 'runs the spec suite over a corpus of samples; it tests the tool '
             + 'rather than operating on a diagram, so it has no meaning as a tool call',
  help: 'not a capability',
};

const MCP_ONLY = {};

// ------------------------------------------------------------------ the CLI

// Read the verbs from `help` rather than from the source. If a verb is not
// documented it is not usable, so parsing the help text checks the manual and
// the dispatch at once.
const help = spawnSync(CLI, ['help'], { encoding: 'utf8' });
const cliVerbs = new Set(
  [...help.stdout.matchAll(/^\s+merpeeps\s+([a-z][a-z-]*)/gm)].map(m => m[1]));

// Control: the parser must be capable of *not* matching. A regex that matched
// everything would make every "is this verb present" check below vacuous.
const bogusAbsent = !cliVerbs.has('banana');
const rejectsBogus = spawnSync(CLI, ['banana'], { encoding: 'utf8' })
  .stderr.includes('unknown command');

// Documented and dispatchable are different things, and the difference is a
// real bug in both directions: a verb in `help` that the dispatch rejects is a
// broken manual, and a verb the dispatch accepts but `help` omits is a feature
// nobody can find. Probing each one with no arguments distinguishes "unknown
// command" from any other complaint, so it detects the first case; checking the
// dispatch against the full expected set detects the second.
const dispatchable = (verb) => {
  const r = spawnSync(CLI, [verb], { encoding: 'utf8' });
  return !(r.stderr ?? '').includes('unknown command');
};

const expectedVerbs = [
  ...CAPABILITIES.map(c => c.cli),
  ...Object.keys(CLI_ONLY).filter(v => v !== 'help'),
];

const undocumented = expectedVerbs.filter(v => !cliVerbs.has(v) && dispatchable(v));
const unimplemented = [...cliVerbs].filter(v => !dispatchable(v));

// ------------------------------------------------------------------ the MCP

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

// Poll for the id rather than sleeping a hopeful interval — a cold `dotnet`
// start otherwise reports "no tools" when the truth is "we asked too early".
const awaitId = async (id, timeoutMs = 30_000) => {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const r = responses.find(x => x.id === id);
    if (r) return r;
    if (proc.exitCode !== null) {
      throw new Error(`server exited (code ${proc.exitCode}) before answering ${id}`);
    }
    await wait(25);
  }
  throw new Error(`no response to request ${id} within ${timeoutMs}ms`);
};

send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {
  protocolVersion: '2024-11-05', capabilities: {},
  clientInfo: { name: 'parity', version: '1' } } });
await awaitId(1);
send({ jsonrpc: '2.0', method: 'notifications/initialized' });
send({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
const listed = await awaitId(2);

proc.stdin.end();
proc.kill();

const mcpTools = new Set((listed.result?.tools ?? []).map(t => t.name));

// ---------------------------------------------------------------- compare

const missingFromCli = CAPABILITIES.filter(c => !cliVerbs.has(c.cli));
const missingFromMcp = CAPABILITIES.filter(c => !mcpTools.has(c.mcp));

const mappedVerbs = new Set(CAPABILITIES.map(c => c.cli));
const mappedTools = new Set(CAPABILITIES.map(c => c.mcp));

const unexplainedVerbs = [...cliVerbs]
  .filter(v => !mappedVerbs.has(v) && !(v in CLI_ONLY));
const unexplainedTools = [...mcpTools]
  .filter(t => !mappedTools.has(t) && !(t in MCP_ONLY));

console.log(`CLI verbs  (${cliVerbs.size}): ${[...cliVerbs].sort().join(', ')}`);
console.log(`MCP tools  (${mcpTools.size}): ${[...mcpTools].sort().join(', ')}`);
console.log('');

for (const c of CAPABILITIES) {
  const cli = cliVerbs.has(c.cli) ? 'ok ' : 'MISSING';
  const mcp = mcpTools.has(c.mcp) ? 'ok ' : 'MISSING';
  console.log(`  ${c.what.padEnd(22)} cli:${cli}  mcp:${mcp}`);
}
console.log('');

for (const [verb, why] of Object.entries(CLI_ONLY)) {
  if (verb !== 'help') console.log(`  CLI-only: ${verb} — ${why}`);
}
console.log('');

if (missingFromCli.length) {
  console.error('the CLI cannot do:');
  for (const c of missingFromCli) console.error(`  ${c.what} (expected verb '${c.cli}')`);
}
if (missingFromMcp.length) {
  console.error('the MCP server cannot do:');
  for (const c of missingFromMcp) console.error(`  ${c.what} (expected tool '${c.mcp}')`);
}
if (unexplainedVerbs.length) {
  console.error(`CLI verbs with no MCP counterpart and no stated reason: ${unexplainedVerbs.join(', ')}`);
  console.error('  add the capability to the MCP server, or list it in CLI_ONLY with why');
}
if (unexplainedTools.length) {
  console.error(`MCP tools with no CLI counterpart and no stated reason: ${unexplainedTools.join(', ')}`);
  console.error('  add the verb to the CLI, or list it in MCP_ONLY with why');
}
if (undocumented.length) {
  console.error(`verbs the CLI accepts but does not document: ${undocumented.join(', ')}`);
  console.error('  add them to the help text — an undocumented verb is an unusable one');
}
if (unimplemented.length) {
  console.error(`verbs the help text promises but the CLI rejects: ${unimplemented.join(', ')}`);
}

console.log('parser can miss   :', bogusAbsent);
console.log('CLI rejects bogus :', rejectsBogus);
console.log('help == dispatch  :', undocumented.length === 0 && unimplemented.length === 0);

const ok = missingFromCli.length === 0 && missingFromMcp.length === 0
  && unexplainedVerbs.length === 0 && unexplainedTools.length === 0
  && undocumented.length === 0 && unimplemented.length === 0
  && bogusAbsent && rejectsBogus && cliVerbs.size > 0 && mcpTools.size > 0;

console.log(ok ? 'SURFACE PARITY PASSED' : 'SURFACE PARITY FAILED');
process.exit(ok ? 0 : 1);
