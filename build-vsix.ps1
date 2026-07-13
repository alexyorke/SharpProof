param(
    [string]$Configuration = 'Release',
    [switch]$RunHarness,
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 6144
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path (Split-Path -Parent $PSCommandPath) 'scripts\JobObjectHelpers.ps1')

function Invoke-DotnetInRepo([string[]]$Arguments) {
    $exitCode = Invoke-ProcessUnderJobObject -FilePath 'dotnet' -ArgumentList $Arguments -MemoryLimitMb $MemoryLimitMb -WorkingDirectory $repoRoot
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode"
    }
}

function Invoke-MSBuildInRepo([string]$MSBuildPath, [string[]]$Arguments) {
    $exitCode = Invoke-ProcessUnderJobObject -FilePath $MSBuildPath -ArgumentList $Arguments -MemoryLimitMb $MemoryLimitMb -WorkingDirectory $repoRoot
    if ($exitCode -ne 0) {
        throw "MSBuild $($Arguments -join ' ') failed with exit code $exitCode"
    }
}

function Find-MSBuild {
    $pf86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $vswhere = Join-Path $pf86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path $vswhere)) {
        throw "vswhere not found at: $vswhere. Please install Visual Studio 2022 (with MSBuild)."
    }
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) {
        throw 'MSBuild not found via vswhere. Ensure Visual Studio Build Tools/MSBuild are installed.'
    }
    return $msbuild
}

$repoRoot = Split-Path -Parent $PSCommandPath
$vsixProj = Join-Path $repoRoot 'SharpProof.Vsix\SharpProof.Vsix.csproj'
if (!(Test-Path $vsixProj)) {
    throw "VSIX project not found: $vsixProj"
}

Write-Host "Locating MSBuild..." -ForegroundColor Cyan
$msbuild = Find-MSBuild
Write-Host "MSBuild: $msbuild" -ForegroundColor Green

Write-Host "Building VSIX ($Configuration)..." -ForegroundColor Cyan
Invoke-MSBuildInRepo $msbuild @(
    $vsixProj,
    '/t:Restore,Build',
    "/p:Configuration=$Configuration",
    '/p:EnableVsixPackaging=true',
    '/v:m')

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
    Invoke-DotnetInRepo @('run', '--project', $harnessProj, '-c', $Configuration, '--', $vsix, $Configuration)
}
