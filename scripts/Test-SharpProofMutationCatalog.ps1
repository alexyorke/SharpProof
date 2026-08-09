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

if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw "ExpectedCommit must be a 40-character commit SHA: '$ExpectedCommit'."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
$contract = Get-Content -LiteralPath $contractPath -Raw |
    ConvertFrom-Json
$policy = $contract.mutationEvidence
$expectedCatalogCount = [int]$policy.expectedCatalogCount
$expectedCatalogSha256 = [string]$policy.expectedCatalogSha256

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
    [string]$evidence.catalogSha256 -ne $expectedCatalogSha256 -or
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

$actualCatalogSha256 = Get-SharpProofMutationCatalogSha256 `
    -Mutations $mutations
if ($actualCatalogSha256 -ne $expectedCatalogSha256) {
    throw 'Mutation evidence catalog descriptors do not match the policy.'
}
$names = @($mutations | ForEach-Object { [string]$_.name })
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
}

Write-Host (
    "Validated $expectedCatalogCount trusted-boundary mutations for " +
    "$($evidence.commit).")
