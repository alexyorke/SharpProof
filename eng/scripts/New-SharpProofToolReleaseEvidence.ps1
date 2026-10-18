[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$RepositoryCommit
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
$sourceFiles = @(Get-ChildItem -LiteralPath $PackageSource -File)
$files = @($sourceFiles | Where-Object Extension -CEQ '.nupkg')
if ($files.Count -ne 1) { throw 'Tool release requires exactly one separately qualified nupkg.' }
$allowedSidecars = @('packages.tsv', 'packages.lock.json', 'Test-SharpProofPublishedPackages.sh')
if (@($sourceFiles | Where-Object { $_.Extension -cne '.nupkg' -and $_.Name -cnotin $allowedSidecars }).Count -ne 0) {
    throw 'Unexpected file in the separately qualified tool feed.'
}
if ($files[0].Name -cnotmatch '^[A-Za-z0-9_.+-]+\.nupkg$') { throw 'Unsafe optional tool package filename.' }
$identity = Get-SharpProofPackageIdentity -Path $files[0].FullName -RequireRepository -RequireSingleIdentity
$core = Get-Content -LiteralPath (Join-Path $OutputDirectory 'release-manifest.json') -Raw | ConvertFrom-Json
if ($identity.Id -cne 'SharpProof.Tool' -or $identity.Version -cne $core.packageVersion -or
    $identity.RepositoryCommit -cne $RepositoryCommit -or $core.repositoryCommit -cne $RepositoryCommit) {
    throw 'Tool release identity/version/source differs from the qualified release.'
}
$digest = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
[ordered]@{
    schemaVersion = 1; repositoryCommit = $RepositoryCommit; packageId = $identity.Id; version = $identity.Version
    fileName = $files[0].Name; bytes = $files[0].Length; sha256 = $digest
    scope = 'Qualified optional tool archive provenance only. The core-package SBOM does not describe this separate dependency closure.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'tool-release-manifest.json') -Encoding utf8NoBOM
"$digest  $($files[0].Name)" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'TOOL-SHA256SUMS') -Encoding utf8NoBOM
Write-Host 'Optional tool identity/version/source and archive digest recorded separately from the core SBOM.'
