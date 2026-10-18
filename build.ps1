[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'quick', 'check', 'pr', 'nightly', 'security', 'coverage',
        'build', 'test', 'test-changed', 'pack', 'samples', 'corpus',
        'fuzz-nightly', 'package-consumers', 'dependency-audit')]
    [Alias('Profile')]
    [string]$BuildProfile = 'check',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.slnx',

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

New-Variable -Name forcedConfigurations -Option Constant -Value (
    [System.Collections.Immutable.ImmutableDictionary[string, string]]::Empty.
    WithComparers([StringComparer]::OrdinalIgnoreCase).
    Add('quick', 'Debug').
    Add('pr', 'Release').
    Add('nightly', 'Release').
    Add('security', 'Release'))
if ($forcedConfigurations.ContainsKey($BuildProfile)) {
    Invoke-Container $BuildProfile $forcedConfigurations[$BuildProfile]
    return
}

switch ($BuildProfile) {
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
    'package-consumers' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw "$BuildProfile requires -PackageSource."
        }
        Invoke-Container $BuildProfile $Configuration @(
            '-PackageSource', $PackageSource)
    }
    default {
        $arguments = if ($BuildProfile -ceq 'build') {
            @('-Target', $Target)
        }
        elseif ($BuildProfile -ceq 'samples' -and
            -not [string]::IsNullOrWhiteSpace($PackageSource)) {
            @('-PackageSource', $PackageSource)
        }
        else { @() }
        Invoke-Container $BuildProfile $Configuration $arguments
    }
}
