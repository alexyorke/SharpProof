[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PlanPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanIdentity.psm1') -Force

$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
Test-SharpProofPublicationPlanIdentity -Plan $plan
Write-Host 'Publication plan identities are valid.'
