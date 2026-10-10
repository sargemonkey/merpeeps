[CmdletBinding()]
param([string] $OutputPath)
$ErrorActionPreference = 'Stop'
$windowsCertificates = @()
if ([OperatingSystem]::IsWindows()) {
    $windowsCertificates = @(Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
        Select-Object Subject, NotAfter)
}
$report = [ordered]@{
    checkedAt = [DateTimeOffset]::UtcNow.ToString('o')
    windowsLocalCertificateAvailable = $windowsCertificates.Count -gt 0
    windowsLocalCertificates = $windowsCertificates
    windowsSigningEnvironmentNamesPresent = @(@('WINDOWS_SIGNING_CERTIFICATE', 'WINDOWS_SIGNING_PASSWORD') |
        Where-Object { [Environment]::GetEnvironmentVariable($_) })
    macSigningEnvironmentNamesPresent = @(@('MACOS_SIGNING_CERTIFICATE', 'MACOS_SIGNING_PASSWORD', 'APPLE_NOTARY_KEY', 'APPLE_NOTARY_KEY_ID', 'APPLE_NOTARY_ISSUER_ID') |
        Where-Object { [Environment]::GetEnvironmentVariable($_) })
    signed = $false
    notarized = $false
    note = 'Local certificate/environment inspection only; not a GitHub secrets inventory. No secrets/private keys are emitted, certificates created, or signing performed.'
}
$json = $report | ConvertTo-Json -Depth 4
if ($OutputPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false)) }
$json
