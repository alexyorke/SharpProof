[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('acceptance', 'coverage', 'mutation', 'package-consumers', 'pilots')]
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
if ($Gate -in @('package-consumers', 'pilots')) {
    $packageArtifacts = @($evidence.packageArtifacts | ForEach-Object {
        $fileName = [string]$_.fileName
        $sha256 = [string]$_.sha256
        $bytes = [int64]$_.bytes
        if ([IO.Path]::GetFileName($fileName) -cne $fileName -or
            $fileName -notmatch '\.(?:nupkg|snupkg)$' -or
            $bytes -le 0 -or
            $sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Qualification package evidence is malformed: '$fileName'."
        }
        [ordered]@{
            fileName = $fileName
            bytes = $bytes
            sha256 = $sha256
        }
    } | Sort-Object fileName)
    if ($packageArtifacts.Count -ne 6 -or
        @($packageArtifacts.fileName | Group-Object).Count -ne 6) {
        throw 'Qualification evidence must bind exactly six unique package artifacts.'
    }
}
$valid = switch ($Gate) {
    'acceptance' {
        [int]$evidence.schemaVersion -eq 1 -and
        [string]$evidence.status -ceq 'passed' -and
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
        (Test-SharpProofPilotReport -Report $evidence -ExpectedCommit $commit) -and
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
        sha256 = (Get-FileHash `
            -LiteralPath $resolvedEvidence `
            -Algorithm SHA256).Hash.ToLowerInvariant()
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
