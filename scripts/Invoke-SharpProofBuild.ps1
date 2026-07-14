[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter()]
    [switch]$NoRestore,

    [Parameter()]
    [switch]$WithTests,

    [Parameter()]
    [switch]$Full,

    [Parameter()]
    [switch]$WithEffectSummaries,

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
if ($Full -and $WithTests)
{
    throw '-Full and -WithTests cannot be used together.'
}
$target = if ($Full)
{
    'SharpProof.sln'
}
elseif ($WithTests)
{
    'SharpProof.Dev.Tests.slnf'
}
else
{
    'SharpProof.Dev.slnf'
}

$buildArguments = [System.Collections.Generic.List[string]]::new()
$buildArguments.Add('build')
$buildArguments.Add($target)
$buildArguments.Add('--configuration')
$buildArguments.Add($Configuration)
$buildArguments.Add('/warnaserror')

if ($NoRestore)
{
    $buildArguments.Add('--no-restore')
}

if (-not $Full)
{
    # Package and extension artifacts belong to the full release path. Avoid
    # generating packages transitively while compiling the developer graph.
    $buildArguments.Add('-p:GeneratePackageOnBuild=false')
    $buildArguments.Add('-p:EnableVsixPackaging=false')
    # A single developer build compiles several projects. Let Roslyn reuse one
    # compiler server within the Job Object; the Job Object still guarantees
    # that the server is cleaned up when the build exits.
    $buildArguments.Add('-p:UseSharedCompilation=true')

    # Regenerating the built-in effect summaries dominates a cold build after
    # the generator changes. Ordinary compile checks do not execute the
    # analyzer, so keep that work in test/full builds unless explicitly asked.
    if (-not $WithTests -and -not $WithEffectSummaries)
    {
        $buildArguments.Add('-p:SharpProofSkipGeneratedEffectSummaries=true')
    }
}

Push-Location $repoRoot
try
{
    & $dotnetWrapper `
        -MemoryLimitMb $MemoryLimitMb `
        -TimeoutSeconds $TimeoutSeconds `
        @buildArguments
    exit $LASTEXITCODE
}
finally
{
    Pop-Location
}
