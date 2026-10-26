[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'quick', 'check', 'pr', 'nightly', 'security', 'coverage',
        'build', 'test', 'test-changed', 'pack', 'samples', 'corpus',
        'fuzz-nightly', 'dependency-audit')]
    [Alias('Profile')]
    [string]$BuildProfile = 'check',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.slnx',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [switch]$Fast
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot
Import-Module (Join-Path `
        $repositoryRoot 'eng/scripts/SharpProof.ContainerExecution.psm1') -Force

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
    [Collections.IDictionary]$Environment = ([ordered]@{}).AsReadOnly()) {
    $previousComposeProgress = $env:COMPOSE_PROGRESS
    try {
        $env:COMPOSE_PROGRESS = 'quiet'
        $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $argumentBuilder.AddRange([string[]]@(
                'compose', 'run', '--rm', '--no-TTY', '--quiet-pull'))
        foreach ($name in @($Environment.Keys | Sort-Object)) {
            $value = [string]$Environment[$name]
            $argumentBuilder.AddRange([string[]]@('-e', "$name=$value"))
        }
        Build-ToolingImage
        $argumentBuilder.AddRange([string[]]@(
                'tooling', $Command, '-Configuration', $CommandConfiguration))
        if ($null -ne $AdditionalArguments) { $argumentBuilder.AddRange($AdditionalArguments) }
        $argumentSnapshot = $argumentBuilder.ToImmutable()
        # Copy directly so PowerShell does not normalize native null arguments to empty strings.
        $nativeArguments = [string[]]::new($argumentSnapshot.Length + [int]($null -eq $AdditionalArguments))
        $argumentSnapshot.CopyTo($nativeArguments)
        Invoke-Docker $nativeArguments
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

$forcedConfigurations = ([ordered]@{
        quick    = 'Debug'
        pr       = 'Release'
        nightly  = 'Release'
        security = 'Release'
    }).AsReadOnly()
if ($forcedConfigurations.Contains($BuildProfile)) {
    Invoke-Container $BuildProfile $forcedConfigurations[$BuildProfile]
    return
}

switch ($BuildProfile) {
    'coverage' {
        $coverageArguments = if ([string]::IsNullOrWhiteSpace($TestFilter)) { @() } else { @('-TestFilter', $TestFilter) }
        Invoke-Container 'coverage' 'Release' $coverageArguments
    }
    'test' {
        $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $argumentBuilder.AddRange([string[]]@('-Target', $Target))
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $argumentBuilder.AddRange([string[]]@('-TestFilter', $TestFilter))
        }
        if ($Fast) { $argumentBuilder.Add('-Fast') }
        [string[]]$nativeArguments = @($argumentBuilder.ToImmutable())
        Invoke-Container 'test' $Configuration $nativeArguments
    }
    'test-changed' {
        $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        if ($Fast) { $argumentBuilder.Add('-Fast') }
        $arguments = $argumentBuilder.ToImmutable()
        $nativeArguments = if ($arguments.IsEmpty) { $null } else { , [string[]]@($arguments) }
        Invoke-Container 'test-changed' $Configuration $nativeArguments
    }
    default {
        $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        if ($BuildProfile -ceq 'build') {
            $argumentBuilder.AddRange([string[]]@('-Target', $Target))
        }
        elseif ($BuildProfile -ceq 'samples' -and
            -not [string]::IsNullOrWhiteSpace($PackageSource)) {
            $argumentBuilder.AddRange([string[]]@('-PackageSource', $PackageSource))
        }
        $arguments = $argumentBuilder.ToImmutable()
        $nativeArguments = if ($arguments.IsEmpty) { $null } else { , [string[]]@($arguments) }
        Invoke-Container $BuildProfile $Configuration $nativeArguments
    }
}
