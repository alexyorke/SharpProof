[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('running', 'failed', 'passed')]
    [string]$Status,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ReleaseCommit,

    [Parameter()]
    [string]$CoverageBaselineCommit,

    [Parameter()]
    [string]$HumanEvidencePath,

    [Parameter()]
    [string]$FailureKind,

    [Parameter()]
    [string]$OutputPath =
        'artifacts/release-qualification/qualification.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$isFinal = $Tag -eq 'v1.0.0'
if ($Status -eq 'passed') {
    if ($CoverageBaselineCommit -notmatch '^[0-9a-f]{40}$' -or
        $CoverageBaselineCommit -eq $ReleaseCommit) {
        throw (
            'Passed qualification requires a distinct lowercase coverage ' +
            'baseline commit SHA.')
    }
    if ($isFinal -and
        [string]::IsNullOrWhiteSpace($HumanEvidencePath)) {
        throw 'Final 1.0 passed qualification requires human evidence.'
    }
}
elseif ($Status -eq 'failed') {
    if ([string]::IsNullOrWhiteSpace($FailureKind) -or
        $FailureKind -match '[\r\n]') {
        throw 'Failed qualification requires a single-line FailureKind.'
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($FailureKind)) {
    throw 'FailureKind is valid only for failed qualification.'
}
if (-not [string]::IsNullOrWhiteSpace($CoverageBaselineCommit) -and
    $CoverageBaselineCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'CoverageBaselineCommit must be a lowercase Git commit SHA.'
}

$humanEvidence = $null
if ($isFinal -and
    -not [string]::IsNullOrWhiteSpace($HumanEvidencePath)) {
    $resolvedHumanEvidence = if (
        [IO.Path]::IsPathRooted($HumanEvidencePath)) {
        [IO.Path]::GetFullPath($HumanEvidencePath)
    }
    else {
        [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $HumanEvidencePath))
    }
    if (-not (Test-Path `
            -LiteralPath $resolvedHumanEvidence `
            -PathType Leaf)) {
        if ($Status -eq 'passed') {
            throw "Human evidence validation is missing: $HumanEvidencePath"
        }
    }
    else {
        $humanEvidence = Get-Content `
            -LiteralPath $resolvedHumanEvidence `
            -Raw |
            ConvertFrom-Json
        if ([int]$humanEvidence.schemaVersion -ne 1 -or
            [string]$humanEvidence.status -ne 'passed' -or
            [string]$humanEvidence.releaseTag -ne $Tag -or
            [string]$humanEvidence.productCommit -ne $ReleaseCommit -or
            [string]$humanEvidence.evidenceRef -ne
                'refs/tags/evidence/v1.0.0' -or
            [string]$humanEvidence.evidenceTagObject -notmatch
                '^[0-9a-f]{40}$' -or
            [string]$humanEvidence.evidenceCommit -notmatch
                '^[0-9a-f]{40}$' -or
            [string]$humanEvidence.evidenceDocumentSha256 -notmatch
                '^[0-9a-f]{64}$') {
            throw (
                'Human evidence validation does not bind the exact final ' +
                'product commit and immutable evidence tag.')
        }
    }
}

$gateStatus = switch ($Status) {
    'running' {
        'pending'
    }
    'failed' {
        'incomplete'
    }
    default {
        'passed'
    }
}
$humanStatus = if (-not $isFinal) {
    'not-required'
}
elseif ($null -ne $humanEvidence) {
    'passed'
}
elseif ($Status -eq 'running') {
    'pending'
}
else {
    'incomplete'
}

$qualification = [pscustomobject][ordered]@{
    schemaVersion = 3
    status = $Status
    failureKind = if ($Status -eq 'failed') {
        $FailureKind
    }
    else {
        $null
    }
    tag = $Tag
    releaseCommit = $ReleaseCommit
    coverageBaselineCommit = if (
        [string]::IsNullOrWhiteSpace($CoverageBaselineCommit)) {
        $null
    }
    else {
        $CoverageBaselineCommit
    }
    humanEvidence = [pscustomobject][ordered]@{
        status = $humanStatus
        ref = if ($null -eq $humanEvidence) {
            $null
        }
        else {
            [string]$humanEvidence.evidenceRef
        }
        tagObject = if ($null -eq $humanEvidence) {
            $null
        }
        else {
            [string]$humanEvidence.evidenceTagObject
        }
        commit = if ($null -eq $humanEvidence) {
            $null
        }
        else {
            [string]$humanEvidence.evidenceCommit
        }
        documentSha256 = if ($null -eq $humanEvidence) {
            $null
        }
        else {
            [string]$humanEvidence.evidenceDocumentSha256
        }
    }
    gates = [pscustomobject][ordered]@{
        acceptance = $gateStatus
        corpus = $gateStatus
        performance = $gateStatus
        fuzz = $gateStatus
        mutations = $gateStatus
        coverage = $gateStatus
        dependencyAudit = $gateStatus
    }
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "OutputPath has no parent directory: '$OutputPath'."
}
[IO.Directory]::CreateDirectory($outputDirectory) |
    Out-Null
$json = ($qualification | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))

Write-Host (
    "Recorded release qualification status '$Status' for $Tag at " +
    "$ReleaseCommit.")
