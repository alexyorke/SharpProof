[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$RepositoryCommit,
    [string]$InventoryPath = (Join-Path $PSScriptRoot '../release/third-party-components.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.repositoryCommit -cne $RepositoryCommit) {
    throw 'Release evidence has an unexpected schema or source commit.'
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-release-evidence-' + [Guid]::NewGuid().ToString('N'))
try {
    & (Join-Path $PSScriptRoot 'New-SharpProofReleaseEvidence.ps1') -PackageSource $PackageSource `
        -OutputDirectory $temporary -RepositoryCommit $RepositoryCommit -InventoryPath $InventoryPath -Timestamp $manifest.createdUtc
    foreach ($name in @('sbom.spdx.json', 'release-manifest.json', 'SHA256SUMS')) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $EvidenceDirectory $name) -Algorithm SHA256).Hash
        $expected = (Get-FileHash -LiteralPath (Join-Path $temporary $name) -Algorithm SHA256).Hash
        if ($actual -cne $expected) { throw "Release evidence differs from the actual qualified package payload: $name" }
    }
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
Write-Host 'Release archive/SBOM/manifest/checksum evidence validated. Signature verification is performed separately by gh attestation verify.'
