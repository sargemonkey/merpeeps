[CmdletBinding()]
param([string] $Tag)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Get-Content -Raw -LiteralPath (Join-Path $root 'deployment\release.json') | ConvertFrom-Json
if ($Tag -and $Tag -cne $release.tag) { throw "Release tag '$Tag' does not match source metadata '$($release.tag)'." }
if ($release.tag -cne "merpie-v$($release.desktop)") { throw 'Desktop release tag and version disagree.' }
$projects = @{
    'src\Merpeeps.App\Merpeeps.App.csproj' = $release.desktop
    'src\Merpeeps.Cli\Merpeeps.Cli.csproj' = $release.desktop
    'src\Merpeeps.Mcp\Merpeeps.Mcp.csproj' = $release.desktop
    'src\Merpeeps.Core\Merpeeps.Core.csproj' = $release.core
    'src\Merpeeps.Render\Merpeeps.Render.csproj' = $release.render
    'src\Merpeeps.Scene\Merpeeps.Scene.csproj' = $release.scene
}
foreach ($project in $projects.Keys) {
    [xml]$xml = Get-Content -Raw -LiteralPath (Join-Path $root $project)
    $version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if ($version -cne $projects[$project]) { throw "Version mismatch in $project." }
}
$npm = Get-Content -Raw -LiteralPath (Join-Path $root 'packages\merpeeps-mermaid-layout\package.json') | ConvertFrom-Json
if ($npm.version -cne $release.npm) { throw 'npm version and release metadata disagree.' }
$release
