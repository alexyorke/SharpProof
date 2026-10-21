Set-StrictMode -Version Latest

function Get-SharpProofSemanticCoverageShards {
    return , [Collections.Immutable.ImmutableArray]::Create[string](
        'semantic-gates-corpus-a', 'semantic-gates-corpus-b', 'semantic-gates-shards',
        'semantic-worker-a', 'semantic-worker-b-2', 'semantic-worker-b-3', 'semantic-analyzer',
        'semantic-projects-rest', 'semantic-extras', 'package-0', 'package-1', 'package-2', 'package-3')
}

function Get-SharpProofSemanticCoverageTasks {
    param([string]$RepositoryRoot)
    $projects = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw | ConvertFrom-Json).solution.projects
    $remaining = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($project in $projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension(([string]$project).Replace('\', '/')).ToLowerInvariant()
        if ($name -in @('sharpproof.gates.tests', 'sharpproof.architecture.tests', 'sharpproof.attributes.tests', 'sharpproof.analyzer.tests', 'sharpproof.fuzz.tests')) { continue }
        $remaining.Add('semantic-' + $name)
    }
    $remaining.AddRange([string[]]@('worker-core', 'worker-claim-manifest', 'worker-compiler-manifest'))
    return ([ordered]@{
            'semantic-gates-corpus-a' = @('gates-corpus')
            'semantic-gates-corpus-b' = @('gates-corpus')
            'semantic-gates-shards'   = @('gates-remainder-1', 'gates-remainder-2', 'gates-remainder-3')
            'semantic-worker-a'       = @('worker-remainder-1', 'worker-remainder-4', 'worker-fixed-checksum')
            'semantic-worker-b-2'     = @('worker-remainder-2')
            'semantic-worker-b-3'     = @('worker-remainder-3')
            'semantic-analyzer'       = @('semantic-sharpproof.analyzer.tests', 'semantic-sharpproof.fuzz.tests')
            'semantic-projects-rest'  = $remaining.ToImmutable()
            'semantic-extras'         = @('architecture')
        }).AsReadOnly()
}

function Assert-SharpProofSemanticCoveragePartitions {
    param([string]$Root, [string]$RepositoryRoot)
    . (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
    $plan = Get-SharpProofSemanticCoverageTasks -RepositoryRoot $RepositoryRoot
    foreach ($phase in $plan.Keys) {
        $cohort = Get-Content -LiteralPath (Join-Path $Root "$phase/semantic/coverage-cohort.json") -Raw | ConvertFrom-Json
        if ($cohort.schemaVersion -ne 1 -or
            (@($cohort.tasks | Sort-Object -CaseSensitive) -join "`n") -cne (@($plan[$phase] | Sort-Object -CaseSensitive) -join "`n")) {
            throw "Semantic coverage shard '$phase' does not match its complete task partition."
        }
    }
    $extras = Get-Content -LiteralPath (Join-Path $Root 'semantic-extras/coverage-cohort.json') -Raw | ConvertFrom-Json
    if ($extras.schemaVersion -ne 1 -or (@($extras.tasks | Sort-Object -CaseSensitive) -join "`n") -cne "attributes`nfuzz-console-cancellation") {
        throw 'Semantic coverage extras did not complete both isolated collectors.'
    }
    $universe = $null
    $assigned = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($part in @('a', 'b')) {
        $partRoot = Join-Path $Root "semantic-gates-corpus-$part/semantic"
        $discovered = @(Read-SharpProofCanonicalDiscovery -Root $partRoot -ExcludedCategories @('Performance', 'Coverage'))
        $names = @(Get-SharpProofTrxDiscoveryNames -Discovery $discovered | Sort-Object -CaseSensitive)
        if ($null -ne $universe -and ($universe -join "`n") -cne ($names -join "`n")) { throw 'Corpus coverage shards discovered different test universes.' }
        $universe = $names
        $expected = @($discovered | Where-Object {
                $snapshot = $_.fullyQualifiedName.StartsWith('SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.', [StringComparison]::Ordinal)
                if ($part -ceq 'a') { $snapshot } else { -not $snapshot }
            })
        $expectedNames = @(Get-SharpProofTrxDiscoveryNames -Discovery $expected | Sort-Object -CaseSensitive)
        $actual = @(Read-SharpProofCanonicalCases -Root $partRoot)
        $actualNames = @($actual | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName } | Sort-Object -CaseSensitive)
        if (($expectedNames -join "`n") -cne ($actualNames -join "`n") -or
            @($actual | Where-Object { $_.outcome -cnotin @('Passed', 'NotExecuted') }).Count) {
            throw 'Corpus coverage execution differs from its exact discovery partition.'
        }
        foreach ($receipt in Get-ChildItem -LiteralPath $partRoot -Recurse -Filter '*.trx' -File) {
            [xml]$trx = Get-Content -LiteralPath $receipt.FullName -Raw
            $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
            if ($null -eq $counters -or [int]$counters.failed -ne 0 -or [int]$counters.error -ne 0) { throw 'Corpus coverage has failed or invalid counters.' }
        }
        $assigned.AddRange([string[]]$actualNames)
    }
    if (($universe -join "`n") -cne (@($assigned.ToImmutable() | Sort-Object -CaseSensitive) -join "`n")) { throw 'Corpus coverage union differs from unpartitioned discovery.' }
    Write-Host "Semantic coverage partitions retain every task and all $($universe.Count) discovered corpus cases."
}
