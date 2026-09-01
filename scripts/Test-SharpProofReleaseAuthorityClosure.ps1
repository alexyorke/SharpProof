[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseAuthorityClosure.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofTcbPaths.ps1')
$inventoryJson = & (Join-Path $PSScriptRoot 'Get-SharpProofProductionInventory.ps1') `
    -RepositoryRoot $repositoryRoot `
    -Configuration Release
$productionInventory = $inventoryJson -join [Environment]::NewLine | ConvertFrom-Json
$contract = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/acceptance/contract.json') -Raw |
    ConvertFrom-Json

$derived = @(Get-SharpProofReleaseAuthorityClosure -RepositoryRoot $repositoryRoot)
$declared = @($contract.releaseAuthorityClosure.paths | ForEach-Object {
        [string]$_
    })
$missing = @($derived | Where-Object { $declared -cnotcontains $_ })
$extra = @($declared | Where-Object { $derived -cnotcontains $_ })
if ($declared.Count -ne @($declared | Select-Object -Unique).Count -or
    $derived.Count -ne $declared.Count -or
    $missing.Count -ne 0) {
    $details = @(
        "derived=$($derived.Count)",
        "declared=$($declared.Count)",
        "missing=$($missing.Count): $($missing -join ', ')",
        "extra=$($extra.Count): $($extra -join ', ')"
    ) -join '; '
    throw "The declared release-authority closure does not equal the independently derived closure: $details"
}
$tcb = @(Get-SharpProofTcbPaths -Contract $contract -ProductionInventory $productionInventory)
foreach ($path in $derived) {
    if (@($tcb | Where-Object { $_ -ceq $path }).Count -ne 1) {
        throw "Release-authority path must occur exactly once in the TCB: '$path'."
    }
}
Write-Host "Release-authority closure paths: $($derived.Count)"
