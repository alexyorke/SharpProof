[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$BuildMetadataPath,
    [Parameter(Mandatory)][ValidatePattern('\Asha256:[a-f0-9]{64}\z')][string]$ExpectedDigest,
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$ExpectedSourceCommit,
    [Parameter(Mandatory)][string]$ImageReference,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Load
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewImage.psm1') -Force
$output = [IO.Path]::GetFullPath($OutputPath)
if ($output -ceq [IO.Path]::GetFullPath($ArchivePath) -or $output -ceq [IO.Path]::GetFullPath($BuildMetadataPath)) {
    throw 'Receipt output must not replace immutable image evidence.'
}
$receipt = Get-SharpProofPrivatePreviewImageReceipt -ArchivePath $ArchivePath -BuildMetadataPath $BuildMetadataPath `
    -ExpectedDigest $ExpectedDigest -ExpectedSourceCommit $ExpectedSourceCommit -ImageReference $ImageReference -Load:$Load
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$temporary = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, (($receipt | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $output, $true)
}
finally { [IO.File]::Delete($temporary) }
Write-Host 'Private OCI image archive validated: original manifest/config/layer hashes and loaded config identity.'
