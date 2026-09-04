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

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer `
    'Semantic test sharding requires the canonical Linux container.'
if ($Fast -and $NoBuild) {
    throw '-Fast and -NoBuild cannot be combined.'
}

$TimeoutSeconds = Resolve-SharpProofSolutionTestTimeoutSeconds `
    -RepositoryRoot $repositoryRoot `
    -TimeoutSeconds $TimeoutSeconds `
    -WasSpecified $PSBoundParameters.ContainsKey('TimeoutSeconds')
$parallelism = Get-SharpProofSemanticTestParallelism `
    -RepositoryRoot $repositoryRoot
$architectureParallelRunSettings = Join-Path `
    $repositoryRoot 'eng/test/architecture-parallel.runsettings'
$semanticSolutionFilter = Join-Path `
    $repositoryRoot 'SharpProof.Semantic.Tests.slnf'
$semanticSolution = Get-Content -LiteralPath $semanticSolutionFilter -Raw |
    ConvertFrom-Json
$semanticProjects = @($semanticSolution.solution.projects |
        ForEach-Object { ([string]$_).Replace('\', '/') })
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
        @($semanticSolution.solution.projects) + @(
            'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj')
    }
    $semanticBuildFilter = Join-Path $repositoryRoot (
        '.sharpproof-semantic-build-' +
        [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        [pscustomobject]@{
            solution = [ordered]@{
                path = 'SharpProof.sln'
                projects = $semanticBuildProjects
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
$architectureClassPrefix = 'SharpProof.ArchitectureTest.'
$architectureCoverageHotspot =
    $architectureClassPrefix +
    'CoverageScriptTests.AuthenticatedCoverageRejectsReportMutations'
$architectureFixtures = @(
    'AcceptanceScriptTests',
    'ArchitectureTests',
    'BoundaryEnforcementTests',
    'BuildSchedulingTests',
    'ChangedTestSelectionTests',
    'ContainedPathAuthorityTests',
    'ContainerAuthorityScriptTests',
    'ContainerSourceCleanlinessTests',
    'CoverageScriptTests',
    'DependencyAutomationTests',
    'DevCheckCommandPlanTests',
    'DocumentationSupportContractTests',
    'FuzzRunnerEvidenceTests',
    'FuzzRunnerEvidenceProcessSafetyTests',
    'GeneratedCodeAnalysisConfigurationTests',
    'GeneratedFileHelperTests',
    'NativeTestBootstrapTests',
    'OpenCodePluginDependencyTests',
    'PackageDependencyAuthorityTests',
    'PilotAuthorityTests',
    'ProductionInventoryAuthorityTests',
    'PublicationDestinationAuthorityTests',
    'PublicationPlanIdentityTests',
    'PublicationPlanTopologyTests',
    'ReleaseAuthorityClosureTests',
    'ReleaseConfigurationScriptTests',
    'ReleaseCoverageBaselineTests',
    'ReleaseJsonAuthorityTests',
    'ReleaseQualificationMatrixTests',
    'ReleaseTagValidationTests',
    'ReleaseVersionAuthorityTests',
    'SharedTestInfrastructureTests',
    'StandaloneGateEvidenceTests',
    'VerifierPublicationTransactionTests'
)
$architectureFixtureSlots = @{
    BoundaryEnforcementTests = 4
    CoverageScriptTests = 8
    DocumentationSupportContractTests = 4
    PackageDependencyAuthorityTests = 4
    ProductionInventoryAuthorityTests = 8
    PublicationDestinationAuthorityTests = 4
    PublicationPlanIdentityTests = 4
    ReleaseAuthorityClosureTests = 8
    ReleaseCoverageBaselineTests = 8
}
$architectureShardingEnabled =
    $ArchitectureOnly -or (
        -not $coverageEnabled -and
        [string]::IsNullOrWhiteSpace($TestFilter))
$semanticProjectShardingEnabled =
    $architectureShardingEnabled -and -not $ArchitectureOnly
$workerClassPrefix = 'SharpProof.Worker.Test.'
$claimFilter =
    'FullyQualifiedName~' + $workerClassPrefix + 'ClaimManifestBuilderTests'
$manifestFilter =
    'FullyQualifiedName~' + $workerClassPrefix + 'CompilerManifestArtifactTests'
$workerCoreClasses = @(
    'WorkerTests',
    'WorkerProgramTests',
    'CompilerCallableLowererTests'
)
$workerCoreFilter = @($workerCoreClasses | ForEach-Object {
        'FullyQualifiedName~' + $workerClassPrefix + $_
    }) -join '|'
$workerRemainderFilter = @(
    @('ClaimManifestBuilderTests', 'CompilerManifestArtifactTests') +
    $workerCoreClasses |
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
$semanticProjectFilter = if ($architectureShardingEnabled) {
    '(FullyQualifiedName!~' + $architectureClassPrefix + ')&(' +
        $semanticFilter + ')'
}
else {
    $semanticFilter
}
$claimTaskFilter = "($claimFilter)&($semanticFilter)"
$manifestTaskFilter = "($manifestFilter)&($semanticFilter)"
$workerCoreTaskFilter = "($workerCoreFilter)&($semanticFilter)"
$workerRemainderTaskFilter = "($workerRemainderFilter)&($semanticFilter)"
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
                Target = Join-Path $repositoryRoot 'SharpProof.Semantic.Tests.slnf'
                Filter = $semanticProjectFilter
                ProjectParallelism = $mainParallelism
                IsolateOutput = $false
                Slots = $mainParallelism
                DefaultEstimatedMilliseconds = 60000L
            })
    }
    $tasks.Add(
        [pscustomobject]@{
            Name = 'worker-claim-manifest'
            Target = $testProject
            Filter = $claimTaskFilter
            ProjectParallelism = 0
            IsolateOutput = $true
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 30000L
        })
    $tasks.Add(
        [pscustomobject]@{
            Name = 'worker-compiler-manifest'
            Target = $testProject
            Filter = $manifestTaskFilter
            ProjectParallelism = 0
            IsolateOutput = $true
            Slots = [Math]::Min($parallelism, 4)
            DefaultEstimatedMilliseconds = 50000L
        })
    $tasks.Add(
        [pscustomobject]@{
            Name = 'worker-core'
            Target = $testProject
            Filter = $workerCoreTaskFilter
            ProjectParallelism = 0
            IsolateOutput = $true
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 50000L
        })
    $tasks.Add(
        [pscustomobject]@{
            Name = 'worker-remainder'
            Target = $testProject
            Filter = $workerRemainderTaskFilter
            ProjectParallelism = 0
            IsolateOutput = $true
            Slots = [Math]::Min($parallelism, 2)
            DefaultEstimatedMilliseconds = 20000L
        })
}
if ($architectureShardingEnabled) {
    $architectureProject = Join-Path $repositoryRoot (
        'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj')
    foreach ($fixture in $architectureFixtures) {
        if ($fixture -ceq 'CoverageScriptTests') {
            $tasks.Add([pscustomobject]@{
                Name = 'architecture-coveragescripttests-hotspot'
                Target = $architectureProject
                Filter = "(FullyQualifiedName~$architectureCoverageHotspot)&(" +
                    $semanticFilter + ')'
                ProjectParallelism = 0
                IsolateOutput = $false
                Slots = [Math]::Min($parallelism, 8)
                RunSettings = $architectureParallelRunSettings
                DefaultEstimatedMilliseconds = 20000L
            })
            $tasks.Add([pscustomobject]@{
                Name = 'architecture-coveragescripttests-remainder'
                Target = $architectureProject
                Filter = '(FullyQualifiedName~' + $architectureClassPrefix +
                    $fixture + ".)&(FullyQualifiedName!~$architectureCoverageHotspot)&(" +
                    $semanticFilter + ')'
                ProjectParallelism = 0
                IsolateOutput = $false
                Slots = [Math]::Min($parallelism, 8)
                RunSettings = $architectureParallelRunSettings
                DefaultEstimatedMilliseconds = 20000L
            })
            continue
        }
        $requestedSlots = if (
            $architectureFixtureSlots.ContainsKey($fixture)) {
            [int]$architectureFixtureSlots[$fixture]
        }
        else {
            1
        }
        $slots = [Math]::Min($parallelism, $requestedSlots)
        $tasks.Add([pscustomobject]@{
            Name = 'architecture-' + $fixture.ToLowerInvariant()
            Target = $architectureProject
            Filter = '(FullyQualifiedName~' + $architectureClassPrefix +
                $fixture + '.)&(' + $semanticFilter + ')'
            ProjectParallelism = 0
            IsolateOutput = $false
            Slots = $slots
            DefaultEstimatedMilliseconds = [long]($requestedSlots * 10000)
        })
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

$temporaryResults = -not $coverageEnabled
$resultsRoot = if ($coverageEnabled) {
    $resolvedCoverageResults
}
else {
    Join-Path ([IO.Path]::GetTempPath()) (
        'sharpproof-semantic-tests-' + [Guid]::NewGuid().ToString('N'))
}
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null
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
            $arguments += "/logger:trx;LogFileName=$($task.Name).trx"
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
                '--logger', "trx;LogFileName=$($task.Name).trx",
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
    foreach ($failure in @($testRun.Failures)) {
        $failures.Add(
            "$($failure.Test.Name) exited $($failure.ExitCode).")
    }
}
finally {
    if ($temporaryResults -and [IO.Directory]::Exists($resultsRoot)) {
        [IO.Directory]::Delete($resultsRoot, $true)
    }
    Remove-SharpProofCoverageOutput -Directory $isolatedOutputRoot
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
