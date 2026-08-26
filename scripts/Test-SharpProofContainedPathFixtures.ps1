[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-contained-path-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $fixture 'Repo'
[IO.Directory]::CreateDirectory((Join-Path $root 'artifacts')) | Out-Null

function Require-Rejection([string]$Path, [string]$Name) {
    $rejected = $false
    try {
        Resolve-SharpProofContainedPath -Root $root -Path $Path -ParameterName $Name | Out-Null
    }
    catch [System.Management.Automation.RuntimeException] {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Contained-path fixture '$Name' was accepted."
    }
}

try {
    $exact = Resolve-SharpProofContainedPath -Root $root `
        -Path 'artifacts/report.json' -ParameterName exact
    if ($exact -cne (Join-Path $root 'artifacts/report.json')) {
        throw 'Exact contained child did not retain canonical identity.'
    }
    $canonical = Resolve-SharpProofContainedPath -Root $root `
        -Path 'artifacts/../artifacts/report.json' -ParameterName canonical
    if ($canonical -cne $exact) { throw 'Contained traversal did not canonicalize.' }
    $absolute = Resolve-SharpProofContainedPath -Root $root `
        -Path $exact -ParameterName absolute
    if ($absolute -cne $exact) { throw 'Absolute contained child was rejected.' }
    Require-Rejection $root root-equality
    $caseVariant = Join-Path $fixture 'repo/out.json'
    if ([IO.Path]::DirectorySeparatorChar -eq [char]'\') {
        $caseResolved = Resolve-SharpProofContainedPath -Root $root `
            -Path $caseVariant -ParameterName case-variant-child
        if (-not [string]::Equals(
                $caseResolved,
                $caseVariant,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Windows case-variant child did not retain filesystem identity.'
        }
    }
    else {
        Require-Rejection $caseVariant case-distinct-sibling
    }
    Require-Rejection (Join-Path $fixture 'RepoSibling/out.json') prefix-sibling
    Require-Rejection '../outside.json' traversal-escape

    $insideTarget = Join-Path $root 'artifacts/linked-target'
    $outsideTarget = Join-Path $fixture 'Outside'
    [IO.Directory]::CreateDirectory($insideTarget) | Out-Null
    [IO.Directory]::CreateDirectory($outsideTarget) | Out-Null
    $insideLink = Join-Path $root 'inside-link'
    $outsideLink = Join-Path $root 'outside-link'
    [IO.Directory]::CreateSymbolicLink($insideLink, $insideTarget) | Out-Null
    [IO.Directory]::CreateSymbolicLink($outsideLink, $outsideTarget) | Out-Null
    $linkedInside = Resolve-SharpProofContainedPath -Root $root `
        -Path 'inside-link/report.json' -ParameterName inside-link
    $expectedLinkedInside = Join-Path $insideTarget 'report.json'
    if ($linkedInside -cne $expectedLinkedInside) {
        throw 'Contained symbolic link did not resolve to its physical target.'
    }
    Require-Rejection 'outside-link/report.json' symbolic-link-escape

    $consumers = @(
        'eng/acceptance/Verify.ps1',
        'scripts/Generate-DiagnosticDescriptors.ps1',
        'scripts/Generate-ProjectionCatalog.ps1',
        'scripts/Generate-Readme.ps1',
        'scripts/Invoke-SharpProofCoverage.ps1',
        'scripts/Invoke-SharpProofGateEvidence.ps1',
        'scripts/Invoke-SharpProofFuzzCampaign.ps1',
        'scripts/Test-CompilerArtifactModelGenerator.ps1',
        'scripts/Test-SharpProofPilots.ps1',
        'scripts/Test-SharpProofReleaseConfiguration.ps1',
        'scripts/Test-SharpProofSamples.ps1',
        'scripts/Test-SharpProofTrustedMutations.ps1')
    foreach ($consumer in $consumers) {
        $text = [IO.File]::ReadAllText((Join-Path $repositoryRoot $consumer))
        if (-not $text.Contains('Resolve-SharpProofContainedPath', [StringComparison]::Ordinal)) {
            throw "Containment consumer does not use shared authority: '$consumer'."
        }
    }
    Write-Host 'Repository-contained path fixtures passed.'
}
finally { if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force } }
