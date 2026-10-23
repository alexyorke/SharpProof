[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Reconciles downloaded Remaining/Gates discovery, TRX, and receipts. It reads
# only evidence files and Git identity. CI runs this gate in the canonical
# container without compiling repository projects.
& (Join-Path $PSScriptRoot 'Test-SharpProofCanonicalEvidence.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
$artifactAttempts = if ([string]::IsNullOrWhiteSpace($env:SHARPPROOF_COHORT_ARTIFACT_ATTEMPTS)) {
    $null
}
else {
    ConvertFrom-Json -InputObject $env:SHARPPROOF_COHORT_ARTIFACT_ATTEMPTS -AsHashtable
}
Assert-SharpProofCanonicalCohorts `
    -Root (Join-Path $RepositoryRoot 'artifacts/canonical-cohorts') `
    -RepositoryRoot $RepositoryRoot `
    -ArtifactAttempts $artifactAttempts
