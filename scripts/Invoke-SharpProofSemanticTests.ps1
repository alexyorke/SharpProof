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

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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
$gatesProject = 'SharpProof.Gates.Test/SharpProof.Gates.Test.csproj'
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
        } } | ConvertTo-Json -Depth 4 |
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

if (-not $NoBuild) {
    $semanticBuildProjects = if ($ArchitectureOnly) {
        @('SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj')
    }
    else {
        if ($SemanticCohort -eq 'All') {
            @($semanticSolution.solution.projects) + @(
                'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj')
        }
        elseif ($SemanticCohort -eq 'Gates') { $semanticProjects }
        else { $semanticProjects + @('SharpProof.Worker.Test/SharpProof.Worker.Test.csproj') }
    }
    $semanticBuildFilter = Join-Path $repositoryRoot (
        '.sharpproof-semantic-build-' +
        [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        [pscustomobject]@{
            solution = [ordered]@{
                path = 'SharpProof.slnx'
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

$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingStem = 'semantic-tests-' + $Configuration.ToLowerInvariant()
$timingSuffix =
    $(if ($ArchitectureOnly) { '-architecture-only' } else { '' }) +
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
    $ArchitectureOnly -or (
        -not $coverageEnabled -and
        [string]::IsNullOrWhiteSpace($TestFilter))
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
# The measured heavy classes account for about half of the remainder's test
# time. Complementary filters keep every current and future test in one shard.
$workerRemainderHeavyClasses = @(
    'WorkerVcShadowSourceGateTests',
    'NativeWorkerRoutingTests',
    'TypedMetadataConversionTests',
    'GoldenWorkerTests',
    'CompilerTotalCallableArtifactTests',
    'NativeAllocationEffectTests',
    'NativePurityEffectTests',
    'TypedMetadataCallTests',
    'CompilerReachableSourceTests',
    'NativeAliasingBoundaryTests',
    'ScalarPatternFlowTests',
    'BitwiseOperatorSemanticsTests')
$workerRemainderHeavyFilter = @(
    $workerRemainderHeavyClasses |
        ForEach-Object {
            'FullyQualifiedName~' + $workerClassPrefix + $_
        }) -join '|'
$workerRemainderOtherFilter = @(
    $workerRemainderHeavyClasses |
        ForEach-Object {
            'FullyQualifiedName!~' + $workerClassPrefix + $_
        }) -join '&'

$testProject = Join-Path $repositoryRoot (
    'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj')
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
$workerRemainderHeavyTaskFilter =
    "($workerRemainderTaskFilter)&($workerRemainderHeavyFilter)"
$workerRemainderOtherTaskFilter =
    "($workerRemainderTaskFilter)&($workerRemainderOtherFilter)"
$tasks = [Collections.Generic.List[object]]::new()
if (-not $ArchitectureOnly) {
    if ($semanticProjectShardingEnabled) {
        foreach ($project in $semanticProjects) {
            if ($project -eq (
                    'SharpProof.ArchitectureTest/' +
                    'SharpProof.ArchitectureTest.csproj')) {
                continue
            }
            $tasks.Add([pscustomobject]@{
                Name = 'semantic-' + (
                    [IO.Path]::GetFileNameWithoutExtension(
                        $project)).ToLowerInvariant()
                Target = Join-Path $repositoryRoot $project
                Filter = $semanticFilter
                ProjectParallelism = 0
                IsolateOutput = $false
                Slots = [Math]::Min($parallelism, 2)
                DefaultEstimatedMilliseconds = 30000L
            })
        }
    }
    else {
        $tasks.Add(
            [pscustomobject]@{
                Name = 'semantic-projects'
                Target = $semanticSolutionFilter
                Filter = $semanticFilter
                ProjectParallelism = $mainParallelism
                IsolateOutput = $false
                # On a four-CPU coverage runner, reserve two slots so this
                # solution can overlap independent worker shards. Other
                # coverage widths retain their existing reservation.
                Slots = $(if ($coverageEnabled -and $parallelism -eq 4) { 2 } elseif ($coverageEnabled) { $parallelism } else { $mainParallelism })
                DefaultEstimatedMilliseconds = 60000L
            })
    }
    $workerTaskDescriptors = @(
        [pscustomobject]@{
            Name = 'worker-claim-manifest'
            Filter = $claimTaskFilter
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 30000L
        },
        [pscustomobject]@{
            Name = 'worker-compiler-manifest'
            Filter = $manifestTaskFilter
            Slots = [Math]::Min($parallelism, 4)
            DefaultEstimatedMilliseconds = 50000L
        },
        [pscustomobject]@{
            Name = 'worker-core'
            Filter = $workerCoreTaskFilter
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 50000L
        })
    if ($coverageEnabled -and $parallelism -ge 4) {
        $workerTaskDescriptors += @(
            [pscustomobject]@{
                Name = 'worker-remainder-heavy'
                Filter = $workerRemainderHeavyTaskFilter
                Slots = 2
                DefaultEstimatedMilliseconds = 160000L
            },
            [pscustomobject]@{
                Name = 'worker-remainder-other'
                Filter = $workerRemainderOtherTaskFilter
                Slots = 2
                DefaultEstimatedMilliseconds = 160000L
            })
    }
    else {
        $workerTaskDescriptors += [pscustomobject]@{
            Name = 'worker-remainder'
            Filter = $workerRemainderTaskFilter
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 20000L
        }
    }
    foreach ($descriptor in $(if ($SemanticCohort -ne 'Gates') { $workerTaskDescriptors } else { @() })) {
        $tasks.Add([pscustomobject]@{
            Name = $descriptor.Name
            Target = $testProject
            Filter = $descriptor.Filter
            ProjectParallelism = 0
            IsolateOutput = $true
            Slots = $descriptor.Slots
            DefaultEstimatedMilliseconds = $descriptor.DefaultEstimatedMilliseconds
        })
    }
}
if ($architectureShardingEnabled) {
    $tasks.Add([pscustomobject]@{
        Name = 'architecture'
        Target = Join-Path $repositoryRoot (
            'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj')
        Filter = $semanticFilter
        ProjectParallelism = 0
        IsolateOutput = $false
        Slots = 1
        DefaultEstimatedMilliseconds = 10000L
    })
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

try {
    $prepareSemanticTest = {
        param([object]$task)

        $environment = @{
            SHARPPROOF_TEST_PROJECT_PARALLELISM = $task.Slots.ToString(
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
                    'SharpProof.Worker.Test/bin/' + $Configuration +
                    '/net9.0')) `
                -DestinationDirectory (Join-Path `
                    $isolatedOutputRoot (
                        $task.Name + '/' + $Configuration + '/net9.0'))
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
                -Enabled $coverageEnabled `
                -Settings $resolvedCoverageSettings
        }
        return [pscustomobject]@{
            Arguments = $arguments
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
            name = $result.Test.Name
            elapsedMilliseconds = $result.ElapsedMilliseconds
            exitCode = $result.ExitCode
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
finally {
    if ($temporaryResults -and [IO.Directory]::Exists($resultsRoot)) {
        [IO.Directory]::Delete($resultsRoot, $true)
    }
    Remove-SharpProofOwnedDirectory -Directory $isolatedOutputRoot
    if ($partitionFilter -and (Test-Path -LiteralPath $partitionFilter)) {
        Remove-Item -LiteralPath $partitionFilter -Force
    }
}

$campaign.Stop()
$temporaryTiming =
    $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[pscustomobject]@{
    schemaVersion = 1
    command = 'semantic-tests'
    configuration = $Configuration
    fast = [bool]$Fast
    architectureOnly = [bool]$ArchitectureOnly
    semanticCohort = $SemanticCohort
    parallelism = $parallelism
    totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
    tasks = @($timings | Sort-Object name)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force

if ($failures.Count -ne 0) {
    throw "Parallel semantic tests failed:`n$($failures -join "`n")"
}
if (-not $Quiet) {
    Write-Host (
        "Semantic tests passed in $($tasks.Count) isolated task(s) with " +
        "$parallelism scheduler slot(s).")
    Write-Host "Timing evidence: $timingOutput"
}
