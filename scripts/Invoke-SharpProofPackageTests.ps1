[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [switch]$NoBuild,

    [switch]$Fast,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800,

    [switch]$Quiet,

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Package tests require the canonical Linux container.'
}
if ($Fast -and $NoBuild) {
    throw '-Fast and -NoBuild cannot be combined.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
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
$coverageEnabled =
    -not [string]::IsNullOrWhiteSpace($CoverageSettings) -or
    -not [string]::IsNullOrWhiteSpace($CoverageResultsDirectory)
if ($coverageEnabled -and
    ([string]::IsNullOrWhiteSpace($CoverageSettings) -or
     [string]::IsNullOrWhiteSpace($CoverageResultsDirectory))) {
    throw (
        'CoverageSettings and CoverageResultsDirectory must be supplied ' +
        'together.')
}
$resolvedCoverageSettings = if ($coverageEnabled) {
    (Resolve-Path -LiteralPath $CoverageSettings -ErrorAction Stop).Path
}
else {
    ''
}
$resolvedCoverageResults = if ($coverageEnabled) {
    [IO.Path]::GetFullPath($CoverageResultsDirectory)
}
else {
    ''
}
$testAssembly = if ($NoBuild -and -not $coverageEnabled) {
    Get-SharpProofTestAssemblyPath `
        -ProjectPath $testProject `
        -Configuration $Configuration
}
else {
    ''
}
$isolatedOutputRoot = if ($coverageEnabled) {
    Join-Path $repositoryRoot (
        '.sharpproof-coverage-output-' + [Guid]::NewGuid().ToString('N'))
}
else {
    ''
}

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
        $rows = [Collections.Generic.List[object]]::new()
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
                $rows.Add([pscustomobject]@{
                    ClassName = $definition.ClassName
                    MethodName = $definition.MethodName
                    Duration = [string]$result.duration
                })
            }
        }
        $script:SharpProofTrxTimingRowsCache[$ResultsRoot] = $rows
    }

    $milliseconds = @{}
    foreach ($row in @($script:SharpProofTrxTimingRowsCache[$ResultsRoot])) {
        if ($row.ClassName -cne $ClassName) {
            continue
        }
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
                Where-Object { $listedMethods -contains $_.Name } |
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
function Invoke-TimedPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
    }
    finally {
        $timer.Stop()
        $phaseTimings.Add([pscustomobject]@{
            name = $Name
            elapsedMilliseconds = [long]$timer.Elapsed.TotalMilliseconds
        })
    }
}
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
    if (-not $NoBuild) {
        Invoke-TimedPhase -Name 'restore' -Action {
            Invoke-SharpProofRequiredDotnet `
                -Arguments @(
                    'restore', 'SharpProof.sln', '--locked-mode',
                    '/nodeReuse:false') `
                -TimeoutSeconds $TimeoutSeconds `
                -Quiet:$Quiet
        }
    }

    $builds = [Collections.Generic.List[object]]::new()
    if (-not $NoBuild) {
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
        Invoke-TimedPhase -Name 'build-prerequisites' -Action {
            Invoke-RequiredBuilds -Builds @($builds)
        }
    }

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        $packageManifest = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'scripts/package-projects.json') -Raw |
            ConvertFrom-Json
        Invoke-TimedPhase -Name 'pack' -Action {
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

    $pending = [Collections.Generic.Queue[object]]::new()
    foreach ($shard in @($shards | Sort-Object `
            @{ Expression = 'EstimatedMilliseconds'; Descending = $true }, `
            @{ Expression = 'Name'; Descending = $false })) {
        $pending.Enqueue($shard)
    }
    $running = [Collections.Generic.List[object]]::new()
    $shardTimings = [Collections.Generic.List[object]]::new()
    $failures = [Collections.Generic.List[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $testPhase = [Diagnostics.Stopwatch]::StartNew()

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $parallelism) {
            $next = $pending.Peek()
            $nextIsExclusive =
                $next.PSObject.Properties.Name -contains 'Exclusive' -and
                [bool]$next.Exclusive
            if ($nextIsExclusive -and $running.Count -gt 0) {
                break
            }
            $shard = $pending.Dequeue()
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
            if ($coverageEnabled) {
                $arguments += @(
                    '--settings', $resolvedCoverageSettings,
                    '--collect', 'Code Coverage;Format=Cobertura')
            }
            $startInfo = New-SharpProofParallelProcessStartInfo `
                -FileName 'dotnet' `
                -WorkingDirectory $repositoryRoot `
                -Arguments $arguments `
                -Environment $environment
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                $process.Dispose()
                throw "Could not start package test $($shard.Name)."
            }
            $running.Add([pscustomobject]@{
                Shard = $shard
                Process = $process
                StartedUtc = $process.StartTime.ToUniversalTime()
                StandardOutput = $process.StandardOutput.ReadToEndAsync()
                StandardError = $process.StandardError.ReadToEndAsync()
            })
            if ($nextIsExclusive) {
                break
            }
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            foreach ($active in @($running)) {
                if (-not $active.Process.HasExited) {
                    $active.Process.Kill($true)
                }
            }
            throw "Parallel package tests exceeded $TimeoutSeconds seconds."
        }

        $completed = @($running | Where-Object { $_.Process.HasExited })
        if ($completed.Count -eq 0) {
            Start-Sleep -Milliseconds 100
            continue
        }
        foreach ($active in $completed) {
            $active.Process.WaitForExit()
            $stdout = $active.StandardOutput.GetAwaiter().GetResult()
            $stderr = $active.StandardError.GetAwaiter().GetResult()
            $exitCode = $active.Process.ExitCode
            if (-not $Quiet -or $exitCode -ne 0) {
                Write-Host "--- Package test $($active.Shard.Name) ---"
                if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                    Write-Host $stdout.TrimEnd()
                }
                if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                    Write-Host $stderr.TrimEnd()
                }
            }
            if ($exitCode -ne 0) {
                $failures.Add(
                    "$($active.Shard.Name) exited ${exitCode}: " +
                    $active.Shard.Filter)
            }
            $shardTimings.Add([pscustomobject]@{
                name = $active.Shard.Name
                filter = $active.Shard.Filter
                elapsedMilliseconds = [long](
                    ($active.Process.ExitTime.ToUniversalTime() -
                        $active.StartedUtc).TotalMilliseconds)
                exitCode = $exitCode
            })
            [void]$running.Remove($active)
            $active.Process.Dispose()
        }
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
    if ($coverageEnabled -and
        [IO.Directory]::Exists($isolatedOutputRoot)) {
        [IO.Directory]::Delete($isolatedOutputRoot, $true)
    }
}
