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
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Package tests require the canonical Linux container.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
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
        $packageManifest = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'scripts/package-projects.json') -Raw |
            ConvertFrom-Json
        foreach ($project in @($packageManifest.projects)) {
            Invoke-RequiredDotnet @(
                'pack', [string]$project, '-c', 'Release', '--no-restore',
                $(if ($NoBuild) { '--no-build' } else { '--nologo' }),
                '--output', $feed, '/p:GeneratePackageOnBuild=false')
        }
    }

    $workerClass =
        'SharpProof.Package.Test.WorkerMsBuildIntegrationTests'
    $workerListPath = Join-Path $root 'worker-test-list.txt'
    & $dotnetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        -OutputPath $workerListPath `
        test $testProject `
        -c $Configuration `
        --no-build `
        --no-restore `
        --list-tests `
        --filter "FullyQualifiedName~$workerClass"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not discover Worker MSBuild integration tests.'
    }
    $workerList = Get-Content -LiteralPath $workerListPath -Raw
    $workerMethods = @(
        [regex]::Matches(
            $workerList,
            '(?m)^\s{4}(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|\s*$)') |
            ForEach-Object { $_.Groups['method'].Value } |
            Sort-Object -Unique)
    if ($workerMethods.Count -lt 40) {
        throw (
            'Worker MSBuild integration discovery returned only ' +
            "$($workerMethods.Count) test methods.")
    }
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
            'BuildTaskTests',
            'DependencyAuditScriptTests',
            'FinalCompilationProbeTests',
            'LauncherArgumentTests',
            'PackageLayoutSmokeTests',
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

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $parallelism) {
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
            $running.Add([pscustomobject]@{
                Shard = $shard
                Process = $process
                StartedUtc = $process.StartTime.ToUniversalTime()
                StandardOutput = $process.StandardOutput.ReadToEndAsync()
                StandardError = $process.StandardError.ReadToEndAsync()
            })
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
            Write-Host "--- Package test $($active.Shard.Name) ---"
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                Write-Host $stdout.TrimEnd()
            }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                Write-Host $stderr.TrimEnd()
            }
            if ($active.Process.ExitCode -ne 0) {
                $failures.Add(
                    "$($active.Shard.Name) exited $($active.Process.ExitCode): " +
                    $active.Shard.Filter)
            }
            $shardTimings.Add([pscustomobject]@{
                name = $active.Shard.Name
                filter = $active.Shard.Filter
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
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
    if ($coverageEnabled -and
        [IO.Directory]::Exists($isolatedOutputRoot)) {
        [IO.Directory]::Delete($isolatedOutputRoot, $true)
    }
}
