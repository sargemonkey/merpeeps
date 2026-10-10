[CmdletBinding()]
param([string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'get-release-info.ps1') | Out-Null
& dotnet build -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Release source build failed.' }
$results = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release-tests'
New-Item -ItemType Directory -Path $results -Force | Out-Null
foreach ($suite in 'Core', 'Scene') {
    $dll = (Resolve-Path -LiteralPath "tests\Merpeeps.$suite.Tests\bin\$Configuration\net10.0\Merpeeps.$suite.Tests.dll").Path
    & dotnet vstest $dll "--logger:trx;LogFileName=$suite.trx" "--ResultsDirectory:$results"
    if ($LASTEXITCODE -ne 0) { throw "$suite tests failed." }
    [xml]$trx = Get-Content -Raw -LiteralPath (Join-Path $results "$suite.trx")
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.total -eq 0 -or [int]$counts.failed -ne 0) { throw "$suite tests did not execute successfully." }
}
$appDll = (Resolve-Path -LiteralPath "tests\Merpeeps.App.Tests\bin\$Configuration\net10.0\Merpeeps.App.Tests.dll").Path
& dotnet $appDll -noLogo -noColor -parallel none -xml (Join-Path $results 'app.xml')
if ($LASTEXITCODE -ne 0) { throw 'Native App tests failed.' }
[xml]$app = Get-Content -Raw -LiteralPath (Join-Path $results 'app.xml')
if ([int]$app.assemblies.assembly.total -eq 0 -or [int]$app.assemblies.assembly.failed -ne 0) {
    throw 'Native App tests did not execute successfully.'
}
