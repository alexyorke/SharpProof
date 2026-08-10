[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'ValidateTag',
        'ResolveCoverageBaseline',
        'WriteQualificationEvidence',
        'Publish')]
    [string]$Mode,

    [string]$PackageSource = 'nupkgs'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Release operations must run in the canonical Linux container.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot

function Get-ReleaseVersion {
    [xml]$release = Get-Content -LiteralPath (
        Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw
    $prefix = [string]$release.Project.PropertyGroup.SharpProofVersionPrefix
    $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).
        Replace('$(SharpProofVersionPrefix)', $prefix)
    if ($version -notmatch
        '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Invalid SharpProof package version '$version'."
    }
    return $version
}

function Require-Environment([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "The $Name environment variable is required."
    }
    return $value
}

function Resolve-RepositoryPath([string]$Path) {
    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $repositoryRoot $Path
    }
    $resolved = [IO.Path]::GetFullPath($candidate)
    $prefix = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::Ordinal)) {
        throw "Path must be inside the repository: $resolved"
    }
    return $resolved
}

switch ($Mode) {
    'ValidateTag' {
        $version = Get-ReleaseVersion
        $ref = Require-Environment 'GITHUB_REF'
        $refName = Require-Environment 'GITHUB_REF_NAME'
        $commit = Require-Environment 'GITHUB_SHA'
        if ($ref.StartsWith('refs/tags/v', [StringComparison]::Ordinal)) {
            if ($refName -cne "v$version") {
                throw "Release tag '$refName' does not match '$version'."
            }
            $tagRef = "refs/tags/$refName"
            if ((& git cat-file -t $tagRef).Trim() -cne 'tag') {
                throw 'Release tag must be annotated.'
            }
            if ((& git rev-parse "${tagRef}^{commit}").Trim() -cne $commit) {
                throw 'Release tag does not identify the checked-out commit.'
            }
            & git merge-base --is-ancestor $commit origin/master
            if ($LASTEXITCODE -ne 0) {
                throw 'Release tags must identify a commit in origin/master.'
            }
        }
        Write-Host "Release identity is valid for $version at $commit."
    }
    'ResolveCoverageBaseline' {
        $tag = Require-Environment 'GITHUB_REF_NAME'
        $commit = Require-Environment 'GITHUB_SHA'
        $selection = & (Join-Path `
            $repositoryRoot 'scripts/Resolve-SharpProofReleaseCoverageBaseline.ps1') `
            -Tag $tag -ReleaseCommit $commit | ConvertFrom-Json
        $directory = Join-Path $repositoryRoot 'artifacts/release-qualification'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $output = Join-Path $directory 'coverage.env'
        [IO.File]::WriteAllText(
            $output,
            "SHARPPROOF_COVERAGE_COMPARISON_REF=$($selection.coverageBaselineCommit)`n",
            [Text.UTF8Encoding]::new($false))
        Write-Host "Coverage baseline evidence: $output"
    }
    'WriteQualificationEvidence' {
        $commit = Require-Environment 'GITHUB_SHA'
        $tag = Require-Environment 'GITHUB_REF_NAME'
        $packageRoot = Resolve-RepositoryPath $PackageSource
        $inputPaths = @(
            'eng/container/Dockerfile',
            'eng/container/toolchain.json',
            'compose.yaml') | ForEach-Object {
                Join-Path $repositoryRoot $_
            }
        $packages = @(Get-ChildItem -LiteralPath $packageRoot -File |
            Where-Object Name -Match '\.(?:nupkg|snupkg)$')
        if ($packages.Count -eq 0) {
            throw "No package artifacts were found in $packageRoot."
        }
        $files = @($inputPaths) + @($packages.FullName)
        $record = [ordered]@{
            schemaVersion = 1
            status = 'passed'
            repositoryCommit = $commit
            tag = $tag
            inputs = @($files | ForEach-Object {
                [ordered]@{
                    path = [IO.Path]::GetRelativePath(
                        $repositoryRoot, $_).Replace('\', '/')
                    sha256 = (Get-FileHash `
                        -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            })
        }
        $directory = Join-Path $repositoryRoot 'artifacts/release-qualification'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $directory 'qualification.json'),
            (($record | ConvertTo-Json -Depth 5) + "`n"),
            [Text.UTF8Encoding]::new($false))
    }
    'Publish' {
        $tag = Require-Environment 'GITHUB_REF_NAME'
        $source = Require-Environment 'NUGET_SOURCE'
        $apiKey = Require-Environment 'NUGET_API_KEY'
        $packageRoot = Resolve-RepositoryPath $PackageSource
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofReleaseArtifacts.ps1') `
            -PackageSource $packageRoot -ExpectedTag $tag
        if ($LASTEXITCODE -ne 0) {
            throw 'Release artifact validation failed before publication.'
        }
        $arguments = @{
            PackageSource = $packageRoot
            Source = $source
            ApiKey = $apiKey
        }
        if (-not [string]::IsNullOrWhiteSpace($env:NUGET_READ_API_KEY)) {
            $arguments.ReadApiKey = $env:NUGET_READ_API_KEY
        }
        & (Join-Path $repositoryRoot 'scripts/Publish-SharpProofRelease.ps1') `
            @arguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Release publication failed.'
        }
    }
}
