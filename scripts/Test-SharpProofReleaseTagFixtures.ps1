[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-release-tag-' + [Guid]::NewGuid().ToString('N'))
$remote = Join-Path $fixtureRoot 'origin.git'
$checkout = Join-Path $fixtureRoot 'checkout'
$version = '1.0.0-preview.1'
$tag = "v$version"
$originalLocation = (Get-Location).Path

function Assert-GitSucceeded([string]$Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed while $Operation."
    }
}

function Invoke-TagCase(
    [string]$Name,
    [AllowEmptyString()][string]$Ref,
    [AllowEmptyString()][string]$RefName,
    [AllowEmptyString()][string]$Commit,
    [bool]$Expected) {
    $env:GITHUB_REF = $Ref
    $env:GITHUB_REF_NAME = $RefName
    $env:GITHUB_SHA = $Commit
    $accepted = $true
    try {
        & (Join-Path $checkout 'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode ValidateTag *> $null
    }
    catch {
        $accepted = $false
    }
    if ($accepted -ne $Expected) {
        throw "Release-tag fixture '$Name' expected accepted=$Expected but got $accepted."
    }
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    & git -c init.defaultBranch=master init --bare --quiet $remote
    Assert-GitSucceeded 'creating the local fixture remote'
    & git -c init.defaultBranch=master init --quiet $checkout
    Assert-GitSucceeded 'creating the fixture checkout'
    & git -C $checkout config user.email fixture@sharpproof.test
    & git -C $checkout config user.name 'SharpProof Fixture'
    [IO.Directory]::CreateDirectory((Join-Path $checkout 'scripts')) | Out-Null
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Invoke-SharpProofReleaseContainer.ps1') `
        -Destination (Join-Path $checkout 'scripts/Invoke-SharpProofReleaseContainer.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Get-SharpProofReleaseVersion.ps1') `
        -Destination (Join-Path $checkout 'scripts/Get-SharpProofReleaseVersion.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Resolve-SharpProofContainedPath.ps1') `
        -Destination (Join-Path $checkout 'scripts/Resolve-SharpProofContainedPath.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') `
        -Destination (Join-Path $checkout 'scripts/SharpProof.ContainerExecution.psm1')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'SharpProof.Release.props') `
        -Destination (Join-Path $checkout 'SharpProof.Release.props')
    & git -C $checkout add -- .
    & git -C $checkout commit --quiet -m source
    Assert-GitSucceeded 'committing fixture source'
    & git -C $checkout commit --quiet --allow-empty -m candidate
    Assert-GitSucceeded 'committing the candidate'
    $head = (& git -C $checkout rev-parse HEAD).Trim()
    $parent = (& git -C $checkout rev-parse HEAD^).Trim()
    & git -C $checkout remote add origin $remote
    & git -C $checkout push --quiet --set-upstream origin master
    Assert-GitSucceeded 'pushing the local fixture master'
    & git -C $checkout tag -a $tag -m candidate $head
    Assert-GitSucceeded 'creating the annotated release tag'

    $env:SHARPPROOF_CONTAINER = '1'
    Invoke-TagCase exact-annotated "refs/tags/$tag" $tag $head $true
    Invoke-TagCase branch refs/heads/master master $head $false
    Invoke-TagCase empty-ref '' $tag $head $false
    Invoke-TagCase non-version-tag refs/tags/release release $head $false
    Invoke-TagCase wrong-version refs/tags/v9.9.9 v9.9.9 $head $false
    Invoke-TagCase wrong-ref-name "refs/tags/$tag" wrong $head $false
    Invoke-TagCase wrong-sha "refs/tags/$tag" $tag ('0' * 40) $false

    & git -C $checkout tag -d $tag *> $null
    Invoke-TagCase missing-tag "refs/tags/$tag" $tag $head $false
    & git -C $checkout tag $tag $head
    Assert-GitSucceeded 'creating the lightweight tag fixture'
    Invoke-TagCase lightweight "refs/tags/$tag" $tag $head $false
    & git -C $checkout tag -d $tag *> $null
    & git -C $checkout tag -a $tag -m candidate $head
    Assert-GitSucceeded 'restoring the annotated tag'

    & git -C $checkout tag -f -a $tag -m wrong-commit $parent *> $null
    Assert-GitSucceeded 'moving the tag to the wrong commit'
    Invoke-TagCase wrong-tag-commit "refs/tags/$tag" $tag $head $false
    & git -C $checkout tag -f -a $tag -m candidate $head *> $null
    Assert-GitSucceeded 'restoring the release tag commit'

    & git -C $checkout checkout --quiet $parent
    Assert-GitSucceeded 'checking out a mismatched HEAD'
    Invoke-TagCase wrong-head "refs/tags/$tag" $tag $head $false
    & git -C $checkout checkout --quiet master
    Assert-GitSucceeded 'restoring the candidate checkout'

    & git -C $checkout update-ref -d refs/remotes/origin/master
    Invoke-TagCase missing-origin-master "refs/tags/$tag" $tag $head $false
    & git -C $checkout update-ref refs/remotes/origin/master $parent
    Assert-GitSucceeded 'creating the divergent ancestry fixture'
    Invoke-TagCase tag-not-in-origin-master "refs/tags/$tag" $tag $head $false
    & git -C $checkout update-ref refs/remotes/origin/master $head
    Assert-GitSucceeded 'restoring origin/master'
    Invoke-TagCase restored-control "refs/tags/$tag" $tag $head $true

    Write-Host 'Release-tag fixtures passed.'
}
finally {
    Set-Location $originalLocation
    Remove-Item Env:GITHUB_REF -ErrorAction SilentlyContinue
    Remove-Item Env:GITHUB_REF_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:GITHUB_SHA -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
