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

function Get-Property($Value, [string]$Name) {
    if ($null -eq $Value) { return $null }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Require-ExactProperties($Value, [string[]]$Names, [string]$Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $expected = @($Names | Sort-Object)
    if (($actual -join '|') -cne ($expected -join '|')) {
        throw "$Label has an invalid property set."
    }
}

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

Require-ExactProperties $ledger `
    @('schemaVersion','commit','packageArtifacts','reviews') `
    'Review ledger'
if ([int](Get-Property $ledger 'schemaVersion') -ne 1 -or
    [string](Get-Property $ledger 'commit') -cne [string]$source.commit) {
    throw 'The review ledger is stale or has the wrong identity.'
}

$sourcePackages = @($source.packageArtifacts | Sort-Object fileName)
$ledgerPackages = @((Get-Property $ledger 'packageArtifacts') | Sort-Object fileName)
if ($sourcePackages.Count -ne 6 -or $ledgerPackages.Count -ne 6) {
    throw 'The review ledger must bind the exact six packages.'
}
for ($index = 0; $index -lt 6; $index++) {
    foreach ($name in @('fileName','packageId','version','repositoryCommit','bytes')) {
        if ([string](Get-Property $sourcePackages[$index] $name) -cne
            [string](Get-Property $ledgerPackages[$index] $name)) {
            throw 'The review ledger package identities do not match the source report.'
        }
    }
}

$expected = @{}
foreach ($pilot in @($source.pilots)) {
    $pilotId = [string]$pilot.id
    foreach ($claim in @($pilot.claimEvidence | Where-Object { $null -ne $_ })) {
        $key = "$pilotId|Claim|$([string]$claim.claimId)"
        if ($expected.ContainsKey($key)) { throw 'Duplicate source claim identity.' }
        $expected[$key] = $pilotId
    }
    $diagnostics = @((Get-Property $pilot 'diagnostics') |
        Where-Object { $null -ne $_ })
    foreach ($diagnostic in $diagnostics) {
        $id = [string](Get-Property $diagnostic 'id')
        if ($id -cnotmatch '^SP[0-9]{4}$') { throw 'Invalid source diagnostic identity.' }
        $key = "$pilotId|Diagnostic|$id"
        if ($expected.ContainsKey($key)) { throw 'Duplicate source diagnostic identity.' }
        $expected[$key] = $pilotId
    }
}

$reviews = @((Get-Property $ledger 'reviews') | Where-Object { $null -ne $_ })
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$falsePositives = @{}
foreach ($review in $reviews) {
    Require-ExactProperties $review @('pilotId','kind','id','disposition') 'Review row'
    $pilotId = [string](Get-Property $review 'pilotId')
    $kind = [string](Get-Property $review 'kind')
    $id = [string](Get-Property $review 'id')
    $disposition = [string](Get-Property $review 'disposition')
    $key = "$pilotId|$kind|$id"
    if (-not $expected.ContainsKey($key) -or -not $seen.Add($key) -or
        @('TruePositive','FalsePositive') -cnotcontains $disposition) {
        throw 'The review ledger contains an unknown, duplicate, or contradictory row.'
    }
    if ($disposition -ceq 'FalsePositive') {
        $falsePositives[$pilotId] = 1 + [int]($falsePositives[$pilotId] ?? 0)
    }
}
if ($seen.Count -ne $expected.Count) {
    throw 'The review ledger is incomplete.'
}

$reviewed = $source | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$reviewed.reviewStatus = 'Reviewed'
foreach ($pilot in @($reviewed.pilots)) {
    $pilot.falsePositiveReports = [int]($falsePositives[[string]$pilot.id] ?? 0)
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($reviewed | ConvertTo-Json -Depth 20) + "`n",
    [Text.UTF8Encoding]::new($false))
Write-Host "Reviewed five pilot reports for $($source.commit)."
