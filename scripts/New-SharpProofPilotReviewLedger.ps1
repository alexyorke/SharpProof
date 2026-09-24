[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceReportPath,
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
$resolvedOutput = Resolve-SharpProofContainedPath -Root $repositoryRoot `
    -Path $OutputPath -ParameterName 'OutputPath'
$sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
$source = [Text.UTF8Encoding]::new($false, $true).GetString($sourceBytes) |
    ConvertFrom-Json -ErrorAction Stop
if ([string]$source.reviewStatus -cne 'Unreviewed' -or
    -not (Test-SharpProofPilotReport -Report $source `
        -ExpectedCommit ([string]$source.commit) -RepositoryRoot $repositoryRoot `
        -CatalogPath $CatalogPath)) {
    throw 'The source pilot report is not valid unreviewed evidence.'
}

$reviews = [Collections.Generic.List[object]]::new()
foreach ($pilot in @($source.pilots)) {
    foreach ($claim in @($pilot.claimEvidence | Where-Object { $null -ne $_ })) {
        $reviews.Add([ordered]@{
            pilotId = [string]$pilot.id
            kind = 'Claim'
            id = [string]$claim.claimId
            disposition = ''
        })
    }
    foreach ($diagnostic in @($pilot.diagnostics | Where-Object { $null -ne $_ })) {
        $reviews.Add([ordered]@{
            pilotId = [string]$pilot.id
            kind = 'Diagnostic'
            id = [string]$diagnostic.id
            disposition = ''
        })
    }
}

$ledger = [ordered]@{
    schemaVersion = 2
    commit = [string]$source.commit
    packageArtifacts = @($source.packageArtifacts)
    reviews = @($reviews.ToArray())
}
$directory = [IO.Path]::GetDirectoryName($resolvedOutput)
[IO.Directory]::CreateDirectory($directory) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($ledger | ConvertTo-Json -Depth 20) + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "Created a blank review ledger for $($reviews.Count) pilot findings."
