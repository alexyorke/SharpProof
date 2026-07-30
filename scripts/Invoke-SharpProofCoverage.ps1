[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResultsDirectory = 'artifacts/coverage',

    [Parameter()]
    [ValidateRange(1, 65536)]
    [int]$MemoryLimitMb = 12288,

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedResultsDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $ResultsDirectory))
$repositoryPrefix =
    $repositoryRoot + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedResultsDirectory.StartsWith(
        $repositoryPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw (
        'ResultsDirectory must be inside the repository: ' +
        $resolvedResultsDirectory)
}
if (Test-Path -LiteralPath $resolvedResultsDirectory -PathType Container) {
    $existingReport = Get-ChildItem `
        -LiteralPath $resolvedResultsDirectory `
        -Recurse `
        -Filter '*.cobertura.xml' `
        -File `
        -ErrorAction Stop |
        Select-Object -First 1
    if ($null -ne $existingReport) {
        throw (
            'ResultsDirectory already contains coverage evidence: ' +
            $existingReport.FullName)
    }
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $resolvedResultsDirectory |
    Out-Null

$dotnetWrapper = Join-Path `
    $repositoryRoot `
    'scripts\Invoke-SharpProofDotnet.ps1'

# The Microsoft collector observes managed execution without rewriting the
# payload files that SharpProof deliberately authenticates by SHA-256. The
# Coverlet collector cannot be used here because its on-disk instrumentation
# turns the trusted Attributes assembly into a different, correctly rejected
# compiler input while the tests are running.
& $dotnetWrapper `
    -MemoryLimitMb $MemoryLimitMb `
    -TimeoutSeconds $TimeoutSeconds `
    test (Join-Path $repositoryRoot 'SharpProof.Dev.Tests.slnf') `
    -c Release `
    --no-build `
    --filter 'TestCategory!=Performance&TestCategory!=Coverage' `
    --collect 'Code Coverage;Format=Cobertura' `
    --results-directory $resolvedResultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Coverage collection failed with exit code $LASTEXITCODE."
}

# The broad pass deliberately excludes wall-clock assertions. Exercise the
# complete performance protocol in a separate structural-evidence test whose
# settings instrument only SharpProof.Gates and never its child processes or
# product payloads. The ordinary uninstrumented Performance test remains the
# authoritative threshold gate.
$gateSettings = Join-Path `
    $repositoryRoot `
    'eng\coverage\SharpProof.Gates.runsettings'
& $dotnetWrapper `
    -MemoryLimitMb $MemoryLimitMb `
    -TimeoutSeconds $TimeoutSeconds `
    test (Join-Path `
        $repositoryRoot `
        'SharpProof.Gates.Test\SharpProof.Gates.Test.csproj') `
    -c Release `
    --no-build `
    --filter 'TestCategory=Coverage' `
    --settings $gateSettings `
    --collect 'Code Coverage;Format=Cobertura' `
    --results-directory $resolvedResultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw (
        'Isolated Gates coverage failed with exit code ' +
        "$LASTEXITCODE.")
}
