[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-SharpProofStandaloneGateResult.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')

$commit = '0123456789abcdef0123456789abcdef01234567'
$mvid = '01234567-89ab-cdef-0123-456789abcdef'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-gate-evidence-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

function New-CorpusResult {
    return [ordered]@{
        Passed = $true; CaseCount = 1; BaseCaseCount = 1
        OpenSourceMethodCount = 1; SupportedOpenSourceMethodCount = 1
        OpenSourceFileCount = 1; SyntheticSeedCount = 1; VariantCount = 1
        DiagnosticCount = 0; SupportedCaseCount = 1
        IntentionallyUnsupportedCaseCount = 0; SupportedUnknownCount = 0
        UnknownCount = 0; SilentUnknownCount = 0; TotalUnknownCount = 0
        UnknownRate = 0.0; SilentUnknownRate = 0.0; TotalUnknownRate = 0.0
        CacheReplayCount = 1; ConcurrentReplayCount = 1
        UnknownReasons = [object[]]@(); AllowedDegradations = [object[]]@()
        Failures = [object[]]@()
    }
}

function New-PerformanceResult {
    return [ordered]@{
        Passed = $true; Warmups = 1; Samples = 1
        PackageBuildEstimatorVersion = '1'
        PackageBuildSdk = [ordered]@{
            ConfiguredVersion = '9.0.316'; RollForward = 'disable'
            ResolvedVersion = '9.0.316'
        }
        PackageBuildSamples = [object[]]@([ordered]@{
                Index = 0; UnannotatedAdvisoryFirst = $false
                BaselineMilliseconds = 1.0
                UnannotatedAdvisoryMilliseconds = 1.0; Ratio = 1.0
            })
        OrderBalancedRatios = [object[]]@(1.0)
        UnannotatedAdvisoryAnalyzerDriverRunCount = 1
        UnannotatedAdvisoryAnalysisSessionCreateCount = 1
        UnannotatedAdvisoryApiSpecCreateCount = 1
        UnannotatedAdvisoryEffectAnalysisCreateCount = 1
        OrderBalancedMedianRatio = 1.0; RawMedianRatio = 1.0
        BaselineFirstMedianRatio = 1.0
        UnannotatedAdvisoryFirstMedianRatio = 1.0; RawP95Ratio = 1.0
        BaselineRetainedBytes = 1; UnannotatedAdvisoryRetainedBytes = 1
        RetainedMemoryRatio = 1.0; RetainedMemoryIncreaseMiB = 0.0
        EnabledRetainedCompilationCount = 0
        EnabledRetainedMemoryIncreaseMiB = 0.0; IdeEdits = 1
        IdeEditP95Milliseconds = 1.0; IdeEditMaximumMilliseconds = 1.0
        IdeDiagnosticReplayFailureCount = 0
        CancellationP95Milliseconds = 1.0; ForcedTerminationMilliseconds = 1.0
        Failures = [object[]]@()
    }
}

function New-Envelope([string]$Gate) {
    return [ordered]@{
        SchemaVersion = 1; Gate = $Gate; Passed = $true
        SourceCommit = $commit
        Executable = [ordered]@{
            Mvid = $mvid
        }
        Result = if ($Gate -ceq 'corpus') {
            New-CorpusResult
        } else {
            New-PerformanceResult
        }
    }
}

function Write-Fixture([object]$Value, [string]$Name) {
    $path = Join-Path $temporaryRoot "$Name.json"
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path
    return $path
}

function Assert-Envelope(
    [object]$Value,
    [string]$Gate,
    [string]$Name,
    [switch]$ExpectRejected) {
    Invoke-SharpProofFixtureAssertion `
        -Name $Name `
        -Write { Write-Fixture $Value $Name } `
        -Validate {
            param($path)
            Assert-SharpProofStandaloneGateResult `
                -Path $path -ExpectedGate $Gate `
                -ExpectedCommit $commit -ExpectedMvid $mvid | Out-Null
        } `
        -ExpectRejected:$ExpectRejected
}

function Assert-Accepted([object]$Value, [string]$Gate, [string]$Name) {
    Assert-Envelope $Value $Gate $Name
}

function Assert-Rejected([object]$Value, [string]$Gate, [string]$Name) {
    Assert-Envelope $Value $Gate $Name -ExpectRejected
}

try {
    Assert-Accepted (New-Envelope 'corpus') 'corpus' 'valid-corpus'
    Assert-Accepted (New-Envelope 'performance') 'performance' 'valid-performance'
    Assert-Rejected ([ordered]@{}) 'corpus' 'empty-object'

    $duplicateJson = (New-Envelope 'corpus' | ConvertTo-Json -Depth 12).Replace(
        '"SchemaVersion": 1,',
        '"SchemaVersion": 1,"SchemaVersion": 1,')
    $duplicatePath = Join-Path $temporaryRoot 'duplicate-key.json'
    [IO.File]::WriteAllText($duplicatePath, $duplicateJson)
    $duplicateRejected = $false
    try {
        Assert-SharpProofStandaloneGateResult `
            -Path $duplicatePath -ExpectedGate corpus `
            -ExpectedCommit $commit -ExpectedMvid $mvid | Out-Null
    }
    catch { $duplicateRejected = $true }
    if (-not $duplicateRejected) {
        throw 'Duplicate standalone gate JSON properties were accepted.'
    }

    $fixture = New-Envelope 'corpus'; $fixture.SchemaVersion = 2
    Assert-Rejected $fixture 'corpus' 'wrong-schema'
    $fixture = New-Envelope 'corpus'; $fixture.Gate = 'performance'
    Assert-Rejected $fixture 'corpus' 'wrong-gate'
    $fixture = New-Envelope 'corpus'; $fixture.Passed = $false
    Assert-Rejected $fixture 'corpus' 'false-envelope-status'
    $fixture = New-Envelope 'corpus'; $fixture.Result.Passed = $false
    Assert-Rejected $fixture 'corpus' 'false-result-status'
    $fixture = New-Envelope 'corpus'; $fixture.SourceCommit = 'f' * 40
    Assert-Rejected $fixture 'corpus' 'stale-commit'
    $fixture = New-Envelope 'corpus'; $fixture.Executable.Mvid = [Guid]::Empty.ToString('D')
    Assert-Rejected $fixture 'corpus' 'wrong-mvid'
    $fixture = New-Envelope 'corpus'; $fixture.Remove('Result')
    Assert-Rejected $fixture 'corpus' 'missing-field'
    $fixture = New-Envelope 'corpus'; $fixture['Extra'] = 'decoy'
    Assert-Rejected $fixture 'corpus' 'extra-field'
    $fixture = New-Envelope 'performance'; $fixture.Result.Remove('Samples')
    Assert-Rejected $fixture 'performance' 'missing-result-field'
    $fixture = New-Envelope 'performance'; $fixture.Result['Decoy'] = 1
    Assert-Rejected $fixture 'performance' 'extra-result-field'
    $fixture = New-Envelope 'corpus'; $fixture.Result.Failures = $null
    Assert-Rejected $fixture 'corpus' 'null-failures'
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

Write-Host 'Standalone gate evidence fixtures passed.'
