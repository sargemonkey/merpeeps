[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $WorkDirectory,
    [string] $DependencySource = 'https://api.nuget.org/v3/index.json',
    [switch] $SkipNuGetAudit
)
$ErrorActionPreference = 'Stop'
$release = & (Join-Path $PSScriptRoot 'get-release-info.ps1')
$packages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw "Refusing to reuse package consumer state: $work" }
foreach ($file in "Merpeeps.Core.$($release.core).nupkg", "Merpeeps.Render.$($release.render).nupkg", "Merpeeps.Scene.$($release.scene).nupkg") {
    if (!(Test-Path -LiteralPath (Join-Path $packages $file))) { throw "Release package missing: $file" }
}
New-Item -ItemType Directory -Path $work -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\tests\PackageConsumer') -File |
    Copy-Item -Destination $work
$project = Join-Path $work 'PackageConsumer.csproj'
$versions = @("-p:CorePackageVersion=$($release.core)", "-p:RenderPackageVersion=$($release.render)", "-p:ScenePackageVersion=$($release.scene)")
$config = [xml]'<configuration><packageSources><clear/><add key="release" value=""/><add key="dependencies" value=""/></packageSources></configuration>'
$config.configuration.packageSources.add[0].SetAttribute('value', $packages)
$config.configuration.packageSources.add[1].SetAttribute('value', $DependencySource)
$configPath = Join-Path $work 'NuGet.Config'
$config.Save($configPath)
$audit = @()
if ($SkipNuGetAudit) { $audit = @('-p:NuGetAudit=false') }
& dotnet restore $project --configfile $configPath --packages (Join-Path $work 'packages') @versions @audit
if ($LASTEXITCODE -ne 0) { throw 'Clean packed-package restore failed.' }
$env:MERPIE_PACKAGE_REPORT = Join-Path $work 'family-results.json'
& dotnet run --project $project -c Release --no-restore @versions
if ($LASTEXITCODE -ne 0) { throw 'The actual packed NuGet consumers failed.' }
