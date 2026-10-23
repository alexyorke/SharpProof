[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HarnessRoot,
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperHarness.psm1') -Force
Invoke-SharpProofDeveloperHarnessTests -HarnessRoot $HarnessRoot -RepositoryRoot $repositoryRoot -PackageSource $PackageSource -ResultsDirectory $ResultsDirectory
