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
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to bind fuzz evidence to the exact source commit.'
}
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$contract = Get-Content `
    -LiteralPath (Join-Path $repositoryRoot 'eng\acceptance\contract.json') `
    -Raw |
    ConvertFrom-Json
$retained = Get-Content `
    -LiteralPath (Join-Path $repositoryRoot 'eng\fuzz\retained-seeds.json') `
    -Raw |
    ConvertFrom-Json
if ($retained.schemaVersion -ne 1 -or
    [int]$retained.casesPerSeed -le 0 -or
    @($retained.seeds).Count -eq 0) {
    throw 'Invalid retained fuzz seed manifest.'
}
if (-not $PSBoundParameters.ContainsKey('RotatingSeed')) {
    $RotatingSeed = [int][DateTime]::UtcNow.ToString(
        'yyyyMMdd',
        [Globalization.CultureInfo]::InvariantCulture)
}
$effectiveRotatingCases = if ($RotatingCases -gt 0) {
    $RotatingCases
}
else {
    [int]$contract.fuzz.nightlyCases
}
$effectiveRetainedCases = if ($RetainedCases -gt 0) {
    $RetainedCases
}
else {
    [int]$retained.casesPerSeed
}
$resolvedOutput = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputDirectory `
    -ParameterName 'OutputDirectory'
New-Item -ItemType Directory -Force -Path $resolvedOutput |
    Out-Null

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
    try {
        if ($process.ExitCode -ne 0) {
            throw "runner exited with code $($process.ExitCode)"
        }
        if (-not (Test-Path -LiteralPath $standardOutput -PathType Leaf)) {
            throw 'runner did not emit a JSON result'
        }
        $result = Get-Content -LiteralPath $standardOutput -Raw |
            ConvertFrom-Json -ErrorAction Stop
        $requiredProperties = @(
            'SchemaVersion',
            'Cases',
            'Seed',
            'MaximumParallelism',
            'Agreements',
            'Abstentions',
            'FrontendAgreements',
            'SmtAgreements',
            'PartialSmtAgreements',
            'CoverageSatisfied',
            'Failures',
            'Passed'
        )
        foreach ($propertyName in $requiredProperties) {
            if ($null -eq $result.PSObject.Properties[$propertyName]) {
                throw "runner result is missing '$propertyName'"
            }
        }

        $runnerSchemaVersion = [int]$result.SchemaVersion
        $observedCases = [int]$result.Cases
        $observedSeed = [int]$result.Seed
        $observedMaximumParallelism = [int]$result.MaximumParallelism
        $agreements = [int]$result.Agreements
        $abstentions = [int]$result.Abstentions
        $failureCount = @($result.Failures).Count
        if ($result.Passed -isnot [bool] -or
            $result.CoverageSatisfied -isnot [bool]) {
            throw 'runner status properties must be JSON booleans'
        }
        $runnerPassed = [bool]$result.Passed

        if ($runnerSchemaVersion -ne 4) {
            throw "runner schema '$runnerSchemaVersion' is not supported"
        }
        if ($observedCases -ne $Cases) {
            throw "runner reported $observedCases cases; expected $Cases"
        }
        if ($observedSeed -ne $Seed) {
            throw "runner reported seed $observedSeed; expected $Seed"
        }
        if ($observedMaximumParallelism -ne
            [int]$contract.fuzz.maximumParallelism) {
            throw (
                "runner reported maximum parallelism " +
                "$observedMaximumParallelism; expected " +
                [string]$contract.fuzz.maximumParallelism)
        }
        if ($agreements -lt 0 -or
            $abstentions -lt 0 -or
            $agreements + $abstentions -ne $observedCases) {
            throw (
                "runner accounted for $($agreements + $abstentions) of " +
                "$observedCases cases")
        }
        if ($abstentions -ne 0 -or $agreements -ne $observedCases) {
            throw (
                "supported-domain fuzzing requires zero abstentions and " +
                "agreement for every case; agreements=$agreements, " +
                "abstentions=$abstentions")
        }
        foreach ($countName in @(
                'FrontendAgreements',
                'SmtAgreements',
                'PartialSmtAgreements')) {
            $count = [int]$result.$countName
            if ($count -ne $observedCases) {
                throw (
                    "runner '$countName' reported $count agreements; " +
                    "expected $observedCases")
            }
        }
        if ($failureCount -ne 0) {
            throw "runner reported $failureCount differential failures"
        }
        if (-not [bool]$result.CoverageSatisfied) {
            throw 'runner did not satisfy required fuzz coverage'
        }
        if (-not $runnerPassed) {
            throw 'runner reported Passed=false'
        }
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
        resultSha256 = if (Test-Path -LiteralPath $standardOutput -PathType Leaf) {
            (Get-FileHash -LiteralPath $standardOutput -Algorithm SHA256).Hash.ToLowerInvariant()
        } else { $null }
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
foreach ($seed in @($retained.seeds)) {
    if ([int]$seed -eq $RotatingSeed) {
        continue
    }
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
    retainedSeeds = @($retained.seeds | ForEach-Object { [int]$_ })
    retainedSeedManifestSha256 = (Get-FileHash -LiteralPath (
        Join-Path $repositoryRoot 'eng\fuzz\retained-seeds.json') `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    requestedCases = [int](@($runs |
        Measure-Object -Property requestedCases -Sum).Sum)
    totalCases = [int](@($runs |
        Measure-Object -Property observedCases -Sum).Sum)
    runs = @($runs)
    passed = @($runs | Where-Object {
            $_.exitCode -ne 0 -or -not $_.validationPassed
        }).Count -eq 0
}
$summaryPath = Join-Path $resolvedOutput 'campaign.json'
$json = ($summary | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
[IO.File]::WriteAllText(
    $summaryPath,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))
$summary | ConvertTo-Json -Depth 6
if (-not $summary.passed) {
    throw "SharpProof fuzz campaign failed. Evidence: $summaryPath"
}
