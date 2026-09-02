param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PEMetadata.psm1') -Force
Get-SharpProofModuleVersionId -Path $Path
