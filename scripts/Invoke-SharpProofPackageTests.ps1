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
if ($Fast -and $NoBuild) {
    throw '-Fast and -NoBuild cannot be combined.'
}

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
    $listedMethods = @(
        [regex]::Matches(
            $list,
            '(?m)^\s{4}(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|\s*$)') |
            ForEach-Object { $_.Groups['method'].Value } |
            Sort-Object -Unique)
    $listedMethodSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($listedMethod in $listedMethods) {
        [void]$listedMethodSet.Add([string]$listedMethod)
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
    $resolvedCoverageResults
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
$timingStem = 'package-tests-' + $Configuration.ToLowerInvariant()
$timingSuffix = if ($coverageEnabled) { '-coverage' } else { '' }
$canonicalTimingOutput = Join-Path $timingDirectory (
    $timingStem + $timingSuffix + '.json')
$timingOutput = Join-Path $timingDirectory (
    $timingStem + $(if ($Fast) { '-fast' } else { '' }) +
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
                    'restore', 'SharpProof.sln', '--locked-mode',
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
    if ([string]::IsNullOrWhiteSpace($PackageSource) -and -not $NoBuild) {
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
            Invoke-RequiredBuilds -Builds @($builds)
        }
    }

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        $packageManifest = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'scripts/package-projects.json') -Raw |
            ConvertFrom-Json
        Invoke-SharpProofTimedPhase -Name 'pack' `
            -Timings $phaseTimings -RecordOnFailure -Action {
            foreach ($project in @($packageManifest.projects)) {
                Invoke-SharpProofRequiredDotnet `
                    -Arguments @(
                        'pack', [string]$project, '-c', 'Release',
                        '--no-restore', '--no-build', '--nologo',
                        '/nodeReuse:false', '--output', $feed,
                        '/p:GeneratePackageOnBuild=false') `
                    -TimeoutSeconds $TimeoutSeconds `
                    -Quiet:$Quiet
            }
        }
    }

    $workerClass =
        'SharpProof.Package.Test.WorkerMsBuildIntegrationTests'
    $packageLayoutClass =
        'SharpProof.Package.Test.PackageLayoutSmokeTests'
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
    $workerBuckets = @(
        for ($index = 0; $index -lt $parallelism; $index++) {
            [pscustomobject]@{
                Index = $index
                Methods = [Collections.Generic.List[string]]::new()
                EstimatedMilliseconds = 0L
            }
        })
    $orderedWorkerMethods = @($workerMethods | Sort-Object `
        @{ Expression = {
                if ($priorMethodMilliseconds.ContainsKey($_)) {
                    [long]$priorMethodMilliseconds[$_]
                }
                else {
                    1L
                }
            }; Descending = $true }, `
        @{ Expression = { $_ }; Descending = $false })
    foreach ($method in $orderedWorkerMethods) {
        $bucket = $workerBuckets | Sort-Object `
            EstimatedMilliseconds, `
            @{ Expression = { $_.Methods.Count } }, `
            Index | Select-Object -First 1
        $bucket.Methods.Add($method)
        $bucket.EstimatedMilliseconds +=
            $(if ($priorMethodMilliseconds.ContainsKey($method)) {
                [long]$priorMethodMilliseconds[$method]
            }
            else {
                1L
            })
    }
    $packageLayoutMethods = @($discoveredMethods[$packageLayoutClass])
    $packageLayoutFilter =
        "FullyQualifiedName~$packageLayoutClass"
    $defaultPackageLayoutMethodMilliseconds =
        if ($priorFilterMilliseconds.ContainsKey($packageLayoutFilter)) {
            [long][Math]::Max(
                1,
                [Math]::Ceiling(
                    [long]$priorFilterMilliseconds[$packageLayoutFilter] /
                        [double]$packageLayoutMethods.Count))
        }
        else {
            1L
        }
    $packageLayoutBuckets = @(
        for ($index = 0; $index -lt [Math]::Min(4, $parallelism); $index++) {
            [pscustomobject]@{
                Index = $index
                Methods = [Collections.Generic.List[string]]::new()
                EstimatedMilliseconds = 0L
            }
        })
    $orderedPackageLayoutMethods = @($packageLayoutMethods | Sort-Object `
        @{ Expression = {
                if ($priorPackageLayoutMethodMilliseconds.ContainsKey($_)) {
                    [long]$priorPackageLayoutMethodMilliseconds[$_]
                }
                else {
                    $defaultPackageLayoutMethodMilliseconds
                }
            }; Descending = $true }, `
        @{ Expression = { $_ }; Descending = $false })
    foreach ($method in $orderedPackageLayoutMethods) {
        $bucket = $packageLayoutBuckets | Sort-Object `
            EstimatedMilliseconds, `
            @{ Expression = { $_.Methods.Count } }, `
            Index | Select-Object -First 1
        $bucket.Methods.Add($method)
        $bucket.EstimatedMilliseconds +=
            $(if ($priorPackageLayoutMethodMilliseconds.ContainsKey($method)) {
                [long]$priorPackageLayoutMethodMilliseconds[$method]
            }
            else {
                $defaultPackageLayoutMethodMilliseconds
            })
    }

    $shards = [Collections.Generic.List[object]]::new()
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
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
        $fixtureClasses = @(
            'DependencyAuditScriptTests',
            'FinalCompilationProbeTests',
            'LauncherArgumentTests',
            'ReleasePublicationScriptTests')
        foreach ($fixtureClass in $fixtureClasses) {
            $filter =
                "FullyQualifiedName~SharpProof.Package.Test.$fixtureClass"
            $shards.Add([pscustomobject]@{
                Name = 'fixture-' + $fixtureClass.ToLowerInvariant()
                Filter = $filter
                EstimatedMilliseconds =
                    $(if ($priorFilterMilliseconds.ContainsKey($filter)) {
                        [long]$priorFilterMilliseconds[$filter]
                    }
                    else {
                        1L
                    })
                })
        }
        $buildTaskClass = 'SharpProof.Package.Test.BuildTaskTests'
        $isolatedBuildTaskMethods = @(
            'OversizedVerifierOutputTriggersPromptBoundedContainment',
            'VerifierExecutionRetainsLiveIncompleteCleanupAnchor',
            'VerifierTaskBoundsTheWholeLauncherProcess')
        $remainingBuildTaskFilter = "FullyQualifiedName~$buildTaskClass"
        foreach ($method in $isolatedBuildTaskMethods) {
            $remainingBuildTaskFilter +=
                "&FullyQualifiedName!~$buildTaskClass.$method"
        }
        $shards.Add([pscustomobject]@{
            Name = 'postflight-buildtask-main'
            Filter = $remainingBuildTaskFilter
            EstimatedMilliseconds = -1L
            Exclusive = $true
        })
        foreach ($method in $isolatedBuildTaskMethods) {
            $shards.Add([pscustomobject]@{
                Name = 'postflight-buildtask-' + $method.ToLowerInvariant()
                Filter = "FullyQualifiedName~$buildTaskClass.$method"
                EstimatedMilliseconds = -1L
                Exclusive = $true
            })
        }
        foreach ($bucket in $packageLayoutBuckets) {
            $filter = @($bucket.Methods | ForEach-Object {
                    "FullyQualifiedName~$packageLayoutClass.$_"
                }) -join '|'
            $shards.Add([pscustomobject]@{
                Name = 'package-layout-' + ($bucket.Index + 1).ToString(
                    'D2', [Globalization.CultureInfo]::InvariantCulture)
                Filter = $filter
                EstimatedMilliseconds =
                    $(if ($priorFilterMilliseconds.ContainsKey($filter)) {
                        [long]$priorFilterMilliseconds[$filter]
                    }
                    else {
                        $bucket.EstimatedMilliseconds
                    })
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
            })
        }
    }

    $orderedShards = @($shards | Sort-Object `
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
    foreach ($failure in @($testRun.Failures)) {
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
    $schedulerMethodMilliseconds = @{}
    foreach ($entry in $priorMethodMilliseconds.GetEnumerator()) {
        $schedulerMethodMilliseconds[[string]$entry.Key] = [long]$entry.Value
    }
    foreach ($entry in $workerMethodTimings) {
        $schedulerMethodMilliseconds[[string]$entry.name] =
            [long]$entry.elapsedMilliseconds
    }
    $schedulerPackageLayoutMethodMilliseconds = @{}
    foreach ($entry in
        $priorPackageLayoutMethodMilliseconds.GetEnumerator()) {
        $schedulerPackageLayoutMethodMilliseconds[[string]$entry.Key] =
            [long]$entry.Value
    }
    foreach ($entry in $packageLayoutMethodTimings) {
        $schedulerPackageLayoutMethodMilliseconds[[string]$entry.name] =
            [long]$entry.elapsedMilliseconds
    }
    $schedulerFilterMilliseconds = @{}
    foreach ($entry in $priorFilterMilliseconds.GetEnumerator()) {
        $schedulerFilterMilliseconds[[string]$entry.Key] = [long]$entry.Value
    }
    foreach ($entry in $shardTimings) {
        $schedulerFilterMilliseconds[[string]$entry.filter] =
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
                $schedulerMethodMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            name = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object name)
            packageLayoutMethods = @(
                $schedulerPackageLayoutMethodMilliseconds.GetEnumerator() |
                    ForEach-Object {
                        [pscustomobject]@{
                            name = [string]$_.Key
                            elapsedMilliseconds = [long]$_.Value
                        }
                    } | Sort-Object name)
            filters = @(
                $schedulerFilterMilliseconds.GetEnumerator() |
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
    Remove-SharpProofCoverageOutput -Directory $isolatedOutputRoot
}
