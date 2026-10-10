[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AppPublishPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')][string] $RuntimeIdentifier,
    [string] $McpPublishPath,
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string] $Version = '0.1.0-preview.1',
    [switch] $SelfContained
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AppPublishPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if ($output.Equals($source, $comparison) -or $output.StartsWith($source.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'Release output must not be inside the published app directory.'
}
$templates = Join-Path $PSScriptRoot '..\deployment\desktop'
$executable = if ($RuntimeIdentifier -eq 'win-x64') { 'Merpie.App.exe' } else { 'Merpie.App' }
$brand = Join-Path $source 'Assets\Brand'
if (!(Test-Path -LiteralPath (Join-Path $source $executable) -PathType Leaf)) {
    throw "The published Merpie executable is missing: $executable"
}
foreach ($asset in 'merpie-app-icon.ico', 'merpie-app-icon.icns', 'merpie-app-icon-512.png', 'merpie-mascot.svg', 'merpie-mascot-dark.svg') {
    if (!(Test-Path -LiteralPath (Join-Path $brand $asset) -PathType Leaf)) {
        throw "Published branding asset is missing: $asset"
    }
    $runtimeLibrary = if ($RuntimeIdentifier -eq 'win-x64') { 'coreclr.dll' }
        elseif ($RuntimeIdentifier.StartsWith('osx-')) { 'libcoreclr.dylib' } else { 'libcoreclr.so' }
    if ($SelfContained -and !(Test-Path -LiteralPath (Join-Path $source $runtimeLibrary) -PathType Leaf)) {
        throw 'The requested self-contained app output does not contain its .NET runtime.'
    }
}
if ($McpPublishPath) {
    $mcp = (Resolve-Path -LiteralPath $McpPublishPath).Path
    if (!(Test-Path -LiteralPath (Join-Path $mcp 'Merpeeps.Mcp.dll') -PathType Leaf)) {
        throw 'The published MCP server is missing.'
    }
    if ($SelfContained -and !(Test-Path -LiteralPath (Join-Path $mcp $runtimeLibrary) -PathType Leaf)) {
        throw 'The requested self-contained MCP output does not contain its .NET runtime.'
    }
}
$package = Join-Path $output "merpie-$Version-$RuntimeIdentifier"
$extension = if ($RuntimeIdentifier -eq 'win-x64') { '.zip' } else { '.tar.gz' }
$archive = $package + $extension
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $archive)) {
    throw "Refusing to overwrite an existing release package: $package"
}
New-Item -ItemType Directory -Path $package -Force | Out-Null
$app = if ($RuntimeIdentifier.StartsWith('osx-')) {
    Join-Path $package 'Merpie.app\Contents\MacOS'
} else {
    Join-Path $package 'app'
}
New-Item -ItemType Directory -Path $app -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $app -Recurse
if ($McpPublishPath) {
    $server = Join-Path $package 'mcp'
    New-Item -ItemType Directory -Path $server | Out-Null
    Get-ChildItem -LiteralPath $mcp -Force | Copy-Item -Destination $server -Recurse
}
Copy-Item -LiteralPath (Join-Path $templates 'README.txt') -Destination $package
$readme = [IO.File]::ReadAllText((Join-Path $package 'README.txt')).Replace('@RUNTIME@',
    $(if ($SelfContained) { 'The .NET 10 runtime is bundled; no separate .NET installation is required.' }
      else { 'This framework-dependent build requires the .NET 10 runtime.' }))
[IO.File]::WriteAllText((Join-Path $package 'README.txt'), $readme, [Text.UTF8Encoding]::new($false))
if ($RuntimeIdentifier.StartsWith('osx-')) {
    $contents = Join-Path $package 'Merpie.app\Contents'
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $resources | Out-Null
    Copy-Item -LiteralPath (Join-Path $brand 'merpie-app-icon.icns') -Destination (Join-Path $resources 'merpie.icns')
    $plist = [IO.File]::ReadAllText((Join-Path $templates 'Merpie.Info.plist')).Replace('@VERSION@', $Version.Split('-')[0])
    [IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist, [Text.UTF8Encoding]::new($false))
} elseif ($RuntimeIdentifier -eq 'linux-x64') {
    $icons = Join-Path $package 'icons'
    New-Item -ItemType Directory -Path $icons | Out-Null
    Copy-Item -LiteralPath (Join-Path $brand 'merpie-app-icon-512.png') -Destination (Join-Path $icons 'merpie.png')
    Copy-Item -LiteralPath (Join-Path $templates 'Merpie.desktop') -Destination $package
    $launcher = [IO.File]::ReadAllText((Join-Path $templates 'merpie')).Replace("`r`n", "`n")
    [IO.File]::WriteAllText((Join-Path $package 'merpie'), $launcher, [Text.UTF8Encoding]::new($false))
}
if (![OperatingSystem]::IsWindows()) {
    $mode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute `
        -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupExecute `
        -bor [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherExecute
    [IO.File]::SetUnixFileMode((Join-Path $app $executable), $mode)
    if ($RuntimeIdentifier -eq 'linux-x64') { [IO.File]::SetUnixFileMode((Join-Path $package 'merpie'), $mode) }
}
$manifest = [ordered]@{
    product = 'Merpie'
    runtime = $RuntimeIdentifier
    version = $Version
    selfContained = [bool]$SelfContained
    executable = [IO.Path]::GetRelativePath($package, (Join-Path $app $executable))
    metadataKey = 'x-merpeeps'
    editorOverride = 'MERPEEPS_APP'
    files = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($package, $_.FullName)
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'release-manifest.json') -Encoding utf8
if ($RuntimeIdentifier -eq 'win-x64') {
    Compress-Archive -LiteralPath $package -DestinationPath $archive
} else {
    $archiveStream = [IO.File]::Create($archive)
    $gzip = [IO.Compression.GZipStream]::new($archiveStream, [IO.Compression.CompressionLevel]::Optimal)
    $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $package -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($output, $file.FullName).Replace('\', '/')
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $relative)
            $entry.Mode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite `
                -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
            if ($file.FullName -eq (Join-Path $app $executable) -or
                $file.FullName -eq (Join-Path $package 'merpie') -or
                $file.Name -eq 'Merpeeps.Mcp') {
                $entry.Mode = $entry.Mode -bor [IO.UnixFileMode]::UserExecute `
                    -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
            }
            $stream = [IO.File]::OpenRead($file.FullName)
            try { $entry.DataStream = $stream; $writer.WriteEntry($entry) }
            finally { $stream.Dispose() }
        }
    } finally { $writer.Dispose(); $gzip.Dispose(); $archiveStream.Dispose() }
}
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', "$checksum  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Product = 'Merpie'; Runtime = $RuntimeIdentifier; Package = $package; Archive = $archive; Files = $manifest.files.Count }
