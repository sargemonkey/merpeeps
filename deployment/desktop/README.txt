Merpie - native visual editor for Mermaid diagrams

Windows: open app\Merpie.App.exe. Its EXE and windows use the approved Merpie icon.
Linux: run ./merpie. Merpie.desktop and icons/merpie.png are supplied for desktop
integration; the desktop entry expects the launcher on PATH and the icon installed
as "merpie". The portable package does not install either globally.
macOS: open Merpie.app. It contains the Merpie ICNS icon and application metadata.

@RUNTIME@
Native platform dependencies are still required. The optional real-Mermaid pane requires
WebView2 (Windows), WKWebView (macOS) or WebKitGTK (Linux).

The optional mcp/ directory contains the existing Merpeeps.Mcp server.
The merpeeps CLI/MCP names, MERPEEPS_APP override, x-merpeeps metadata and
Merpeeps package/library IDs are unchanged. Existing .mmd files need no migration.
Set MERPEEPS_APP to the app executable (or Merpie.app bundle) if a CLI installed
elsewhere cannot locate this package.

This is an unsigned portable build, not an installer. Packaging does not install
shortcuts, register file associations, replace an existing app, or change PATH.
No macOS notarization or Windows signing is implied. A macOS apphost may carry
its toolchain's ad-hoc signature, which is not Developer ID distribution signing.
This preview is not an instruction to bypass an operating-system trust warning.
