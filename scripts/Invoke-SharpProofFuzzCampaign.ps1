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
    [int]$RetainedCases
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
. (Join-Path $PSScriptRoot 'Assert-SharpProofFuzzRunnerResult.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')
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
    $retainedSeeds = @($retained.Seeds)
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
        -RetainedSeeds ([int[]]$retainedSeeds) `
        -RotatingCases $effectiveRotatingCases `
        -RetainedCases $effectiveRetainedCases `
        -MaximumCases $maximumCampaignCases
    $dotnetWrapper = Get-SharpProofDotnetWrapperPath
    $fuzzProject = Join-Path `
        $repositoryRoot 'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj'

    $buildProcess = Start-SharpProofEncodedPowerShell `
        -WrapperPath $dotnetWrapper `
        -TimeoutSeconds ([int]$contract.worker.maximumProjectWallSeconds) `
        -WorkingDirectory $repositoryRoot `
        -Arguments @(
        'build',
        $fuzzProject,
        '-c',
        'Release',
        '--no-restore',
        '--no-incremental',
        '--nologo')
    if ($buildProcess.ExitCode -ne 0) {
        throw "SharpProof fuzz runner rebuild failed with code $($buildProcess.ExitCode)."
    }
    $builtCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $repositoryRoot
    if ($builtCommit -cne $sourceCommit) {
        throw 'Fuzz source changed while rebuilding the runner.'
    }
    $runnerAssembly = Join-Path $repositoryRoot `
        'Tools\SharpProof.Fuzz\bin\Release\net9.0\SharpProof.Fuzz.dll'
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
            [int]$Seed
        )

        $standardOutput = Join-Path $resolvedOutput "$Name.stdout.json"
        $standardError = Join-Path $resolvedOutput "$Name.stderr.txt"
        $dotnetArguments = @(
            $runnerAssembly,
            '--cases',
            [string]$Cases,
            '--seed',
            [string]$Seed,
            '--max-parallelism',
            [string]$contract.fuzz.maximumParallelism
        )
        $process = Start-SharpProofEncodedPowerShell `
            -WrapperPath $dotnetWrapper `
            -TimeoutSeconds ([int]$contract.worker.maximumProjectWallSeconds) `
            -WorkingDirectory $repositoryRoot `
            -Arguments $dotnetArguments `
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
            $result = Assert-SharpProofFuzzRunnerResult `
                -Path $standardOutput `
                -ExpectedCases $Cases `
                -ExpectedSeed $Seed `
                -ExpectedMaximumParallelism ([int]$contract.fuzz.maximumParallelism)
            $runnerSchemaVersion = [int]$result.SchemaVersion
            $observedCases = [int]$result.Cases
            $agreements = [int]$result.Agreements
            $abstentions = [int]$result.Abstentions
            $runnerPassed = [bool]$result.Passed
        }
        catch {
            $validationError = $_.Exception.Message
        }
        return [pscustomobject][ordered]@{
            name = $Name
            requestedCases = $Cases
            observedCases = $observedCases
            seed = $Seed
            exitCode = $process.ExitCode
            runnerSchemaVersion = $runnerSchemaVersion
            agreements = $agreements
            abstentions = $abstentions
            runnerPassed = $runnerPassed
            validationPassed = $null -eq $validationError
            validationError = $validationError
            standardOutput = "$logicalOutput/$Name.stdout.json"
            standardError = "$logicalOutput/$Name.stderr.txt"
        }
    }

    $runs = [Collections.Generic.List[object]]::new()
    $runs.Add((Invoke-FuzzRun `
        -Name "rotating-$RotatingSeed" `
        -Cases $schedule.RotatingCases `
        -Seed $RotatingSeed))
    foreach ($seed in $schedule.RetainedRunSeeds) {
        $runs.Add((Invoke-FuzzRun `
            -Name "retained-$seed" `
            -Cases $effectiveRetainedCases `
            -Seed ([int]$seed)))
    }
    $completedCommit = Get-SharpProofCleanFuzzSourceCommit `
        -RepositoryRoot $repositoryRoot
    if ($completedCommit -cne $sourceCommit) {
        throw 'Fuzz source changed during the campaign.'
    }
    $failedRuns = @($runs | Where-Object {
            $_.exitCode -ne 0 -or -not $_.validationPassed
        })
    $campaignPassed = $failedRuns.Count -eq 0
    $summary = [pscustomobject][ordered]@{
        schemaVersion = 5
        status = if ($campaignPassed) { 'passed' } else { 'failed' }
        commit = $sourceCommit
        rotatingSeed = $RotatingSeed
        rotatingCases = $schedule.RotatingCases
        requestedRotatingCases = $schedule.RequestedRotatingCases
        retainedCasesPerSeed = $schedule.RetainedCasesPerSeed
        retainedSeeds = $schedule.RetainedSeeds
        requestedCases = $schedule.RequestedCases
        totalCases = [int](@($runs |
            Measure-Object -Property observedCases -Sum).Sum)
        runs = @($runs)
        passed = $campaignPassed
    }
    Complete-SharpProofFuzzEvidence `
        -OutputDirectory $resolvedOutput `
        -Summary $summary
}
finally {
    Exit-SharpProofFuzzEvidenceLease -Lease $evidenceLease
}
