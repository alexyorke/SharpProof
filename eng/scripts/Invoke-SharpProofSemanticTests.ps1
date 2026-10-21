[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [switch]$Fast,

    [switch]$Quiet,

    [switch]$ArchitectureOnly,

    [int]$TimeoutSeconds,

    [string]$TestFilter = '',

    [ValidateSet('All', 'Remaining', 'Gates')]
    [string]$SemanticCohort = 'All',

    [string]$TestResultsDirectory = '',

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = '',

    [switch]$AttributesCollectedSeparately,

    # Coverage may split the original corpus selection into snapshot workflows
    # and its complete complement. Analyzer, ProjectsRest, and Extras partition
    # the original Projects group; the isolated collectors run in Extras.
    # Legacy groups and All preserve the existing local task selections.
    [ValidateSet('All', 'GatesCorpus', 'GatesCorpusA', 'GatesCorpusB', 'GatesShards', 'Worker', 'WorkerA', 'WorkerA1', 'WorkerA4', 'WorkerB', 'WorkerB2', 'WorkerB3', 'Projects', 'Analyzer', 'ProjectsRest', 'Extras')]
    [string]$TaskGroup = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path `
        $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer `
    'Semantic test sharding requires the canonical Linux container.'
Assert-SharpProofTestSwitches -Fast:$Fast -NoBuild:$NoBuild

$TimeoutSeconds = Resolve-SharpProofSolutionTestTimeoutSeconds `
    -RepositoryRoot $repositoryRoot `
    -TimeoutSeconds $TimeoutSeconds `
    -WasSpecified $PSBoundParameters.ContainsKey('TimeoutSeconds')
$parallelism = Get-SharpProofSemanticTestParallelism `
    -RepositoryRoot $repositoryRoot
$semanticSolutionFilter = Join-Path `
    $repositoryRoot 'SharpProof.Semantic.Tests.slnf'
$semanticSolution = Get-Content -LiteralPath $semanticSolutionFilter -Raw |
    ConvertFrom-Json
$semanticProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($semanticSolution.solution.projects |
            ForEach-Object { ([string]$_).Replace('\', '/') }))
$gatesProject = 'src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj'
$partitionFilter = ''
if ($SemanticCohort -ne 'All') {
    if ($TestFilter -cne 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus') {
        throw 'Canonical partitions require the unchanged canonical test filter.'
    }
    if ($ArchitectureOnly -or
        -not [string]::IsNullOrWhiteSpace($CoverageSettings) -or
        -not [string]::IsNullOrWhiteSpace($CoverageResultsDirectory) -or
        [string]::IsNullOrWhiteSpace($TestResultsDirectory)) {
        throw 'Canonical partitioning requires retained TRX results and does not support coverage or architecture-only selection.'
    }
    if (@($semanticProjects | Where-Object { $_ -ceq $gatesProject }).Count -ne 1) {
        throw 'The canonical semantic project list must contain exactly one Gates project.'
    }
    $semanticProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($semanticProjects | Where-Object {
                if ($SemanticCohort -eq 'Gates') { $_ -ceq $gatesProject }
                else { $_ -cne $gatesProject }
            }))
    $partitionFilter = Join-Path $repositoryRoot (
        '.sharpproof-canonical-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    ([ordered]@{ solution = ([ordered]@{
                path = 'SharpProof.slnx'; projects = $semanticProjects
            }).AsReadOnly()
    }).AsReadOnly() | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $partitionFilter -Encoding utf8NoBOM
    $semanticSolutionFilter = $partitionFilter
}
$coverageRequested =
-not [string]::IsNullOrWhiteSpace($CoverageSettings) -or
-not [string]::IsNullOrWhiteSpace($CoverageResultsDirectory)
if ($ArchitectureOnly -and $coverageRequested) {
    throw 'Architecture-only sharding does not support coverage collection.'
}
$coverage = New-SharpProofCoverageContext `
    -RepositoryRoot $repositoryRoot `
    -CoverageSettings $CoverageSettings `
    -CoverageResultsDirectory $CoverageResultsDirectory `
    -CreateResultsDirectory
$coverageEnabled = [bool]$coverage.Enabled
$resolvedCoverageSettings = [string]$coverage.Settings
$resolvedCoverageResults = [string]$coverage.Results
$isolatedOutputRoot = [string]$coverage.IsolatedOutputRoot

if ($AttributesCollectedSeparately -and -not $coverageEnabled) {
    throw 'Separate Attributes collection requires coverage collection.'
}
if ($TaskGroup -ne 'All' -and -not $coverageEnabled -and
    ($SemanticCohort -ne 'Remaining' -or $TaskGroup -notin @('Worker', 'WorkerA', 'WorkerB', 'Projects'))) {
    throw 'Without coverage, task groups select only Worker, WorkerA, WorkerB, or Projects of the Remaining cohort.'
}
if ($coverageEnabled -and $TaskGroup -eq 'Worker') {
    throw 'Coverage selects the Worker remainder shards as WorkerA and WorkerB.'
}
if ($coverageEnabled) {
    # Gates runs as its own task set below; its corpus verifier task reserves
    # every slot. Attributes has its own collector stage.
    # Architecture XML/DAG checks retain a separate strict noninstrumented TRX task.
    $semanticProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($semanticProjects | Where-Object {
                $_ -cne $gatesProject -and
                $_ -cne 'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj' -and
                (-not $AttributesCollectedSeparately -or
                $_ -cne 'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj')
            }))
    $partitionFilter = Join-Path $repositoryRoot (
        '.sharpproof-coverage-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    ([ordered]@{ solution = ([ordered]@{
                path = 'SharpProof.slnx'; projects = $semanticProjects
            }).AsReadOnly()
    }).AsReadOnly() | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $partitionFilter -Encoding utf8NoBOM
    $semanticSolutionFilter = $partitionFilter
}

if (-not $NoBuild) {
    $semanticBuildProjectBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    if ($ArchitectureOnly) {
        $semanticBuildProjectBuilder.Add('src\SharpProof.Architecture.Tests\SharpProof.Architecture.Tests.csproj')
    }
    elseif ($SemanticCohort -eq 'All') {
        foreach ($project in $semanticSolution.solution.projects) { $semanticBuildProjectBuilder.Add([string]$project) }
        $semanticBuildProjectBuilder.Add('src\SharpProof.Worker.Tests\SharpProof.Worker.Tests.csproj')
    }
    else {
        $semanticBuildProjectBuilder.AddRange($semanticProjects)
        if ($SemanticCohort -ne 'Gates') {
            $semanticBuildProjectBuilder.Add('src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj')
        }
    }
    $semanticBuildProjects = $semanticBuildProjectBuilder.ToImmutable()
    $semanticBuildFilter = Join-Path $repositoryRoot (
        '.sharpproof-semantic-build-' +
        [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        ([ordered]@{
            solution = ([ordered]@{
                    path     = 'SharpProof.slnx'
                    projects = $semanticBuildProjects
                }).AsReadOnly()
        }).AsReadOnly() | ConvertTo-Json -Depth 4 |
            Set-Content `
                -LiteralPath $semanticBuildFilter `
                -Encoding utf8NoBOM
        Invoke-SharpProofRequiredDotnet `
            -Arguments @('restore', $semanticBuildFilter, '--locked-mode') `
            -TimeoutSeconds $TimeoutSeconds `
            -Quiet:$Quiet
        $buildArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $buildArgumentBuilder.AddRange([string[]]@(
                'build', $semanticBuildFilter,
                '-c', $Configuration, '--no-restore'))
        if ($Fast) {
            $buildArgumentBuilder.Add('-p:RunAnalyzersDuringBuild=false')
        }
        Invoke-SharpProofRequiredDotnet `
            -Arguments ([string[]]@($buildArgumentBuilder.ToImmutable())) `
            -TimeoutSeconds $TimeoutSeconds `
            -Quiet:$Quiet
    }
    finally {
        if (Test-Path -LiteralPath $semanticBuildFilter) {
            Remove-Item -LiteralPath $semanticBuildFilter -Force
        }
    }
}

& (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration

$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingStem = 'semantic-tests-' + $Configuration.ToLowerInvariant()
$timingSuffix =
$(if ($ArchitectureOnly) { '-architecture-only' } else { '' }) +
$(if ($TaskGroup -ne 'All') { '-' + $TaskGroup.ToLowerInvariant() } else { '' }) +
$(if ($coverageEnabled) { '-coverage' } else { '' })
$canonicalTimingOutput = Join-Path $timingDirectory (
    $timingStem + $timingSuffix + '.json')
$timingOutput = Join-Path $timingDirectory (
    $timingStem + $(if ($Fast) { '-fast' } else { '' }) +
    $timingSuffix + '.json')
$priorDurationBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long]([StringComparer]::OrdinalIgnoreCase)
foreach ($priorTimingPath in $(if ($Fast) {
            @($canonicalTimingOutput, $timingOutput)
        }
        else {
            @($timingOutput)
        })) {
    if (-not (Test-Path -LiteralPath $priorTimingPath -PathType Leaf)) {
        continue
    }
    try {
        $prior = Get-Content -LiteralPath $priorTimingPath -Raw |
            ConvertFrom-Json
        foreach ($task in @($prior.tasks)) {
            $elapsed = [long]$task.elapsedMilliseconds
            if ($elapsed -gt 0) {
                $priorDurationBuilder[[string]$task.name] = $elapsed
            }
        }
    }
    catch {
        Write-Warning (
            "Ignoring malformed semantic timing '$priorTimingPath': " +
            $_.Exception.Message)
    }
}

$priorDurations = $priorDurationBuilder.ToImmutable()

$mainParallelism = [Math]::Max(
    1,
    [Math]::Floor($parallelism / 2))
$architectureShardingEnabled =
$coverageEnabled -or $ArchitectureOnly -or (
    -not $coverageEnabled -and
    [string]::IsNullOrWhiteSpace($TestFilter))
# Separate project results roots also prevent cross-project VSTest collector attachment races.
$semanticProjectShardingEnabled =
$architectureShardingEnabled -and -not $ArchitectureOnly
$workerClassPrefix = 'SharpProof.Worker.Test.'
New-Variable -Name workerDedicatedClasses -Option Constant -Value (
    [System.Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            'ClaimManifestBuilderTests',
            'CompilerManifestArtifactTests')))
$claimFilter =
'FullyQualifiedName~' + $workerClassPrefix + $workerDedicatedClasses[0]
$manifestFilter =
'FullyQualifiedName~' + $workerClassPrefix + $workerDedicatedClasses[1]
New-Variable -Name workerCoreClasses -Option Constant -Value (
    [System.Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            'WorkerTests',
            'WorkerProgramTests',
            'CompilerCallableLowererTests'
        )))
$workerCoreFilter = @($workerCoreClasses | ForEach-Object {
        'FullyQualifiedName~' + $workerClassPrefix + $_
    }) -join '|'
$workerRemainderFilter = @(
    @($workerDedicatedClasses) + @($workerCoreClasses) |
        ForEach-Object {
            'FullyQualifiedName!~' + $workerClassPrefix + $_
        }) -join '&'
# Preserve the measured upstream class-prefix partition, with an immutable
# outer vector and immutable ordered prefix groups. The final filter is the
# complement, so every current and future remainder class selects one shard.
$workerPrefixGroupBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$workerPrefixGroupBuilder.Add([Collections.Immutable.ImmutableArray]::Create[string]('NativeA'))
$workerPrefixGroupBuilder.Add([Collections.Immutable.ImmutableArray]::Create[string]('NativeS', 'C'))
$workerPrefixGroupBuilder.Add([Collections.Immutable.ImmutableArray]::Create[string]('W', 'T', 'G', 'F'))
$workerRemainderShardPrefixes = $workerPrefixGroupBuilder.ToImmutable()
$workerShardFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$workerComplementFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($prefixes in $workerRemainderShardPrefixes) {
    $includedPrefixes = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($prefix in $prefixes) {
        $includedPrefixes.Add('FullyQualifiedName~' + $workerClassPrefix + $prefix)
        $workerComplementFilterBuilder.Add('FullyQualifiedName!~' + $workerClassPrefix + $prefix)
    }
    $workerShardFilterBuilder.Add('(' + ($includedPrefixes.ToImmutable() -join '|') + ')')
}
$workerShardFilterBuilder.Add($workerComplementFilterBuilder.ToImmutable() -join '&')
$workerRemainderShardFilters = $workerShardFilterBuilder.ToImmutable()

$testProject = Join-Path $repositoryRoot (
    'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj')
$semanticFilter = if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
}
else {
    $TestFilter
}
$claimTaskFilter = "($claimFilter)&($semanticFilter)"
$manifestTaskFilter = "($manifestFilter)&($semanticFilter)"
$workerCoreTaskFilter = "($workerCoreFilter)&($semanticFilter)"
$workerRemainderTaskFilter = "($workerRemainderFilter)&($semanticFilter)"
$checksumMethod = $workerClassPrefix +
'FixedChecksumNormalWorkerEntryTests.FrozenChecksumUsesActualDefaultNativeWorker'
$checksumTaskFilter = "($workerRemainderTaskFilter)&(FullyQualifiedName=$checksumMethod)"
if ($coverageEnabled) {
    $workerRemainderTaskFilter = [string]::Concat($workerRemainderTaskFilter, "&(FullyQualifiedName!=$checksumMethod)")
}
$workerRemainderNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
for ($index = 1; $index -le $workerRemainderShardFilters.Length; $index++) {
    $workerRemainderNameBuilder.Add('worker-remainder-' + $index)
}
$workerRemainderTaskNames = $workerRemainderNameBuilder.ToImmutable()
# Preserve the upstream Gates partition with immutable prefix groups and filters.
$gatesShardingEnabled =
$semanticProjectShardingEnabled -or $SemanticCohort -eq 'Gates'
$gatesClassPrefix = 'SharpProof.Gates.Test.'
$gatesCorpusClasses = [Collections.Immutable.ImmutableArray]::Create[string](
    'CorpusSnapshotWorkflowTests', 'GatesCommandWorkflowTests')
$gatesCorpusFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$gatesRemainderFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($gatesCorpusClass in $gatesCorpusClasses) {
    $gatesCorpusFilterBuilder.Add('FullyQualifiedName~' + $gatesClassPrefix + $gatesCorpusClass)
    $gatesRemainderFilterBuilder.Add('FullyQualifiedName!~' + $gatesClassPrefix + $gatesCorpusClass)
}
$gatesCorpusFilterBuilder.Add('TestCategory=Corpus')
$gatesRemainderFilterBuilder.Add('TestCategory!=Corpus')
$gatesCorpusFilter = $gatesCorpusFilterBuilder.ToImmutable() -join '|'
$unpartitionedCorpusFilter = "($gatesCorpusFilter)&($semanticFilter)"
if ($TaskGroup -eq 'GatesCorpusA') {
    $gatesCorpusFilter = "($gatesCorpusFilter)&(FullyQualifiedName~SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.)"
}
elseif ($TaskGroup -eq 'GatesCorpusB') {
    $gatesCorpusFilter = "($gatesCorpusFilter)&(FullyQualifiedName!~SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.)"
}
$gatesRemainderFilter = $gatesRemainderFilterBuilder.ToImmutable() -join '&'
$gatesPrefixGroupBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$gatesPrefixGroupBuilder.Add([Collections.Immutable.ImmutableArray]::Create[string]('NativeEffectOracleGate'))
$gatesPrefixGroupBuilder.Add([Collections.Immutable.ImmutableArray]::Create[string]('NativeEffectOracleSelection', 'CorpusGate'))
$gatesRemainderShardPrefixes = $gatesPrefixGroupBuilder.ToImmutable()
$gatesShardFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$gatesComplementFilterBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($prefixes in $gatesRemainderShardPrefixes) {
    $includedPrefixes = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($prefix in $prefixes) {
        $includedPrefixes.Add('FullyQualifiedName~' + $gatesClassPrefix + $prefix)
        $gatesComplementFilterBuilder.Add('FullyQualifiedName!~' + $gatesClassPrefix + $prefix)
    }
    $gatesShardFilterBuilder.Add('(' + ($includedPrefixes.ToImmutable() -join '|') + ')')
}
$gatesShardFilterBuilder.Add($gatesComplementFilterBuilder.ToImmutable() -join '&')
$gatesRemainderShardFilters = $gatesShardFilterBuilder.ToImmutable()
$gatesRemainderNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
for ($index = 1; $index -le $gatesRemainderShardFilters.Length; $index++) {
    $gatesRemainderNameBuilder.Add('gates-remainder-' + $index)
}
$gatesRemainderTaskNames = $gatesRemainderNameBuilder.ToImmutable()
$taskBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
if (-not $ArchitectureOnly) {
    if ($semanticProjectShardingEnabled) {
        foreach ($project in $semanticProjects) {
            if ($project -ceq $gatesProject -or $project -eq (
                    'src/SharpProof.Architecture.Tests/' +
                    'SharpProof.Architecture.Tests.csproj')) {
                continue
            }
            $taskBuilder.Add(([ordered]@{
                        Name                         = 'semantic-' + (
                            [IO.Path]::GetFileNameWithoutExtension(
                                $project)).ToLowerInvariant()
                        Target                       = Join-Path $repositoryRoot $project
                        Filter                       = $semanticFilter
                        ProjectParallelism           = 0
                        IsolateOutput                = $false
                        Slots                        = [Math]::Min($parallelism, 2)
                        DefaultEstimatedMilliseconds = 30000L
                    }).AsReadOnly())
        }
    }
    elseif ($SemanticCohort -ne 'Gates') {
        $taskBuilder.Add(
            ([ordered]@{
                Name                         = 'semantic-projects'
                Target                       = $semanticSolutionFilter
                Filter                       = $semanticFilter
                ProjectParallelism           = $mainParallelism
                IsolateOutput                = $false
                # On a four-CPU coverage runner, reserve two slots so this
                # solution can overlap independent worker shards. Other
                # coverage widths retain their existing reservation.
                Slots                        = $(if ($coverageEnabled -and $parallelism -eq 4) { 2 } elseif ($coverageEnabled) { $parallelism } else { $mainParallelism })
                DefaultEstimatedMilliseconds = $(if ($coverageEnabled -and $parallelism -eq 4) { 500000L } else { 60000L })
            }).AsReadOnly())
    }
    # The corpus task reserves the verifier pool and its host; coverage
    # reserves every slot. Other shards retain upstream process isolation.
    $gatesDescriptorBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $gatesDescriptorBuilder.Add(([ordered]@{
                Name                         = 'gates-corpus'
                Filter                       = "($gatesCorpusFilter)&($semanticFilter)"
                Slots                        = $(if ($coverageEnabled) { $parallelism } else { [Math]::Min($parallelism, 3) })
                DefaultEstimatedMilliseconds = 250000L
            }).AsReadOnly())
    for ($index = 0; $index -lt $gatesRemainderShardFilters.Length; $index++) {
        $gatesDescriptorBuilder.Add(([ordered]@{
                    Name                         = $gatesRemainderTaskNames[$index]
                    Filter                       = "($gatesRemainderFilter)&($semanticFilter)&$($gatesRemainderShardFilters[$index])"
                    Slots                        = $(if ($coverageEnabled) { [Math]::Min($parallelism, 2) } else { 1 })
                    DefaultEstimatedMilliseconds = 100000L
                }).AsReadOnly())
    }
    $gatesTaskDescriptors = $gatesDescriptorBuilder.ToImmutable()
    if ($gatesShardingEnabled) {
        foreach ($descriptor in $gatesTaskDescriptors) {
            $taskBuilder.Add(([ordered]@{
                        Name                         = $descriptor.Name
                        Target                       = Join-Path $repositoryRoot $gatesProject
                        Filter                       = $descriptor.Filter
                        ProjectParallelism           = 0
                        IsolateOutput                = $true
                        Slots                        = $descriptor.Slots
                        DefaultEstimatedMilliseconds = $descriptor.DefaultEstimatedMilliseconds
                    }).AsReadOnly())
        }
    }
    $workerDescriptorBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $workerDescriptorBuilder.AddRange([object[]]@(
            ([ordered]@{
                Name                         = 'worker-claim-manifest'
                Filter                       = $claimTaskFilter
                Slots                        = [Math]::Min($parallelism, 2)
                DefaultEstimatedMilliseconds = 30000L
            }).AsReadOnly(),
            ([ordered]@{
                Name                         = 'worker-compiler-manifest'
                Filter                       = $manifestTaskFilter
                Slots                        = [Math]::Min($parallelism, 4)
                DefaultEstimatedMilliseconds = 50000L
            }).AsReadOnly(),
            ([ordered]@{
                Name                         = 'worker-core'
                Filter                       = $workerCoreTaskFilter
                Slots                        = [Math]::Min($parallelism, 2)
                DefaultEstimatedMilliseconds = 50000L
            }).AsReadOnly()))
    if ($coverageEnabled) {
        # The fixed-deadline native check retains its existing process hint,
        # while reserving the scheduler so competing test processes cannot overlap.
        $workerDescriptorBuilder.Add(([ordered]@{
                    Name                         = 'worker-fixed-checksum'
                    Filter                       = $checksumTaskFilter
                    Slots                        = $parallelism
                    ProcessSlots                 = [Math]::Min($parallelism, 2)
                    DefaultEstimatedMilliseconds = 10000L
                }).AsReadOnly())
    }
    # Retain the upstream four-shard policy and memory-aware slot reservation.
    for ($index = 0; $index -lt $workerRemainderShardFilters.Length; $index++) {
        $workerDescriptorBuilder.Add(([ordered]@{
                    Name                         = $workerRemainderTaskNames[$index]
                    Filter                       = "($workerRemainderTaskFilter)&$($workerRemainderShardFilters[$index])"
                    Slots                        = $(if ($coverageEnabled) { [Math]::Min($parallelism, 2) } else { 1 })
                    DefaultEstimatedMilliseconds = 200000L
                }).AsReadOnly())
    }
    $workerTaskDescriptors = $workerDescriptorBuilder.ToImmutable()
    foreach ($descriptor in $(if ($SemanticCohort -ne 'Gates') { $workerTaskDescriptors } else { @() })) {
        $taskBuilder.Add(([ordered]@{
                    Name                         = $descriptor.Name
                    Target                       = $testProject
                    Filter                       = $descriptor.Filter
                    ProjectParallelism           = 0
                    IsolateOutput                = $true
                    Slots                        = $descriptor.Slots
                    ProcessSlots                 = $(if ((($descriptor -is [Collections.IDictionary] -and $descriptor.Keys -contains 'ProcessSlots') -or ($descriptor -isnot [Collections.IDictionary] -and $descriptor.PSObject.Properties.Name -contains 'ProcessSlots'))) {
                            $descriptor.ProcessSlots
                        }
                        else { $descriptor.Slots })
                    DefaultEstimatedMilliseconds = $descriptor.DefaultEstimatedMilliseconds
                }).AsReadOnly())
    }
}
if ($architectureShardingEnabled) {
    $taskBuilder.Add(([ordered]@{
                Name                         = 'architecture'
                EvidenceKind                 = 'ArchitectureOnly'
                Target                       = Join-Path $repositoryRoot (
                    'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj')
                Filter                       = $semanticFilter
                ProjectParallelism           = 0
                IsolateOutput                = $false
                Slots                        = 1
                DefaultEstimatedMilliseconds = 10000L
            }).AsReadOnly())
}
if ($TaskGroup -ne 'All') {
    $workerTaskNameBuilder = $workerRemainderTaskNames.ToBuilder()
    $workerTaskNameBuilder.Add('worker-fixed-checksum')
    $workerTaskNames = $workerTaskNameBuilder.ToImmutable()
    # WorkerB takes two measured-balanced remainder shards; WorkerA takes every
    # other Worker task, so a new remainder shard still runs exactly once.
    $workerBTaskNames = [Collections.Immutable.ImmutableArray]::Create[string](
        'worker-remainder-2', 'worker-remainder-3')
    # Capture the name first: inside switch, $_ is the switch value.
    $taskSnapshot = $taskBuilder.ToImmutable()
    $selectedTasks = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($taskSnapshot | Where-Object {
                $taskName = [string]$_.Name
                switch ($TaskGroup) {
                    'GatesCorpus' { $taskName -ceq 'gates-corpus' }
                    'GatesCorpusA' { $taskName -ceq 'gates-corpus' }
                    'GatesCorpusB' { $taskName -ceq 'gates-corpus' }
                    'GatesShards' { $taskName -cin $gatesRemainderTaskNames }
                    'Worker' { $taskName -cin $workerTaskNames }
                    'WorkerA' { $taskName -cin $workerTaskNames -and $taskName -cnotin $workerBTaskNames }
                    'WorkerB' { $taskName -cin $workerBTaskNames }
                    'WorkerA1' { $taskName -cin @('worker-remainder-1', 'worker-fixed-checksum') }
                    'WorkerA4' { $taskName -ceq 'worker-remainder-4' }
                    'WorkerB2' { $taskName -ceq 'worker-remainder-2' }
                    'WorkerB3' { $taskName -ceq 'worker-remainder-3' }
                    'Projects' { $taskName -cne 'gates-corpus' -and $taskName -cnotin $gatesRemainderTaskNames -and $taskName -cnotin $workerTaskNames }
                    'Analyzer' { $taskName -cin @('semantic-sharpproof.analyzer.tests', 'semantic-sharpproof.fuzz.tests') }
                    'ProjectsRest' { $taskName -cne 'gates-corpus' -and $taskName -cnotin $gatesRemainderTaskNames -and $taskName -cnotin $workerTaskNames -and $taskName -cnotin @('architecture', 'semantic-sharpproof.analyzer.tests', 'semantic-sharpproof.fuzz.tests') }
                    'Extras' { $taskName -ceq 'architecture' }
                }
            }))
    $taskBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($selectedTask in $selectedTasks) { $taskBuilder.Add($selectedTask) }
    if ($taskBuilder.Count -eq 0) {
        throw "Task group '$TaskGroup' selected no tasks."
    }
}
$estimatedTaskBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
foreach ($task in $taskBuilder) {
    $properties = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
    foreach ($key in $task.Keys) { $properties[$key] = $task[$key] }
    $properties['EstimatedMilliseconds'] = if ($priorDurations.ContainsKey($task.Name)) {
        [long]$priorDurations[$task.Name]
    }
    else {
        [long]$task.DefaultEstimatedMilliseconds
    }
    $estimatedTaskBuilder.Add($properties.ToImmutable())
}
$taskSnapshot = $estimatedTaskBuilder.ToImmutable()
$orderedTaskBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
foreach ($orderedTask in ($taskSnapshot | Sort-Object `
        @{ Expression = { $_.EstimatedMilliseconds }; Descending = $true }, `
        @{ Expression = { $_.Name }; Descending = $false }) ) {
    $orderedTaskBuilder.Add($orderedTask)
}
$tasks = $orderedTaskBuilder.ToImmutable()

$retainCanonicalResults = -not [string]::IsNullOrWhiteSpace($TestResultsDirectory)
if ($retainCanonicalResults -and $coverageEnabled) {
    throw 'Canonical TRX retention does not alter coverage result handling.'
}
$temporaryResults = -not $coverageEnabled -and -not $retainCanonicalResults
$resultsRoot = if ($coverageEnabled) {
    $resolvedCoverageResults
}
elseif ($retainCanonicalResults) {
    $canonicalRoot = [IO.Path]::GetFullPath($TestResultsDirectory, $repositoryRoot)
    if ([IO.Directory]::Exists($canonicalRoot) -and
        @(Get-ChildItem -LiteralPath $canonicalRoot -Force).Count) {
        throw 'Canonical cohort results require a new empty directory.'
    }
    $canonicalRoot
}
else {
    Join-Path ([IO.Path]::GetTempPath()) (
        'sharpproof-semantic-tests-' + [Guid]::NewGuid().ToString('N'))
}
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null
$retainCorpusDiscovery = $coverageEnabled -and $TaskGroup -in @('GatesCorpusA', 'GatesCorpusB')
$discoverySettings = ''
if ($retainCanonicalResults -or $retainCorpusDiscovery) {
    $discoverySettings = Join-Path $resultsRoot 'discovery.runsettings'
    '<RunSettings><NUnit><DumpXmlTestDiscovery>true</DumpXmlTestDiscovery></NUnit></RunSettings>' |
        Set-Content -LiteralPath $discoverySettings -Encoding utf8NoBOM
}
$timings = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$failureBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$campaign = [Diagnostics.Stopwatch]::StartNew()
$campaignFailure = $null

try {
    $prepareSemanticTest = {
        param([object]$task)

        $processSlots = if ((($task -is [Collections.IDictionary] -and $task.Keys -contains 'ProcessSlots') -or ($task -isnot [Collections.IDictionary] -and $task.PSObject.Properties.Name -contains 'ProcessSlots'))) {
            $task.ProcessSlots
        }
        else { $task.Slots }
        $environment = ([ordered]@{
                SHARPPROOF_TEST_PROJECT_PARALLELISM = $processSlots.ToString(
                    [Globalization.CultureInfo]::InvariantCulture)
            }).AsReadOnly()
        if ($retainCanonicalResults -or $retainCorpusDiscovery) {
            $taskRoot = Join-Path $resultsRoot $task.Name
            [IO.Directory]::CreateDirectory($taskRoot) | Out-Null
            $discoveryProjectBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
            if ($task.Target.EndsWith('.slnf', [StringComparison]::Ordinal)) {
                foreach ($project in (Get-Content -LiteralPath $task.Target -Raw | ConvertFrom-Json).solution.projects) {
                    $discoveryProjectBuilder.Add([string]$project)
                }
            }
            else { $discoveryProjectBuilder.Add([string]$task.Target) }
            $discoveryProjects = $discoveryProjectBuilder.ToImmutable()
            $discoveryArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
            $discoveryArgumentBuilder.AddRange([string[]]@('vstest'))
            $discoveryAssemblyBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
            foreach ($project in $discoveryProjects) {
                $projectPath = [IO.Path]::GetFullPath(([string]$project).Replace('\', '/'), $repositoryRoot)
                $discoveryAssemblyBuilder.Add((Get-SharpProofTestAssemblyPath `
                            -ProjectPath $projectPath -Configuration $Configuration))
            }
            $discoveryAssemblies = $discoveryAssemblyBuilder.ToImmutable()
            $discoveryArgumentBuilder.AddRange($discoveryAssemblies)
            $discoveryArgumentBuilder.AddRange([string[]]@(
                    '/ListFullyQualifiedTests',
                    ('/Settings:' + $discoverySettings),
                    ('/ListTestsTargetPath:' + (Join-Path $taskRoot 'discovered-tests.txt')),
                    ('/TestCaseFilter:' + $(if ($retainCorpusDiscovery) { $unpartitionedCorpusFilter } else { $task.Filter }))))
            Invoke-SharpProofRequiredDotnet -Arguments ([string[]]@($discoveryArgumentBuilder.ToImmutable())) `
                -TimeoutSeconds $TimeoutSeconds -Quiet:$Quiet | Out-Host
            $dumpRoot = Join-Path $taskRoot 'discovery'
            [IO.Directory]::CreateDirectory($dumpRoot) | Out-Null
            foreach ($assembly in $discoveryAssemblies) {
                $name = [IO.Path]::GetFileName($assembly)
                $dump = Join-Path (Split-Path -Parent $assembly) ('Dump/D_' + $name + '.dump')
                if (-not (Test-Path -LiteralPath $dump -PathType Leaf)) {
                    throw 'Canonical NUnit discovery did not produce its actual per-case XML dump.'
                }
                Copy-Item -LiteralPath $dump -Destination (Join-Path $dumpRoot ($name + '.discovery.dump'))
            }
        }
        $isolatedOutput = ''
        if ($coverageEnabled -and $task.IsolateOutput) {
            $isolatedOutput = New-SharpProofIsolatedTestOutput `
                -SourceDirectory (Split-Path -Parent (
                    Get-SharpProofTestAssemblyPath `
                        -ProjectPath $task.Target -Configuration $Configuration)) `
                -DestinationDirectory (Join-Path `
                    $isolatedOutputRoot (
                    $task.Name + '/' + $Configuration + '/net10.0'))
        }
        $directVstest = -not $coverageEnabled -and
        $task.Target.EndsWith(
            '.csproj', [StringComparison]::OrdinalIgnoreCase)
        if ($directVstest) {
            $assembly = Get-SharpProofTestAssemblyPath `
                -ProjectPath $task.Target `
                -Configuration $Configuration
            $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
            $argumentBuilder.AddRange([string[]]@('vstest', $assembly))
            $argumentBuilder.Add('/TestCaseFilter:' + $task.Filter)
            $argumentBuilder.Add('/logger:console;verbosity=minimal')
            $argumentBuilder.Add("/logger:trx;LogFilePrefix=$($task.Name)")
            $argumentBuilder.Add('/ResultsDirectory:' + (
                    Join-Path $resultsRoot $task.Name))
            if ((($task -is [Collections.IDictionary] -and $task.Keys -contains 'RunSettings') -or ($task -isnot [Collections.IDictionary] -and $task.PSObject.Properties.Name -contains 'RunSettings'))) {
                $argumentBuilder.Add('/Settings:' + $task.RunSettings)
            }
            $arguments = $argumentBuilder.ToImmutable()
        }
        else {
            $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
            $argumentBuilder.AddRange([string[]]@(
                    'test', $task.Target, '-c', $Configuration,
                    '--no-build', '--no-restore'))
            if (-not [string]::IsNullOrWhiteSpace($isolatedOutput)) {
                $argumentBuilder.Add('-p:OutDir=' + $isolatedOutput + '/')
            }
            $argumentBuilder.AddRange([string[]]@(
                    '--filter', $task.Filter,
                    '--logger', 'console;verbosity=minimal',
                    '--logger', "trx;LogFilePrefix=$($task.Name)",
                    '--results-directory', (Join-Path $resultsRoot $task.Name)))
            if ($task.ProjectParallelism -gt 0) {
                $argumentBuilder.Add("/m:$($task.ProjectParallelism)")
            }
            $coverageArguments = Add-SharpProofCoverageArguments `
                -ImmutableArguments $argumentBuilder.ToImmutable() `
                -Enabled ($coverageEnabled -and $task.Name -cne 'architecture') `
                -Settings $resolvedCoverageSettings
            $arguments = $coverageArguments
        }
        return ([ordered]@{
                Arguments   = $arguments
                Environment = $environment
            }).AsReadOnly()
    }.GetNewClosure()
    $testRun = Invoke-SharpProofParallelDotnetTests `
        -Tests $tasks `
        -RepositoryRoot $repositoryRoot `
        -Parallelism $parallelism `
        -TimeoutSeconds $TimeoutSeconds `
        -Prepare $prepareSemanticTest `
        -Label 'Semantic test' `
        -Quiet:$Quiet
    foreach ($result in @($testRun.Completed)) {
        $timings.Add(([ordered]@{
                    name                = $result.Test.Name
                    elapsedMilliseconds = $result.ElapsedMilliseconds
                    exitCode            = $result.ExitCode
                }).AsReadOnly())
    }
    if ($coverageEnabled) {
        Save-SharpProofCoverageCohort -Root $resultsRoot -Expected $tasks -Completed @($testRun.Completed)
    }
    if ($retainCanonicalResults) {
        Save-SharpProofCanonicalCohort -Root $resultsRoot `
            -RepositoryRoot $repositoryRoot -Cohort $SemanticCohort `
            -Configuration $Configuration -Filter $semanticFilter `
            -Projects $semanticProjects -Expected $tasks `
            -Completed @($testRun.Completed)
    }
    foreach ($failure in @($testRun.Completed | Where-Object {
                $_.ExitCode -ne 0
            })) {
        $failureBuilder.Add(
            "$($failure.Test.Name) exited $($failure.ExitCode).")
    }
}
catch {
    $campaignFailure = $_
    throw
}
finally {
    if ($temporaryResults -and [IO.Directory]::Exists($resultsRoot)) {
        [IO.Directory]::Delete($resultsRoot, $true)
    }
    Remove-SharpProofOwnedDirectory -Directory $isolatedOutputRoot
    if ($partitionFilter -and (Test-Path -LiteralPath $partitionFilter)) {
        Remove-Item -LiteralPath $partitionFilter -Force
    }
    $campaign.Stop()
    $timingSnapshot = $timings.ToImmutable()
    try {
        $temporaryTiming =
        $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        ([ordered]@{
            schemaVersion            = 1
            command                  = 'semantic-tests'
            configuration            = $Configuration
            fast                     = [bool]$Fast
            architectureOnly         = [bool]$ArchitectureOnly
            semanticCohort           = $SemanticCohort
            taskGroup                = $TaskGroup
            parallelism              = $parallelism
            totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
            tasks                    = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($timingSnapshot | Sort-Object { $_.name }))
        }).AsReadOnly() | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
        Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force
    }
    catch {
        if ($null -eq $campaignFailure) {
            throw
        }
        Write-Warning ('Could not save semantic timing evidence: ' + $_.Exception.Message) -WarningAction Continue
    }
}

$failures = $failureBuilder.ToImmutable()
if ($failures.Length -ne 0) {
    throw "Parallel semantic tests failed:`n$($failures -join "`n")"
}
if (-not $Quiet) {
    Write-Host (
        "Semantic tests passed in $($tasks.Count) isolated task(s) with " +
        "$parallelism scheduler slot(s).")
    Write-Host "Timing evidence: $timingOutput"
}
