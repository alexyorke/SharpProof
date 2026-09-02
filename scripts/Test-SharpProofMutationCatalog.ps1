[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force

if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw "ExpectedCommit must be a 40-character commit SHA: '$ExpectedCommit'."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $ExpectedCommit) {
    throw 'Mutation evidence must be validated at the exact repository commit.'
}
$dirty = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
    throw 'Mutation evidence reuse requires a clean tracked repository tree.'
}
$contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
$contract = Get-Content -LiteralPath $contractPath -Raw |
    ConvertFrom-Json
$policy = $contract.mutationEvidence
$expectedCatalogCount = [int]$policy.expectedCatalogCount

$resolvedEvidence = [IO.Path]::GetFullPath($EvidencePath)
if (-not [IO.File]::Exists($resolvedEvidence)) {
    throw "Mutation evidence file is missing: '$EvidencePath'."
}
$evidence = Get-Content -LiteralPath $resolvedEvidence -Raw |
    ConvertFrom-Json
$mutations = @($evidence.mutations)

if ([int]$evidence.schemaVersion -ne 2 -or
    [string]$evidence.selection -ne 'full' -or
    [int]$evidence.catalogCount -ne $expectedCatalogCount -or
    [int]$evidence.mutationCount -ne $expectedCatalogCount -or
    [int]$evidence.killedCount -ne $expectedCatalogCount -or
    $mutations.Count -ne $expectedCatalogCount) {
    throw (
        'Mutation evidence does not prove a complete current trusted-boundary ' +
        'catalog run.')
}
if ([string]$evidence.commit -ne $ExpectedCommit) {
    throw (
        "Mutation evidence commit '$($evidence.commit)' does not match " +
        "expected commit '$ExpectedCommit'.")
}

$names = @($mutations | ForEach-Object { [string]$_.name })
if ($names | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
    throw 'Mutation evidence contains a mutation with an empty catalog identity.'
}
if ($names | Group-Object | Where-Object Count -gt 1) {
    throw 'Mutation evidence contains duplicate catalog identities.'
}
foreach ($mutation in $mutations) {
    if (-not $mutation.killed -or
        [int]$mutation.assertionFailureCount -lt 1 -or
        [int]$mutation.exitCode -eq 0) {
        throw (
            "Mutation '$($mutation.name)' lacks assertion-backed kill evidence.")
    }

    $evidenceDirectory = Split-Path -Parent $resolvedEvidence
    foreach ($receipt in @(
            @{ Path = [string]$mutation.log; Name = 'log' },
            @{ Path = [string]$mutation.trx; Name = 'TRX' })) {
        if ([string]::IsNullOrWhiteSpace($receipt.Path)) {
            throw "Mutation '$($mutation.name)' has invalid $($receipt.Name) receipt evidence."
        }
        $receiptPath = [IO.Path]::GetFullPath(
            (Join-Path $evidenceDirectory $receipt.Path))
        $receiptPrefix = $evidenceDirectory + [IO.Path]::DirectorySeparatorChar
        if (-not $receiptPath.StartsWith(
                $receiptPrefix,
                [StringComparison]::Ordinal) -or
            -not [IO.File]::Exists($receiptPath)) {
            throw "Mutation '$($mutation.name)' has a missing $($receipt.Name) receipt."
        }
    }

    $filterPrefix = 'FullyQualifiedName~'
    $test = [string]$mutation.test
    if (-not $test.StartsWith($filterPrefix, [StringComparison]::Ordinal)) {
        throw "Mutation '$($mutation.name)' has an invalid selected-test filter."
    }
    $baselineInvocation = Get-SharpProofMutationBaselineInvocation `
        -Project ([string]$mutation.project) `
        -Filter $test `
        -Configuration ([string]$evidence.configuration)
    if ([string]$mutation.baselineInvocation -ne
            $baselineInvocation.Identity -or
        @($mutation.baselineSelectedTests).Count -eq 0) {
        throw "Mutation '$($mutation.name)' has invalid focused baseline evidence."
    }
    $baselineTrxPath = [IO.Path]::GetFullPath(
        (Join-Path $evidenceDirectory ([string]$mutation.baselineTrx)))
    $receiptPrefix = $evidenceDirectory + [IO.Path]::DirectorySeparatorChar
    if (-not $baselineTrxPath.StartsWith(
            $receiptPrefix, [StringComparison]::Ordinal) -or
        -not [IO.File]::Exists($baselineTrxPath)) {
        throw "Mutation '$($mutation.name)' has an invalid baseline TRX receipt."
    }
    [void](Read-SharpProofMutationTestEvidence `
            -TrxPath $baselineTrxPath `
            -EvidenceName ([string]$mutation.name + ' baseline') `
            -Mode Baseline `
            -ProcessExitCode 0 `
            -ExpectedMethodName $test.Substring($filterPrefix.Length) `
            -ExpectedLedger @($mutation.baselineSelectedTests))
    $trxPath = [IO.Path]::GetFullPath(
        (Join-Path $evidenceDirectory ([string]$mutation.trx)))
    $testEvidence = Read-SharpProofMutationTestEvidence `
        -TrxPath $trxPath `
        -EvidenceName ([string]$mutation.name) `
        -Mode Mutation `
        -ProcessExitCode ([int]$mutation.exitCode) `
        -ExpectedMethodName $test.Substring($filterPrefix.Length) `
        -ExpectedLedger @($mutation.selectedTests)
    if ($testEvidence.executedCount -ne [int]$mutation.executedCount -or
        $testEvidence.failedCount -ne [int]$mutation.failedCount -or
        $testEvidence.assertionFailureCount -ne [int]$mutation.assertionFailureCount -or
        [string]::Join("`n", @($testEvidence.testLedger)) -ne
            [string]::Join("`n", @($mutation.selectedTests))) {
        throw "Mutation '$($mutation.name)' does not match its TRX receipt."
    }
}

Write-Host (
    "Validated $expectedCatalogCount trusted-boundary mutations for " +
    "$($evidence.commit).")
