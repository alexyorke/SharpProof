[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800,

    [string]$TestFilter = '',

    [string]$CoverageSettings = '',

    [string]$CoverageResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Semantic test sharding requires the canonical Linux container.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
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
if ($coverageEnabled) {
    [IO.Directory]::CreateDirectory($resolvedCoverageResults) | Out-Null
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

if (-not $NoBuild) {
    Invoke-RequiredDotnet @('restore', 'SharpProof.sln', '--locked-mode')
    Invoke-RequiredDotnet @(
        'build', 'SharpProof.sln', '-c', $Configuration, '--no-restore')
}

$timingDirectory = Join-Path $repositoryRoot 'artifacts/timings'
[IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
$timingOutput = Join-Path $timingDirectory (
    'semantic-tests-' + $Configuration.ToLowerInvariant() +
    $(if ($coverageEnabled) { '-coverage' } else { '' }) + '.json')
$priorDurations = @{}
if (Test-Path -LiteralPath $timingOutput -PathType Leaf) {
    try {
        $prior = Get-Content -LiteralPath $timingOutput -Raw |
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
            'Ignoring malformed prior semantic timing evidence: ' +
            $_.Exception.Message)
    }
}

$mainParallelism = [Math]::Max(1, $parallelism - 4)
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
$claimTaskFilter = "($claimFilter)&($semanticFilter)"
$manifestTaskFilter = "($manifestFilter)&($semanticFilter)"
$workerCoreTaskFilter = "($workerCoreFilter)&($semanticFilter)"
$workerRemainderTaskFilter = "($workerRemainderFilter)&($semanticFilter)"
$tasks = @(
    [pscustomobject]@{
        Name = 'semantic-projects'
        Target = Join-Path $repositoryRoot 'SharpProof.Semantic.Tests.slnf'
        Filter = $semanticFilter
        ProjectParallelism = $mainParallelism
        IsolateOutput = $false
    },
    [pscustomobject]@{
        Name = 'worker-claim-manifest'
        Target = $testProject
        Filter = $claimTaskFilter
        ProjectParallelism = 0
        IsolateOutput = $true
    },
    [pscustomobject]@{
        Name = 'worker-compiler-manifest'
        Target = $testProject
        Filter = $manifestTaskFilter
        ProjectParallelism = 0
        IsolateOutput = $true
    },
    [pscustomobject]@{
        Name = 'worker-core'
        Target = $testProject
        Filter = $workerCoreTaskFilter
        ProjectParallelism = 0
        IsolateOutput = $true
    },
    [pscustomobject]@{
        Name = 'worker-remainder'
        Target = $testProject
        Filter = $workerRemainderTaskFilter
        ProjectParallelism = 0
        IsolateOutput = $true
    }
)
foreach ($task in $tasks) {
    $task | Add-Member -NotePropertyName EstimatedMilliseconds `
        -NotePropertyValue $(if ($priorDurations.ContainsKey($task.Name)) {
            [long]$priorDurations[$task.Name]
        }
        else {
            1L
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
$pending = [Collections.Generic.Queue[object]]::new()
foreach ($task in $tasks) {
    $pending.Enqueue($task)
}
$running = [Collections.Generic.List[object]]::new()
$timings = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$campaign = [Diagnostics.Stopwatch]::StartNew()

try {
    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $parallelism) {
            $task = $pending.Dequeue()
            $startInfo = [Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'dotnet'
            $startInfo.WorkingDirectory = $repositoryRoot
            $startInfo.UseShellExecute = $false
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
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
                throw "Could not start semantic test task '$($task.Name)'."
            }
            $running.Add([pscustomobject]@{
                Task = $task
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
            throw "Parallel semantic tests exceeded $TimeoutSeconds seconds."
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
            Write-Host "--- Semantic test $($active.Task.Name) ---"
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                Write-Host $stdout.TrimEnd()
            }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                Write-Host $stderr.TrimEnd()
            }
            $elapsed = [long](
                ($active.Process.ExitTime.ToUniversalTime() -
                    $active.StartedUtc).TotalMilliseconds)
            $timings.Add([pscustomobject]@{
                name = $active.Task.Name
                elapsedMilliseconds = $elapsed
                exitCode = $active.Process.ExitCode
            })
            if ($active.Process.ExitCode -ne 0) {
                $failures.Add(
                    "$($active.Task.Name) exited $($active.Process.ExitCode).")
            }
            [void]$running.Remove($active)
            $active.Process.Dispose()
        }
    }
}
finally {
    foreach ($active in @($running)) {
        if (-not $active.Process.HasExited) {
            $active.Process.Kill($true)
            $active.Process.WaitForExit()
        }
        $active.Process.Dispose()
    }
    if ($temporaryResults -and [IO.Directory]::Exists($resultsRoot)) {
        [IO.Directory]::Delete($resultsRoot, $true)
    }
    if ($coverageEnabled -and
        [IO.Directory]::Exists($isolatedOutputRoot)) {
        [IO.Directory]::Delete($isolatedOutputRoot, $true)
    }
}

$campaign.Stop()
$temporaryTiming =
    $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[pscustomobject]@{
    schemaVersion = 1
    command = 'semantic-tests'
    configuration = $Configuration
    parallelism = $parallelism
    totalElapsedMilliseconds = [long]$campaign.Elapsed.TotalMilliseconds
    tasks = @($timings | Sort-Object name)
} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $temporaryTiming -Encoding utf8NoBOM
Move-Item -LiteralPath $temporaryTiming -Destination $timingOutput -Force

if ($failures.Count -ne 0) {
    throw "Parallel semantic tests failed:`n$($failures -join "`n")"
}
Write-Host (
    "Semantic tests passed in $($tasks.Count) isolated task(s) with " +
    "parallelism $parallelism.")
Write-Host "Timing evidence: $timingOutput"
