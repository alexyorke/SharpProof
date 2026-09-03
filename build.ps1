[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'quick', 'check', 'pr', 'nightly', 'security', 'coverage',
        'build', 'test', 'test-changed', 'acceptance', 'pack', 'samples',
        'performance', 'gates', 'corpus', 'fuzz-nightly',
        'package-consumers', 'dependency-audit', 'mutation', 'pilots',
        'release-plan')]
    [string]$Profile = 'check',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.sln',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [string]$ComparisonRef = '',

    [switch]$Fast
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path $PSScriptRoot).Path
Set-Location $repositoryRoot
Import-Module (Join-Path `
    $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') -Force

function Invoke-Docker([string[]]$Arguments) {
    Invoke-SharpProofCheckedCommand `
        -Command 'docker' `
        -Arguments $Arguments
}

function Build-ToolingImage {
    Invoke-Docker @('compose', 'build', 'tooling')
}

function Invoke-Container(
    [string]$Command,
    [string]$CommandConfiguration = $Configuration,
    [string[]]$AdditionalArguments = @(),
    [hashtable]$Environment = @{}) {
    $previousComposeProgress = $env:COMPOSE_PROGRESS
    try {
        $env:COMPOSE_PROGRESS = 'quiet'
        $arguments = @(
            'compose', 'run', '--rm', '--no-TTY', '--quiet-pull')
        foreach ($name in @($Environment.Keys | Sort-Object)) {
            $value = [string]$Environment[$name]
            $arguments += @('-e', "$name=$value")
        }
        Build-ToolingImage
        $arguments += @(
            'tooling', $Command, '-Configuration', $CommandConfiguration)
        $arguments += $AdditionalArguments
        Invoke-Docker $arguments
    }
    finally {
        if ($null -eq $previousComposeProgress) {
            Remove-Item Env:COMPOSE_PROGRESS -ErrorAction SilentlyContinue
        }
        else {
            $env:COMPOSE_PROGRESS = $previousComposeProgress
        }
    }
}

$forcedConfigurations = @{
    quick = 'Debug'
    pr = 'Release'
    nightly = 'Release'
    security = 'Release'
}
if ($forcedConfigurations.ContainsKey($Profile)) {
    Invoke-Container $Profile $forcedConfigurations[$Profile]
    return
}

switch ($Profile) {
    'coverage' {
        if ([string]::IsNullOrWhiteSpace($ComparisonRef)) {
            throw 'coverage requires -ComparisonRef <commit-or-ref>.'
        }
        $exactComparison = (& git rev-parse --verify "$ComparisonRef^{commit}").Trim()
        if ($LASTEXITCODE -ne 0 -or
            $exactComparison -notmatch '^[0-9a-f]{40}$') {
            throw "Coverage comparison '$ComparisonRef' is not one exact commit."
        }
        Invoke-Container 'coverage' 'Release' @() @{
            SHARPPROOF_COVERAGE_COMPARISON_REF = $exactComparison
        }
    }
    'test' {
        $arguments = @('-Target', $Target)
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('-TestFilter', $TestFilter)
        }
        if ($Fast) { $arguments += '-Fast' }
        Invoke-Container 'test' $Configuration $arguments
    }
    'test-changed' {
        $arguments = if ($Fast) { @('-Fast') } else { @() }
        Invoke-Container 'test-changed' $Configuration $arguments
    }
    { $_ -in @('package-consumers', 'pilots', 'release-plan') } {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw "$Profile requires -PackageSource."
        }
        Invoke-Container $Profile $Configuration @(
            '-PackageSource', $PackageSource)
    }
    default {
        $arguments = if ($Profile -ceq 'build') {
            @('-Target', $Target)
        }
        else { @() }
        Invoke-Container $Profile $Configuration $arguments
    }
}
