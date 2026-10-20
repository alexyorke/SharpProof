[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Reconciles downloaded Remaining/Gates discovery, TRX, and receipts. It reads
# only evidence files and Git identity, so CI runs it on the catalog-pinned
# PowerShell archive without building the tooling image; the container command
# runs the same script.
& (Join-Path $PSScriptRoot 'Test-SharpProofCanonicalEvidence.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
Assert-SharpProofCanonicalCohorts `
    -Root (Join-Path $RepositoryRoot 'artifacts/canonical-cohorts') `
    -RepositoryRoot $RepositoryRoot
