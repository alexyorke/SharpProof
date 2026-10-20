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

    # Coverage CI spreads the semantic coverage tasks across runners. Gates
    # holds every slot, the Worker remainder shards dominate the rest, and
    # Projects holds every other task. All keeps the single-runner task set.
    [ValidateSet('All', 'Gates', 'Worker', 'Projects')]
    [string]$CoverageTaskGroup = 'All'
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
$semanticProjects = @($semanticSolution.solution.projects |
        ForEach-Object { ([string]$_).Replace('\', '/') })
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
    $semanticProjects = @($semanticProjects | Where-Object {
            if ($SemanticCohort -eq 'Gates') { $_ -ceq $gatesProject }
            else { $_ -cne $gatesProject }
        })
    $partitionFilter = Join-Path $repositoryRoot (
        '.sharpproof-canonical-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    [ordered]@{ solution = [ordered]@{
            path = 'SharpProof.slnx'; projects = $semanticProjects
        }
    } | ConvertTo-Json -Depth 4 |
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
if ($CoverageTaskGroup -ne 'All' -and -not $coverageEnabled) {
    throw 'Coverage task groups require coverage collection.'
}
if ($coverageEnabled) {
    # The corpus verifier starts its own worker pool and must run in a separate
    # task that reserves every slot. Attributes has its own collector stage.
    # Architecture XML/DAG checks retain a separate strict noninstrumented TRX task.
    $semanticProjects = @($semanticProjects | Where-Object {
            $_ -cne $gatesProject -and
            $_ -cne 'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj' -and
            (-not $AttributesCollectedSeparately -or
            $_ -cne 'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj')
        })
    $partitionFilter = Join-Path $repositoryRoot (
        '.sharpproof-coverage-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    [ordered]@{ solution = [ordered]@{
            path = 'SharpProof.slnx'; projects = $semanticProjects
        }
    } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $partitionFilter -Encoding utf8NoBOM
    $semanticSolutionFilter = $partitionFilter
}

if (-not $NoBuild) {
    $semanticBuildProjects = if ($ArchitectureOnly) {
        @('src\SharpProof.Architecture.Tests\SharpProof.Architecture.Tests.csproj')
    }
    else {
        if ($SemanticCohort -eq 'All') {
            @($semanticSolution.solution.projects) + @(
                'src\SharpProof.Worker.Tests\SharpProof.Worker.Tests.csproj')
        }
        elseif ($SemanticCohort -eq 'Gates') { $semanticProjects }
        else { $semanticProjects + @('src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj') }
    }
    $semanticBuildFilter = Join-Path $repositoryRoot (
        '.sharpproof-semantic-build-' +
        [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        [pscustomobject]@{
            solution = [ordered]@{
                path     = 'SharpProof.slnx'
                projects = @($semanticBuildProjects)
            }
        } | ConvertTo-Json -Depth 4 |
            Set-Content `
                -LiteralPath $semanticBuildFilter `
                -Encoding utf8NoBOM
        Invoke-SharpProofRequiredDotnet `
            -Arguments @('restore', $semanticBuildFilter, '--locked-mode') `
            -TimeoutSeconds $TimeoutSeconds `
            -Quiet:$Quiet
        $buildArguments = @(
            'build', $semanticBuildFilter,
            '-c', $Configuration, '--no-restore')
        if ($Fast) {
            $buildArguments += '-p:RunAnalyzersDuringBuild=false'
        }
        Invoke-SharpProofRequiredDotnet `
            -Arguments $buildArguments `
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
$(if ($CoverageTaskGroup -ne 'All') { '-' + $CoverageTaskGroup.ToLowerInvariant() } else { '' }) +
$(if ($coverageEnabled) { '-coverage' } else { '' })
$canonicalTimingOutput = Join-Path $timingDirectory (
    $timingStem + $timingSuffix + '.json')
$timingOutput = Join-Path $timingDirectory (
    $timingStem + $(if ($Fast) { '-fast' } else { '' }) +
    $timingSuffix + '.json')
$priorDurations = @{}
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
                $priorDurations[[string]$task.name] = $elapsed
            }
        }
    }
    catch {
        Write-Warning (
            "Ignoring malformed semantic timing '$priorTimingPath': " +
            $_.Exception.Message)
    }
}

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
$workerDedicatedClasses = @(
    'ClaimManifestBuilderTests',
    'CompilerManifestArtifactTests')
$claimFilter =
'FullyQualifiedName~' + $workerClassPrefix + $workerDedicatedClasses[0]
$manifestFilter =
'FullyQualifiedName~' + $workerClassPrefix + $workerDedicatedClasses[1]
$workerCoreClasses = @(
    'WorkerTests',
    'WorkerProgramTests',
    'CompilerCallableLowererTests'
)
$workerCoreFilter = @($workerCoreClasses | ForEach-Object {
        'FullyQualifiedName~' + $workerClassPrefix + $_
    }) -join '|'
$workerRemainderFilter = @(
    $workerDedicatedClasses + $workerCoreClasses |
        ForEach-Object {
            'FullyQualifiedName!~' + $workerClassPrefix + $_
        }) -join '&'
# The remainder holds about thirteen minutes of serial Worker test time, and
# one test process keeps about one CPU busy. Split it by test class name
# prefix into shards balanced by measured class durations (about three
# minutes each uninstrumented). The last shard is the complement of the
# listed prefixes, so every current and future class runs in exactly one
# shard. Rebalance the prefixes when one shard grows well past the others.
$workerRemainderShardPrefixes = @(
    , @('NativeA')
    , @('NativeS', 'C')
    , @('W', 'T', 'G', 'F'))
$workerRemainderShardFilters = @(
    foreach ($prefixes in $workerRemainderShardPrefixes) {
        '(' + (@($prefixes | ForEach-Object {
                    'FullyQualifiedName~' + $workerClassPrefix + $_
                }) -join '|') + ')'
    }
    @($workerRemainderShardPrefixes | ForEach-Object { $_ } | ForEach-Object {
            'FullyQualifiedName!~' + $workerClassPrefix + $_
        }) -join '&')

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
    $workerRemainderTaskFilter += "&(FullyQualifiedName!=$checksumMethod)"
}
$workerRemainderTaskNames = @(
    for ($index = 1; $index -le $workerRemainderShardFilters.Count; $index++) {
        'worker-remainder-' + $index
    })
$tasks = [Collections.Generic.List[object]]::new()
if (-not $ArchitectureOnly) {
    if ($semanticProjectShardingEnabled) {
        foreach ($project in $semanticProjects) {
            if ($project -eq (
                    'src/SharpProof.Architecture.Tests/' +
                    'SharpProof.Architecture.Tests.csproj')) {
                continue
            }
            $tasks.Add([pscustomobject]@{
                    Name                         = 'semantic-' + (
                        [IO.Path]::GetFileNameWithoutExtension(
                            $project)).ToLowerInvariant()
                    Target                       = Join-Path $repositoryRoot $project
                    Filter                       = $semanticFilter
                    ProjectParallelism           = 0
                    IsolateOutput                = $false
                    Slots                        = [Math]::Min($parallelism, 2)
                    DefaultEstimatedMilliseconds = 30000L
                })
        }
    }
    else {
        $tasks.Add(
            [pscustomobject]@{
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
            })
    }
    if ($coverageEnabled) {
        $tasks.Add([pscustomobject]@{
                Name                         = 'semantic-gates'
                Target                       = Join-Path $repositoryRoot $gatesProject
                Filter                       = $semanticFilter
                ProjectParallelism           = 0
                IsolateOutput                = $false
                Slots                        = $parallelism
                DefaultEstimatedMilliseconds = 500000L
            })
    }
    $workerTaskDescriptors = @(
        [pscustomobject]@{
            Name                         = 'worker-claim-manifest'
            Filter                       = $claimTaskFilter
            Slots                        = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 30000L
        },
        [pscustomobject]@{
            Name                         = 'worker-compiler-manifest'
            Filter                       = $manifestTaskFilter
            Slots                        = [Math]::Min($parallelism, 4)
            DefaultEstimatedMilliseconds = 50000L
        },
        [pscustomobject]@{
            Name                         = 'worker-core'
            Filter                       = $workerCoreTaskFilter
            Slots                        = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 50000L
        })
    if ($coverageEnabled) {
        # The fixed-deadline native check retains its existing process hint,
        # while reserving the scheduler so competing test processes cannot overlap.
        $workerTaskDescriptors += [pscustomobject]@{
            Name                         = 'worker-fixed-checksum'
            Filter                       = $checksumTaskFilter
            Slots                        = $parallelism
            ProcessSlots                 = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 10000L
        }
    }
    # Each remainder shard keeps about one CPU busy, so uninstrumented runs
    # schedule one slot per shard and overlap every shard. Instrumented
    # processes use more memory, so coverage keeps two shards at a time.
    for ($index = 0; $index -lt $workerRemainderShardFilters.Count; $index++) {
        $workerTaskDescriptors += [pscustomobject]@{
            Name                         = $workerRemainderTaskNames[$index]
            Filter                       = "($workerRemainderTaskFilter)&$($workerRemainderShardFilters[$index])"
            Slots                        = $(if ($coverageEnabled) { [Math]::Min($parallelism, 2) } else { 1 })
            DefaultEstimatedMilliseconds = 200000L
        }
    }
    foreach ($descriptor in $(if ($SemanticCohort -ne 'Gates') { $workerTaskDescriptors } else { @() })) {
        $tasks.Add([pscustomobject]@{
                Name                         = $descriptor.Name
                Target                       = $testProject
                Filter                       = $descriptor.Filter
                ProjectParallelism           = 0
                IsolateOutput                = $true
                Slots                        = $descriptor.Slots
                ProcessSlots                 = $(if ($descriptor.PSObject.Properties.Name -contains 'ProcessSlots') {
                        $descriptor.ProcessSlots
                    }
                    else { $descriptor.Slots })
                DefaultEstimatedMilliseconds = $descriptor.DefaultEstimatedMilliseconds
            })
    }
}
if ($architectureShardingEnabled) {
    $tasks.Add([pscustomobject]@{
            Name                         = 'architecture'
            EvidenceKind                 = 'ArchitectureOnly'
            Target                       = Join-Path $repositoryRoot (
                'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj')
            Filter                       = $semanticFilter
            ProjectParallelism           = 0
            IsolateOutput                = $false
            Slots                        = 1
            DefaultEstimatedMilliseconds = 10000L
        })
}
if ($CoverageTaskGroup -ne 'All') {
    $gatesTaskNames = @('semantic-gates')
    $workerTaskNames = $workerRemainderTaskNames + @('worker-fixed-checksum')
    # Capture the name first: inside switch, $_ is the switch value.
    $tasks = [Collections.Generic.List[object]]@($tasks | Where-Object {
            $taskName = [string]$_.Name
            switch ($CoverageTaskGroup) {
                'Gates' { $taskName -cin $gatesTaskNames }
                'Worker' { $taskName -cin $workerTaskNames }
                'Projects' { $taskName -cnotin ($gatesTaskNames + $workerTaskNames) }
            }
        })
    if ($tasks.Count -eq 0) {
        throw "Coverage task group '$CoverageTaskGroup' selected no tasks."
    }
}
foreach ($task in $tasks) {
    $task | Add-Member -NotePropertyName EstimatedMilliseconds `
        -NotePropertyValue $(if ($priorDurations.ContainsKey($task.Name)) {
            [long]$priorDurations[$task.Name]
        }
        else {
            [long]$task.DefaultEstimatedMilliseconds
        })
}
$tasks = @($tasks | Sort-Object `
    @{ Expression = 'EstimatedMilliseconds'; Descending = $true }, `
    @{ Expression = 'Name'; Descending = $false })

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
$discoverySettings = ''
if ($retainCanonicalResults) {
    $discoverySettings = Join-Path $resultsRoot 'discovery.runsettings'
    '<RunSettings><NUnit><DumpXmlTestDiscovery>true</DumpXmlTestDiscovery></NUnit></RunSettings>' |
        Set-Content -LiteralPath $discoverySettings -Encoding utf8NoBOM
}
$timings = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$campaign = [Diagnostics.Stopwatch]::StartNew()
$campaignFailure = $null

try {
    $prepareSemanticTest = {
        param([object]$task)

        $processSlots = if ($task.PSObject.Properties.Name -contains 'ProcessSlots') {
            $task.ProcessSlots
        }
        else { $task.Slots }
        $environment = @{
            SHARPPROOF_TEST_PROJECT_PARALLELISM = $processSlots.ToString(
                [Globalization.CultureInfo]::InvariantCulture)
        }
        if ($retainCanonicalResults) {
            $taskRoot = Join-Path $resultsRoot $task.Name
            [IO.Directory]::CreateDirectory($taskRoot) | Out-Null
            $discoveryProjects = if ($task.Target.EndsWith('.slnf', [StringComparison]::Ordinal)) {
                @((Get-Content -LiteralPath $task.Target -Raw | ConvertFrom-Json).solution.projects)
            }
            else { @($task.Target) }
            $discoveryArguments = @('vstest')
            $discoveryAssemblies = @()
            foreach ($project in $discoveryProjects) {
                $projectPath = [IO.Path]::GetFullPath(([string]$project).Replace('\', '/'), $repositoryRoot)
                $discoveryAssemblies += Get-SharpProofTestAssemblyPath `
                    -ProjectPath $projectPath -Configuration $Configuration
            }
            $discoveryArguments += $discoveryAssemblies
            $discoveryArguments += @(
                '/ListFullyQualifiedTests',
                ('/Settings:' + $discoverySettings),
                ('/ListTestsTargetPath:' + (Join-Path $taskRoot 'discovered-tests.txt')),
                ('/TestCaseFilter:' + $task.Filter))
            Invoke-SharpProofRequiredDotnet -Arguments $discoveryArguments `
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
                -SourceDirectory (Join-Path $repositoryRoot (
                    'artifacts/bin/SharpProof.Worker.Tests/' + $Configuration +
                    '/net10.0')) `
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
            $arguments = @('vstest', $assembly)
            $arguments += '/TestCaseFilter:' + $task.Filter
            $arguments += '/logger:console;verbosity=minimal'
            $arguments += "/logger:trx;LogFilePrefix=$($task.Name)"
            $arguments += '/ResultsDirectory:' + (
                Join-Path $resultsRoot $task.Name)
            if ($task.PSObject.Properties.Name -contains 'RunSettings') {
                $arguments += '/Settings:' + $task.RunSettings
            }
        }
        else {
            $arguments = @(
                'test', $task.Target, '-c', $Configuration,
                '--no-build', '--no-restore')
            if (-not [string]::IsNullOrWhiteSpace($isolatedOutput)) {
                $arguments += '-p:OutDir=' + $isolatedOutput + '/'
            }
            $arguments += @(
                '--filter', $task.Filter,
                '--logger', 'console;verbosity=minimal',
                '--logger', "trx;LogFilePrefix=$($task.Name)",
                '--results-directory', (Join-Path $resultsRoot $task.Name))
            if ($task.ProjectParallelism -gt 0) {
                $arguments += "/m:$($task.ProjectParallelism)"
            }
            $arguments = Add-SharpProofCoverageArguments `
                -Arguments $arguments `
                -Enabled ($coverageEnabled -and $task.Name -cne 'architecture') `
                -Settings $resolvedCoverageSettings
        }
        return [pscustomobject]@{
            Arguments   = $arguments
            Environment = $environment
        }
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
        $timings.Add([pscustomobject]@{
                name                = $result.Test.Name
                elapsedMilliseconds = $result.ElapsedMilliseconds
                exitCode            = $result.ExitCode
            })
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
        $failures.Add(
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
    try {
        $temporaryTiming =
        $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [pscustomobject]@{
            schemaVersion            = 1
            command                  = 'semantic-tests'
            configuration            = $Configuration
            fast                     = [bool]$Fast
            architectureOnly         = [bool]$ArchitectureOnly
            semanticCohort           = $SemanticCohort
            coverageTaskGroup        = $CoverageTaskGroup
            parallelism              = $parallelism
            totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
            tasks                    = @($timings | Sort-Object name)
        } | ConvertTo-Json -Depth 5 |
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

if ($failures.Count -ne 0) {
    throw "Parallel semantic tests failed:`n$($failures -join "`n")"
}
if (-not $Quiet) {
    Write-Host (
        "Semantic tests passed in $($tasks.Count) isolated task(s) with " +
        "$parallelism scheduler slot(s).")
    Write-Host "Timing evidence: $timingOutput"
}
