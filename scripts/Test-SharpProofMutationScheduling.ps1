[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationScheduling.psm1') -Force

$mutations = @(
    0..11 | ForEach-Object {
        [pscustomobject]@{
            Name = "mutation-$_"
            Project = if ($_ -lt 4) { 'slow' } else { 'fast' }
        }
    })
$weights = @{ slow = 8; fast = 1 }
$first = Get-SharpProofWeightedMutationShards `
    -Mutations $mutations -ShardCount 4 -ProjectWeights $weights -DefaultWeight 1
$second = Get-SharpProofWeightedMutationShards `
    -Mutations $mutations -ShardCount 4 -ProjectWeights $weights -DefaultWeight 1

$ordinals = @($first.Shards | ForEach-Object { $_ } |
    ForEach-Object CatalogOrdinal)
if (($ordinals | Sort-Object) -join ',' -cne ((0..11) -join ',')) {
    throw 'Weighted mutation shards do not cover every catalog ordinal once.'
}
foreach ($shard in $first.Shards) {
    $actual = @($shard | ForEach-Object CatalogOrdinal)
    $sorted = @($actual | Sort-Object)
    if (($actual -join ',') -cne ($sorted -join ',')) {
        throw 'Mutation order within a shard is not canonical.'
    }
}
$firstShape = @($first.Shards | ForEach-Object {
        (@($_ | ForEach-Object CatalogOrdinal) -join ',')
    }) -join '|'
$secondShape = @($second.Shards | ForEach-Object {
        (@($_ | ForEach-Object CatalogOrdinal) -join ',')
    }) -join '|'
if ($firstShape -cne $secondShape) {
    throw 'Weighted mutation scheduling is not deterministic.'
}
$minimumLoad = ($first.Loads | Measure-Object -Minimum).Minimum
$maximumLoad = ($first.Loads | Measure-Object -Maximum).Maximum
if ($maximumLoad - $minimumLoad -gt 2) {
    throw "Weighted mutation shards are unexpectedly imbalanced: $($first.Loads -join ',')."
}

Write-Host "Weighted mutation scheduling fixtures passed: $firstShape."
