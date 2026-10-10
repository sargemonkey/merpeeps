// Exercise family detection through the real CLI and, optionally, MCP stdio.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const cliPath = process.argv[2];
const mcpPath = process.argv[3];
if (!cliPath) {
  console.error('usage: node capability-test.mjs <merpeeps-exe> [Merpeeps.Mcp.dll]');
  process.exit(2);
}

const dir = mkdtempSync(join(tmpdir(), 'merpeeps-capabilities-'));
const cli = (...args) => {
  const result = spawnSync(resolve(cliPath), args, {
    encoding: 'utf8', timeout: 30_000, maxBuffer: 8 * 1024 * 1024,
  });
  if (result.error) throw result.error;
  return result;
};
const okCli = (...args) => {
  const result = cli(...args);
  assert.equal(result.status, 0, `${args[0]} failed: ${result.stderr}`);
  return result.stdout;
};
const unsupported = join(dir, 'future.mmd');
const positions = join(dir, 'positions.json');
const svg = join(dir, 'existing.svg');
const source = '---\nx-merpeeps:\n  spec: 1\n  future: {value: 0.5}\n' +
  '  style:\n    nodes:\n      Alice: {icon: "aws:lambda"}\n---\n' +
  'futureDiagram\n    title Work\n';
const assertUnsupported = capabilities => {
  assert.equal(capabilities.kind, 'futureDiagram');
  assert.equal(capabilities.canReadMetadata, true);
  assert.equal(capabilities.canEdit, false);
  assert.equal(capabilities.canLayout, false);
  assert.equal(capabilities.canRenderNatively, false);
  assert.match(capabilities.diagnostic, /futureDiagram/);
};

let server;
try {
  writeFileSync(unsupported, source);
  writeFileSync(positions, '{"Alice":{"x":10.5,"y":20.25}}');
  writeFileSync(svg, 'ORIGINAL SVG');

  assertUnsupported(JSON.parse(okCli('capabilities', unsupported)));
  for (const args of [
    ['describe', unsupported],
    ['layout', unsupported, '--write'],
    ['edit', unsupported, '--ops', '[{"op":"addNode","id":"wrong"}]', '--write'],
    ['adopt', unsupported, '--positions', positions, '--write'],
    ['svg', unsupported, '--out', svg],
    ['bake', unsupported, '--out', svg],
  ]) {
    const result = cli(...args);
    assert.equal(result.status, 2, `${args[0]} must reject unsupported native work`);
    assert.match(result.stderr, /futureDiagram/);
    assert.equal(result.stdout, '');
    assert.equal(readFileSync(unsupported, 'utf8'), source);
    assert.equal(readFileSync(svg, 'utf8'), 'ORIGINAL SVG');
  }

  const validation = okCli('validate', unsupported);
  assert.match(validation, /PASS/);
  assert.match(validation, /metadata invariants only/);
  assert.match(validation, /syntax was not checked/);
  assert.match(validation, /not supported for 'futureDiagram'/);
  assert.match(validation, /icon packs referenced: aws/);
  assert.equal(JSON.parse(okCli('read', unsupported)).future.value, 0.5);
  assert.equal(okCli('strip', unsupported), 'futureDiagram\n    title Work\n');
  assert.equal(readFileSync(unsupported, 'utf8'), source);
  const preview = join(dir, 'preview.html');
  okCli('preview', unsupported, '--plain', '--out', preview);
  assert.ok(readFileSync(preview, 'utf8').includes('futureDiagram'));

  for (const directive of ['flowchartish', 'graphical', 'stateDiagram-v3']) {
    const path = join(dir, `${directive}.mmd`);
    writeFileSync(path, `${directive}\n    a --> b\n`);
    const capabilities = JSON.parse(okCli('capabilities', path));
    assert.equal(capabilities.kind, directive);
    assert.equal(capabilities.canEdit, false);
    assert.equal(cli('describe', path).status, 2);
  }

  // Positive control: guards must not disable supported native operations.
  const supported = join(dir, 'flow.mmd');
  writeFileSync(supported, '%% prefix\nflowchart LR\n    a[Alpha] --> b[Beta]\n');
  assert.equal(JSON.parse(okCli('capabilities', supported)).canEdit, true);
  assert.equal(JSON.parse(okCli('describe', supported)).nodeCount, 2);
  okCli('edit', supported, '--ops', '[{"op":"addNode","id":"c"}]', '--write');
  okCli('layout', supported, '--engine', 'tree', '--write');
  okCli('svg', supported, '--out', svg);
  assert.match(readFileSync(svg, 'utf8'), /<svg/);
  assert.equal(JSON.parse(okCli('describe', supported)).nodeCount, 3);
  const sequenceFile = join(dir, 'sequence.mmd');
  const sequenceSource = 'sequenceDiagram\nparticipant A\nparticipant B\nA->>B: Hello\n';
  writeFileSync(sequenceFile, sequenceSource);
  const sequenceCapabilities = JSON.parse(okCli('capabilities', sequenceFile));
  assert.equal(sequenceCapabilities.canEdit, true);
  assert.equal(sequenceCapabilities.canLayout, false);
  const sequenceSummary = JSON.parse(okCli('describe', sequenceFile));
  assert.equal(sequenceSummary.participantCount, 2);
  assert.equal(sequenceSummary.messageCount, 1);
  assert.equal(sequenceSummary.nodeCount, undefined);
  assert.equal(cli('layout', sequenceFile, '--write').status, 2);
  const graphEdit = cli('edit', sequenceFile, '--ops', '[{"op":"addNode","id":"wrong"}]', '--write');
  assert.equal(graphEdit.status, 1);
  assert.match(graphEdit.stderr, /typed SequenceEditing operations/);
  assert.match(graphEdit.stderr, /nothing was written/);
  assert.equal(cli('adopt', sequenceFile, '--positions', positions, '--write').status, 2);
  assert.equal(readFileSync(sequenceFile, 'utf8'), sequenceSource);
  okCli('svg', sequenceFile, '--out', svg);
  assert.match(readFileSync(svg, 'utf8'), /data-sequence-owner/);
  console.log('CLI: native guards, output preservation, metadata-only validation and positive controls passed');

  if (mcpPath) {
    server = spawn('dotnet', [resolve(mcpPath)], { stdio: ['pipe', 'pipe', 'pipe'] });
    const responses = new Map();
    let buffer = '', stderr = '', protocolError;
    server.on('error', error => { protocolError = error; });
    server.stderr.on('data', data => { stderr = (stderr + data).slice(-4000); });
    server.stdout.on('data', data => {
      buffer += data;
      let newline;
      while ((newline = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, newline).trim();
        buffer = buffer.slice(newline + 1);
        if (!line) continue;
        try {
          const response = JSON.parse(line);
          if (response.id !== undefined) responses.set(response.id, response);
        } catch (error) {
          protocolError = new Error(`Invalid MCP stdout: ${error.message}`);
        }
      }
    });
    let nextId = 0;
    const send = request => server.stdin.write(JSON.stringify(request) + '\n');
    const request = async (method, params) => {
      const id = ++nextId;
      send({ jsonrpc: '2.0', id, method, params });
      const deadline = Date.now() + 30_000;
      while (!responses.has(id)) {
        if (protocolError) throw protocolError;
        if (server.exitCode !== null) throw new Error(`MCP exited: ${stderr}`);
        if (Date.now() >= deadline) throw new Error(`MCP timeout on ${method}: ${stderr}`);
        await new Promise(resolveWait => setTimeout(resolveWait, 25));
      }
      const response = responses.get(id);
      assert.equal(response.error, undefined, JSON.stringify(response.error));
      return response.result;
    };
    const tool = async (name, args) => {
      const result = await request('tools/call', { name, arguments: args });
      assert.notEqual(result.isError, true, JSON.stringify(result));
      return JSON.parse(result.content[0].text);
    };

    await request('initialize', {
      protocolVersion: '2024-11-05', capabilities: {},
      clientInfo: { name: 'capability-regression', version: '1' },
    });
    send({ jsonrpc: '2.0', method: 'notifications/initialized' });
    const listed = await request('tools/list', {});
    assert.ok(listed.tools.some(t => t.name === 'merpeeps_capabilities'));
    assertUnsupported((await tool('merpeeps_capabilities', { path: unsupported })).capabilities);

    writeFileSync(svg, 'ORIGINAL SVG');
    for (const [name, extra] of [
      ['merpeeps_describe', {}],
      ['merpeeps_edit', { operationsJson: '[{"op":"addNode","id":"wrong"}]' }],
      ['merpeeps_layout', {}],
      ['merpeeps_svg', { outPath: svg }],
      ['merpeeps_bake', { outPath: svg }],
    ]) {
      const result = await tool(name, { path: unsupported, ...extra });
      assert.equal(result.ok, false, `${name} must reject unsupported native work`);
      assertUnsupported(result.capabilities);
      assert.equal(readFileSync(unsupported, 'utf8'), source);
      assert.equal(readFileSync(svg, 'utf8'), 'ORIGINAL SVG');
    }

    const validated = await tool('merpeeps_validate', { path: unsupported });
    assert.equal(validated.ok, true);
    assert.equal(validated.scope, 'metadata');
    assert.equal(validated.syntaxValidated, false);
    assertUnsupported(validated.capabilities);
    const metadata = await tool('merpeeps_read_metadata', { path: unsupported });
    assert.equal(JSON.parse(metadata.merpeeps).future.value, 0.5);
    assert.equal((await tool('merpeeps_set_positions', {
      path: unsupported, positionsJson: '{"Alice":{"x":10.5,"y":20.25}}',
    })).ok, true);
    assert.ok(readFileSync(unsupported, 'utf8').endsWith('futureDiagram\n    title Work\n'));
    const sequenceDescription = await tool('merpeeps_describe', { path: sequenceFile });
    assert.equal(sequenceDescription.participantCount, 2);
    assert.equal(sequenceDescription.messageCount, 1);
    assert.equal((await tool('merpeeps_layout', { path: sequenceFile })).ok, false);
    const nativeLimited = join(dir, 'sequence-native-limited.mmd');
    const nativeLimitedSource = 'sequenceDiagram\nA->>B: First\ndestroy B\nA-xB: End\nB->>A: Later\n';
    writeFileSync(nativeLimited, nativeLimitedSource);
    const policy = await tool('merpeeps_describe', { path: nativeLimited });
    assert.equal(policy.ok, false);
    assert.ok(policy.diagnostics.some(d => d.code === 'participant-lifetime' && d.kind === 'NativeUnsupported'));
    assert.equal(readFileSync(nativeLimited, 'utf8'), nativeLimitedSource);
    assert.equal((await tool('merpeeps_describe', { path: supported })).nodeCount, 3);
    assert.equal((await tool('merpeeps_layout', { path: supported })).ok, true);
    console.log('MCP: actual tools reject invented native models while metadata operations remain available');
  }
  console.log('CAPABILITY CONTRACT PASSED');
} finally {
  if (server) {
    server.stdin.end();
    server.kill();
  }
  rmSync(dir, { recursive: true, force: true });
}
