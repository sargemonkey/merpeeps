# Merpie preview releases

Desktop is cross-platform Avalonia/.NET. Downloads use `--self-contained true`:
users do not separately install .NET, but platform native dependencies remain.
Windows x64, Linux x64, Intel macOS x64 and Apple Silicon macOS arm64 are distinct
assets. OS-hosted tests plus manual platform acceptance are required; cross-build
success alone is not proof of a usable Mac app.

## Source of truth and draft-only workflow

`deployment/release.json` owns the tag and per-product versions, validated by
`scripts/get-release-info.ps1` against the csproj/npm versions. Shared packages
retain independent identities/version lines. Do not overwrite frozen artifacts.

After committing and pushing validated source, push the matching
tag from `deployment/release.json` or dispatch **Merpie draft release** with that tag.
The workflow checks out the exact tag, runs Core/Scene/App tests on native host
runners, publishes runtime-bundled platform packages, packs libraries/npm,
tests actual packed consumers, emits checksums, and creates a **draft
prerelease only**. It refuses to replace an existing release's assets. It does
not run `npm publish`, NuGet push, install shortcuts or publish the draft.

`deployment/release-notes.md` is the reviewed release body. Versions in a macOS
Info.plist are numeric; the complete prerelease version is in the package
filename/manifest and executable informational version.

## Manual package validation

Run `scripts/test-release-source.ps1`, `scripts/test-package-consumer.ps1` with
a fresh scratch directory, `npm test --prefix packages/merpeeps-mermaid-layout`,
`node oracle/package-consumer-test.mjs` and `node oracle/theme-export-test.mjs`.
The last check needs the oracle's existing Playwright/browser dependencies and
the built CLI. It checks actual browser contrast, uniformly translated imported
groups, route identity, source safety, and preview PNG/static SVG alpha offline.
Desktop packaging/checks are in the user guide. CI uses the same scripts.
Archives include SHA256 checksums and each package includes a manifest.
Use [platform acceptance](PLATFORM-ACCEPTANCE.md) to record human and signing
results separately. Automated tests are not human usability approval.

Preview.1 stays immutable. Preview.2 uses fresh prerelease versions, including a
Scene version-only increment, so newly built packages do not reuse earlier
attachment identities. Public release and registry publication remain separate.

## Publication gates

Before changing a draft to public: review the source commit/tag and every
attachment, complete OS-native launch/UX checks, fix or prominently disclose
known contrast/appearance issues, and configure signing for the intended audience.
Developer ID signing/notarization is required for normal macOS distribution.
Windows code signing, installers, updates, WinGet/Homebrew/store channels are
subsequent deployment steps, not claims made by this preview.

Registry publication requires a separate explicit approval and credentials.
Keep the npm companion's limited contract clear: all-family typed editing stays
in C# today. A browser/editor SDK with equivalent authoring is a separate port.
