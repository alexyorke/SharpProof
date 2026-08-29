[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [switch]$NoBuild,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800,

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Assert-SharpProofUniqueJsonProperties.ps1')
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Package tests require the canonical Linux container.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.PackageScheduling.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
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
$isolatedOutputRoot = if ($coverageEnabled) {
    Join-Path $repositoryRoot (
        '.sharpproof-coverage-output-' + [Guid]::NewGuid().ToString('N'))
}
else {
    ''
}

function Invoke-RequiredDotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-WorkerMethodTimings {
    param(
        [Parameter(Mandatory = $true)][string]$ResultsRoot,
        [Parameter(Mandatory = $true)][string]$WorkerClass
    )

    $milliseconds = @{}
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
            if ($definition.ClassName -cne $WorkerClass) {
                continue
            }
            $match = [regex]::Match(
                $definition.MethodName,
                '^(?<name>[A-Za-z_][A-Za-z0-9_]*)')
            if (-not $match.Success) {
                continue
            }
            $name = $match.Groups['name'].Value
            $elapsed = [TimeSpan]::Parse(
                [string]$result.duration,
                [Globalization.CultureInfo]::InvariantCulture)
            if (-not $milliseconds.ContainsKey($name)) {
                $milliseconds[$name] = 0L
            }
            $milliseconds[$name] += [long][Math]::Ceiling(
                $elapsed.TotalMilliseconds)
        }
    }
    return @($milliseconds.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{
                name = [string]$_.Key
                elapsedMilliseconds = [long]$_.Value
            }
        } | Sort-Object name)
}

function Get-ListedTestNames {
    param(
        [Parameter(Mandatory = $true)][string]$DotNetWrapper,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$TestProject,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    & $DotNetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        -OutputPath $OutputPath `
        test $TestProject `
        -c $Configuration `
        --no-build `
        --no-restore `
        --list-tests `
        --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "Could not discover package tests for filter '$Filter'."
    }

    $text = Get-Content -LiteralPath $OutputPath -Raw
    return @(
        [regex]::Matches(
            $text,
            '(?m)^\s{4}(?<name>[^\r\n]+?)\s*$') |
            ForEach-Object { $_.Groups['name'].Value.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique)
}

function Get-TestMethodName {
    param([Parameter(Mandatory = $true)][string]$Name)

    $index = $Name.IndexOf('(', [StringComparison]::Ordinal)
    if ($index -lt 0) {
        return $Name
    }
    return $Name.Substring(0, $index)
}

function Get-TestFixtureClassNames {
    param([Parameter(Mandatory = $true)][string]$PackageTestRoot)

    $names = [Collections.Generic.List[string]]::new()
    foreach ($source in Get-ChildItem `
            -LiteralPath $PackageTestRoot `
            -Recurse `
            -Filter *.cs `
            -File) {
        $text = Get-Content -LiteralPath $source.FullName -Raw
        foreach ($match in [regex]::Matches(
                $text,
                '(?ms)\[TestFixture(?:\s*\([^\)]*\))?\]\s*' +
                '(?:(?:\[[^\]]*\]\s*)*)' +
                '(?:(?:public|internal|private|protected)\s+)?' +
                '(?:(?:sealed|abstract|partial)\s+)*class\s+' +
                '(?<name>[A-Za-z_][A-Za-z0-9_]*)')) {
            $names.Add($match.Groups['name'].Value)
        }
    }
    return @($names | Sort-Object -Unique)
}

function Get-TrxExecutedCount {
    param([Parameter(Mandatory = $true)][string]$ResultsDirectory)

    if (-not [IO.Directory]::Exists($ResultsDirectory)) {
        return [pscustomobject]@{ Files = 0; Executed = 0 }
    }

    $files = @(Get-ChildItem `
        -LiteralPath $ResultsDirectory `
        -Recurse `
        -Filter *.trx `
        -File)
    $executed = 0
    foreach ($file in $files) {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
        $results = @($document.TestRun.Results.UnitTestResult)
        $executed += $results.Count
    }
    return [pscustomobject]@{
        Files = $files.Count
        Executed = $executed
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
$running = [Collections.Generic.List[object]]::new()

function Stop-SharpProofPackageProcesses {
    foreach ($active in @($running)) {
        try {
            if (-not $active.Process.HasExited) {
                $active.Process.Kill($true)
            }
        }
        catch {
            # Cleanup must not replace the setup or test failure that caused
            # this path.  Wait/dispose below still get a chance to run.
        }
        try {
            $active.Process.WaitForExit(5000) | Out-Null
        }
        catch {
        }
        try {
            $active.Process.Dispose()
        }
        catch {
        }
    }
    $running.Clear()
}

$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingOutput = Join-Path $timingDirectory (
    'package-tests-' + $Configuration.ToLowerInvariant() +
    $(if ($coverageEnabled) { '-coverage' } else { '' }) + '.json')
$priorMethodMilliseconds = @{}
$priorFilterMilliseconds = @{}
if (Test-Path -LiteralPath $timingOutput -PathType Leaf) {
    try {
        $priorTiming = Get-Content -LiteralPath $timingOutput -Raw |
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
            'Ignoring malformed prior package timing evidence: ' +
            $_.Exception.Message)
    }
}

try {
    if (-not $NoBuild) {
        Invoke-RequiredDotnet @(
            'restore', 'SharpProof.sln', '--locked-mode')
        Invoke-RequiredDotnet @(
            'build', $testProject, '-c', $Configuration, '--no-restore',
            "/m:$parallelism")
    }

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        $packageManifestText = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'scripts/package-projects.json') -Raw
        $packageManifestDocument = [System.Text.Json.JsonDocument]::Parse(
            $packageManifestText)
        try {
            Assert-SharpProofUniqueJsonProperties `
                -Value $packageManifestDocument.RootElement `
                -Context 'package-projects manifest'
        }
        finally {
            $packageManifestDocument.Dispose()
        }
        $packageManifest = $packageManifestText | ConvertFrom-Json
        foreach ($project in @($packageManifest.projects)) {
            Invoke-RequiredDotnet @(
                'pack', [string]$project, '-c', 'Release', '--no-restore',
                $(if ($NoBuild) { '--no-build' } else { '--nologo' }),
                '--output', $feed, '/p:GeneratePackageOnBuild=false')
        }
    }

    $workerClass =
        'SharpProof.Package.Test.WorkerMsBuildIntegrationTests'
    $packageTestRoot = Join-Path $repositoryRoot 'SharpProof.Package.Test'
    $shards = [Collections.Generic.List[object]]::new()
    $expectedTestKeys = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)

    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $fixtureClasses = @(Get-TestFixtureClassNames $packageTestRoot)
        if ($fixtureClasses.Count -eq 0) {
            throw 'Package test discovery found no TestFixture classes.'
        }
        $selectedCount = 0
        $selectedWorkerIndex = 0
        foreach ($fixtureClass in $fixtureClasses) {
            $fixtureFilter =
                "($TestFilter)&FullyQualifiedName~SharpProof.Package.Test.$fixtureClass"
            $selectedListPath = Join-Path $root (
                'selected-test-list-' + $fixtureClass + '.txt')
            $selectedTests = @(Get-ListedTestNames `
                -DotNetWrapper $dotnetWrapper `
                -TimeoutSeconds $TimeoutSeconds `
                -TestProject $testProject `
                -Configuration $Configuration `
                -Filter $fixtureFilter `
                -OutputPath $selectedListPath)
            if ($selectedTests.Count -eq 0) {
                continue
            }
            $selectedCount += $selectedTests.Count
            if ($fixtureClass -cne 'WorkerMsBuildIntegrationTests') {
                $shards.Add([pscustomobject]@{
                    Name = 'selected-fixture-' +
                        $fixtureClass.ToLowerInvariant()
                    Filter = $fixtureFilter
                    ExpectedTests = $selectedTests
                    ExpectedCount = $selectedTests.Count
                    Exclusive =
                        $fixtureClass -ceq 'LauncherArgumentTests'
                    EstimatedMilliseconds =
                        $(if ($priorFilterMilliseconds.ContainsKey(
                                    $fixtureFilter)) {
                            [long]$priorFilterMilliseconds[$fixtureFilter]
                        }
                        else {
                            1L
                        })
                })
                continue
            }

            $selectedTestsByMethod = @{}
            foreach ($test in $selectedTests) {
                $method = Get-TestMethodName $test
                if (-not $selectedTestsByMethod.ContainsKey($method)) {
                    $selectedTestsByMethod[$method] =
                        [Collections.Generic.List[string]]::new()
                }
                $selectedTestsByMethod[$method].Add($test)
            }
            foreach ($method in @($selectedTestsByMethod.Keys | Sort-Object)) {
                $selectedWorkerIndex++
                $methodFilter =
                    "($TestFilter)&FullyQualifiedName~$workerClass.$method"
                $methodTests = @($selectedTestsByMethod[$method])
                $shards.Add([pscustomobject]@{
                    Name = 'selected-worker-' +
                        $selectedWorkerIndex.ToString(
                            'D2',
                            [Globalization.CultureInfo]::InvariantCulture)
                    Filter = $methodFilter
                    ExpectedTests = $methodTests
                    ExpectedCount = $methodTests.Count
                    Exclusive = $false
                    EstimatedMilliseconds =
                        $(if ($priorMethodMilliseconds.ContainsKey($method)) {
                            [long]$priorMethodMilliseconds[$method]
                        }
                        elseif ($priorFilterMilliseconds.ContainsKey(
                                    $methodFilter)) {
                            [long]$priorFilterMilliseconds[$methodFilter]
                        }
                        else {
                            1L
                        })
                })
            }
        }
        if ($selectedCount -eq 0) {
            throw "The package test filter matched no tests: $TestFilter"
        }
    }
    else {
        $fixtureClasses = @(Get-TestFixtureClassNames $packageTestRoot)
        if ($fixtureClasses.Count -eq 0) {
            throw 'Package test discovery found no TestFixture classes.'
        }

        $fixtureInventory = [Collections.Generic.List[object]]::new()
        foreach ($fixtureClass in $fixtureClasses) {
            $filter =
                "FullyQualifiedName~SharpProof.Package.Test.$fixtureClass"
            $listPath = Join-Path $root (
                'test-list-' + $fixtureClass + '.txt')
            $tests = @(Get-ListedTestNames `
                -DotNetWrapper $dotnetWrapper `
                -TimeoutSeconds $TimeoutSeconds `
                -TestProject $testProject `
                -Configuration $Configuration `
                -Filter $filter `
                -OutputPath $listPath)
            if ($tests.Count -eq 0) {
                throw (
                    "Package fixture '$fixtureClass' discovered no tests " +
                    "for filter '$filter'.")
            }

            $qualifiedTests = @($tests | ForEach-Object {
                    "$fixtureClass.$_"
                })
            foreach ($test in $qualifiedTests) {
                if (-not $expectedTestKeys.Add($test)) {
                    throw "Package test identity was discovered twice: $test"
                }
            }
            $fixtureInventory.Add([pscustomobject]@{
                ClassName = $fixtureClass
                Filter = $filter
                Tests = $qualifiedTests
            })
        }

        $workerFixture = $fixtureInventory | Where-Object {
            $_.ClassName -eq 'WorkerMsBuildIntegrationTests'
        } | Select-Object -First 1
        if ($null -eq $workerFixture) {
            throw "Package test discovery did not find $workerClass."
        }

        $workerMethods = @($workerFixture.Tests | ForEach-Object {
                Get-TestMethodName ($_.Substring('WorkerMsBuildIntegrationTests.'.Length))
            } | Sort-Object -Unique)
        if ($workerMethods.Count -eq 0) {
            throw 'Worker MSBuild integration discovery returned no test methods.'
        }
        $workerTestsByMethod = @{}
        foreach ($test in $workerFixture.Tests) {
            $method = Get-TestMethodName (
                $test.Substring('WorkerMsBuildIntegrationTests.'.Length))
            if (-not $workerTestsByMethod.ContainsKey($method)) {
                $workerTestsByMethod[$method] =
                    [Collections.Generic.List[string]]::new()
            }
            $workerTestsByMethod[$method].Add($test)
        }

        $workerBuckets = @(
            for ($index = 0; $index -lt $parallelism; $index++) {
                [pscustomobject]@{
                    Index = $index
                    Methods = [Collections.Generic.List[string]]::new()
                    Tests = [Collections.Generic.List[string]]::new()
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
            foreach ($test in @($workerTestsByMethod[$method])) {
                $bucket.Tests.Add($test)
            }
            $bucket.EstimatedMilliseconds +=
                $(if ($priorMethodMilliseconds.ContainsKey($method)) {
                    [long]$priorMethodMilliseconds[$method]
                }
                else {
                    1L
                })
        }

        foreach ($fixture in @($fixtureInventory | Where-Object {
                    $_.ClassName -ne 'WorkerMsBuildIntegrationTests'
                })) {
            $shards.Add([pscustomobject]@{
                Name = 'fixture-' + $fixture.ClassName.ToLowerInvariant()
                Filter = $fixture.Filter
                ExpectedTests = $fixture.Tests
                ExpectedCount = $fixture.Tests.Count
                Exclusive =
                    $fixture.ClassName -ceq 'LauncherArgumentTests'
                EstimatedMilliseconds =
                    $(if ($priorFilterMilliseconds.ContainsKey($fixture.Filter)) {
                        [long]$priorFilterMilliseconds[$fixture.Filter]
                    }
                    else {
                        1L
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
                ExpectedTests = @($bucket.Tests)
                ExpectedCount = $bucket.Tests.Count
                Exclusive = $false
                EstimatedMilliseconds = $bucket.EstimatedMilliseconds
            })
        }

        $assignedTestKeys = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($shard in $shards) {
            foreach ($test in @($shard.ExpectedTests)) {
                if (-not $assignedTestKeys.Add([string]$test)) {
                    throw "Package test identity was assigned twice: $test"
                }
            }
        }
        if ($assignedTestKeys.Count -ne $expectedTestKeys.Count) {
            throw (
                'Package test shard coverage mismatch: discovered ' +
                "$($expectedTestKeys.Count), assigned $($assignedTestKeys.Count).")
        }
    }

    $pending = [Collections.Generic.Queue[object]]::new()
    foreach ($shard in Get-SharpProofPackageShardSchedule -Shards @($shards)) {
        $pending.Enqueue($shard)
    }
    $shardTimings = [Collections.Generic.List[object]]::new()
    $failures = [Collections.Generic.List[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $parallelism) {
            $runningExclusive = @($running | Where-Object {
                    $_.Shard.Exclusive
                }).Count -gt 0
            if ($runningExclusive -or
                ($pending.Peek().Exclusive -and $running.Count -gt 0)) {
                break
            }
            $shard = $pending.Dequeue()
            $startInfo = [Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'dotnet'
            $startInfo.WorkingDirectory = $repositoryRoot
            $startInfo.UseShellExecute = $false
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.Environment['SHARPPROOF_PACKAGE_SOURCE'] = $feed
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
            $arguments = @(
                'test', $testProject, '-c', $Configuration,
                '--no-build', '--no-restore')
            if (-not [string]::IsNullOrWhiteSpace($isolatedOutput)) {
                $arguments += '-p:OutDir=' + $isolatedOutput + '/'
            }
            $arguments += @(
                    '--filter', $shard.Filter,
                    '--logger', 'console;verbosity=minimal',
                    '--logger', "trx;LogFileName=$($shard.Name).trx",
                    '--results-directory', (Join-Path $results $shard.Name))
            if ($coverageEnabled) {
                $arguments += @(
                    '--settings', $resolvedCoverageSettings,
                    '--collect', 'Code Coverage;Format=Cobertura')
            }
            foreach ($argument in $arguments) {
                [void]$startInfo.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                $process.Dispose()
                throw "Could not start package test $($shard.Name)."
            }
            $active = [pscustomobject]@{
                Shard = $shard
                Process = $process
                StartedUtc = [DateTime]::UtcNow
                StandardOutput = $null
                StandardError = $null
            }
            $running.Add($active)
            $active.StartedUtc = $process.StartTime.ToUniversalTime()
            $active.StandardOutput = $process.StandardOutput.ReadToEndAsync()
            $active.StandardError = $process.StandardError.ReadToEndAsync()
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            Stop-SharpProofPackageProcesses
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
            Write-Host "--- Package test $($active.Shard.Name) ---"
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                Write-Host $stdout.TrimEnd()
            }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                Write-Host $stderr.TrimEnd()
            }
            $trx = Get-TrxExecutedCount (Join-Path $results $active.Shard.Name)
            if ($trx.Files -eq 0) {
                $failures.Add(
                    "$($active.Shard.Name) produced no TRX execution evidence: " +
                    $active.Shard.Filter)
            }
            elseif ($trx.Executed -eq 0) {
                $failures.Add(
                    "$($active.Shard.Name) executed zero tests: " +
                    $active.Shard.Filter)
            }
            elseif ($trx.Executed -ne $active.Shard.ExpectedCount) {
                $failures.Add(
                    "$($active.Shard.Name) executed $($trx.Executed) tests, " +
                    "but discovery expected " +
                    "$($active.Shard.ExpectedCount): $($active.Shard.Filter)")
            }
            if ($active.Process.ExitCode -ne 0) {
                $failures.Add(
                    "$($active.Shard.Name) exited $($active.Process.ExitCode): " +
                    $active.Shard.Filter)
            }
            $shardTimings.Add([pscustomobject]@{
                name = $active.Shard.Name
                filter = $active.Shard.Filter
                expectedCount = $active.Shard.ExpectedCount
                executedCount = $trx.Executed
                elapsedMilliseconds = [long](
                    ($active.Process.ExitTime.ToUniversalTime() -
                        $active.StartedUtc).TotalMilliseconds)
                exitCode = $active.Process.ExitCode
            })
            [void]$running.Remove($active)
            $active.Process.Dispose()
        }
    }

    $campaign.Stop()
    $workerMethodTimings = Get-WorkerMethodTimings `
        -ResultsRoot $results `
        -WorkerClass $workerClass
    $schedulerMethodMilliseconds = @{}
    foreach ($entry in $priorMethodMilliseconds.GetEnumerator()) {
        $schedulerMethodMilliseconds[[string]$entry.Key] = [long]$entry.Value
    }
    foreach ($entry in $workerMethodTimings) {
        $schedulerMethodMilliseconds[[string]$entry.name] =
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
        parallelism = $parallelism
        totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
        shards = @($shardTimings | Sort-Object name)
        workerMethods = $workerMethodTimings
        scheduler = [ordered]@{
            workerMethods = @(
                $schedulerMethodMilliseconds.GetEnumerator() |
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
    Write-Host (
        "Package tests passed in $($shards.Count) isolated shard(s) " +
        "with parallelism $parallelism.")
    Write-Host "Timing evidence: $timingOutput"
}
finally {
    Stop-SharpProofPackageProcesses
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
    if ($coverageEnabled -and
        [IO.Directory]::Exists($isolatedOutputRoot)) {
        [IO.Directory]::Delete($isolatedOutputRoot, $true)
    }
}
