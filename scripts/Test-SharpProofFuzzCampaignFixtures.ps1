[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-fuzz-campaign-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path `
    (Join-Path $fixture 'scripts'),
    (Join-Path $fixture 'eng/acceptance'),
    (Join-Path $fixture 'eng/fuzz'),
    (Join-Path $fixture 'artifacts') -Force | Out-Null

try {
    foreach ($name in @(
            'Invoke-SharpProofFuzzCampaign.ps1',
            'Assert-SharpProofFuzzRunnerResult.ps1',
            'SharpProof.FuzzEvidenceLifecycle.ps1',
            'Resolve-SharpProofContainedPath.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/$name") `
            -Destination (Join-Path $fixture "scripts/$name")
    }
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/acceptance/contract.json') `
        -Destination (Join-Path $fixture 'eng/acceptance/contract.json')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/fuzz/retained-seeds.json') `
        -Destination (Join-Path $fixture 'eng/fuzz/retained-seeds.json')

    @'
{
  "schemaVersion": 1,
  "casesPerSeed": 3,
  "seeds": [123, 456]
}
'@ | Set-Content -LiteralPath (
        Join-Path $fixture 'eng/fuzz/retained-seeds.json') -Encoding utf8NoBOM

    @'
[CmdletBinding()]
param(
    [int]$TimeoutSeconds,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)
Set-StrictMode -Version Latest
$casesIndex = [Array]::IndexOf($Arguments, '--cases')
$seedIndex = [Array]::IndexOf($Arguments, '--seed')
$parallelismIndex = [Array]::IndexOf($Arguments, '--max-parallelism')
$cases = [int]$Arguments[$casesIndex + 1]
$seed = [int]$Arguments[$seedIndex + 1]
$parallelism = [int]$Arguments[$parallelismIndex + 1]
$coverage = [ordered]@{
    TextParameters = 1; StringLiterals = 1; NullStrings = 1
    StringConcatenations = 1; StringLengths = 1; StringCasts = 1
    ArrayLengths = 1; ArrayIndexes = 1; DivideByZeroExceptions = 0
    OverflowExceptions = 0; NullReferenceExceptions = 0
    IndexOutOfRangeExceptions = 0; InvalidCastExceptions = 0
}
$result = [ordered]@{
    SchemaVersion = 6; Cases = $cases; Seed = $seed
    MaximumParallelism = $parallelism; Agreements = 0; Abstentions = 0
    FrontendAgreements = 0; SmtAgreements = $cases
    FiniteSmtSatisfiable = $cases; FiniteSmtUnsatisfiable = 0
    FiniteSmtAssumptions = $cases; PartialSmtAgreements = $cases
    PartialSmtDefinedTrue = $cases; PartialSmtDefinedFalse = 0
    PartialSmtUndefined = $cases; FrontendCoverage = $coverage
    CoverageSatisfied = $true
    Failures = @([ordered]@{
        Case = 0; Seed = $seed; CampaignSeed = 0; Oracle = 'frontend'; Original = 'original'
        Minimized = 'minimized'; Detail = 'semantic mismatch'; Term = 'minimized'
    })
    AbstentionEvidence = [object[]]@(); Passed = $false
}
$result | ConvertTo-Json -Depth 8
exit 1
'@ | Set-Content -LiteralPath (
        Join-Path $fixture 'scripts/Invoke-SharpProofDotnet.ps1') `
        -Encoding utf8NoBOM

    & git -C $fixture init --quiet
    & git -C $fixture config user.email fixture@sharpproof.test
    & git -C $fixture config user.name 'SharpProof Fixture'
    & git -C $fixture add -- .
    & git -C $fixture commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fuzz campaign fixture.' }

    $output = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Invoke-SharpProofFuzzCampaign.ps1') `
        -OutputDirectory artifacts/fuzz `
        -RotatingSeed 123 -RotatingCases 1 -RetainedCases 3 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Fuzz campaign semantic-failure fixture unexpectedly passed.'
    }
    $summaryPath = Join-Path $fixture 'artifacts/fuzz/campaign.json'
    if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
        throw "Fuzz campaign did not publish a failure summary: $output"
    }
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    if ([string]$summary.status -cne 'failed' -or
        [bool]$summary.passed -or [int]$summary.totalCases -ne 6 -or
        @($summary.runs).Count -ne 2 -or
        [string]$summary.runs[0].name -cne 'rotating-retained-123' -or
        [int]$summary.runs[0].requestedCases -ne 3 -or
        [string]$summary.runs[1].name -cne 'retained-456' -or
        [int]$summary.runs[1].requestedCases -ne 3 -or
        @($summary.runs | Where-Object {
                [int]$_.exitCode -ne 1 -or
                -not [bool]$_.validationPassed -or
                [int]$_.observedCases -ne 3 -or
                [int]$_.runnerSchemaVersion -ne 6 -or
                [string]::IsNullOrWhiteSpace([string]$_.resultSha256)
            }).Count -ne 0) {
        throw ('The campaign did not retain structurally valid semantic failures: ' +
            ($summary | ConvertTo-Json -Depth 8 -Compress))
    }
    Write-Host 'Fuzz campaign semantic-failure fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
