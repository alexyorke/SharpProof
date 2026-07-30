[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ReleaseCommit,

    [Parameter()]
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $resolvedRepositoryRoot = (
        Resolve-Path (Join-Path $PSScriptRoot '..')
    ).Path
}
else {
    $resolvedRepositoryRoot = (
        Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop
    ).Path
}
$workTreeOutput = @(
    & git `
        -C $resolvedRepositoryRoot `
        rev-parse `
        --is-inside-work-tree 2>&1
)
if ($LASTEXITCODE -ne 0 -or
    $workTreeOutput.Count -ne 1 -or
    [string]$workTreeOutput[0] -ne 'true') {
    throw "RepositoryRoot is not a Git repository: $resolvedRepositoryRoot"
}

# Each release is qualified against the complete delta from its approved
# predecessor. The first preview is anchored to the immutable pre-hardening
# commit; later releases are anchored to the preceding protected release tag.
$baselineReferences = @{
    'v1.0.0-preview.1' =
        '8347a70187a63cc7302b35e747d484747a929f6c'
    'v1.0.0-preview.2' = 'v1.0.0-preview.1'
    'v1.0.0-rc.1' = 'v1.0.0-preview.2'
    'v1.0.0' = 'v1.0.0-rc.1'
}
if (-not $baselineReferences.ContainsKey($Tag)) {
    throw "Release tag '$Tag' is not allowlisted."
}

function Resolve-Commit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Reference
    )

    $output = @(
        & git `
            -C $resolvedRepositoryRoot `
            rev-parse `
            --verify `
            "$Reference^{commit}" 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Could not resolve release coverage reference '$Reference': " +
            ($output -join [Environment]::NewLine))
    }
    $commit = [string]$output[-1]
    if ($commit -notmatch '^[0-9a-f]{40}$') {
        throw (
            "Release coverage reference '$Reference' did not resolve to " +
            'an exact commit SHA.')
    }
    return $commit
}

$resolvedReleaseCommit = Resolve-Commit -Reference $ReleaseCommit
if ($resolvedReleaseCommit -ne $ReleaseCommit.ToLowerInvariant()) {
    throw 'ReleaseCommit did not resolve to the supplied exact commit SHA.'
}
$baselineReference = [string]$baselineReferences[$Tag]
$baselineCommit = Resolve-Commit -Reference $baselineReference
if ($baselineCommit -eq $resolvedReleaseCommit) {
    throw 'The release coverage baseline must precede the release commit.'
}

$ancestryOutput = @(
    & git `
        -C $resolvedRepositoryRoot `
        merge-base `
        --is-ancestor `
        $baselineCommit `
        $resolvedReleaseCommit 2>&1
)
if ($LASTEXITCODE -ne 0) {
    $detail = if ($ancestryOutput.Count -eq 0) {
        ''
    }
    else {
        ': ' + ($ancestryOutput -join [Environment]::NewLine)
    }
    throw (
        "Coverage baseline '$baselineReference' ($baselineCommit) is not " +
        "an ancestor of release commit '$resolvedReleaseCommit'$detail")
}

$checkedOutCommit = Resolve-Commit -Reference 'HEAD'
if ($resolvedReleaseCommit -ne $checkedOutCommit) {
    throw (
        "ReleaseCommit '$resolvedReleaseCommit' does not identify the " +
        "checked-out HEAD '$checkedOutCommit'.")
}

$selection = [pscustomobject][ordered]@{
    schemaVersion = 1
    tag = $Tag
    baselineReference = $baselineReference
    coverageBaselineCommit = $baselineCommit
    releaseCommit = $resolvedReleaseCommit
}
($selection | ConvertTo-Json -Compress) -replace "`r`n", "`n"
