[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedCommit,

    [Parameter()]
    [string]$EvidencePath = 'eng\release\human-gates.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedEvidence = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $EvidencePath))
if (-not $resolvedEvidence.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "EvidencePath must be inside the repository: $resolvedEvidence"
}
if (-not (Test-Path -LiteralPath $resolvedEvidence -PathType Leaf)) {
    throw (
        "Final 1.0 release evidence is missing: $EvidencePath. " +
        'Copy eng/release/human-gates.template.json, replace every ' +
        'placeholder with owner-reviewed evidence, and commit it.')
}

$evidence = Get-Content -LiteralPath $resolvedEvidence -Raw |
    ConvertFrom-Json
if ([int]$evidence.schemaVersion -ne 1 -or
    [string]$evidence.releaseTag -ne 'v1.0.0' -or
    [string]$evidence.commit -ne $ExpectedCommit) {
    throw 'Human release evidence does not identify the exact v1.0.0 commit.'
}

function Assert-EvidenceUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $uri = $null
    if (-not [Uri]::TryCreate(
            $Value,
            [UriKind]::Absolute,
            [ref]$uri) -or
        $uri.Scheme -ne 'https' -or
        $uri.Host.EndsWith(
            '.invalid',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "$Owner must contain an absolute HTTPS evidence URL and cannot " +
            'use a reserved placeholder domain.')
    }
}

$pilots = @($evidence.pilots)
if ($pilots.Count -lt 2) {
    throw 'Final 1.0 requires evidence from at least two pilot libraries.'
}
$totalClaims = 0
$pilotIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($pilot in $pilots) {
    $pilotId = [string]$pilot.id
    if ([string]::IsNullOrWhiteSpace($pilotId) -or
        -not $pilotIds.Add($pilotId)) {
        throw 'Pilot IDs must be nonblank and unique.'
    }
    $selectedClaims = [int]$pilot.selectedClaims
    if ($selectedClaims -le 0) {
        throw "Pilot '$pilotId' must report selected claims."
    }
    $totalClaims += $selectedClaims
    $cycles = @($pilot.weeklyCycles)
    if ($cycles.Count -lt 4) {
        throw "Pilot '$pilotId' must have at least four weekly cycles."
    }
    $previousDate = $null
    foreach ($cycle in $cycles) {
        $date = [DateTime]::MinValue
        if (-not [DateTime]::TryParseExact(
                [string]$cycle.weekEnding,
                'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref]$date)) {
            throw "Pilot '$pilotId' contains an invalid weekEnding date."
        }
        if ($null -ne $previousDate -and
            ($date - $previousDate).TotalDays -ne 7) {
            throw "Pilot '$pilotId' weekly cycles must be consecutive."
        }
        $previousDate = $date
        if ([string]$cycle.status -ne 'passed' -or
            [int]$cycle.selectedClaims -ne $selectedClaims -or
            [int]$cycle.unwaivedUnknown -ne 0) {
            throw (
                "Pilot '$pilotId' must pass every cycle with the same " +
                'selected-claim count and zero unwaived Unknown outcomes.')
        }
        Assert-EvidenceUrl `
            -Value ([string]$cycle.evidenceUrl) `
            -Owner "Pilot '$pilotId' cycle $($cycle.weekEnding)"
    }
}
if ($totalClaims -lt 100) {
    throw "Pilot evidence contains only $totalClaims selected claims; 100 are required."
}

if ([int]$evidence.openDefects.p0 -ne 0 -or
    [int]$evidence.openDefects.p1 -ne 0) {
    throw 'Final 1.0 cannot have an open P0 or P1 defect.'
}
Assert-EvidenceUrl `
    -Value ([string]$evidence.openDefects.evidenceUrl) `
    -Owner 'Open-defect evidence'

$reviews = @($evidence.soundnessReviews)
if ($reviews.Count -lt 2) {
    throw 'Final 1.0 requires at least two independent soundness reviews.'
}
$reviewers = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($review in $reviews) {
    $reviewer = [string]$review.reviewer
    if ([string]::IsNullOrWhiteSpace($reviewer) -or
        -not $reviewers.Add($reviewer) -or
        $review.independent -isnot [bool] -or
        -not [bool]$review.independent -or
        [string]$review.commit -ne $ExpectedCommit -or
        [string]$review.disposition -ne 'approved') {
        throw (
            'Every soundness review must have a distinct reviewer, be ' +
            'independent, approve the exact release commit, and be approved.')
    }
    Assert-EvidenceUrl `
        -Value ([string]$review.evidenceUrl) `
        -Owner "Soundness review by '$reviewer'"
}

$governance = $evidence.governance
foreach ($property in @(
        'protectedDefaultBranch',
        'protectedReleaseTags',
        'protectedPublishingEnvironments',
        'requiredChecks',
        'independentReviewRequired')) {
    if ($null -eq $governance.PSObject.Properties[$property] -or
        $governance.$property -isnot [bool] -or
        -not [bool]$governance.$property) {
        throw "Governance evidence '$property' must be true."
    }
}
Assert-EvidenceUrl `
    -Value ([string]$governance.evidenceUrl) `
    -Owner 'Governance evidence'

Write-Host (
    "Validated final 1.0 human gates for $($pilots.Count) pilots, " +
    "$totalClaims selected claims, and $($reviews.Count) independent reviews.")
