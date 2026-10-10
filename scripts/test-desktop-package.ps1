[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [Parameter(Mandatory)][string] $ArchivePath
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'release-manifest.json') | ConvertFrom-Json
if ($manifest.product -ne 'Merpie' -or $manifest.metadataKey -ne 'x-merpeeps' -or $manifest.editorOverride -ne 'MERPEEPS_APP') {
    throw 'Product branding or compatibility identities are wrong.'
}
if (!(Test-Path -LiteralPath (Join-Path $package $manifest.executable) -PathType Leaf)) {
    throw 'The declared Merpie executable is missing.'
}
foreach ($file in $manifest.files) {
    $actual = Join-Path $package $file.path
    if (!(Test-Path -LiteralPath $actual -PathType Leaf) -or (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Release file is missing or changed: $($file.path)"
    }
}
if ($manifest.selfContained) {
    $library = if ($manifest.runtime -eq 'win-x64') { 'coreclr.dll' }
        elseif ($manifest.runtime.StartsWith('osx-')) { 'libcoreclr.dylib' } else { 'libcoreclr.so' }
    $app = Split-Path -Parent (Join-Path $package $manifest.executable)
    if (!(Test-Path -LiteralPath (Join-Path $app $library))) { throw 'Self-contained app runtime is absent.' }
    if (Test-Path -LiteralPath (Join-Path $package 'mcp')) {
        if (!(Test-Path -LiteralPath (Join-Path $package "mcp\$library"))) { throw 'Self-contained MCP runtime is absent.' }
    }
}
if ($manifest.runtime.StartsWith('osx-')) {
    $contents = Join-Path $package 'Merpie.app\Contents'
    $plist = [IO.File]::ReadAllText((Join-Path $contents 'Info.plist'))
    foreach ($value in 'Merpie', 'Merpie.App', 'merpie.icns', $manifest.version.Split('-')[0]) {
        if (!$plist.Contains("<string>$value</string>")) { throw "Bundle metadata is missing $value." }
    }
    $icon = Join-Path $contents 'Resources\merpie.icns'
    if ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($icon), 0, 4) -ne 'icns') { throw 'Invalid macOS icon container.' }
} elseif ($manifest.runtime -eq 'linux-x64') {
    $entry = [IO.File]::ReadAllText((Join-Path $package 'Merpie.desktop'))
    if (!$entry.Contains('Name=Merpie') -or !$entry.Contains('Exec=merpie %F') -or !$entry.Contains('Icon=merpie')) {
        throw 'The Linux desktop entry is inconsistent.'
    }
    $launcher = [IO.File]::ReadAllText((Join-Path $package 'merpie'))
    if ($launcher.Contains("`r") -or !$launcher.Contains('app/Merpie.App')) { throw 'The Linux launcher has invalid line endings or target.' }
    if (!(Test-Path -LiteralPath (Join-Path $package 'icons\merpie.png'))) { throw 'The Linux icon is missing.' }
}
if ($archive.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if (!($zip.Entries | Where-Object { $_.FullName.EndsWith('/' + $manifest.executable.Replace('\', '/')) })) {
            throw 'The archive is missing its declared executable.'
        }
    } finally { $zip.Dispose() }
} else {
    $archiveStream = [IO.File]::OpenRead($archive)
    $gzip = [IO.Compression.GZipStream]::new($archiveStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [System.Formats.Tar.TarReader]::new($gzip, $true)
    $hasExecutable = $false
    $hasLauncher = $manifest.runtime -ne 'linux-x64'
    try {
        while ($null -ne ($entry = $reader.GetNextEntry())) {
            if ($entry.Name.EndsWith('/' + $manifest.executable.Replace('\', '/'))) {
                if (!($entry.Mode -band [IO.UnixFileMode]::UserExecute)) { throw 'Archived app is not executable.' }
                $hasExecutable = $true
            }
            if ($manifest.runtime -eq 'linux-x64' -and $entry.Name.EndsWith('/merpie')) {
                if (!($entry.Mode -band [IO.UnixFileMode]::UserExecute)) { throw 'Archived Linux launcher is not executable.' }
                $hasLauncher = $true
            }
        }
    } finally { $reader.Dispose(); $gzip.Dispose(); $archiveStream.Dispose() }
    if (!$hasExecutable -or !$hasLauncher) { throw 'The archive is missing its executable or launcher.' }
}
if (![OperatingSystem]::IsWindows() -and $manifest.runtime -ne 'win-x64') {
    $executables = @($manifest.executable)
    if ($manifest.runtime -eq 'linux-x64') { $executables += 'merpie' }
    foreach ($file in $executables) {
        $mode = [IO.File]::GetUnixFileMode((Join-Path $package $file))
        if (!($mode -band [IO.UnixFileMode]::UserExecute)) { throw "Executable permission is missing: $file" }
    }
}
$checksum = [IO.File]::ReadAllText($archive + '.sha256').Split(' ')[0].Trim()
if ($checksum -ne (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'The downloadable archive checksum does not match.'
}
"VERIFIED: Merpie $($manifest.runtime), $($manifest.files.Count) hashed files, branded executable/platform metadata and archive."
