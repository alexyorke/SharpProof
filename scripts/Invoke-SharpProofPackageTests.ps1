[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [switch]$NoBuild,

    [switch]$ReuseTestHarness,

    [switch]$Fast,

    [int]$TimeoutSeconds,

    [switch]$Quiet,

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer `
    'Package tests require the canonical Linux container.'
Assert-SharpProofTestSwitches -Fast:$Fast -NoBuild:$NoBuild

$TimeoutSeconds = Resolve-SharpProofSolutionTestTimeoutSeconds `
    -RepositoryRoot $repositoryRoot `
    -TimeoutSeconds $TimeoutSeconds `
    -WasSpecified $PSBoundParameters.ContainsKey('TimeoutSeconds')
$parallelism = Get-SharpProofPackageTestParallelism `
    -RepositoryRoot $repositoryRoot
$buildParallelism = Get-SharpProofBuildParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetCommand = Get-Command `
    dotnet `
    -CommandType Application `
    -ErrorAction Stop | Select-Object -First 1
$dotnetItem = Get-Item -LiteralPath $dotnetCommand.Source
$dotnetTarget = $dotnetItem.ResolveLinkTarget($true)
$resolvedDotnetHost = if ($null -eq $dotnetTarget) {
    $dotnetItem.FullName
}
else {
    $dotnetTarget.FullName
}
if (-not [IO.Path]::IsPathRooted($resolvedDotnetHost) -or
    -not (Test-Path -LiteralPath $resolvedDotnetHost -PathType Leaf)) {
    throw "Could not resolve the canonical dotnet host: $resolvedDotnetHost"
}
$testProject = Join-Path `
    $repositoryRoot 'SharpProof.Package.Test/SharpProof.Package.Test.csproj'
$coverage = New-SharpProofCoverageContext `
    -RepositoryRoot $repositoryRoot `
    -CoverageSettings $CoverageSettings `
    -CoverageResultsDirectory $CoverageResultsDirectory
$coverageEnabled = [bool]$coverage.Enabled
$resolvedCoverageSettings = [string]$coverage.Settings
$resolvedCoverageResults = [string]$coverage.Results
$testAssembly = if (($NoBuild -or $ReuseTestHarness) -and
    -not $coverageEnabled) {
    Get-SharpProofTestAssemblyPath `
        -ProjectPath $testProject `
        -Configuration $Configuration
}
else {
    ''
}
$isolatedOutputRoot = [string]$coverage.IsolatedOutputRoot

function Invoke-RequiredBuilds {
    param([Parameter(Mandatory = $true)][object[]]$Builds)

    Invoke-SharpProofParallelDotnetBuilds `
        -Builds $Builds `
        -RepositoryRoot $repositoryRoot `
        -Parallelism $buildParallelism `
        -TimeoutSeconds $TimeoutSeconds `
        -Quiet:$Quiet
}

function New-SharpProofWeightedBuckets {
    param(
        [Parameter(Mandatory = $true)][object[]]$Methods,
        [Parameter(Mandatory = $true)][hashtable]$HistoricalMilliseconds,
        [Parameter(Mandatory = $true)][long]$DefaultMilliseconds,
        [Parameter(Mandatory = $true)][int]$BucketCount
    )

    $buckets = @(
        for ($index = 0; $index -lt $BucketCount; $index++) {
            [pscustomobject]@{
                Index = $index
                Methods = [Collections.Generic.List[string]]::new()
                EstimatedMilliseconds = 0L
            }
        })
    $orderedMethods = @($Methods | Sort-Object `
        @{ Expression = {
                if ($HistoricalMilliseconds.ContainsKey($_)) {
                    [long]$HistoricalMilliseconds[$_]
                }
                else {
                    $DefaultMilliseconds
                }
            }; Descending = $true }, `
        @{ Expression = { $_ }; Descending = $false })
    foreach ($method in $orderedMethods) {
        $bucket = $buckets | Sort-Object `
            EstimatedMilliseconds, `
            @{ Expression = { $_.Methods.Count } }, `
            Index | Select-Object -First 1
        $bucket.Methods.Add($method)
        $bucket.EstimatedMilliseconds +=
            $(if ($HistoricalMilliseconds.ContainsKey($method)) {
                [long]$HistoricalMilliseconds[$method]
            }
            else {
                $DefaultMilliseconds
            })
    }

    return $buckets
}

function Get-SharpProofBucketMaximum {
    param(
        [Parameter(Mandatory = $true)][object[]]$Buckets,
        [Parameter(Mandatory = $true)][hashtable]$HistoricalMilliseconds,
        [Parameter(Mandatory = $true)][long]$DefaultMilliseconds
    )

    $maximum = 0L
    foreach ($bucket in $Buckets) {
        $estimate = 0L
        foreach ($method in $bucket.Methods) {
            $estimate += if ($HistoricalMilliseconds.ContainsKey($method)) {
                [long]$HistoricalMilliseconds[$method]
            }
            else {
                $DefaultMilliseconds
            }
        }
        if ($estimate -gt $maximum) {
            $maximum = $estimate
        }
    }
    return $maximum
}

$script:SharpProofTrxTimingRowsCache = @{}

function Get-TestMethodTimings {
    param(
        [Parameter(Mandatory = $true)][string]$ResultsRoot,
        [Parameter(Mandatory = $true)][string]$ClassName
    )

    if (-not $script:SharpProofTrxTimingRowsCache.ContainsKey($ResultsRoot)) {
        $rowsByClass = [Collections.Generic.Dictionary[string,
            Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
        foreach ($trx in Get-ChildItem `
                -LiteralPath $ResultsRoot -Recurse -Filter *.trx) {
            [xml]$document = Get-Content -LiteralPath $trx.FullName -Raw
            $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
            $namespace.AddNamespace(
                'trx',
                'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $definitions = @{}
            foreach ($definition in @($document.SelectNodes(
                    '//trx:UnitTest', $namespace))) {
                $method = $definition.SelectSingleNode(
                    'trx:TestMethod', $namespace)
                if ($null -ne $method) {
                    $definitions[[string]$definition.id] = [pscustomobject]@{
                        ClassName = [string]$method.className
                        MethodName = [string]$method.name
                    }
                }
            }
            foreach ($result in @($document.SelectNodes(
                    '//trx:UnitTestResult', $namespace))) {
                $testId = [string]$result.testId
                if (-not $definitions.ContainsKey($testId)) {
                    continue
                }
                $definition = $definitions[$testId]
                $classRows = $null
                if (-not $rowsByClass.TryGetValue(
                        $definition.ClassName,
                        [ref]$classRows)) {
                    $classRows = [Collections.Generic.List[object]]::new()
                    $rowsByClass.Add(
                        $definition.ClassName,
                        $classRows)
                }
                $classRows.Add([pscustomobject]@{
                    ClassName = $definition.ClassName
                    MethodName = $definition.MethodName
                    Duration = [string]$result.duration
                })
            }
        }
        $script:SharpProofTrxTimingRowsCache[$ResultsRoot] = $rowsByClass
    }

    $rowsByClass = $script:SharpProofTrxTimingRowsCache[$ResultsRoot]
    $rows = $null
    if (-not $rowsByClass.TryGetValue($ClassName, [ref]$rows)) {
        return @()
    }
    $milliseconds = @{}
    foreach ($row in $rows) {
        $match = [regex]::Match(
            $row.MethodName,
            '^(?<name>[A-Za-z_][A-Za-z0-9_]*)')
        if (-not $match.Success) {
            continue
        }
        $name = $match.Groups['name'].Value
        $elapsed = [TimeSpan]::Parse(
            $row.Duration,
            [Globalization.CultureInfo]::InvariantCulture)
        if (-not $milliseconds.ContainsKey($name)) {
            $milliseconds[$name] = 0L
        }
        $milliseconds[$name] += [long][Math]::Ceiling(
            $elapsed.TotalMilliseconds)
    }
    return @($milliseconds.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{
                name = [string]$_.Key
                elapsedMilliseconds = [long]$_.Value
            }
        } | Sort-Object name)
}

function Get-DiscoveredTestMethods {
    param(
        [Parameter(Mandatory = $true)][string]$Assembly,
        [Parameter(Mandatory = $true)][string[]]$ClassNames,
        [Parameter(Mandatory = $true)][hashtable]$MinimumCounts,
        [Parameter(Mandatory = $true)][hashtable]$Descriptions
    )

    $list = & dotnet vstest $Assembly /ListTests 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not discover package test methods.'
    }
    $listedMethodSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches(
        $list,
        '(?m)^\s{4}(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|\s*$)')) {
        [void]$listedMethodSet.Add($match.Groups['method'].Value)
    }
    $testAssembly = [Reflection.Assembly]::LoadFrom($Assembly)
    $bindingFlags = [Reflection.BindingFlags]::Instance -bor
        [Reflection.BindingFlags]::Static -bor
        [Reflection.BindingFlags]::Public -bor
        [Reflection.BindingFlags]::NonPublic
    $methodsByClass = @{}
    foreach ($className in $ClassNames) {
        $type = $testAssembly.GetType($className, $false, $false)
        $methods = if ($null -eq $type) {
            @()
        }
        else {
            @($type.GetMethods($bindingFlags) |
                Where-Object { $listedMethodSet.Contains([string]$_.Name) } |
                ForEach-Object { $_.Name } |
                Sort-Object -Unique)
        }
        if ($methods.Count -lt [int]$MinimumCounts[$className]) {
            $description = [string]$Descriptions[$className]
            throw (
                "$description discovery returned only " +
                "$($methods.Count) test methods.")
        }
        $methodsByClass[$className] = $methods
    }
    return $methodsByClass
}

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-package-tests-' + [Guid]::NewGuid().ToString('N'))
$feed = if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    Join-Path $root 'feed'
}
else {
    (Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
}
$results = if ($coverageEnabled) {
    # Coverage callers may retain the results directory across selected and
    # default campaigns. Keep this run's TRX files in a private directory so
    # timing extraction cannot mix historical shard receipts into the new
    # scheduler profile. Coverage report collection is recursive, so the
    # additional campaign level remains part of the published evidence.
    Join-Path $resolvedCoverageResults (
        'campaign-' + [Guid]::NewGuid().ToString('N'))
}
else {
    Join-Path $root 'results'
}
if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    [IO.Directory]::CreateDirectory($feed) | Out-Null
}
[IO.Directory]::CreateDirectory($results) | Out-Null
$campaign = [Diagnostics.Stopwatch]::StartNew()
$phaseTimings = [Collections.Generic.List[object]]::new()
$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$canonicalPackageFilter =
    'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
$useDefaultShardPlan = [string]::IsNullOrWhiteSpace($TestFilter) -or
    $TestFilter -ceq $canonicalPackageFilter
$timingStem = 'package-tests-' + $Configuration.ToLowerInvariant()
$timingSuffix = if ($coverageEnabled) { '-coverage' } else { '' }
$canonicalTimingOutput = Join-Path $timingDirectory (
    $timingStem + $timingSuffix + '.json')
$timingOutput = Join-Path $timingDirectory (
    $timingStem + $(if ($Fast) { '-fast' } else { '' }) +
    $(if ($useDefaultShardPlan) { '' } else { '-selected' }) +
    $timingSuffix + '.json')
$priorMethodMilliseconds = @{}
$priorPackageLayoutMethodMilliseconds = @{}
$priorFilterMilliseconds = @{}
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
        $priorTiming = Get-Content -LiteralPath $priorTimingPath -Raw |
            ConvertFrom-Json
        # Do not let a profile from another lane width distort the current
        # weighted partition.  This matters when a local run changes
        # SHARPPROOF_TEST_PROJECT_PARALLELISM and then returns to the normal
        # container width; method timings are not comparable under different
        # contention levels.
        if ($priorTiming.PSObject.Properties.Name -contains 'parallelism' -and
            [int]$priorTiming.parallelism -ne $parallelism) {
            continue
        }
        $hasScheduler =
            $priorTiming.PSObject.Properties.Name -contains 'scheduler'
        $methodHistory = if ($hasScheduler) {
            @($priorTiming.scheduler.workerMethods)
        }
        else {
            @($priorTiming.workerMethods)
        }
        foreach ($method in $methodHistory) {
            $elapsed = [long]$method.elapsedMilliseconds
            if ($elapsed -gt 0) {
                $priorMethodMilliseconds[[string]$method.name] = $elapsed
            }
        }
        $packageLayoutMethodHistory = if ($hasScheduler -and
            $priorTiming.scheduler.PSObject.Properties.Name -contains
                'packageLayoutMethods') {
            @($priorTiming.scheduler.packageLayoutMethods)
        }
        else {
            @()
        }
        foreach ($method in $packageLayoutMethodHistory) {
            $elapsed = [long]$method.elapsedMilliseconds
            if ($elapsed -gt 0) {
                $priorPackageLayoutMethodMilliseconds[
                    [string]$method.name] = $elapsed
            }
        }
        $filterHistory = if ($hasScheduler) {
            @($priorTiming.scheduler.filters)
        }
        else {
            @($priorTiming.shards)
        }
        foreach ($shard in $filterHistory) {
            $elapsed = [long]$shard.elapsedMilliseconds
            if ($elapsed -gt 0) {
                $priorFilterMilliseconds[[string]$shard.filter] = $elapsed
            }
        }
    }
    catch {
        Write-Warning (
            "Ignoring malformed package timing '$priorTimingPath': " +
            $_.Exception.Message)
    }
}

try {
    if (-not $NoBuild -and -not $ReuseTestHarness) {
        Invoke-SharpProofTimedPhase -Name 'restore' `
            -Timings $phaseTimings -RecordOnFailure -Action {
            Invoke-SharpProofRequiredDotnet `
                -Arguments @(
                    'restore', 'SharpProof.slnx', '--locked-mode',
                    '/nodeReuse:false') `
                -TimeoutSeconds $TimeoutSeconds `
                -Quiet:$Quiet
        }
    }

    $builds = [Collections.Generic.List[object]]::new()
    if (-not $NoBuild -and -not $ReuseTestHarness) {
        $testHarnessBuildArguments = @(
            'build', $testProject, '-c', $Configuration,
            '--no-restore')
        if ($Fast) {
            $testHarnessBuildArguments +=
                '-p:RunAnalyzersDuringBuild=false'
        }
        $builds.Add([pscustomobject]@{
            Name = 'test-harness-' + $Configuration.ToLowerInvariant()
            Arguments = $testHarnessBuildArguments
        })
    }
    # The Release test-harness project references Verifier with no output
    # assembly, so its build already materializes the complete package
    # dependency closure. Keep the explicit product build for Debug, where
    # the harness is not built in the package's Release configuration.
    if ([string]::IsNullOrWhiteSpace($PackageSource) -and -not $NoBuild -and
        $Configuration -cne 'Release') {
        $packageProductBuildArguments = @(
            'build',
            'SharpProof.Verifier/SharpProof.Verifier.csproj',
            '-c', 'Release', '--no-restore',
            '-p:GeneratePackageOnBuild=false')
        if ($Fast) {
            $packageProductBuildArguments +=
                '-p:RunAnalyzersDuringBuild=false'
        }
        $builds.Add([pscustomobject]@{
            Name = 'package-products-release'
            Arguments = $packageProductBuildArguments
        })
    }
    if ($builds.Count -gt 0) {
        Invoke-SharpProofTimedPhase -Name 'build-prerequisites' `
            -Timings $phaseTimings -RecordOnFailure -Action {
            if ($Configuration -ceq 'Release' -and $builds.Count -eq 2) {
                # The test harness builds the complete Release dependency
                # closure. Wait for it before building the empty Verifier
                # root, whose package payload consumes those outputs.
                Invoke-RequiredBuilds -Builds @(
                    $builds | Where-Object Name -eq 'test-harness-release')
                Invoke-RequiredBuilds -Builds @(
                    $builds | Where-Object Name -eq 'package-products-release')
            }
            else {
                Invoke-RequiredBuilds -Builds @($builds)
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        Invoke-SharpProofTimedPhase -Name 'pack' `
            -Timings $phaseTimings -RecordOnFailure -Action {
            Invoke-SharpProofRequiredDotnet `
                -Arguments @(
                    'pack', 'SharpProof.slnx', '-c', 'Release',
                    '--no-restore', '--no-build', '--nologo',
                    '/nodeReuse:false', '--output', $feed,
                    '/p:GeneratePackageOnBuild=false') `
                -TimeoutSeconds $TimeoutSeconds `
                -Quiet:$Quiet
        }
    }

    $workerClass =
        'SharpProof.Package.Test.WorkerMsBuildIntegrationTests'
    $packageLayoutClass =
        'SharpProof.Package.Test.PackageLayoutSmokeTests'
    function Get-SharpProofHistoricalFilterMilliseconds {
        param([Parameter(Mandatory = $true)][string]$Filter)

        $historyFilters = if ($TestFilter -ceq $canonicalPackageFilter) {
            @("($TestFilter)&($Filter)", $Filter)
        }
        else {
            @($Filter)
        }
        foreach ($historyFilter in $historyFilters) {
            if ($priorFilterMilliseconds.ContainsKey($historyFilter)) {
                return [long]$priorFilterMilliseconds[$historyFilter]
            }
        }
        return $null
    }

    $containmentSlots = if ($parallelism -ge 16) {
        [Math]::Ceiling($parallelism / 4.0)
    }
    else {
        [Math]::Ceiling($parallelism / 2.0)
    }
    $shards = [Collections.Generic.List[object]]::new()
    if (-not $useDefaultShardPlan) {
        $shards.Add([pscustomobject]@{
            Name = 'selected'
            Filter = $TestFilter
            EstimatedMilliseconds =
                $(if ($priorFilterMilliseconds.ContainsKey($TestFilter)) {
                    [long]$priorFilterMilliseconds[$TestFilter]
                }
                else {
                    1L
                })
        })
    }
    else {
        if ([string]::IsNullOrWhiteSpace($testAssembly)) {
            $testAssembly = Get-SharpProofTestAssemblyPath `
                -ProjectPath $testProject `
                -Configuration $Configuration
        }
        $discoveredMethods = Get-DiscoveredTestMethods `
            -Assembly $testAssembly `
            -ClassNames @($workerClass, $packageLayoutClass) `
            -MinimumCounts @{
                $workerClass = 40
                $packageLayoutClass = 15
            } `
            -Descriptions @{
                $workerClass = 'Worker MSBuild integration'
                $packageLayoutClass = 'package-layout'
            }
        $workerMethods = @($discoveredMethods[$workerClass])
        $threeTargetWorkerMethod =
            'ThreeTargetAbsoluteSarifSurvivesSerialInitialAndParallelIncrementalAndCleanBuilds'
        $threeTargetWorkerHistoryNames = @(
            $threeTargetWorkerMethod,
            'ThreeTargetAbsoluteSarifSurvivesSerialIncrementalAndCleanBuilds')
        # This test performs one serial initial build followed by parallel
        # incremental and clean builds. At the normal wide package wave,
        # keeping it in a worker bucket lets its analyzer CPU compete with
        # every other worker and creates the tail. Reserve a bounded slice at
        # every useful multi-lane width; retain the bucketed path for 1-3
        # lanes where it would consume the wave.
        $isolateThreeTargetWorker =
            $parallelism -ge 4 -and
            $workerMethods -contains $threeTargetWorkerMethod
        $bucketWorkerMethods = if ($isolateThreeTargetWorker) {
            @($workerMethods | Where-Object {
                    $_ -cne $threeTargetWorkerMethod
                })
        }
        else {
            $workerMethods
        }
        # Keep one worker bucket per available lane. Nested fixture workers
        # are charged separately through each shard's Slots reservation, so
        # reducing the bucket count only makes the heavy integration methods
        # share a process and creates a longer tail.
        $workerShardLimit = [Math]::Max(1, $parallelism)
        $workerShardCount = [Math]::Min(
            $bucketWorkerMethods.Count,
            $workerShardLimit)
        # Method durations include analyzer and nested-MSBuild contention, so
        # historical LPT is not always a better CI-width plan.  Build both
        # deterministic count-balanced and historical plans, and use history
        # only when it predicts a materially shorter worker tail.  Wider
        # containers retain the historical plan because their extra lanes make
        # those samples useful and the existing wide-wave tuning depends on it.
        $countBalancedBuckets = @(New-SharpProofWeightedBuckets `
            -Methods $bucketWorkerMethods `
            -HistoricalMilliseconds @{} `
            -DefaultMilliseconds 1L `
            -BucketCount $workerShardCount)
        $weightedBuckets = @(New-SharpProofWeightedBuckets `
            -Methods $bucketWorkerMethods `
            -HistoricalMilliseconds $priorMethodMilliseconds `
            -DefaultMilliseconds 1L `
            -BucketCount $workerShardCount)
        $countMaximum = Get-SharpProofBucketMaximum `
            -Buckets $countBalancedBuckets `
            -HistoricalMilliseconds $priorMethodMilliseconds `
            -DefaultMilliseconds 1L
        $weightedMaximum = Get-SharpProofBucketMaximum `
            -Buckets $weightedBuckets `
            -HistoricalMilliseconds $priorMethodMilliseconds `
            -DefaultMilliseconds 1L
        $workerBuckets = if ($parallelism -le 4) {
            if ($weightedMaximum + 1000L -lt $countMaximum) {
                $weightedBuckets
            }
            else {
                $countBalancedBuckets
            }
        }
        else {
            $weightedBuckets
        }
        $packageLayoutMethods = @($discoveredMethods[$packageLayoutClass])
        $packageLayoutFilter = "FullyQualifiedName~$packageLayoutClass"
        $defaultPackageLayoutMethodMilliseconds =
            if ($null -ne ($packageLayoutHistoricalMilliseconds =
                    Get-SharpProofHistoricalFilterMilliseconds `
                        $packageLayoutFilter)) {
                [long][Math]::Max(
                    1,
                    [Math]::Ceiling(
                        [long]$packageLayoutHistoricalMilliseconds /
                            [double]$packageLayoutMethods.Count))
            }
            else {
                # A fresh CI-width run has no method history. The layout
                # host launches nested MSBuild processes and is substantially
                # heavier than a single worker method; give it a conservative
                # one-second estimate so it starts before the exclusive
                # postflight shards instead of becoming a cold-start tail.
                if ($parallelism -le 4) { 1000L } else { 1L }
            }
        # Split the layout fixture only on wider waves. Two hosts let the
        # uneven restore/build methods overlap while each host retains its
        # four-worker NUnit pool; a four-lane wave keeps one host so it does
        # not serialize two full pools or duplicate package-cache startup.
        $packageLayoutBucketCount = if ($parallelism -ge 8) { 2 } else { 1 }
        $packageLayoutBuckets = @(New-SharpProofWeightedBuckets `
            -Methods $packageLayoutMethods `
            -HistoricalMilliseconds $priorPackageLayoutMethodMilliseconds `
            -DefaultMilliseconds $defaultPackageLayoutMethodMilliseconds `
            -BucketCount $packageLayoutBucketCount)
        $fixtureClasses = @(
            'CompilerProbeInputConsistencyTests|CompilerProbeSnapshotTests|SarifProjectionTests|VerifierDiagnosticTransportTests|VerifierProcessSupervisorBug202Tests|DependencyAuditScriptTests|LauncherArgumentTests|RefutedContractDiagnosticTests',
            'FinalCompilationProbeTests',
            'ReleasePublicationScriptTests')
        foreach ($fixtureClass in $fixtureClasses) {
            $classNames = $fixtureClass -split '\|'
            $classFilters = @($classNames | ForEach-Object {
                    "FullyQualifiedName~SharpProof.Package.Test.$_"
                })
            $filter = $classFilters -join '|'
            $estimatedMilliseconds =
                Get-SharpProofHistoricalFilterMilliseconds $filter
            if ($null -eq $estimatedMilliseconds) {
                # Retain the individual-class lookup for timing files written
                # by older schedulers and fixture-specific test runs.
                $estimatedMilliseconds = [long](@($classFilters |
                        ForEach-Object {
                            if ($priorFilterMilliseconds.ContainsKey($_)) {
                                [long]$priorFilterMilliseconds[$_]
                            }
                            else {
                                1L
                            }
                        } | Measure-Object -Sum).Sum)
            }
            # Keep the shard directory short enough for TRX and coverage
            # output paths on Linux filesystems. The first fixture groups
            # several small classes, so its filter is intentionally long.
            $fixtureName = if ($classNames.Count -gt 1) {
                'fixture-core'
            }
            else {
                'fixture-' + $fixtureClass.ToLowerInvariant()
            }
            $shards.Add([pscustomobject]@{
                Name = $fixtureName
                Filter = $filter
                EstimatedMilliseconds = $estimatedMilliseconds
                # FinalCompilationProbeTests runs up to four NUnit children
                # concurrently, each launching an MSBuild process. Charge
                # those internal workers to the outer wave scheduler so they
                # do not consume untracked CPU alongside every worker shard.
                Slots = if ($fixtureClass -ceq 'FinalCompilationProbeTests') {
                    [Math]::Min(4, $parallelism)
                }
                else {
                    1
                }
                })
        }
        $buildTaskClass = 'SharpProof.Package.Test.BuildTaskTests'
        $isolatedBuildTaskMethods = @(
            'OversizedVerifierOutputTriggersPromptBoundedContainment',
            'VerifierExecutionRetainsLiveIncompleteCleanupAnchor',
            'VerifierTaskBoundsTheWholeLauncherProcess',
            'VerifierPreLaunchSetupDoesNotConsumeCleanupReserve')
        $remainingBuildTaskFilter = "FullyQualifiedName~$buildTaskClass"
        foreach ($method in $isolatedBuildTaskMethods) {
            $remainingBuildTaskFilter +=
                "&FullyQualifiedName!~$buildTaskClass.$method"
        }
        $isolatedBuildTaskFilter = @($isolatedBuildTaskMethods | ForEach-Object {
                "FullyQualifiedName~$buildTaskClass.$_"
            }) -join '|'
        $shards.Add([pscustomobject]@{
            Name = 'postflight-buildtask-main'
            Filter = $remainingBuildTaskFilter
            EstimatedMilliseconds =
                $(if ($null -ne ($historicalMilliseconds =
                        Get-SharpProofHistoricalFilterMilliseconds `
                            $remainingBuildTaskFilter)) {
                    $historicalMilliseconds
                }
                else {
                    10000L
                })
            # Keep the fresh dotnet test host required by BuildTaskTests, but
            # schedule this independent process by duration, not as a tail
            # after every integration shard has started.
            Slots = 1
            Exclusive = $true
        })
        $shards.Add([pscustomobject]@{
            Name = 'postflight-buildtask-containment'
            Filter = $isolatedBuildTaskFilter
            EstimatedMilliseconds =
                $(if ($null -ne ($historicalMilliseconds =
                        Get-SharpProofHistoricalFilterMilliseconds `
                            $isolatedBuildTaskFilter)) {
                    $historicalMilliseconds
                }
                else {
                    8000L
                })
            # These deadline-sensitive tests need headroom. Keep the
            # half-machine reservation at CI width, but use a quarter-machine
            # reservation on wide local waves so worker shards can enter the
            # first wave instead of waiting behind containment setup.
            Slots = [Math]::Max(
                1,
                $containmentSlots)
            Exclusive = $true
        })
        foreach ($bucket in $packageLayoutBuckets) {
            $filter = @($bucket.Methods | ForEach-Object {
                    "FullyQualifiedName~$packageLayoutClass.$_"
                }) -join '|'
            $shards.Add([pscustomobject]@{
                Name = 'package-layout-' + ($bucket.Index + 1).ToString(
                    'D2', [Globalization.CultureInfo]::InvariantCulture)
                Filter = $filter
                EstimatedMilliseconds =
                    $(if ($null -ne ($historicalMilliseconds =
                            Get-SharpProofHistoricalFilterMilliseconds $filter)) {
                        $historicalMilliseconds
                    }
                    else {
                        $bucket.EstimatedMilliseconds
                    })
                # PackageLayoutSmokeTests enables NUnit child parallelism up
                # to four workers. Charge that nested pool to the outer
                # scheduler so the package-layout host does not oversubscribe
                # the integration-test wave alongside worker shards.
                Slots = [Math]::Min(4, $parallelism)
            })
        }
        foreach ($bucket in @($workerBuckets | Where-Object {
                    $_.Methods.Count -gt 0
                })) {
            $shards.Add([pscustomobject]@{
                Name = 'worker-' + ($bucket.Index + 1).ToString(
                    'D2', [Globalization.CultureInfo]::InvariantCulture)
                Filter = @($bucket.Methods | ForEach-Object {
                    "FullyQualifiedName~$workerClass.$_"
                }) -join '|'
                EstimatedMilliseconds = $bucket.EstimatedMilliseconds
                Slots = 1
            })
        }
        if ($isolateThreeTargetWorker) {
            $threeTargetEstimatedMilliseconds = 1L
            foreach ($historyName in $threeTargetWorkerHistoryNames) {
                if ($priorMethodMilliseconds.ContainsKey($historyName)) {
                    $threeTargetEstimatedMilliseconds =
                        [long]$priorMethodMilliseconds[$historyName]
                    break
                }
            }
            $shards.Add([pscustomobject]@{
                Name = 'worker-three-target'
                Filter = "FullyQualifiedName~$workerClass.$threeTargetWorkerMethod"
                EstimatedMilliseconds = $threeTargetEstimatedMilliseconds
                # The test performs a complete analyzer pass for each
                # framework. Reserve a bounded lane slice while allowing the
                # independent worker shards to overlap its work.
                Slots = [Math]::Min(3, $parallelism)
            })
        }
    }

    if ([string]::IsNullOrWhiteSpace($testAssembly) -and
        -not $coverageEnabled) {
        $testAssembly = Get-SharpProofTestAssemblyPath `
            -ProjectPath $testProject `
            -Configuration $Configuration
    }

    if ($TestFilter -ceq $canonicalPackageFilter) {
        foreach ($shard in $shards) {
            $shard.Filter = "($TestFilter)&($($shard.Filter))"
        }
    }

    # Start high-resource shards first so reserved lanes overlap their tails
    # instead of leaving the containment process as the final wave.
    $orderedShards = @($shards | Sort-Object `
        @{ Expression = {
                $slots = if ($_.PSObject.Properties.Name -contains 'Slots') {
                    [long]$_.Slots
                }
                elseif ($_.PSObject.Properties.Name -contains 'Exclusive' -and
                    [bool]$_.Exclusive) {
                    [long]$parallelism
                }
                else {
                    1L
                }
                [long]$_.EstimatedMilliseconds * $slots
            }; Descending = $true }, `
        @{ Expression = 'EstimatedMilliseconds'; Descending = $true }, `
        @{ Expression = 'Name'; Descending = $false })
    $testPhase = [Diagnostics.Stopwatch]::StartNew()
    $preparePackageTest = {
        param([object]$shard)

        $nextIsExclusive =
            $shard.PSObject.Properties.Name -contains 'Exclusive' -and
            [bool]$shard.Exclusive
        $environment = @{
            SHARPPROOF_PACKAGE_SOURCE = $feed
        }
        $isolatedOutput = ''
        if ($coverageEnabled) {
            $isolatedOutput = New-SharpProofIsolatedTestOutput `
                -SourceDirectory (Join-Path $repositoryRoot (
                    'SharpProof.Package.Test/bin/' + $Configuration +
                    '/net9.0')) `
                -DestinationDirectory (Join-Path `
                    $isolatedOutputRoot (
                        $shard.Name + '/' + $Configuration + '/net9.0'))
        }
        $directVstest = -not $coverageEnabled -and
            -not $nextIsExclusive
        if ($directVstest) {
            $environment['DOTNET_HOST_PATH'] = $resolvedDotnetHost
        }
        $arguments = if ($directVstest) {
            @('vstest', $testAssembly)
        }
        else {
            @(
                'test', $testProject, '-c', $Configuration,
                '--no-build', '--no-restore')
        }
        if (-not $directVstest -and
            -not [string]::IsNullOrWhiteSpace($isolatedOutput)) {
            $arguments += '-p:OutDir=' + $isolatedOutput + '/'
        }
        if ($directVstest) {
            $arguments += '/TestCaseFilter:' + $shard.Filter
            $arguments += '/logger:console;verbosity=minimal'
            $arguments += "/logger:trx;LogFileName=$($shard.Name).trx"
            $arguments += '/ResultsDirectory:' + (
                Join-Path $results $shard.Name)
        }
        else {
            $arguments += @(
                '--filter', $shard.Filter,
                '--logger', 'console;verbosity=minimal',
                '--logger', "trx;LogFileName=$($shard.Name).trx",
                '--results-directory', (Join-Path $results $shard.Name))
        }
        $arguments = Add-SharpProofCoverageArguments `
            -Arguments $arguments `
            -Enabled $coverageEnabled `
            -Settings $resolvedCoverageSettings
        return [pscustomobject]@{
            Arguments = $arguments
            Environment = $environment
        }
    }.GetNewClosure()
    $testRun = Invoke-SharpProofParallelDotnetTests `
        -Tests $orderedShards `
        -RepositoryRoot $repositoryRoot `
        -Parallelism $parallelism `
        -TimeoutSeconds $TimeoutSeconds `
        -Prepare $preparePackageTest `
        -Label 'Package test' `
        -Quiet:$Quiet
    $shardTimings = [Collections.Generic.List[object]]::new()
    foreach ($result in @($testRun.Completed)) {
        $shardTimings.Add([pscustomobject]@{
            name = $result.Test.Name
            filter = $result.Test.Filter
            elapsedMilliseconds = $result.ElapsedMilliseconds
            exitCode = $result.ExitCode
        })
    }
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($failure in @($testRun.Completed | Where-Object {
                $_.ExitCode -ne 0
            })) {
        $failures.Add(
            "$($failure.Test.Name) exited $($failure.ExitCode): " +
            $failure.Test.Filter)
    }
    $testPhase.Stop()
    $phaseTimings.Add([pscustomobject]@{
        name = 'test-shards'
        elapsedMilliseconds = [long]$testPhase.Elapsed.TotalMilliseconds
    })

    $campaign.Stop()
    $workerMethodTimings = Get-TestMethodTimings `
        -ResultsRoot $results `
        -ClassName $workerClass
    $packageLayoutMethodTimings = Get-TestMethodTimings `
        -ResultsRoot $results `
        -ClassName $packageLayoutClass
    foreach ($entry in $workerMethodTimings) {
        $priorMethodMilliseconds[[string]$entry.name] =
            [long]$entry.elapsedMilliseconds
    }
    foreach ($entry in $packageLayoutMethodTimings) {
        $priorPackageLayoutMethodMilliseconds[[string]$entry.name] =
            [long]$entry.elapsedMilliseconds
    }
    foreach ($entry in $shardTimings) {
        $priorFilterMilliseconds[[string]$entry.filter] =
            [long]$entry.elapsedMilliseconds
    }
    $temporaryTiming =
        $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [pscustomobject]@{
        schemaVersion = 1
        command = 'package-tests'
        configuration = $Configuration
        fast = [bool]$Fast
        parallelism = $parallelism
        totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
        phases = @($phaseTimings)
        shards = @($shardTimings | Sort-Object name)
        workerMethods = $workerMethodTimings
        packageLayoutMethods = $packageLayoutMethodTimings
        scheduler = [ordered]@{
            workerMethods = @(
                $priorMethodMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            name = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object name)
            packageLayoutMethods = @(
                $priorPackageLayoutMethodMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            name = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object name)
            filters = @(
                $priorFilterMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            filter = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object filter)
        }
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force

    if ($failures.Count -ne 0) {
        throw "Package test shards failed:`n$($failures -join "`n")"
    }
    if (-not $Quiet) {
        Write-Host (
            "Package tests passed in $($shards.Count) isolated shard(s) " +
            "with parallelism $parallelism.")
        Write-Host "Timing evidence: $timingOutput"
    }
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
    Remove-SharpProofOwnedDirectory -Directory $isolatedOutputRoot
}
