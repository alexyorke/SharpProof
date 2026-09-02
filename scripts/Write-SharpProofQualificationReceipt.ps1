[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('acceptance-debug', 'acceptance-release', 'coverage', 'mutation',
        'package-consumers', 'pilots', 'release-configuration',
        'portable-linux', 'portable-windows', 'portable-macos')]
    [string]$Gate,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$ReceiptDirectory = 'artifacts/release-qualification/qualification-receipts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$resolvedEvidence = (Resolve-Path -LiteralPath $EvidencePath).Path
$relativeEvidence = [IO.Path]::GetRelativePath(
    $repositoryRoot,
    $resolvedEvidence).Replace('\', '/')
if ($relativeEvidence.StartsWith('../', [StringComparison]::Ordinal) -or
    [IO.Path]::IsPathRooted($relativeEvidence)) {
    throw 'Qualification gate evidence must remain inside the repository.'
}
$evidence = Get-Content -LiteralPath $resolvedEvidence -Raw |
    ConvertFrom-Json -ErrorAction Stop
$packageArtifacts = @()
if ($Gate -in @(
        'package-consumers', 'pilots', 'portable-linux',
        'portable-windows', 'portable-macos')) {
    $packageArtifacts = @($evidence.packageArtifacts | ForEach-Object {
        $fileName = [string]$_.fileName
        $bytes = [int64]$_.bytes
        if ([IO.Path]::GetFileName($fileName) -cne $fileName -or
            $fileName -notmatch '\.(?:nupkg|snupkg)$' -or
            $bytes -le 0) {
            throw "Qualification package evidence is malformed: '$fileName'."
        }
        [ordered]@{
            fileName = $fileName
            bytes = $bytes
        }
    } | Sort-Object fileName)
    if ($packageArtifacts.Count -ne 6 -or
        @($packageArtifacts.fileName | Group-Object).Count -ne 6) {
        throw 'Qualification evidence must bind exactly six unique package artifacts.'
    }
}
$valid = switch -Regex ($Gate) {
    '^acceptance-(?:debug|release)$' {
        [int]$evidence.schemaVersion -eq 1 -and
        [string]$evidence.command -ceq 'acceptance' -and
        [string]$evidence.configuration -ceq $Gate.Substring(11) -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit
    }
    '^portable-(?:linux|windows|macos)$' {
        [int]$evidence.schemaVersion -eq 1 -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit -and
        [string]$evidence.osFamily -ceq $Gate.Substring(9) -and
        $packageArtifacts.Count -eq 6
    }
    '^release-configuration$' {
        [int]$evidence.schemaVersion -eq 1 -and
        [string]$evidence.commit -ceq $commit
    }
    'coverage' {
        [int]$evidence.schemaVersion -eq 1 -and
        [bool]$evidence.passed -and
        [string]$evidence.commit -ceq $commit
    }
    'mutation' {
        [int]$evidence.schemaVersion -eq 2 -and
        [string]$evidence.selection -ceq 'full' -and
        [string]$evidence.commit -ceq $commit -and
        [int]$evidence.mutationCount -gt 0 -and
        [int]$evidence.mutationCount -eq [int]$evidence.killedCount
    }
    'package-consumers' {
        [int]$evidence.schemaVersion -eq 1 -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit -and
        $packageArtifacts.Count -eq 6
    }
    'pilots' {
        [string]$evidence.reviewStatus -ceq 'Reviewed' -and
        (Test-SharpProofPilotReport -Report $evidence -ExpectedCommit $commit `
            -RepositoryRoot $repositoryRoot) -and
        $packageArtifacts.Count -eq 6
    }
}
if (-not $valid) {
    throw "Qualification evidence is incomplete, stale, or failed: '$Gate'."
}
$receiptCandidate = if ([IO.Path]::IsPathRooted($ReceiptDirectory)) {
    $ReceiptDirectory
}
else {
    Join-Path $repositoryRoot $ReceiptDirectory
}
$receiptDirectory = [IO.Path]::GetFullPath($receiptCandidate)
if (-not $receiptDirectory.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) {
    throw 'ReceiptDirectory must remain inside the repository.'
}
[IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
$receipt = [ordered]@{
    schemaVersion = 1
    gate = $Gate
    status = 'passed'
    commit = $commit
    evidence = [ordered]@{
        path = $relativeEvidence
        bytes = [int64](Get-Item -LiteralPath $resolvedEvidence).Length
    }
}
if ($packageArtifacts.Count -ne 0) {
    $receipt.packageArtifacts = $packageArtifacts
}
if ($Gate -eq 'pilots') {
    $receipt.pilotEvidence = @($evidence.pilots | Sort-Object id | ForEach-Object {
            [ordered]@{ id = [string]$_.id; evidence = @($_.evidence) }
        })
}
[IO.File]::WriteAllText(
    (Join-Path $receiptDirectory "$Gate.json"),
    (($receipt | ConvertTo-Json -Depth 5) + "`n"),
    [Text.UTF8Encoding]::new($false))
