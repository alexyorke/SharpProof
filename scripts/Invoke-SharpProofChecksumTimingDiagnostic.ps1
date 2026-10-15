[CmdletBinding()]
param([ValidateSet('alone', 'overlap')][string]$Mode)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Checksum timing diagnostics require the canonical Linux container.'
$project = 'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj'
$results = Join-Path $repositoryRoot ('artifacts/checksum-controlled-timing/' + $Mode)
[IO.Directory]::CreateDirectory($results) | Out-Null
$coverage = New-SharpProofCoverageContext -RepositoryRoot $repositoryRoot `
    -CoverageSettings (Join-Path $repositoryRoot 'eng/coverage/SharpProof.Managed.runsettings') `
    -CoverageResultsDirectory $results -CreateResultsDirectory
$tasks = [Collections.Generic.List[object]]::new()
if ($Mode -ceq 'overlap') {
    $tasks.Add([pscustomobject]@{
        Name = 'heavy'; Slots = 2
        Filter = 'FullyQualifiedName=SharpProof.Worker.Test.NativeWorkerRoutingTests.NativeWorkerRetainsDirectVerifierOutcomesAcrossTheQualifiedUniverse'
    })
}
$tasks.Add([pscustomobject]@{
    Name = 'checksum'; Slots = 2
    Filter = 'FullyQualifiedName=SharpProof.Worker.Test.FixedChecksumNormalWorkerEntryTests.FrozenChecksumUsesActualDefaultNativeWorker'
})
$started = [DateTime]::UtcNow
$run = $null
$campaignFailure = $null
try {
    # One locked restore/build; no oracle, deadline, budget or analyzer changes.
    Invoke-SharpProofRequiredDotnet -Arguments @('restore', $project, '--locked-mode')
    Invoke-SharpProofRequiredDotnet -Arguments @('build', $project, '-c', 'Release', '--no-restore')
    $prepare = {
        param([object]$task)
        $output = New-SharpProofIsolatedTestOutput `
            -SourceDirectory (Join-Path $repositoryRoot 'SharpProof.Worker.Test/bin/Release/net9.0') `
            -DestinationDirectory (Join-Path $coverage.IsolatedOutputRoot ($task.Name + '/Release/net9.0'))
        $arguments = @('test', $project, '-c', 'Release', '--no-build', '--no-restore',
            ('-p:OutDir=' + $output + '/'), '--filter', $task.Filter,
            '--logger', 'console;verbosity=normal', '--logger', ('trx;LogFilePrefix=' + $task.Name),
            '--results-directory', (Join-Path $results $task.Name))
        $arguments = Add-SharpProofCoverageArguments -Arguments $arguments -Enabled $true -Settings $coverage.Settings
        return [pscustomobject]@{
            Arguments = $arguments
            Environment = @{ SHARPPROOF_TEST_PROJECT_PARALLELISM = '2' }
        }
    }.GetNewClosure()
    $run = Invoke-SharpProofParallelDotnetTests -Tests @($tasks) -RepositoryRoot $repositoryRoot `
        -Parallelism 4 -TimeoutSeconds 1800 -Prepare $prepare -Label 'Controlled checksum diagnostic'
    if (@($run.Failures).Count -ne 0) { throw ($run.Failures -join [Environment]::NewLine) }
}
catch {
    $campaignFailure = $_.Exception.ToString()
    throw
}
finally {
    [ordered]@{
        schemaVersion = 1; mode = $Mode; startedUtc = $started; endedUtc = [DateTime]::UtcNow
        cpuLimit = $env:SHARPPROOF_CONTAINER_CPU_LIMIT
        workflowSourceCommit = $env:SHARPPROOF_CHECKSUM_DIAGNOSTIC_COMMIT
        fixtureSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'SharpProof.Worker.Test/FixedChecksumNormalWorkerEntryTests.cs') -Algorithm SHA256).Hash
        campaignFailure = $campaignFailure
        expectedBaseline = '33ac8c1e1d75e49e0ea6fd77042935589110653a'
        tasks = @($(if ($null -ne $run) { $run.Completed } else { @() }) | ForEach-Object {
            [ordered]@{ name = $_.Test.Name; filter = $_.Test.Filter; slots = $_.Test.Slots
                elapsedMilliseconds = $_.ElapsedMilliseconds; exitCode = $_.ExitCode }
        })
        qualification = 'Diagnostic only. TRX must prove exact case counts and actual overlap; neither isolated outcome qualifies the full gate.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'timing.json') -Encoding utf8NoBOM
    Remove-SharpProofOwnedDirectory -Directory $coverage.IsolatedOutputRoot
}
