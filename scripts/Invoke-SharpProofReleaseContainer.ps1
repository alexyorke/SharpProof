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

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer `
    'Release operations must run in the canonical Linux container.'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

function Require-Environment([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "The $Name environment variable is required."
    }
    return $value
}

function Resolve-RepositoryPath([string]$Path) {
    return Resolve-SharpProofContainedPath `
        -Root $repositoryRoot `
        -Path $Path `
        -ParameterName 'Release path'
}

function Assert-AnnotatedTagCommit {
    param(
        [Parameter(Mandatory = $true)][string]$TagRef,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit,
        [Parameter(Mandatory = $true)][string]$InvalidTagMessage,
        [Parameter(Mandatory = $true)][string]$CommitMismatchMessage
    )

    $tagType = (& git -C $repositoryRoot cat-file -t $TagRef 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagType -cne 'tag') {
        throw $InvalidTagMessage
    }
    $tagCommit = (& git -C $repositoryRoot rev-parse `
            "${TagRef}^{commit}" 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $ExpectedCommit) {
        throw $CommitMismatchMessage
    }
}

switch ($Mode) {
    'ValidateTag' {
        $version = Get-SharpProofReleaseVersion `
            -RepositoryRoot $repositoryRoot
        $ref = Require-Environment 'GITHUB_REF'
        $refName = Require-Environment 'GITHUB_REF_NAME'
        $commit = Require-Environment 'GITHUB_SHA'
        $expectedTag = "v$version"
        $expectedRef = "refs/tags/$expectedTag"
        if ($ref -cne $expectedRef) {
            throw "Release ref '$ref' does not match '$expectedRef'."
        }
        if ($refName -cne $expectedTag) {
            throw "Release tag '$refName' does not match '$version'."
        }
        $head = (& git -C $repositoryRoot rev-parse HEAD 2>$null).Trim()
        if ($LASTEXITCODE -ne 0 -or $commit -cne $head) {
            throw "Release commit '$commit' does not match checkout HEAD '$head'."
        }
        Assert-AnnotatedTagCommit `
            -TagRef $expectedRef `
            -ExpectedCommit $commit `
            -InvalidTagMessage 'Release tag must exist as an annotated tag object.' `
            -CommitMismatchMessage 'Release tag does not identify the checked-out commit.'
        & git -C $repositoryRoot merge-base --is-ancestor `
            $commit origin/master 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw 'Release tags must identify a commit in origin/master.'
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
        $head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        if ($commit -cne $head) {
            throw "Qualification commit '$commit' does not match checkout HEAD '$head'."
        }
        $trackedChanges = @(& git -C $repositoryRoot status --porcelain `
                --untracked-files=no)
        if ($LASTEXITCODE -ne 0) {
            throw 'Qualification could not inspect tracked checkout state.'
        }
        $packageRelativePath = [IO.Path]::GetRelativePath(
            $repositoryRoot,
            $packageRoot).Replace('\', '/')
        $allUntrackedChanges = @(& git -C $repositoryRoot ls-files `
                --others --exclude-standard -- .)
        if ($LASTEXITCODE -ne 0) {
            throw 'Qualification could not inspect untracked checkout state.'
        }
        $packagePrefix = $packageRelativePath.TrimEnd('/') + '/'
        $untrackedChanges = @($allUntrackedChanges | Where-Object {
                -not $_.StartsWith(
                    $packagePrefix,
                    [StringComparison]::Ordinal)
            })
        if ($trackedChanges.Count -ne 0 -or
            $untrackedChanges.Count -ne 0) {
            throw 'Qualification requires a clean checkout.'
        }
        $version = Get-SharpProofReleaseVersion `
            -RepositoryRoot $repositoryRoot
        if ($tag -cne "v$version") {
            throw "Qualification tag '$tag' does not match package version '$version'."
        }
        $tagRef = "refs/tags/$tag"
        Assert-AnnotatedTagCommit `
            -TagRef $tagRef `
            -ExpectedCommit $head `
            -InvalidTagMessage 'Qualification requires an annotated tag at checkout HEAD.' `
            -CommitMismatchMessage 'Qualification requires an annotated tag at checkout HEAD.'
        & (Join-Path $repositoryRoot `
            'scripts/Test-SharpProofReleaseArtifacts.ps1') `
            -PackageSource $packageRoot `
            -ExpectedTag $tag
        if ($LASTEXITCODE -ne 0) {
            throw 'Strict release artifact validation failed.'
        }
        $inputPaths = @(
            'eng/container/Dockerfile',
            'eng/container/toolchain.json',
            'compose.yaml') | ForEach-Object {
                Join-Path $repositoryRoot $_
            }
        $packages = @(Get-ChildItem -LiteralPath $packageRoot -File |
            Where-Object Name -Match '\.(?:nupkg|snupkg)$')
        if ($packages.Count -ne 6) {
            throw "Qualification requires exactly six package artifacts."
        }
        $packageArtifacts = @($packages |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{
                    fileName = $_.Name
                    bytes = [int64]$_.Length
                }
            })
        $packageArtifactJson = $packageArtifacts | ConvertTo-Json -Compress
        $matrixPath = Join-Path $repositoryRoot `
            'eng/acceptance/preview-evidence.v1.json'
        $matrix = Get-Content -LiteralPath $matrixPath -Raw |
            ConvertFrom-Json -ErrorAction Stop
        $requiredGates = @($matrix.releaseQualificationMatrix |
            ForEach-Object { [string]$_.receipt } |
            Select-Object -Unique)
        if ($requiredGates.Count -ne 10) {
            throw 'Release qualification matrix must project exactly ten receipts.'
        }
        $receiptDirectory = Join-Path `
            $repositoryRoot `
            'artifacts/release-qualification/qualification-receipts'
        $gateReceipts = [ordered]@{}
        foreach ($gate in $requiredGates) {
            $receiptPath = Join-Path $receiptDirectory "$gate.json"
            if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
                throw "Qualification gate receipt is missing: '$gate'."
            }
            $receipt = Get-Content -LiteralPath $receiptPath -Raw |
                ConvertFrom-Json -ErrorAction Stop
            $evidencePath = Resolve-RepositoryPath ([string]$receipt.evidence.path)
            if ([int]$receipt.schemaVersion -ne 1 -or
                [string]$receipt.gate -cne $gate -or
                [string]$receipt.status -cne 'passed' -or
                [string]$receipt.commit -cne $head -or
                -not (Test-Path -LiteralPath $evidencePath -PathType Leaf) -or
                [int64](Get-Item -LiteralPath $evidencePath).Length -ne
                    [int64]$receipt.evidence.bytes) {
                throw "Qualification gate receipt is stale or failed: '$gate'."
            }
            if ($gate -in @(
                    'package-consumers', 'pilots', 'portable-linux',
                    'portable-windows', 'portable-macos') -and
                (@($receipt.packageArtifacts) |
                    Sort-Object fileName |
                    ConvertTo-Json -Compress) -cne $packageArtifactJson) {
                throw "Qualification gate receipt targets different packages: '$gate'."
            }
            $gateReceipts[$gate] = [string]$receipt.status
        }
        $files = @($inputPaths) + @($matrixPath) + @($packages.FullName)
        $record = [ordered]@{
            schemaVersion = 2
            status = 'passed'
            releaseCommit = $commit
            tag = $tag
            gateReceipts = $gateReceipts
            inputs = @($files | ForEach-Object {
                [ordered]@{
                    path = [IO.Path]::GetRelativePath(
                        $repositoryRoot, $_).Replace('\', '/')
                    bytes = [int64](Get-Item -LiteralPath $_).Length
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
        $releaseVersion = Get-SharpProofReleaseVersion `
            -RepositoryRoot $repositoryRoot
        if ($tag -cne "v$releaseVersion") {
            throw "Release tag '$tag' does not match package version '$releaseVersion'."
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
