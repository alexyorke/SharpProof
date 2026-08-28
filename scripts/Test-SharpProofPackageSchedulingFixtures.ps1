[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageScheduling.psm1') -Force

$shards = @(
    [pscustomobject]@{ Name = 'medium'; EstimatedMilliseconds = 10L },
    [pscustomobject]@{ Name = 'tie-b'; EstimatedMilliseconds = 100L },
    [pscustomobject]@{ Name = 'unknown'; EstimatedMilliseconds = $null },
    [pscustomobject]@{ Name = 'slow'; EstimatedMilliseconds = 500L },
    [pscustomobject]@{ Name = 'tie-a'; EstimatedMilliseconds = 100L })
$ordered = @(Get-SharpProofPackageShardSchedule -Shards $shards)
if (($ordered | ForEach-Object Name) -join '|' -cne
    'slow|tie-a|tie-b|medium|unknown') {
    throw 'Package shard scheduling is not weighted longest-first and deterministic.'
}

$selected = @(
    Get-SharpProofPackageShardSchedule -Shards @(
        [pscustomobject]@{ Name = 'selected'; EstimatedMilliseconds = $null }))
if ($selected.Count -ne 1 -or $selected[0].Name -cne 'selected') {
    throw 'A selected package shard was not retained by the scheduler.'
}

Write-Host 'Package scheduling fixtures passed.'
