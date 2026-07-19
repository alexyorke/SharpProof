[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 6144,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 1200,

    [Parameter()]
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'Invoke-SharpProofBuild.ps1') `
    -Configuration $Configuration `
    -Full `
    -NoRestore:$NoRestore `
    -MemoryLimitMb $MemoryLimitMb `
    -TimeoutSeconds $TimeoutSeconds
exit $LASTEXITCODE
