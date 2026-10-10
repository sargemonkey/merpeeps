<#
.SYNOPSIS
    Refresh the vendored mermaid bundle from oracle/node_modules.

.DESCRIPTION
    The comparison pane renders against a pinned copy of mermaid so that a visual
    difference is a real difference rather than an upstream release landing
    underneath us. This script re-vendors that copy and records the version.

    Run `npm ci` in oracle/ first — this reads from oracle/node_modules.

    After bumping, re-run the oracle harnesses: the vendored bundle is the
    reference the editor compares against, so changing it changes what
    "correct" looks like.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'oracle\node_modules\mermaid'
$dest = Join-Path $repo 'src\Merpeeps.Render\Assets\vendor\mermaid'

if (-not (Test-Path $src)) {
    throw "mermaid not found at $src. Run 'npm ci' in oracle/ first."
}

$bundle = Join-Path $src 'dist\mermaid.min.js'
$licence = Join-Path $src 'LICENSE'

foreach ($f in @($bundle, $licence)) {
    if (-not (Test-Path $f)) { throw "expected file missing: $f" }
}

New-Item -ItemType Directory -Path $dest -Force | Out-Null

$version = (Get-Content (Join-Path $src 'package.json') -Raw | ConvertFrom-Json).version
$previous = if (Test-Path (Join-Path $dest '.version')) {
    (Get-Content (Join-Path $dest '.version') -Raw).Trim()
} else { '(none)' }

Copy-Item $bundle  (Join-Path $dest 'mermaid.min.js') -Force
Copy-Item $licence (Join-Path $dest 'LICENSE') -Force
$version | Set-Content (Join-Path $dest '.version') -NoNewline

$size = (Get-Item (Join-Path $dest 'mermaid.min.js')).Length / 1KB

Write-Host "vendored mermaid $version (was $previous), $([math]::Round($size)) KB"
if ($previous -ne '(none)' -and $previous -ne $version) {
    Write-Warning "Version changed. Re-run the oracle harnesses - the reference render has moved."
}
