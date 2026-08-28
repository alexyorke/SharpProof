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
$resolvedOutput = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputDirectory `
    -ParameterName 'OutputDirectory'
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to bind fuzz evidence to the exact source commit.'
}
$workingTreeStatus = & git -C $repositoryRoot status --porcelain=v1 `
    --untracked-files=all
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect working-tree source state.'
}
if (@($workingTreeStatus).Count -ne 0) {
    throw 'SharpProof fuzz campaign requires clean exact-commit source.'
}
Initialize-SharpProofFuzzEvidence -OutputDirectory $resolvedOutput
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
    $RotatingSeed = [int][DateTime]::UtcNow.ToString(
        'yyyyMMdd',
        [Globalization.CultureInfo]::InvariantCulture)
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
$retainedRunSeeds = @($retainedSeeds | Where-Object {
        [int]$_ -ne $RotatingSeed -or
        $effectiveRotatingCases -lt $effectiveRetainedCases
    })
$requestedCampaignCases = Assert-SharpProofFuzzCampaignBudget `
    -RotatingCases $effectiveRotatingCases `
    -RetainedCases $effectiveRetainedCases `
    -RetainedRunCount $retainedRunSeeds.Count `
    -MaximumCases $maximumCampaignCases
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
    $wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
    $dotnetArguments = @(
        'run',
        '--project',
        (Join-Path $repositoryRoot 'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj'),
        '-c',
        'Release',
        '--no-build',
        '--',
        '--cases',
        [string]$Cases,
        '--seed',
        [string]$Seed,
        '--max-parallelism',
        [string]$contract.fuzz.maximumParallelism
    )
    $quotedArguments = @(
        $dotnetArguments |
            ForEach-Object {
                "'" + ([string]$_).Replace("'", "''") + "'"
            }
    ) -join ','
    $escapedWrapper = $wrapper.Replace("'", "''")
    $command = (
        "& '$escapedWrapper' -TimeoutSeconds " +
        [string]$contract.worker.maximumProjectWallSeconds +
        " @($quotedArguments); exit " + '$LASTEXITCODE')
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-EncodedCommand',
        $encodedCommand
    )
    $process = Start-Process `
        -FilePath 'pwsh' `
        -ArgumentList $arguments `
        -WorkingDirectory $repositoryRoot `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $standardOutput `
        -RedirectStandardError $standardError
    $validationError = $null
    $observedCases = 0
    $agreements = 0
    $abstentions = 0
    $runnerSchemaVersion = $null
    $runnerPassed = $false
    $resultSha256 = $null
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
        $resultSha256 = [string]$result.ResultSha256
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
        resultSha256 = $resultSha256
        standardOutput = [IO.Path]::GetRelativePath(
            $repositoryRoot,
            $standardOutput).Replace('\', '/')
        standardError = [IO.Path]::GetRelativePath(
            $repositoryRoot,
            $standardError).Replace('\', '/')
    }
}

$runs = [Collections.Generic.List[object]]::new()
$runs.Add((Invoke-FuzzRun `
    -Name "rotating-$RotatingSeed" `
    -Cases $effectiveRotatingCases `
    -Seed $RotatingSeed))
foreach ($seed in $retainedRunSeeds) {
    $runs.Add((Invoke-FuzzRun `
        -Name "retained-$seed" `
        -Cases $effectiveRetainedCases `
        -Seed ([int]$seed)))
}
$summary = [pscustomobject][ordered]@{
    schemaVersion = 3
    status = if (@($runs | Where-Object {
                $_.exitCode -ne 0 -or -not $_.validationPassed
            }).Count -eq 0) { 'passed' } else { 'failed' }
    commit = $sourceCommit
    rotatingSeed = $RotatingSeed
    rotatingCases = $effectiveRotatingCases
    retainedCasesPerSeed = $effectiveRetainedCases
    retainedSeeds = $retainedSeeds
    retainedSeedManifestSha256 = $retained.Sha256
    requestedCases = $requestedCampaignCases
    totalCases = [int](@($runs |
        Measure-Object -Property observedCases -Sum).Sum)
    runs = @($runs)
    passed = @($runs | Where-Object {
            $_.exitCode -ne 0 -or -not $_.validationPassed
        }).Count -eq 0
}
$summaryPath = Join-Path $resolvedOutput 'campaign.json'
$json = ($summary | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
$summary | ConvertTo-Json -Depth 6
Publish-SharpProofFuzzEvidence `
    -OutputDirectory $resolvedOutput `
    -Json ($json + "`n")
if (-not $summary.passed) {
    throw "SharpProof fuzz campaign failed. Evidence: $summaryPath"
}
