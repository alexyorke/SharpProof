Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofWeightedMutationShards {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Mutations,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 16)]
        [int]$ShardCount,

        [Parameter(Mandatory = $true)]
        [Collections.IDictionary]$ProjectWeights,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$DefaultWeight
    )

    if ($Mutations.Count -lt $ShardCount) {
        throw 'Mutation shard count cannot exceed the mutation count.'
    }

    $buckets = [object[]]::new($ShardCount)
    $loads = [long[]]::new($ShardCount)
    for ($index = 0; $index -lt $ShardCount; $index++) {
        $buckets[$index] = [Collections.Generic.List[object]]::new()
    }

    $weighted = for ($index = 0; $index -lt $Mutations.Count; $index++) {
        $mutation = $Mutations[$index]
        $project = [string]$mutation.Project
        $weight = if ($ProjectWeights.Contains($project)) {
            [int]$ProjectWeights[$project]
        }
        else {
            $DefaultWeight
        }
        if ($weight -lt 1) {
            throw "Mutation project weight must be positive: $project."
        }
        [pscustomobject]@{
            Mutation = $mutation
            CatalogOrdinal = $index
            Weight = $weight
        }
    }

    $assignmentOrder = @($weighted | Sort-Object `
        @{ Expression = { [int]$_.Weight }; Descending = $true }, `
        @{ Expression = { [int]$_.CatalogOrdinal }; Descending = $false })
    foreach ($item in $assignmentOrder) {
        $target = 0
        for ($index = 1; $index -lt $ShardCount; $index++) {
            if ($loads[$index] -lt $loads[$target]) {
                $target = $index
            }
        }
        $buckets[$target].Add($item)
        $loads[$target] += [int]$item.Weight
    }

    $shards = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $ShardCount; $index++) {
        $shards.Add([object[]]@(
            $buckets[$index] | Sort-Object CatalogOrdinal))
    }
    return [pscustomobject]@{
        Shards = @($shards)
        Loads = @($loads)
    }
}

Export-ModuleMember -Function 'Get-SharpProofWeightedMutationShards'
