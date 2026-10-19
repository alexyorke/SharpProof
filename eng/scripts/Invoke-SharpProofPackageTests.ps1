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
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PackageTestDiscovery.ps1')
& (Join-Path $PSScriptRoot 'Test-SharpProofPackageDiscovery.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
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
    $repositoryRoot 'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj'
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

function ConvertTo-SharpProofTimingHistory {
    param([AllowNull()][object]$Value)
    $items = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($item in @($Value)) { $items.Add($item) }
    return , $items.ToImmutable()
}

function Set-SharpProofTimingEstimate {
    param(
        [Parameter(Mandatory = $true)][Collections.Generic.IDictionary[string, long]]$Estimates,
        [AllowEmptyString()][string]$Name,
        [long]$Milliseconds
    )
    $storedKey = [string]::Empty
    if ($Estimates.TryGetKey($Name, [ref]$storedKey)) {
        $Estimates[$storedKey] = $Milliseconds
    }
    else {
        $Estimates.Add($Name, $Milliseconds)
    }
}

function Invoke-RequiredBuilds {
    param([Parameter(Mandatory = $true)][Collections.Generic.IReadOnlyList[object]]$Builds)

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
        [Parameter(Mandatory = $true)][Collections.Generic.IReadOnlyDictionary[string, long]]$HistoricalMilliseconds,
        [Parameter(Mandatory = $true)][long]$DefaultMilliseconds,
        [Parameter(Mandatory = $true)][int]$BucketCount
    )

    $bucketBuilders = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $estimatedMilliseconds = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, long]()
    for ($index = 0; $index -lt $BucketCount; $index++) {
        $bucketBuilders.Add(([ordered]@{
                    Index   = $index
                    Methods = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
                }).AsReadOnly())
        $estimatedMilliseconds.Add($index, 0L)
    }
    $orderedMethods = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            $Methods | Sort-Object -Property @(
                @{ Expression     = {
                        if ($HistoricalMilliseconds.ContainsKey($_)) {
                            [long]$HistoricalMilliseconds[$_]
                        }
                        else { $DefaultMilliseconds }
                    }; Descending = $true
                },
                @{ Expression = { $_ }; Descending = $false })))
    foreach ($method in $orderedMethods) {
        $bucket = $bucketBuilders | Sort-Object -Property @(
            @{ Expression = { $estimatedMilliseconds[$_.Index] } },
            @{ Expression = { $_.Methods.Count } },
            @{ Expression = { $_.Index } }) | Select-Object -First 1
        $bucket.Methods.Add($method)
        $estimatedMilliseconds[$bucket.Index] +=
        $(if ($HistoricalMilliseconds.ContainsKey($method)) {
                [long]$HistoricalMilliseconds[$method]
            }
            else { $DefaultMilliseconds })
    }

    $buckets = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($bucket in $bucketBuilders) {
        $buckets.Add(([ordered]@{
                    Index                 = $bucket.Index
                    Methods               = $bucket.Methods.ToImmutable()
                    EstimatedMilliseconds = $estimatedMilliseconds[$bucket.Index]
                }).AsReadOnly())
    }
    return $buckets.ToImmutable()
}

function Get-SharpProofBucketMaximum {
    param(
        [Parameter(Mandatory = $true)][object[]]$Buckets,
        [Parameter(Mandatory = $true)][Collections.Generic.IReadOnlyDictionary[string, long]]$HistoricalMilliseconds,
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

$script:SharpProofTrxTimingRowsCache =
[Collections.Immutable.ImmutableDictionary]::Create[string, object](
    [StringComparer]::OrdinalIgnoreCase)

function Get-TestMethodTimings {
    param(
        [Parameter(Mandatory = $true)][string]$ResultsRoot,
        [Parameter(Mandatory = $true)][string]$ClassName
    )

    if (-not $script:SharpProofTrxTimingRowsCache.ContainsKey($ResultsRoot)) {
        $rowBuilders = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object](
            [StringComparer]::Ordinal)
        foreach ($trx in Get-ChildItem `
                -LiteralPath $ResultsRoot -Recurse -Filter *.trx) {
            [xml]$document = Get-Content -LiteralPath $trx.FullName -Raw
            $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
            $namespace.AddNamespace(
                'trx',
                'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $definitionBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object](
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($definition in @($document.SelectNodes(
                        '//trx:UnitTest', $namespace))) {
                $method = $definition.SelectSingleNode(
                    'trx:TestMethod', $namespace)
                if ($null -ne $method) {
                    $definitionBuilder[[string]$definition.id] = ([ordered]@{
                            ClassName  = [string]$method.className
                            MethodName = [string]$method.name
                        }).AsReadOnly()
                }
            }
            $definitions = $definitionBuilder.ToImmutable()
            foreach ($result in @($document.SelectNodes(
                        '//trx:UnitTestResult', $namespace))) {
                $testId = [string]$result.testId
                if (-not $definitions.ContainsKey($testId)) {
                    continue
                }
                $definition = $definitions[$testId]
                $classRows = $null
                if (-not $rowBuilders.TryGetValue(
                        $definition.ClassName,
                        [ref]$classRows)) {
                    $classRows = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
                    $rowBuilders.Add(
                        $definition.ClassName,
                        $classRows)
                }
                $classRows.Add(([ordered]@{
                            ClassName  = $definition.ClassName
                            MethodName = $definition.MethodName
                            Duration   = [string]$result.duration
                        }).AsReadOnly())
            }
        }
        $rowsByClassBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object](
            [StringComparer]::Ordinal)
        foreach ($entry in $rowBuilders.GetEnumerator()) {
            $rowsByClassBuilder.Add($entry.Key, $entry.Value.ToImmutable())
        }
        $cacheBuilder = $script:SharpProofTrxTimingRowsCache.ToBuilder()
        $cacheBuilder[$ResultsRoot] = $rowsByClassBuilder.ToImmutable()
        $script:SharpProofTrxTimingRowsCache = $cacheBuilder.ToImmutable()
    }

    $rowsByClass = $script:SharpProofTrxTimingRowsCache[$ResultsRoot]
    $rows = $null
    if (-not $rowsByClass.TryGetValue($ClassName, [ref]$rows)) {
        return @()
    }
    $milliseconds = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long](
        [StringComparer]::OrdinalIgnoreCase)
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
        $canonicalName = $null
        if ($milliseconds.TryGetKey($name, [ref]$canonicalName)) {
            $name = $canonicalName
        }
        else {
            $milliseconds[$name] = 0L
        }
        $milliseconds[$name] += [long][Math]::Ceiling(
            $elapsed.TotalMilliseconds)
    }
    return @($milliseconds.ToImmutable().GetEnumerator() | ForEach-Object {
            ([ordered]@{
                name                = [string]$_.Key
                elapsedMilliseconds = [long]$_.Value
            }).AsReadOnly()
        } | Sort-Object { $_.name })
}

function Get-DiscoveredTestMethods {
    param(
        [Parameter(Mandatory = $true)][string]$Assembly,
        [Parameter(Mandatory = $true)][string[]]$ClassNames,
        [Parameter(Mandatory = $true)][hashtable]$MinimumCounts,
        [Parameter(Mandatory = $true)][hashtable]$Descriptions
    )

    # Discovery must execute in the assembly's dotnet testhost. PowerShell may
    # run on an older runtime and cannot safely reflect a net10 test assembly.
    $listingPath = Join-Path ([IO.Path]::GetTempPath()) (
        'SharpProofPackageDiscovery.' + [Guid]::NewGuid().ToString('N') + '.txt')
    try {
        $list = & dotnet vstest $Assembly /ListFullyQualifiedTests "/ListTestsTargetPath:$listingPath" 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $listingPath -PathType Leaf)) {
            throw ('Could not discover package test methods: ' + $list)
        }
        return Get-SharpProofPackageMethodsFromDiscovery `
            -FullyQualifiedNames @(Get-Content -LiteralPath $listingPath) `
            -ClassNames $ClassNames -MinimumCounts $MinimumCounts -Descriptions $Descriptions
    }
    finally {
        [IO.File]::Delete($listingPath)
    }
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
$phaseTimings = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
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
$priorMethodMillisecondsBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long]([StringComparer]::OrdinalIgnoreCase)
$priorPackageLayoutMethodMillisecondsBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long]([StringComparer]::OrdinalIgnoreCase)
$priorFilterMillisecondsBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, long]([StringComparer]::OrdinalIgnoreCase)
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
            (ConvertTo-SharpProofTimingHistory -Value $priorTiming.scheduler.workerMethods)
        }
        else {
            (ConvertTo-SharpProofTimingHistory -Value $priorTiming.workerMethods)
        }
        foreach ($method in $methodHistory) {
            $elapsed = [long]$method.elapsedMilliseconds
            if ($elapsed -gt 0) {
                Set-SharpProofTimingEstimate -Estimates $priorMethodMillisecondsBuilder -Name ([string]$method.name) -Milliseconds $elapsed
            }
        }
        $packageLayoutMethodHistory = if ($hasScheduler -and
            $priorTiming.scheduler.PSObject.Properties.Name -contains
            'packageLayoutMethods') {
            (ConvertTo-SharpProofTimingHistory -Value $priorTiming.scheduler.packageLayoutMethods)
        }
        else {
            ([Collections.Immutable.ImmutableArray[object]]::Empty)
        }
        foreach ($method in $packageLayoutMethodHistory) {
            $elapsed = [long]$method.elapsedMilliseconds
            if ($elapsed -gt 0) {
                Set-SharpProofTimingEstimate -Estimates $priorPackageLayoutMethodMillisecondsBuilder -Name ([string]$method.name) -Milliseconds $elapsed
            }
        }
        $filterHistory = if ($hasScheduler) {
            (ConvertTo-SharpProofTimingHistory -Value $priorTiming.scheduler.filters)
        }
        else {
            (ConvertTo-SharpProofTimingHistory -Value $priorTiming.shards)
        }
        foreach ($shard in $filterHistory) {
            $elapsed = [long]$shard.elapsedMilliseconds
            if ($elapsed -gt 0) {
                Set-SharpProofTimingEstimate -Estimates $priorFilterMillisecondsBuilder -Name ([string]$shard.filter) -Milliseconds $elapsed
            }
        }
    }
    catch {
        Write-Warning (
            "Ignoring malformed package timing '$priorTimingPath': " +
            $_.Exception.Message)
    }
}

$priorMethodMilliseconds = $priorMethodMillisecondsBuilder.ToImmutable()
$priorPackageLayoutMethodMilliseconds = $priorPackageLayoutMethodMillisecondsBuilder.ToImmutable()
$priorFilterMilliseconds = $priorFilterMillisecondsBuilder.ToImmutable()

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

    $buildBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    if (-not $NoBuild -and -not $ReuseTestHarness) {
        $testHarnessBuildArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $testHarnessBuildArgumentBuilder.AddRange([string[]]@(
                'build', $testProject, '-c', $Configuration,
                '--no-restore'))
        if ($Fast) {
            $testHarnessBuildArgumentBuilder.Add('-p:RunAnalyzersDuringBuild=false')
        }
        $buildBuilder.Add([pscustomobject]@{
                Name      = 'test-harness-' + $Configuration.ToLowerInvariant()
                Arguments = $testHarnessBuildArgumentBuilder.ToImmutable()
            })
    }
    # The Release test-harness project references Verifier with no output
    # assembly, so its build already materializes the complete package
    # dependency closure. Keep the explicit product build for Debug, where
    # the harness is not built in the package's Release configuration.
    if ([string]::IsNullOrWhiteSpace($PackageSource) -and -not $NoBuild -and
        $Configuration -cne 'Release') {
        $packageProductBuildArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $packageProductBuildArgumentBuilder.AddRange([string[]]@(
                'build',
                'src/SharpProof.Verifier/SharpProof.Verifier.csproj',
                '-c', 'Release', '--no-restore',
                '-p:GeneratePackageOnBuild=false'))
        if ($Fast) {
            $packageProductBuildArgumentBuilder.Add('-p:RunAnalyzersDuringBuild=false')
        }
        $buildBuilder.Add([pscustomobject]@{
                Name      = 'package-products-release'
                Arguments = $packageProductBuildArgumentBuilder.ToImmutable()
            })
    }
    $builds = $buildBuilder.ToImmutable()
    if ($builds.Length -gt 0) {
        Invoke-SharpProofTimedPhase -Name 'build-prerequisites' `
            -Timings $phaseTimings -RecordOnFailure -Action {
            if ($Configuration -ceq 'Release' -and $builds.Length -eq 2) {
                # The test harness builds the complete Release dependency
                # closure. Wait for it before building the empty Verifier
                # root, whose package payload consumes those outputs.
                $harnessBuilds = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
                $productBuilds = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
                foreach ($build in $builds) {
                    if ($build.Name -eq 'test-harness-release') { $harnessBuilds.Add($build) }
                    if ($build.Name -eq 'package-products-release') { $productBuilds.Add($build) }
                }
                Invoke-RequiredBuilds -Builds ($harnessBuilds.ToImmutable())
                Invoke-RequiredBuilds -Builds ($productBuilds.ToImmutable())
            }
            else {
                Invoke-RequiredBuilds -Builds $builds
            }
        }
    }

    & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration
    if ($Configuration -ne 'Release') {
        & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') -RepositoryRoot $repositoryRoot -Configuration Release
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
    $verifierDefaultsClass =
    'SharpProof.Package.Test.VerifierPackageDefaultsTests'
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

    $shards = [Collections.Generic.List[object]]::new()
    if (-not $useDefaultShardPlan) {
        $shards.Add([pscustomobject]@{
                Name                  = 'selected'
                Filter                = $TestFilter
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
            -ClassNames @($workerClass, $packageLayoutClass, $verifierDefaultsClass) `
            -MinimumCounts @{
            $workerClass           = 40
            $packageLayoutClass    = 15
            $verifierDefaultsClass = 4
        } `
            -Descriptions @{
            $workerClass           = 'Worker MSBuild integration'
            $packageLayoutClass    = 'package-layout'
            $verifierDefaultsClass = 'verifier-package defaults'
        }
        $workerMethods = @($discoveredMethods[$workerClass])
        $threeTargetWorkerMethod =
        'ThreeTargetAbsoluteSarifSurvivesSerialInitialAndParallelIncrementalAndCleanBuilds'
        New-Variable -Name threeTargetWorkerHistoryNames -Option Constant -Value (
            [System.Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    $threeTargetWorkerMethod,
                    'ThreeTargetAbsoluteSarifSurvivesSerialIncrementalAndCleanBuilds')))
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
                -HistoricalMilliseconds ([Collections.Immutable.ImmutableDictionary[string, long]]::Empty) `
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
        New-Variable -Name fixtureClasses -Option Constant -Value (
            [System.Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    'CompilerCaptureFreshnessControlTests',
                    'CompilerCaptureFreshnessRegressionTests',
                    'CompilerProbeInputConsistencyTests|CompilerProbeSnapshotTests|SarifProjectionTests|VerifierDiagnosticTransportTests|LauncherArgumentTests|LauncherWorkflowTests|LauncherResultPresentationTests|RefutedContractDiagnosticTests|RuntimeEnvironmentIsolationTests',
                    'NaNPublicBuildWorkflowAuditTests',
                    'VerifierPackageDefaultsTests',
                    'VerificationPublicationNullArgumentTests',
                    'FinalCompilationProbeTests')))
        $plannedFixtures = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($name in @($workerClass, $packageLayoutClass,
                'SharpProof.Package.Test.BuildTaskTests')) {
            [void]$plannedFixtures.Add($name)
        }
        foreach ($group in $fixtureClasses) {
            foreach ($name in ($group -split '\|')) {
                [void]$plannedFixtures.Add("SharpProof.Package.Test.$name")
            }
        }
        # Inspect metadata without loading fixture dependencies into PowerShell's
        # runtime. Package fixtures follow the <Name>Tests naming convention.
        $fixtureStream = [IO.File]::OpenRead($testAssembly)
        $fixturePeReader = [Reflection.PortableExecutable.PEReader]::new($fixtureStream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(
                $fixturePeReader, [Reflection.Metadata.MetadataReaderOptions]::None)
            foreach ($handle in $metadata.TypeDefinitions) {
                $definition = $metadata.GetTypeDefinition($handle)
                $namespace = $metadata.GetString($definition.Namespace)
                $name = $metadata.GetString($definition.Name)
                if ($namespace -ceq 'SharpProof.Package.Test' -and $name.EndsWith('Tests', [StringComparison]::Ordinal)) {
                    $fullName = "$namespace.$name"
                    if (-not $plannedFixtures.Contains($fullName)) {
                        throw "Package fixture is missing from the default shard plan: $fullName"
                    }
                }
            }
        }
        finally {
            $fixturePeReader.Dispose()
            $fixtureStream.Dispose()
        }
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
                    Name                  = $fixtureName
                    Filter                = $filter
                    EstimatedMilliseconds = $estimatedMilliseconds
                    # FinalCompilationProbeTests runs up to four NUnit children
                    # concurrently, each launching an MSBuild process. Charge
                    # those internal workers to the outer wave scheduler so they
                    # do not consume untracked CPU alongside every worker shard.
                    Slots                 = if ($fixtureClass -ceq 'FinalCompilationProbeTests') {
                        [Math]::Min(4, $parallelism)
                    }
                    else {
                        1
                    }
                })
        }
        $buildTaskFilter = 'FullyQualifiedName~SharpProof.Package.Test.BuildTaskTests'
        $shards.Add([pscustomobject]@{
                Name                  = 'postflight-buildtask'
                Filter                = $buildTaskFilter
                EstimatedMilliseconds =
                $(if ($null -ne ($historicalMilliseconds =
                            Get-SharpProofHistoricalFilterMilliseconds `
                                $buildTaskFilter)) {
                        $historicalMilliseconds
                    }
                    else {
                        10000L
                    })
                # BuildTaskTests needs a fresh dotnet test host.
                Slots                 = 1
                Exclusive             = $true
            })
        foreach ($bucket in $packageLayoutBuckets) {
            $filter = @($bucket.Methods | ForEach-Object {
                    "FullyQualifiedName~$packageLayoutClass.$_"
                }) -join '|'
            $shards.Add([pscustomobject]@{
                    Name                  = 'package-layout-' + ($bucket.Index + 1).ToString(
                        'D2', [Globalization.CultureInfo]::InvariantCulture)
                    Filter                = $filter
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
                    Slots                 = [Math]::Min(4, $parallelism)
                })
        }
        foreach ($bucket in @($workerBuckets | Where-Object {
                    $_.Methods.Count -gt 0
                })) {
            $shards.Add([pscustomobject]@{
                    Name                  = 'worker-' + ($bucket.Index + 1).ToString(
                        'D2', [Globalization.CultureInfo]::InvariantCulture)
                    Filter                = @($bucket.Methods | ForEach-Object {
                            "FullyQualifiedName~$workerClass.$_"
                        }) -join '|'
                    EstimatedMilliseconds = $bucket.EstimatedMilliseconds
                    Slots                 = 1
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
                    Name                  = 'worker-three-target'
                    Filter                = "FullyQualifiedName~$workerClass.$threeTargetWorkerMethod"
                    EstimatedMilliseconds = $threeTargetEstimatedMilliseconds
                    # The test performs a complete analyzer pass for each
                    # framework. Reserve a bounded lane slice while allowing the
                    # independent worker shards to overlap its work.
                    Slots                 = [Math]::Min(3, $parallelism)
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
    $orderedShardBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($orderedShard in ($shards | Sort-Object `
            @{ Expression     = {
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
                }; Descending = $true
            }, `
            @{ Expression = 'EstimatedMilliseconds'; Descending = $true }, `
            @{ Expression = 'Name'; Descending = $false }) ) {
        $orderedShardBuilder.Add($orderedShard)
    }
    $orderedShards = $orderedShardBuilder.ToImmutable()
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
                    'artifacts/bin/SharpProof.Package.IntegrationTests/' + $Configuration +
                    '/net10.0')) `
                -DestinationDirectory (Join-Path `
                    $isolatedOutputRoot (
                    $shard.Name + '/' + $Configuration + '/net10.0'))
        }
        $directVstest = -not $coverageEnabled -and
        -not $nextIsExclusive
        if ($directVstest) {
            $environment['DOTNET_HOST_PATH'] = $resolvedDotnetHost
        }
        $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        if ($directVstest) {
            $argumentBuilder.AddRange([string[]]@('vstest', $testAssembly))
        }
        else {
            $argumentBuilder.AddRange([string[]]@(
                    'test', $testProject, '-c', $Configuration,
                    '--no-build', '--no-restore'))
        }
        if (-not $directVstest -and
            -not [string]::IsNullOrWhiteSpace($isolatedOutput)) {
            $argumentBuilder.Add('-p:OutDir=' + $isolatedOutput + '/')
        }
        if ($directVstest) {
            $argumentBuilder.Add('/TestCaseFilter:' + $shard.Filter)
            $argumentBuilder.Add('/logger:console;verbosity=minimal')
            $argumentBuilder.Add("/logger:trx;LogFilePrefix=$($shard.Name)")
            $argumentBuilder.Add('/ResultsDirectory:' + (
                    Join-Path $results $shard.Name))
        }
        else {
            $argumentBuilder.AddRange([string[]]@(
                    '--filter', $shard.Filter,
                    '--logger', 'console;verbosity=minimal',
                    '--logger', "trx;LogFilePrefix=$($shard.Name)",
                    '--results-directory', (Join-Path $results $shard.Name)))
        }
        $coverageArguments = Add-SharpProofCoverageArguments `
            -Arguments ([string[]]@($argumentBuilder.ToImmutable())) `
            -Enabled $coverageEnabled `
            -Settings $resolvedCoverageSettings
        $arguments = [Collections.Immutable.ImmutableArray]::CreateRange[string](
            [string[]]@($coverageArguments))
        return [pscustomobject]@{
            Arguments   = $arguments
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
    $shardTimings = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    if ($coverageEnabled) {
        Save-SharpProofCoverageCohort -Root $results -Expected $orderedShards -Completed @($testRun.Completed) -ProjectPath $testProject
    }
    foreach ($result in @($testRun.Completed)) {
        $shardTimings.Add([pscustomobject]@{
                name                = $result.Test.Name
                filter              = $result.Test.Filter
                elapsedMilliseconds = $result.ElapsedMilliseconds
                exitCode            = $result.ExitCode
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
            name                = 'test-shards'
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
        Set-SharpProofTimingEstimate -Estimates $priorMethodMillisecondsBuilder -Name ([string]$entry.name) -Milliseconds ([long]$entry.elapsedMilliseconds)
    }
    foreach ($entry in $packageLayoutMethodTimings) {
        Set-SharpProofTimingEstimate -Estimates $priorPackageLayoutMethodMillisecondsBuilder -Name ([string]$entry.name) -Milliseconds ([long]$entry.elapsedMilliseconds)
    }
    foreach ($entry in $shardTimings) {
        Set-SharpProofTimingEstimate -Estimates $priorFilterMillisecondsBuilder -Name ([string]$entry.filter) -Milliseconds ([long]$entry.elapsedMilliseconds)
    }
    $priorMethodMilliseconds = $priorMethodMillisecondsBuilder.ToImmutable()
    $priorPackageLayoutMethodMilliseconds = $priorPackageLayoutMethodMillisecondsBuilder.ToImmutable()
    $priorFilterMilliseconds = $priorFilterMillisecondsBuilder.ToImmutable()
    $temporaryTiming =
    $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [pscustomobject]@{
        schemaVersion            = 1
        command                  = 'package-tests'
        configuration            = $Configuration
        fast                     = [bool]$Fast
        parallelism              = $parallelism
        totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
        phases                   = @($phaseTimings.ToImmutable())
        shards                   = @($shardTimings.ToImmutable() | Sort-Object name)
        workerMethods            = $workerMethodTimings
        packageLayoutMethods     = $packageLayoutMethodTimings
        scheduler                = [ordered]@{
            workerMethods            = @(
                $priorMethodMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            name                = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object name)
                packageLayoutMethods = @(
                    $priorPackageLayoutMethodMilliseconds.GetEnumerator() |
                        ForEach-Object {
                            [pscustomobject]@{
                                name                = [string]$_.Key
                                elapsedMilliseconds = [long]$_.Value
                            }
                        } | Sort-Object name)
                    filters          = @(
                        $priorFilterMilliseconds.GetEnumerator() |
                            ForEach-Object {
                                [pscustomobject]@{
                                    filter              = [string]$_.Key
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
