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
$retainedRunSeeds = @($retainedSeeds | Where-Object {
        [int]$_ -ne $RotatingSeed -or
        $effectiveRotatingCases -lt $effectiveRetainedCases
    })
$requestedCampaignCases = Assert-SharpProofFuzzCampaignBudget `
    -RotatingCases $effectiveRotatingCases `
    -RetainedCases $effectiveRetainedCases `
    -RetainedRunCount $retainedRunSeeds.Count `
    -MaximumCases $maximumCampaignCases
$dotnetWrapper = Join-Path `
    $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$fuzzProject = Join-Path `
    $repositoryRoot 'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj'

function Invoke-BoundedDotnetProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$DotnetArguments,

        [string]$StandardOutput = '',

        [string]$StandardError = ''
    )

    $quotedArguments = @(
        $DotnetArguments |
            ForEach-Object {
                "'" + ([string]$_).Replace("'", "''") + "'"
            }
    ) -join ','
    $escapedWrapper = $dotnetWrapper.Replace("'", "''")
    $command = (
        "& '$escapedWrapper' -TimeoutSeconds " +
        [string]$contract.worker.maximumProjectWallSeconds +
        " @($quotedArguments); exit " + '$LASTEXITCODE')
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    $startParameters = @{
        FilePath = 'pwsh'
        ArgumentList = @(
            '-NoLogo', '-NoProfile', '-EncodedCommand', $encodedCommand)
        WorkingDirectory = $repositoryRoot
        Wait = $true
        PassThru = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($StandardOutput)) {
        $startParameters.RedirectStandardOutput = $StandardOutput
    }
    if (-not [string]::IsNullOrWhiteSpace($StandardError)) {
        $startParameters.RedirectStandardError = $StandardError
    }
    return Start-Process @startParameters
}

$buildProcess = Invoke-BoundedDotnetProcess -DotnetArguments @(
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
$runnerSha256 = (Get-FileHash `
    -LiteralPath $runnerAssembly `
    -Algorithm SHA256).Hash.ToLowerInvariant()

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
        'run',
        '--project',
        $fuzzProject,
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
    $process = Invoke-BoundedDotnetProcess `
        -DotnetArguments $dotnetArguments `
        -StandardOutput $standardOutput `
        -StandardError $standardError
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
$completedCommit = Get-SharpProofCleanFuzzSourceCommit `
    -RepositoryRoot $repositoryRoot
$completedRunnerSha256 = (Get-FileHash `
    -LiteralPath $runnerAssembly `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($completedCommit -cne $sourceCommit -or
    $completedRunnerSha256 -cne $runnerSha256) {
    throw 'Fuzz source or runner identity changed during the campaign.'
}
$summary = [pscustomobject][ordered]@{
    schemaVersion = 4
    status = if (@($runs | Where-Object {
                $_.exitCode -ne 0 -or -not $_.validationPassed
            }).Count -eq 0) { 'passed' } else { 'failed' }
    commit = $sourceCommit
    runnerSha256 = $runnerSha256
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
Complete-SharpProofFuzzEvidence `
    -OutputDirectory $resolvedOutput `
    -Summary $summary
