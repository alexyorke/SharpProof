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
        SchemaVersion = 6; Cases = $Cases; Seed = $Seed
        MaximumParallelism = 4; Agreements = $Cases; Abstentions = 0
        FrontendAgreements = $Cases; SmtAgreements = $Cases
        FiniteSmtSatisfiable = [Math]::Max(1, [Math]::Floor($Cases / 2))
        FiniteSmtUnsatisfiable = $Cases - [Math]::Max(1, [Math]::Floor($Cases / 2))
        FiniteSmtAssumptions = $Cases
        PartialSmtDefinedTrue = $Cases; PartialSmtDefinedFalse = 0
        PartialSmtUndefined = $Cases
        PartialSmtAgreements = $Cases
        FrontendCoverage = [ordered]@{
            TextParameters = 1; StringLiterals = 1; NullStrings = 1
            StringConcatenations = 1; StringLengths = 1; StringCasts = 1
            ArrayLengths = 1; ArrayIndexes = 1; DivideByZeroExceptions = 1
            OverflowExceptions = 1; NullReferenceExceptions = 1
            IndexOutOfRangeExceptions = 1; InvalidCastExceptions = 1
        }
        CoverageSatisfied = $true; Failures = [object[]]@()
        AbstentionEvidence = [object[]]@(); Passed = $true
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

function Assert-Rejected(
    [object]$Value,
    [string]$Name,
    [int]$Cases = 10,
    [int]$Seed = 123,
    [int]$MaximumParallelism = 4) {
    try {
        Assert-SharpProofFuzzRunnerResult `
            -Path (Write-Result $Value $Name) `
            -ExpectedCases $Cases -ExpectedSeed $Seed `
            -ExpectedMaximumParallelism $MaximumParallelism | Out-Null
    }
    catch { return }
    throw "Fixture '$Name' was unexpectedly accepted."
}

try {
    $canonical = New-CanonicalResult 10 123
    Assert-Accepted $canonical 'canonical-rotating'
    $hashPath = Write-Result $canonical 'canonical-hash'
    $hashed = Assert-SharpProofFuzzRunnerResult `
        -Path $hashPath -ExpectedCases 10 -ExpectedSeed 123 `
        -ExpectedMaximumParallelism 4
    $expectedHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [IO.File]::ReadAllBytes($hashPath))).ToLowerInvariant()
    if ($hashed.ResultSha256 -cne $expectedHash) {
        throw 'The fuzz runner result hash did not bind the validated bytes.'
    }
    $racePath = Write-Result $canonical 'canonical-race'
    $raceBytes = [IO.File]::ReadAllBytes($racePath)
    $replacement = New-CanonicalResult 10 456
    $raced = Assert-SharpProofFuzzRunnerResult `
        -Path $racePath -ExpectedCases 10 -ExpectedSeed 123 `
        -ExpectedMaximumParallelism 4 `
        -AfterValidation {
            param($validatedPath)
            $replacement | ConvertTo-Json -Depth 8 |
                Set-Content -LiteralPath $validatedPath
        }
    $expectedRaceHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $raceBytes)).ToLowerInvariant()
    if ($raced.Seed -ne 123 -or
        $raced.ResultSha256 -cne $expectedRaceHash) {
        throw 'The fuzz runner result changed after its validated read.'
    }
    $encoding = [Text.UTF8Encoding]::new($false)
    $boundedJson = $canonical | ConvertTo-Json -Depth 8 -Compress
    $exactJson = $boundedJson +
        (' ' * (1048576 - $encoding.GetByteCount($boundedJson)))
    $boundedPath = Join-Path $temporaryRoot 'exact-byte-limit.json'
    [IO.File]::WriteAllText($boundedPath, $exactJson, $encoding)
    $bounded = Assert-SharpProofFuzzRunnerResult `
        -Path $boundedPath -ExpectedCases 10 -ExpectedSeed 123 `
        -ExpectedMaximumParallelism 4
    if ($bounded.Cases -ne 10 -or $bounded.Seed -ne 123) {
        throw 'The exact-limit fuzz runner result was not preserved.'
    }
    [IO.File]::AppendAllText($boundedPath, ' ', $encoding)
    $oversizedRejected = $false
    try {
        [void](Assert-SharpProofFuzzRunnerResult `
            -Path $boundedPath -ExpectedCases 10 -ExpectedSeed 123 `
            -ExpectedMaximumParallelism 4)
    }
    catch { $oversizedRejected = $true }
    if (-not $oversizedRejected) {
        throw 'An oversized fuzz runner result was accepted.'
    }
    Assert-Accepted (New-CanonicalResult 5 23063) `
        'canonical-retained' 5 23063
    $small = New-CanonicalResult 1 7
    foreach ($name in @($small.FrontendCoverage.Keys)) {
        $small.FrontendCoverage[$name] = 0
    }
    Assert-Accepted $small 'canonical-small-budget' 1 7

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
    $fixture = New-CanonicalResult 0 123
    $fixture.MaximumParallelism = 0
    foreach ($name in @($fixture.FrontendCoverage.Keys)) {
        $fixture.FrontendCoverage[$name] = 0
    }
    Assert-Rejected $fixture 'zero-domain' 0 123 0
    $fixture = Copy-Result $canonical; $fixture.MaximumParallelism = 5
    Assert-Rejected $fixture 'parallelism-above-domain' 10 123 5
    $fixture = Copy-Result $canonical; $fixture.Passed = $false
    Assert-Rejected $fixture 'false-status'
    $fixture = Copy-Result $canonical; $fixture.CoverageSatisfied = $false
    Assert-Rejected $fixture 'false-coverage-status'
    $fixture = Copy-Result $canonical; $fixture.Agreements = 9
    Assert-Rejected $fixture 'count-mismatch'
    $fixture = Copy-Result $canonical; $fixture.FrontendAgreements = 9
    Assert-Rejected $fixture 'frontend-count-mismatch'
    $fixture = Copy-Result $canonical; $fixture.FiniteSmtSatisfiable = 0
    Assert-Rejected $fixture 'finite-smt-satisfiable-missing'
    $fixture = Copy-Result $canonical; $fixture.FiniteSmtUnsatisfiable = 0
    Assert-Rejected $fixture 'finite-smt-unsatisfiable-missing'
    $fixture = Copy-Result $canonical; $fixture.FiniteSmtAssumptions = 0
    Assert-Rejected $fixture 'finite-smt-assumptions-missing'
    $fixture = Copy-Result $canonical
    $fixture.FiniteSmtUnsatisfiable = $fixture.FiniteSmtUnsatisfiable + 1
    Assert-Rejected $fixture 'finite-smt-outcome-sum-mismatch'
    $fixture = Copy-Result $canonical
    [void]$fixture.FrontendCoverage.Remove('StringCasts')
    Assert-Rejected $fixture 'missing-coverage-field'
    $fixture = Copy-Result $canonical; $fixture.FrontendCoverage['Decoy'] = 1
    Assert-Rejected $fixture 'extra-coverage-field'
    $fixture = Copy-Result $canonical; $fixture.FrontendCoverage.ArrayIndexes = '1'
    Assert-Rejected $fixture 'coverage-numeric-string'
    $expanded = New-CanonicalResult 1000 123
    $expanded.FrontendCoverage.ArrayIndexes = 0
    Assert-Rejected $expanded 'empty-expanded-coverage-category' 1000
    $fixture = Copy-Result $canonical
    $fixture.FrontendCoverage.DivideByZeroExceptions = 3
    $fixture.FrontendCoverage.OverflowExceptions = 3
    $fixture.FrontendCoverage.NullReferenceExceptions = 3
    $fixture.FrontendCoverage.IndexOutOfRangeExceptions = 3
    $fixture.FrontendCoverage.InvalidCastExceptions = 3
    Assert-Rejected $fixture 'impossible-exception-total'
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

Write-Host 'Strict fuzz runner result fixtures: 26'
