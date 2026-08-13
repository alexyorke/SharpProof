[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-SharpProofFuzzRunnerResult.ps1')

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-fuzz-result-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

function New-CanonicalResult([int]$Cases, [int]$Seed) {
    return [ordered]@{
        SchemaVersion = 4; Cases = $Cases; Seed = $Seed
        MaximumParallelism = 4; Agreements = $Cases; Abstentions = 0
        FrontendAgreements = $Cases; SmtAgreements = $Cases
        PartialSmtAgreements = $Cases
        FrontendCoverage = [ordered]@{
            TextParameters = 1; StringLiterals = 1; NullStrings = 1
            StringConcatenations = 1; StringLengths = 1; StringCasts = 1
            ArrayLengths = 1; ArrayIndexes = 1; DivideByZeroExceptions = 1
            OverflowExceptions = 1; NullReferenceExceptions = 1
            IndexOutOfRangeExceptions = 1; InvalidCastExceptions = 1
        }
        CoverageSatisfied = $true; Failures = [object[]]@(); Passed = $true
    }
}

function Copy-Result([object]$Value) {
    return $Value | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable
}

function Write-Result([object]$Value, [string]$Name) {
    $path = Join-Path $temporaryRoot "$Name.json"
    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    return $path
}

function Assert-Accepted(
    [object]$Value,
    [string]$Name,
    [int]$Cases = 10,
    [int]$Seed = 123) {
    Assert-SharpProofFuzzRunnerResult `
        -Path (Write-Result $Value $Name) `
        -ExpectedCases $Cases -ExpectedSeed $Seed `
        -ExpectedMaximumParallelism 4 | Out-Null
}

function Assert-Rejected([object]$Value, [string]$Name) {
    try {
        Assert-SharpProofFuzzRunnerResult `
            -Path (Write-Result $Value $Name) `
            -ExpectedCases 10 -ExpectedSeed 123 `
            -ExpectedMaximumParallelism 4 | Out-Null
    }
    catch { return }
    throw "Fixture '$Name' was unexpectedly accepted."
}

try {
    $canonical = New-CanonicalResult 10 123
    Assert-Accepted $canonical 'canonical-rotating'
    Assert-Accepted (New-CanonicalResult 3 23063) `
        'canonical-retained' 3 23063

    $fixture = Copy-Result $canonical; $fixture.Cases = '10'
    Assert-Rejected $fixture 'numeric-string'
    $fixture = Copy-Result $canonical; $fixture.Failures = $null
    Assert-Rejected $fixture 'null-failures'
    $fixture = Copy-Result $canonical; $fixture.Failures = [ordered]@{}
    Assert-Rejected $fixture 'non-array-failures'
    $fixture = Copy-Result $canonical; [void]$fixture.Remove('Passed')
    Assert-Rejected $fixture 'omitted-field'
    $fixture = Copy-Result $canonical; $fixture['Extra'] = 0
    Assert-Rejected $fixture 'extra-field'
    $fixture = Copy-Result $canonical; $fixture.SchemaVersion = 3
    Assert-Rejected $fixture 'wrong-schema'
    $fixture = Copy-Result $canonical; $fixture.Passed = $false
    Assert-Rejected $fixture 'false-status'
    $fixture = Copy-Result $canonical; $fixture.CoverageSatisfied = $false
    Assert-Rejected $fixture 'false-coverage-status'
    $fixture = Copy-Result $canonical; $fixture.Agreements = 9
    Assert-Rejected $fixture 'count-mismatch'
    $fixture = Copy-Result $canonical; $fixture.FrontendAgreements = 9
    Assert-Rejected $fixture 'frontend-count-mismatch'
    $fixture = Copy-Result $canonical
    [void]$fixture.FrontendCoverage.Remove('StringCasts')
    Assert-Rejected $fixture 'missing-coverage-field'
    $fixture = Copy-Result $canonical; $fixture.FrontendCoverage['Decoy'] = 1
    Assert-Rejected $fixture 'extra-coverage-field'
    $fixture = Copy-Result $canonical; $fixture.FrontendCoverage.ArrayIndexes = '1'
    Assert-Rejected $fixture 'coverage-numeric-string'
    $fixture = Copy-Result $canonical; $fixture.FrontendCoverage.ArrayIndexes = 0
    Assert-Rejected $fixture 'empty-coverage-category'
    $fixture = Copy-Result $canonical
    $fixture.Failures = [object[]]@([ordered]@{
            Case = 1; Seed = 123; Oracle = 'frontend'; Original = 'a'
            Minimized = 'a'; Detail = 'mismatch'; Term = 'a'
        })
    Assert-Rejected $fixture 'reported-failure'
    $fixture = Copy-Result $canonical
    $fixture.Failures = [object[]]@([ordered]@{ Case = 1 })
    Assert-Rejected $fixture 'malformed-failure'
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

Write-Host 'Strict fuzz runner result fixtures passed.'
