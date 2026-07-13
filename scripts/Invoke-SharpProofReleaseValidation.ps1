[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 6144,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 1200,

    [Parameter()]
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'

$projects = @(
    @{
        Label = 'SharpProof.ProofCore'
        Path = 'SharpProof.ProofCore\SharpProof.ProofCore.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.Symbolic'
        Path = 'SharpProof.Symbolic\SharpProof.Symbolic.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.Attributes'
        Path = 'SharpProof.Attributes\SharpProof.Attributes.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.Analyzer'
        Path = 'SharpProof.Analyzer\SharpProof.Analyzer.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.CodeFixes'
        Path = 'SharpProof.CodeFixes\SharpProof.CodeFixes.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.Package'
        Path = 'SharpProof.Package\SharpProof.Package.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'Tools/SharpProof.SymbolicCli'
        Path = 'Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'Tools/SharpProof.EffectSummary'
        Path = 'Tools\SharpProof.EffectSummary\SharpProof.EffectSummary.csproj'
        ExtraArgs = @()
    },
    @{
        Label = 'SharpProof.Vsix'
        Path = 'SharpProof.Vsix\SharpProof.Vsix.csproj'
        ExtraArgs = @('/p:EnableVsixPackaging=true')
    }
)

Push-Location $repoRoot
try
{
    foreach ($project in $projects)
    {
        $projectPath = $project.Path
        $label = $project.Label
        $dotnetArgs = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in @(
            'build',
            $projectPath,
            '--configuration',
            $Configuration))
        {
            $dotnetArgs.Add($argument)
        }

        if ($NoRestore)
        {
            $dotnetArgs.Add('--no-restore')
        }

        foreach ($argument in @(
            '/m:1',
            '/warnaserror',
            '/clp:ErrorsOnly;Summary'))
        {
            $dotnetArgs.Add($argument)
        }

        foreach ($argument in $project.ExtraArgs)
        {
            $dotnetArgs.Add([string]$argument)
        }

        Write-Host "Validating $label with warnings as errors..."
        & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @dotnetArgs
        if ($LASTEXITCODE -ne 0)
        {
            throw "Release validation failed for $label."
        }
    }
}
finally
{
    Pop-Location
}
