# Contributing

## Building

You need the **.NET 10 SDK**. Node 22 is needed only for the browser-based gates.

```bash
dotnet build -c Release
dotnet test -c Release
```

That is 1,140 tests and should take about a minute. The suite is expected to run
with **zero warnings**; a new warning is treated as a failure.

For the gates that render in a real browser:

```bash
cd oracle
npm install
node run-oracle.mjs
```

---

## How this project decides it is correct

Worth reading before adding a test, because the convention here is unusual and
the reasons are not obvious.

**Real mermaid.js is the oracle.** Whether a Merpeeps file survives a stock
renderer is not a question our own code can answer, so it is asked of the actual
library in a real browser, across every diagram type it supports. When mermaid
and our tests disagree, mermaid is right by definition.

**A result without a control means nothing.** This has caught several genuinely
wrong conclusions in this codebase. If a check passes, ask what it would have
done had the feature been broken — and if the answer is "passed anyway", the
check is decoration. Examples that live in the suite:

- The replay gate shifts every node by a fixed offset before storing it. Without
  that, harvesting dagre and replaying it under dagre would pass whether the
  coordinates were read or not.
- The leak detector is differential — extended output versus baseline — because
  numeric tokens collide with SVG path geometry (`4242` occurs inside
  `-0.2025895406114242`).
- Some diagram types are non-deterministic. `gitgraph` mints random commit hashes,
  so every baseline is rendered *twice* to tell engine entropy from a real change.

**Measure, don't assume.** Several confidently-written claims in this repo turned
out to be false when finally checked — including one in the README about
`securityLevel: 'strict'` disabling HTML labels, which it does not. If you are
about to document how a dependency behaves, run it first.

**Verify what the user sees, not what the API returns.** A tooltip once passed
its tests — correct text, `IsOpen` true — while rendering in the corner of the
screen, 1,300px from the control it described. `oracle/grab-screen.ps1` captures
the real desktop for exactly this reason; per-window screenshots miss popups,
which are separate top-level windows.

---

## The test layers

| Layer | What it covers | Command |
|---|---|---|
| `Merpeeps.Core.Tests` | Format invariants, parsing, layout, edit commands, the surgical writer | `dotnet test tests/Merpeeps.Core.Tests` |
| `Merpeeps.App.Tests` | The real window, real XAML, real handlers, headless — asserting on captured pixels | `dotnet test tests/Merpeeps.App.Tests` |
| `oracle/run-oracle.mjs` | 30 diagram types × 9 invariants against real mermaid in Chrome | `node oracle/run-oracle.mjs` |
| 13 further gates | Harvest replay, the npm plugin, MCP over real stdio, CLI/MCP parity, bake, previews, themes | see `oracle/` |

The UI suite renders with real Skia and asserts on pixels rather than on what the
drawing code intended. Two bugs — icons still drawn with styling switched off,
and unreadable labels in dark mode — shipped past a green suite and were caught
only by a person looking at a screenshot. That is why it works this way.

---

## Running the gates

Most take no arguments:

```bash
cd oracle
node run-oracle.mjs            # the conformance suite
node harvest-test.mjs          # store-and-replay, with its constructed control
node layout-plugin-test.mjs    # the published npm package in a browser
node theme-colour-probe.mjs    # every mermaid theme, colours read back
```

Two need the built MCP server:

```bash
node mcp-smoke.mjs      ../src/Merpeeps.Mcp/bin/Release/net10.0/Merpeeps.Mcp.dll
node mcp-agent-test.mjs ../src/Merpeeps.Mcp/bin/Release/net10.0/Merpeeps.Mcp.dll
```

One needs both front ends, because comparing them is the point:

```bash
node surface-parity-test.mjs \
  ../src/Merpeeps.Cli/bin/Release/net10.0/merpeeps \
  ../src/Merpeeps.Mcp/bin/Release/net10.0/Merpeeps.Mcp.dll
```

It fails if either surface grows a capability the other lacks, or if the CLI
accepts a verb its own help text does not mention. Deliberate asymmetries live
in `CLI_ONLY` / `MCP_ONLY` at the top of the file and need a stated reason —
the point is to make drift a decision rather than an accident.

Forgetting the argument prints usage and exits non-zero, which looks exactly like
a failure — check the message before assuming you broke something.

---

## Style

**Comments explain why, not what.** `// increment i` earns nothing. A note
recording that both registration orders were measured to work, and why one was
chosen anyway, earns its line — it stops the next person redoing the experiment.

**Record what a test is defending against.** A test named for its mechanism ages
badly; one that names the failure survives. Several XML doc comments here
describe the bug that prompted them, including ones where an earlier assertion
was wrong.

**Keep changes proportional to the edit.** `SurgicalWriter` exists so that
renaming a node changes one line rather than regenerating the document. Tests
assert **changed line counts**, not substrings — a regenerated file contains the
right text too, so a substring check passes on a whole-file rewrite.

**Don't add dependencies casually.** The editor deliberately has no Node runtime
requirement and no process boundary in a redraw.

---

## Things that will bite you

**Avalonia's XAML compiler fails quietly on a stale restore.** The build reports
"0 Errors" while emitting no compiled XAML, and the app then dies at startup with
"No precompiled XAML found". `dotnet restore --force` surfaces the real errors.

**Unresolvable XAML hangs rather than throwing.** `AvaloniaUseCompiledBindings`
is on, so `{Binding}` needs `x:DataType`. A missing one is an apparent freeze, not
an exception.

**Headless UI tests must not construct a WebView.** Each costs 24 seconds and
throws `RPC_E_CHANGED_MODE` on teardown. The suite detects the headless platform
by looking for the `Avalonia.Headless` assembly.

**The editor's default sample is a shared fixture.** `docs/styled-sample.mmd` is
what the app opens when you try it out, so anyone experimenting edits it — which
silently changed the diagram under three gates and failed them on code that was
fine. Gates read `oracle/fixtures/` instead. Keep it that way.

---

## Pull requests

- Say what failure the change prevents, not just what it does.
- Include the verification: what you ran, and what it said.
- If you could not run something, say so — an honest gap is worth more than an
  implied guarantee.
