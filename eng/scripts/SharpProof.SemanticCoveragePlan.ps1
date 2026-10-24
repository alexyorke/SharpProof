Set-StrictMode -Version Latest

# This is a conservative build selection, not coverage authority. Build every
# non-test solution project before the collector derives its evaluated Compile
# items and PDB universe. The gate still recomputes that authority independently.
function Get-SharpProofCoverageAuthorityBuildProjects {
    param(
        [string]$RepositoryRoot,
        [AllowEmptyCollection()]
        [string[]]$AdditionalProjects = @()
    )
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    [xml]$solution = [IO.File]::ReadAllText((Join-Path $root 'SharpProof.slnx'))
    $solutionProjects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    $buildProjects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    foreach ($project in @($solution.SelectNodes('/Solution//Project'))) {
        $attribute = $project.Attributes['Path']
        if ($null -eq $attribute -or [string]::IsNullOrWhiteSpace($attribute.Value)) {
            throw 'Coverage build solution has a project without a Path.'
        }
        $candidate = $attribute.Value.Replace('\', '/')
        if ([IO.Path]::GetExtension($candidate) -cne '.csproj') { continue }
        $absolute = [IO.Path]::GetFullPath($candidate, $root)
        if (-not $absolute.StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw 'Coverage build solution has a project outside the repository.'
        }
        $relative = [IO.Path]::GetRelativePath($root, $absolute).Replace('\', '/')
        if (-not $solutionProjects.Add($relative)) {
            throw 'Coverage build solution has a duplicate project.'
        }
        if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
            throw "Coverage build solution project is absent: '$relative'."
        }
        # Repository layout requires these suffixes for all test projects.
        # Do not infer production membership from an unevaluated project flag.
        if ($relative -cnotmatch '\.(Tests|UnitTests|IntegrationTests|PerformanceTests)\.csproj$') {
            [void]$buildProjects.Add($relative)
        }
    }
    if ($buildProjects.Count -eq 0) {
        throw 'Coverage authority build has no non-test solution projects.'
    }
    foreach ($project in $AdditionalProjects) {
        if ([string]::IsNullOrWhiteSpace($project)) {
            throw 'Coverage authority build has an empty additional project.'
        }
        $absolute = [IO.Path]::GetFullPath($project.Replace('\', '/'), $root)
        $relative = [IO.Path]::GetRelativePath($root, $absolute).Replace('\', '/')
        if (-not $absolute.StartsWith($prefix, [StringComparison]::Ordinal) -or
            -not $solutionProjects.Contains($relative)) {
            throw 'Coverage authority build additional project is not in the solution.'
        }
        [void]$buildProjects.Add($relative)
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string](
        [string[]]@($buildProjects.ToImmutable() | Sort-Object -CaseSensitive))
}

function Get-SharpProofSemanticCoverageShards {
    return , [Collections.Immutable.ImmutableArray]::Create[string](
        'semantic-gates-corpus-a', 'semantic-gates-corpus-b', 'semantic-gates-shards',
        'semantic-worker-a-1', 'semantic-worker-a-4a', 'semantic-worker-a-4b', 'semantic-worker-b-2', 'semantic-worker-b-3', 'semantic-analyzer',
        'semantic-projects-rest', 'semantic-extras', 'package-0', 'package-1', 'package-2', 'package-3')
}

function Get-SharpProofSemanticCoverageTasks {
    param([string]$RepositoryRoot)
    $projects = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw | ConvertFrom-Json).solution.projects
    [xml]$solution = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.slnx') -Raw
    $solutionTests = @($solution.SelectNodes('//Project') | ForEach-Object { ([string]$_.Path).Replace('\', '/') } |
            Where-Object { $_ -cmatch '\.(Tests|UnitTests|IntegrationTests|PerformanceTests)\.csproj$' } |
            Sort-Object -CaseSensitive)
    $collectedTests = @(@($projects | ForEach-Object { ([string]$_).Replace('\', '/') }) + @(
            'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj',
            'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj') |
            Sort-Object -CaseSensitive)
    if (($solutionTests -join "`n") -cne ($collectedTests -join "`n")) {
        throw 'The coverage plan must retain every solution test project exactly once across the semantic filter and dedicated Worker/package collectors.'
    }
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
            'semantic-worker-a-1'     = @('worker-remainder-1', 'worker-fixed-checksum')
            'semantic-worker-a-4a'    = @('worker-remainder-4')
            'semantic-worker-a-4b'    = @('worker-remainder-4')
            'semantic-worker-b-2'     = @('worker-remainder-2')
            'semantic-worker-b-3'     = @('worker-remainder-3')
            'semantic-analyzer'       = @('semantic-sharpproof.analyzer.tests', 'semantic-sharpproof.fuzz.tests')
            'semantic-projects-rest'  = $remaining.ToImmutable()
            'semantic-extras'         = @('architecture')
        }).AsReadOnly()
}

# Each collector needs the complete production build plus only its own test
# projects. Canonical PR builds still compile the full solution with analyzers.
function Get-SharpProofCoverageBuildProjects {
    param([string]$RepositoryRoot, [string]$Phase)
    $plan = Get-SharpProofSemanticCoverageTasks -RepositoryRoot $RepositoryRoot
    $projects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    if ($Phase -cmatch '^package-[0-3]$') {
        [void]$projects.Add('src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj')
    }
    elseif ($Phase -ceq 'semantic-worker-a-4') {
        [void]$projects.Add('src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj')
    }
    elseif ($plan.Contains($Phase)) {
        $semanticProjects = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw | ConvertFrom-Json).solution.projects
        foreach ($task in $plan[$Phase]) {
            if ($task.StartsWith('semantic-', [StringComparison]::Ordinal)) {
                $projectMatches = @($semanticProjects | Where-Object { ('semantic-' + [IO.Path]::GetFileNameWithoutExtension(([string]$_).Replace('\', '/')).ToLowerInvariant()) -ceq $task })
                if ($projectMatches.Count -ne 1) { throw "Coverage task has no unique project: '$task'." }
                [void]$projects.Add(([string]$projectMatches[0]).Replace('\', '/'))
            }
            elseif ($task.StartsWith('worker-', [StringComparison]::Ordinal)) {
                [void]$projects.Add('src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj')
            }
            elseif ($task.StartsWith('gates-', [StringComparison]::Ordinal)) {
                [void]$projects.Add('src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj')
            }
            elseif ($task -ceq 'architecture') {
                [void]$projects.Add('src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj')
            }
            else { throw "Coverage build has an unknown task: '$task'." }
        }
        if ($Phase -ceq 'semantic-extras') {
            [void]$projects.Add('src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj')
            [void]$projects.Add('src/SharpProof.Fuzz.Tests/SharpProof.Fuzz.Tests.csproj')
        }
    }
    else { throw "Coverage build has an unknown phase: '$Phase'." }
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "Coverage test project is absent: '$project'." }
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($projects.ToImmutable() | Sort-Object -CaseSensitive))
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
    Assert-SharpProofWorkerA4CoveragePartitions -Root $Root
    Write-Host "Semantic coverage partitions retain every task and all $($universe.Count) discovered corpus cases."
}

# Keep complete fixture classes together; the complementary prefixes partition
# every current and future discovered case without changing runner scheduling.
function Test-SharpProofWorkerA4FirstPart {
    param([string]$Name)
    return $Name.StartsWith('SharpProof.Worker.Test.Native', [StringComparison]::Ordinal) -or
    $Name.StartsWith('SharpProof.Worker.Test.Actual', [StringComparison]::Ordinal)
}

function Assert-SharpProofWorkerA4CoveragePartitions {
    param([string]$Root)
    $universe = $null
    $assigned = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($part in @('a', 'b')) {
        $partRoot = Join-Path $Root "semantic-worker-a-4$part/semantic"
        $discovered = @(Read-SharpProofCanonicalDiscovery -Root $partRoot -ExcludedCategories @('Performance', 'Coverage'))
        $names = @(Get-SharpProofTrxDiscoveryNames -Discovery $discovered | Sort-Object -CaseSensitive)
        if ($null -ne $universe -and ($universe -join "`n") -cne ($names -join "`n")) { throw 'Worker A4 coverage shards discovered different test universes.' }
        $universe = $names
        $expected = @($discovered | Where-Object {
                $snapshot = Test-SharpProofWorkerA4FirstPart -Name $_.fullyQualifiedName
                if ($part -ceq 'a') { $snapshot } else { -not $snapshot }
            })
        $expectedNames = @(Get-SharpProofTrxDiscoveryNames -Discovery $expected | Sort-Object -CaseSensitive)
        $actual = @(Read-SharpProofCanonicalCases -Root $partRoot)
        $actualNames = @($actual | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName } | Sort-Object -CaseSensitive)
        if (($expectedNames -join "`n") -cne ($actualNames -join "`n") -or
            @($actual | Where-Object { $_.outcome -cnotin @('Passed', 'NotExecuted') }).Count) {
            throw 'Worker A4 coverage execution differs from its exact discovery partition.'
        }
        foreach ($receipt in Get-ChildItem -LiteralPath $partRoot -Recurse -Filter '*.trx' -File) {
            [xml]$trx = Get-Content -LiteralPath $receipt.FullName -Raw
            $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
            if ($null -eq $counters -or [int]$counters.failed -ne 0 -or [int]$counters.error -ne 0) { throw 'Worker A4 coverage has failed or invalid counters.' }
        }
        $assigned.AddRange([string[]]$actualNames)
    }
    if (($universe -join "`n") -cne (@($assigned.ToImmutable() | Sort-Object -CaseSensitive) -join "`n")) { throw 'Worker A4 coverage union differs from unpartitioned discovery.' }
    Write-Host "Worker A4 partitions retain all $($universe.Count) discovered cases exactly once."
}
