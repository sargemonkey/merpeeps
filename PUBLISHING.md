# Publishing

Two packages ship from this repo. Both are verified by CI before release; the
commands below are the only manual steps.

## Before you publish anything

```powershell
cd <repo root>

dotnet build Merpeeps.slnx -c Release                     # must be 0 warnings
dotnet test tests/Merpeeps.Core.Tests -c Release          # 896 tests
dotnet test tests/Merpeeps.App.Tests  -c Release          # 244 tests (headless + real pixels)
cd packages\merpeeps-mermaid-layout; npm test; cd ..\..   # 28 tests

node oracle/package-consumer-test.mjs   # the tarball actually installs
node oracle/run-oracle.mjs              # 30 diagram types in real Chrome
node oracle/layout-plugin-test.mjs      # nodes land at stored coordinates
node oracle/preview-page-test.mjs       # published source works in a browser
node oracle/bake-render-test.mjs        # baked output renders with no plugin
```

Two of these earn their place more than the rest.

**`package-consumer-test.mjs`** — the package's own tests import from `../src`,
so they pass even when `files` or `exports` is wrong. Only installing the real
tarball catches that.

**`preview-page-test.mjs`** — loads this package's *published source* in real
Chrome, offline, and asserts stored positions and fills actually apply. It is
the closest thing to a user's first five minutes, and it is the reason the
package is fit to publish rather than merely believed to be.

## npm — `merpeeps-mermaid-layout`

The name is available and the package publishes to the public registry
regardless of the machine's default (`publishConfig.registry`).

> **This machine cannot publish.** npm is pointed at
> `packagefeedproxy.microsoft.io`, an install-only mirror, and
> `registry.npmjs.org` fails at the TLS layer
> (`ERR_SSL_SSL/TLS_ALERT_HANDSHAKE_FAILURE`) for both `npm view` and
> `npm whoami`. Everything short of the upload is verifiable here; the publish
> itself needs a machine with direct access to npmjs.org.

```powershell
cd packages\merpeeps-mermaid-layout

npm login --registry=https://registry.npmjs.org/   # opens a browser
npm publish --access public
```

Verify:

```powershell
npm view merpeeps-mermaid-layout --registry=https://registry.npmjs.org/
```

### First-publish checklist

- [ ] `npm pack --dry-run` lists exactly these 9 files, 17.1 kB packed:
      `LICENSE`, `README.md`, `SPEC.md`, `TRADEMARKS.md`, `package.json`,
      `src/index.js`, `src/index.d.ts`, `src/mermaid-plugin.js`,
      `src/mermaid-plugin.d.ts`
- [ ] Version in `package.json` is the one you intend (npm will not let you
      republish the same version, and unpublishing is restricted after 72 hours)
- [ ] `TRADEMARKS.md` is present in the tarball — it is the disclaimer that
      makes the descriptive use of "Mermaid" defensible

## NuGet — `Merpeeps.Core`

```powershell
cd <repo root>
dotnet pack src\Merpeeps.Core -c Release -o artifacts

dotnet nuget push artifacts\Merpeeps.Core.0.1.0.nupkg `
  --source https://api.nuget.org/v3/index.json `
  --api-key $env:NUGET_API_KEY
```

The symbol package (`.snupkg`) is pushed automatically alongside it.

## Naming rules for anything new

These are not stylistic preferences; they are what keeps the descriptive use of
"Mermaid" defensible. See [`TRADEMARKS.md`](TRADEMARKS.md).

| Do | Don't |
|---|---|
| `merpeeps-mermaid-layout`, `Merpeeps.*` | `mermaid-extended`, `mermaid-pro` — identity claims |
| "a visual editor for Mermaid diagrams" (tagline) | "Mermaid Editor" (product name) |
| "Mermaid" as a noun for the format | "Mermaid" as an adjective on our products |
| `mermaid` as a **descriptor** in a package id | `mermaid` as the **distinguishing** part of the id |
| Unscoped, or your own scope | Anything implying the `@mermaid-js` scope |
| An original logo | The Mermaid logo |
| Ordinary words and public-domain mythology | Fictional character names |

The distinction that matters: using the mark to say what your tool is
*compatible with* is descriptive; using it to say what your tool *is* is
identity. Automattic is currently litigating exactly that line against WP Engine
over "Headless WordPress", while "runs WordPress" was never at issue.

That is why `merpeeps-mermaid-layout` is acceptable and `mermaid-extended` is not.
In the first, `merpeeps` is the distinguishing element and `mermaid` merely names
the format — the same construction as `rehype-mermaid` and
`vitepress-plugin-mermaid`, unchallenged in the community for years. In the
second, the mark itself is the name and "extended" claims to improve on it.

**Why the package id contains "mermaid" at all:** npm search matches package
names far more strongly than keywords. A search for "mermaid" on npmjs.com
returns essentially only packages with "mermaid" in the name — 20 of the top 20,
measured rather than assumed — so an otherwise-named package cannot be found by
the people it is for. Keywords alone are not enough.

The last row is why the project is called Merpeeps and not Ariel. "Merpeeps" is a
coined word from *mer-* and *peeps*, owned by nobody and checked against npm,
crates.io, NuGet and GitHub before adoption. **Disney holds USPTO registration
3814910 for ARIEL covering Class 9 computer software**, and renewed it in 2020 —
so a mermaid-adjacent developer tool called Ariel would sit directly inside a
live registration in its own class. Sebastian, Ursula, Flounder and King Triton
carry the same character association. Famous marks also attract **dilution**
claims under 15 U.S.C. §1125(c), which do not require proving any consumer
confusion at all.

**Check the neighbours, not just the register.** Before adopting a name, search
npm, crates.io, NuGet and GitHub **for tools in this domain**, not merely for
whether the name is free to publish under. Availability on a registry is a much
weaker signal than absence from the neighbourhood: a name can be unclaimed and
still be indistinguishable, to a user searching, from a project that already
exists. That is not a legal problem, and being legally in the right does not fix
it.

## Versioning

`SPEC.md` carries the format version (`spec: 1`); packages carry their own
semver. A breaking change to the on-disk format bumps `spec`, and readers must
keep understanding older values — files outlive tools.
