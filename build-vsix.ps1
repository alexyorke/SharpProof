param(
    [string]$Configuration = 'Release',
    [switch]$RunHarness,
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 6144
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSCommandPath
$dotnetWrapper = Join-Path $repoRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$vsixProj = Join-Path $repoRoot 'SharpProof.Vsix\SharpProof.Vsix.csproj'
if (!(Test-Path $vsixProj)) {
    throw "VSIX project not found: $vsixProj"
}

Write-Host "Building VSIX ($Configuration)..." -ForegroundColor Cyan
& $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @(
    'build',
    $vsixProj,
    '-c',
    $Configuration,
    '/p:EnableVsixPackaging=true',
    '/v:m')
if ($LASTEXITCODE -ne 0) { throw "dotnet VSIX build failed with exit code $LASTEXITCODE" }

$vsixDir = Join-Path $repoRoot "SharpProof.Vsix\bin\$Configuration"
$vsix = Get-ChildItem -Path $vsixDir -Recurse -Filter *.vsix | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName -First 1
if (-not $vsix) {
    throw "No VSIX produced under: $vsixDir"
}

Write-Host "VSIX built: $vsix" -ForegroundColor Green

if ($RunHarness) {
    $harnessProj = Join-Path $repoRoot 'Tools\VsixHarness\VsixHarness.csproj'
    if (!(Test-Path $harnessProj)) {
        throw "Harness project not found: $harnessProj"
    }
    Write-Host "Running harness against VSIX..." -ForegroundColor Cyan
    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @(
        'run', '--project', $harnessProj, '-c', $Configuration, '--', $vsix, $Configuration)
    if ($LASTEXITCODE -ne 0) { throw "dotnet run failed with exit code $LASTEXITCODE" }
}
