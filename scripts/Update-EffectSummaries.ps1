[CmdletBinding()]
param(
    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 4096,

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetWrapper = Join-Path $repoRoot 'scripts\Invoke-PurelySharpDotnet.ps1'
$artifactSpecPath = Join-Path $repoRoot 'Tools\PurelySharp.EffectSummary\ReviewedRuntimeArtifactSpec.json'
$projectPath = Join-Path $repoRoot 'Tools\PurelySharp.EffectSummary\PurelySharp.EffectSummary.csproj'

if (-not (Test-Path $dotnetWrapper)) {
    throw "Missing dotnet wrapper: $dotnetWrapper"
}

if (-not (Test-Path $artifactSpecPath)) {
    throw "Missing reviewed artifact spec: $artifactSpecPath"
}

if (-not (Test-Path $projectPath)) {
    throw "Missing effect summary tool project: $projectPath"
}

Push-Location $repoRoot
try {
    Write-Host 'Building effect summary tool...' -ForegroundColor Cyan
    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @('build', $projectPath, '-c', $Configuration, '-m:20', '--no-restore')
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build effect summary tool."
    }

    Write-Host 'Regenerating checked-in effect summary artifacts...' -ForegroundColor Cyan
    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @('run', '--project', $projectPath, '-c', $Configuration, '--no-build', '--', '--artifact-spec', $artifactSpecPath)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to regenerate checked-in effect summary artifacts."
    }
}
finally {
    Pop-Location
}
