[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory = 'artifacts\fuzz',

    [Parameter()]
    [int]$RotatingSeed,

    [Parameter()]
    [ValidateRange(0, 1000000)]
    [int]$RotatingCases,

    [Parameter()]
    [ValidateRange(0, 1000000)]
    [int]$RetainedCases,

    [Parameter()]
    [Collections.IDictionary]$PreparedRunnerReceipt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
. (Join-Path $PSScriptRoot 'Assert-SharpProofFuzzRunnerResult.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.FuzzRunnerBuild.ps1')
Import-Module (Join-Path `
        $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$resolvedOutput = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputDirectory `
    -ParameterName 'OutputDirectory'
$logicalOutput = [IO.Path]::GetRelativePath(
    $repositoryRoot,
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))).Replace('\', '/')
$evidenceLease = Enter-SharpProofFuzzEvidenceLease `
    -OutputDirectory $resolvedOutput
try {
    Initialize-SharpProofFuzzEvidence -OutputDirectory $resolvedOutput
    [IO.File]::Delete((Join-Path $resolvedOutput 'runner-build.json'))
    $sourceCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $repositoryRoot
    $contract = Get-Content `
        -LiteralPath (Join-Path $repositoryRoot 'eng\acceptance\contract.json') `
        -Raw |
        ConvertFrom-Json
    $nightlyCases = Assert-SharpProofFuzzCaseBudget `
        -Value $contract.fuzz.nightlyCases `
        -Name 'contract.fuzz.nightlyCases'
    $retainedManifestPath = Join-Path `
        $repositoryRoot 'eng\fuzz\retained-seeds.json'
    $retained = Read-SharpProofRetainedFuzzSeedManifest `
        -Path $retainedManifestPath
    $retainedSeeds = [Collections.Immutable.ImmutableArray]::CreateRange[int]([int[]]@($retained.Seeds))
    if (-not $PSBoundParameters.ContainsKey('RotatingSeed')) {
        # FuzzRunner advances each case seed by 397.  A calendar-shaped seed
        # (yyyyMMdd) therefore causes dates 397 days apart to replay the same
        # sequence.  Use a monotonic day number with a small quotient term so
        # those campaign offsets are not congruent modulo the case stride.
        $RotatingSeed = Get-SharpProofRotatingSeed -UtcDate ([DateTime]::UtcNow)
    }
    $effectiveRotatingCases = if ($RotatingCases -gt 0) {
        $RotatingCases
    }
    else {
        $nightlyCases
    }
    $effectiveRetainedCases = if ($RetainedCases -gt 0) {
        $RetainedCases
    }
    else {
        $retained.CasesPerSeed
    }
    $maximumCampaignCases = Assert-SharpProofFuzzCaseBudget `
        -Value $contract.fuzz.maximumCampaignCases `
        -Name 'contract.fuzz.maximumCampaignCases'
    $schedule = Get-SharpProofFuzzCampaignSchedule `
        -RotatingSeed $RotatingSeed `
        -RetainedSeeds ([int[]]@($retainedSeeds)) `
        -RotatingCases $effectiveRotatingCases `
        -RetainedCases $effectiveRetainedCases `
        -MaximumCases $maximumCampaignCases
    # The resource oracle is its own run, on the rotating seed and every retained
    # seed. It has its own wall budget and evidence file, so it cannot push the
    # runs above out of theirs, and the runner never pays for it unless asked.
    $resourceBoundCases = Assert-SharpProofFuzzCaseBudget `
        -Value $contract.fuzz.nightlyResourceBoundCases `
        -Name 'contract.fuzz.nightlyResourceBoundCases'
    $requestedResourceBoundCases = Assert-SharpProofFuzzCampaignBudget `
        -RotatingCases $resourceBoundCases `
        -RetainedCases $effectiveRetainedCases `
        -RetainedRunCount $schedule.RetainedRunSeeds.Length `
        -MaximumCases ($maximumCampaignCases - $schedule.RequestedCases)
    $dotnetWrapper = Get-SharpProofDotnetWrapperPath
    $fuzzProject = Join-Path `
        $repositoryRoot 'tests\SharpProof.Fuzz\SharpProof.Fuzz.csproj'

    if ($PSBoundParameters.ContainsKey('PreparedRunnerReceipt')) {
        if ($null -eq $PreparedRunnerReceipt) {
            throw 'A prepared fuzz runner requires its same-workspace build receipt.'
        }
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $repositoryRoot `
            -SourceCommit $sourceCommit -Receipt $PreparedRunnerReceipt
        Write-Host 'Fuzz runner: validated the fresh normal build in this private workspace.'
    }
    else {
        $buildArguments = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                'build', $fuzzProject, '-c', 'Release', '--no-restore', '--no-incremental', '--nologo'))
        $buildTimeoutSeconds = [int]$contract.automation.solutionBuildWallSeconds
        $buildStandardOutput = Join-Path $resolvedOutput 'runner-build.stdout.txt'
        $buildStandardError = Join-Path $resolvedOutput 'runner-build.stderr.txt'
        $buildProcess = $null
        $buildTimer = [Diagnostics.Stopwatch]::StartNew()
        try {
            $buildProcess = Start-SharpProofEncodedPowerShell `
                -WrapperPath $dotnetWrapper `
                -TimeoutSeconds $buildTimeoutSeconds `
                -WorkingDirectory $repositoryRoot `
                -ImmutableArguments $buildArguments `
                -StandardOutput $buildStandardOutput `
                -StandardError $buildStandardError
        }
        finally {
            $buildTimer.Stop()
            $buildExitCode = if ($null -eq $buildProcess) { $null } else { [int]$buildProcess.ExitCode }
            $buildReceipt = ([ordered]@{
                    schemaVersion       = 1
                    commit              = $sourceCommit
                    deadlineSeconds     = $buildTimeoutSeconds
                    elapsedMilliseconds = $buildTimer.ElapsedMilliseconds
                    exitCode            = $buildExitCode
                    timedOut            = $buildExitCode -eq 124
                }).AsReadOnly()
            [IO.File]::WriteAllText((Join-Path $resolvedOutput 'runner-build.json'),
                (($buildReceipt | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
            Write-Host ('Fuzz runner rebuild: elapsedMilliseconds={0} deadlineSeconds={1} exitCode={2}' -f
                $buildReceipt.elapsedMilliseconds, $buildReceipt.deadlineSeconds, $buildReceipt.exitCode)
        }
        if ($buildProcess.ExitCode -ne 0) {
            $buildFailure = if ($buildProcess.ExitCode -eq 124) {
                "timed out after $buildTimeoutSeconds seconds (code 124)"
            }
            else {
                "failed with code $($buildProcess.ExitCode)"
            }
            $buildFailureTail = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    foreach ($logPath in @($buildStandardOutput, $buildStandardError)) {
                        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                            "$([IO.Path]::GetFileName($logPath)) (last 20 lines):"
                            Get-Content -LiteralPath $logPath -Tail 20 | ForEach-Object {
                                $line = [string]$_
                                $line.Substring(0, [Math]::Min(400, $line.Length))
                            }
                        }
                    }))
            throw ("SharpProof fuzz runner rebuild $buildFailure. " +
                "Logs: $logicalOutput/runner-build.stdout.txt; $logicalOutput/runner-build.stderr.txt`n" +
                ($buildFailureTail -join "`n"))
        }
    }
    $builtCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $repositoryRoot
    if ($builtCommit -cne $sourceCommit) {
        throw 'Fuzz source changed while rebuilding the runner.'
    }
    $runnerAssembly = Join-Path $repositoryRoot `
        'artifacts\bin\SharpProof.Fuzz\Release\net10.0\SharpProof.Fuzz.dll'
    if (-not (Test-Path -LiteralPath $runnerAssembly -PathType Leaf)) {
        throw 'The rebuilt fuzz runner assembly is missing.'
    }

    function Invoke-FuzzRun {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Name,

            [Parameter(Mandatory = $true)]
            [int]$Cases,

            [Parameter(Mandatory = $true)]
            [int]$Seed,

            [Parameter(Mandatory = $true)]
            [int]$TimeoutSeconds,

            [switch]$ResourceBounds
        )

        $standardOutput = Join-Path $resolvedOutput "$Name.stdout.json"
        $standardError = Join-Path $resolvedOutput "$Name.stderr.txt"
        $dotnetArguments = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                $runnerAssembly,
                '--cases',
                [string]$Cases,
                '--seed',
                [string]$Seed,
                '--max-parallelism',
                [string]$contract.fuzz.maximumParallelism
                if ($ResourceBounds) {
                    '--oracle'
                    'resource-bounds'
                }
            ))
        $process = Start-SharpProofEncodedPowerShell `
            -WrapperPath $dotnetWrapper `
            -TimeoutSeconds $TimeoutSeconds `
            -WorkingDirectory $repositoryRoot `
            -ImmutableArguments $dotnetArguments `
            -StandardOutput $standardOutput `
            -StandardError $standardError
        $validationError = $null
        $observedCases = 0
        $agreements = 0
        $abstentions = 0
        $runnerSchemaVersion = $null
        $runnerPassed = $false
        try {
            if ($process.ExitCode -ne 0) {
                throw "runner exited with code $($process.ExitCode)"
            }
            if (-not (Test-Path -LiteralPath $standardOutput -PathType Leaf)) {
                throw 'runner did not emit a JSON result'
            }
            if ($ResourceBounds) {
                $result = Assert-SharpProofResourceBoundFuzzRunnerResult `
                    -Path $standardOutput `
                    -ExpectedCases $Cases `
                    -ExpectedSeed $Seed `
                    -ExpectedMaximumParallelism ([int]$contract.fuzz.maximumParallelism)
                $agreements = [int]$result.Coverage.ProvenCases
                $abstentions = [int]$result.Coverage.UnknownCases
            }
            else {
                $result = Assert-SharpProofFuzzRunnerResult `
                    -Path $standardOutput `
                    -ExpectedCases $Cases `
                    -ExpectedSeed $Seed `
                    -ExpectedMaximumParallelism ([int]$contract.fuzz.maximumParallelism)
                $agreements = [int]$result.Agreements
                $abstentions = [int]$result.Abstentions
            }
            $runnerSchemaVersion = [int]$result.SchemaVersion
            $observedCases = [int]$result.Cases
            $runnerPassed = [bool]$result.Passed
        }
        catch {
            $validationError = $_.Exception.Message
        }
        return ([ordered]@{
                name                = $Name
                requestedCases      = $Cases
                observedCases       = $observedCases
                seed                = $Seed
                exitCode            = $process.ExitCode
                runnerSchemaVersion = $runnerSchemaVersion
                agreements          = $agreements
                abstentions         = $abstentions
                runnerPassed        = $runnerPassed
                validationPassed    = $null -eq $validationError
                validationError     = $validationError
                standardOutput      = "$logicalOutput/$Name.stdout.json"
                standardError       = "$logicalOutput/$Name.stderr.txt"
            }).AsReadOnly()
    }

    $runs = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $runs.Add((Invoke-FuzzRun `
                -Name "rotating-$RotatingSeed" `
                -Cases $schedule.RotatingCases `
                -Seed $RotatingSeed `
                -TimeoutSeconds ([int]$contract.fuzz.rotatingRunWallSeconds)))
    foreach ($seed in $schedule.RetainedRunSeeds) {
        $runs.Add((Invoke-FuzzRun `
                    -Name "retained-$seed" `
                    -Cases $effectiveRetainedCases `
                    -Seed ([int]$seed) `
                    -TimeoutSeconds ([int]$contract.worker.maximumProjectWallSeconds)))
    }
    $runs.Add((Invoke-FuzzRun `
                -Name "resource-bounds-$RotatingSeed" `
                -Cases $resourceBoundCases `
                -Seed $RotatingSeed `
                -TimeoutSeconds ([int]$contract.fuzz.resourceBoundRunWallSeconds) `
                -ResourceBounds))
    foreach ($seed in $schedule.RetainedRunSeeds) {
        $runs.Add((Invoke-FuzzRun `
                    -Name "resource-bounds-$seed" `
                    -Cases $effectiveRetainedCases `
                    -Seed ([int]$seed) `
                    -TimeoutSeconds ([int]$contract.fuzz.resourceBoundRunWallSeconds) `
                    -ResourceBounds))
    }
    $completedCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $repositoryRoot
    if ($completedCommit -cne $sourceCommit) {
        throw 'Fuzz source changed during the campaign.'
    }
    if ($null -ne $PreparedRunnerReceipt) {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $repositoryRoot `
            -SourceCommit $sourceCommit -Receipt $PreparedRunnerReceipt
    }
    $runSnapshot = $runs.ToImmutable()
    $failedRuns = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($runSnapshot | Where-Object {
                $_.exitCode -ne 0 -or -not $_.validationPassed
            }))
    $campaignPassed = $failedRuns.Length -eq 0
    $summary = ([ordered]@{
            schemaVersion          = 6
            status                 = if ($campaignPassed) { 'passed' } else { 'failed' }
            commit                 = $sourceCommit
            rotatingSeed           = $RotatingSeed
            rotatingCases          = $schedule.RotatingCases
            requestedRotatingCases = $schedule.RequestedRotatingCases
            retainedCasesPerSeed   = $schedule.RetainedCasesPerSeed
            retainedSeeds          = $schedule.RetainedSeeds
            resourceBoundCases     = $resourceBoundCases
            requestedCases         = $schedule.RequestedCases + $requestedResourceBoundCases
            totalCases             = [int](@($runSnapshot |
                        Measure-Object -Property { $_.observedCases } -Sum).Sum)
            runs                   = $runSnapshot
            passed                 = $campaignPassed
        }).AsReadOnly()
    Complete-SharpProofFuzzEvidence `
        -OutputDirectory $resolvedOutput `
        -Summary $summary
}
finally {
    Exit-SharpProofFuzzEvidenceLease -Lease $evidenceLease
}
