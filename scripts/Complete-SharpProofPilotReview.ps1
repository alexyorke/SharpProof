[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceReportPath,
    [Parameter(Mandatory = $true)][string]$ReviewLedgerPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$CatalogPath = (Join-Path $PSScriptRoot '..\eng\pilots\catalog.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

$sourcePath = Resolve-SharpProofContainedPath -Root $repositoryRoot `
    -Path $SourceReportPath -ParameterName 'SourceReportPath'
$ledgerPath = Resolve-SharpProofContainedPath -Root $repositoryRoot `
    -Path $ReviewLedgerPath -ParameterName 'ReviewLedgerPath'
$resolvedOutput = Resolve-SharpProofContainedPath -Root $repositoryRoot `
    -Path $OutputPath -ParameterName 'OutputPath'
$sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
$ledgerBytes = [IO.File]::ReadAllBytes($ledgerPath)
$source = [Text.Encoding]::UTF8.GetString($sourceBytes) | ConvertFrom-Json
$ledger = [Text.Encoding]::UTF8.GetString($ledgerBytes) | ConvertFrom-Json
if (-not (Test-SharpProofPilotReport -Report $source `
        -ExpectedCommit ([string]$source.commit) -RepositoryRoot $repositoryRoot `
        -CatalogPath $CatalogPath) -or
    [string]$source.reviewStatus -cne 'Unreviewed') {
    throw 'The source pilot report is not valid unreviewed evidence.'
}

$reviewSummary = Get-SharpProofPilotReviewLedgerSummary `
    -Report $source -Ledger $ledger
$ledgerSha256 = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::HashData($ledgerBytes)).Replace('-', '').ToLowerInvariant()

$source.reviewStatus = 'Reviewed'
$source | Add-Member -NotePropertyName reviewLedgerSha256 `
    -NotePropertyValue $ledgerSha256 -Force
foreach ($pilot in @($source.pilots)) {
    $pilot.falsePositiveReports = [int]($reviewSummary.falsePositiveCounts[[string]$pilot.id] ?? 0)
}
if (-not (Test-SharpProofPilotReport -Report $source `
        -ExpectedCommit ([string]$source.commit) -RepositoryRoot $repositoryRoot `
        -CatalogPath $CatalogPath)) {
    throw 'The reviewed pilot report failed validation.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($source | ConvertTo-Json -Depth 20) + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "Reviewed five pilot reports for $($source.commit)."
