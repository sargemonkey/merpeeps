[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'oracle\node_modules\@zenuml\core'
$destination = Join-Path $repo 'src\Merpeeps.Render\Assets\vendor\zenuml'
$package = Get-Content -LiteralPath (Join-Path $source 'package.json') -Raw | ConvertFrom-Json
if ($package.version -ne '3.47.9') {
    throw "Expected the pinned @zenuml/core 3.47.9 package, found $($package.version)."
}
$bundle = Join-Path $source 'dist\zenuml.js'
$license = Join-Path $source 'LICENSE'
if (-not (Test-Path -LiteralPath $bundle) -or -not (Test-Path -LiteralPath $license)) {
    throw 'The installed ZenUML package is missing its bundle or license.'
}
if ([IO.File]::ReadAllText($bundle) -match '(?i)</script') {
    throw 'The upstream bundle contains an HTML script terminator; embedding needs review.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $bundle -Destination (Join-Path $destination 'zenuml.js')
Copy-Item -LiteralPath $license -Destination (Join-Path $destination 'LICENSE')
[IO.File]::WriteAllText((Join-Path $destination '.version'), $package.version)
node (Join-Path $PSScriptRoot 'zenuml-license-notices.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Could not collect the bundled dependency license notices.' }
Write-Host "Vendored standalone ZenUML parser $($package.version) and its MIT license."
