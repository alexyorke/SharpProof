param(
    [string]$Configuration = "Release",
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 6144
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Section([string]$Message) {
    Write-Host "==> $Message"
}

$root = $PSScriptRoot
if (-not $root -or $root -eq "") {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$dotnetWrapper = Join-Path $root 'scripts\Invoke-SharpProofDotnet.ps1'

Write-Section "Changing directory to repo root: $root"
Set-Location -Path $root -ErrorAction Stop

Write-Section "Restoring packages"
& $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @("restore")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Section "Building non-VSIX projects ($Configuration)"
& $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @(
    "build",
    ".\SharpProof.Dev.slnf",
    "-c",
    $Configuration,
    "--no-restore",
    "/warnaserror",
    "-p:GeneratePackageOnBuild=false",
    "-p:EnableVsixPackaging=false",
    "-p:UseSharedCompilation=true")
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$vsixDir = Join-Path $root "SharpProof.Vsix\bin\$Configuration"
$nugetOutputDir = Join-Path $root "artifacts\nuget"
New-Item -ItemType Directory -Force -Path $nugetOutputDir | Out-Null

$vsix = Get-ChildItem -Path $vsixDir -Filter *.vsix -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$latestVsixInput = Get-ChildItem -Path @(
        (Join-Path $root 'SharpProof.Vsix'),
        (Join-Path $root 'SharpProof.Analyzer'),
        (Join-Path $root 'SharpProof.CodeFixes')) -Recurse -File -ErrorAction Stop |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $vsix -or $latestVsixInput.LastWriteTimeUtc -gt $vsix.LastWriteTimeUtc) {
    Write-Section "Building VSIX"
    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @(
        "build",
        ".\SharpProof.Vsix\SharpProof.Vsix.csproj",
        "-c",
        $Configuration,
        "--no-restore",
        "/p:EnableVsixPackaging=true")
    if ($LASTEXITCODE -ne 0) { throw "dotnet VSIX build failed with exit code $LASTEXITCODE" }

    $vsix = Get-ChildItem -Path $vsixDir -Filter *.vsix -Recurse -ErrorAction Stop | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

Write-Section "Packing NuGet packages"
Get-ChildItem -Path $nugetOutputDir -Filter *.nupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force

$packageProjects = @((Get-Content -LiteralPath (Join-Path $root 'scripts\package-projects.json') -Raw | ConvertFrom-Json).projects)
foreach ($packageProject in $packageProjects) {
    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs @(
        "pack", $packageProject, "-c", $Configuration, "-o", $nugetOutputDir, "--no-build", "--no-restore")
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }
}

$nupkgs = Get-ChildItem -Path $nugetOutputDir -Filter *.nupkg -File -ErrorAction Stop | Sort-Object Name

Write-Host ""
Write-Section "Artifacts"
if ($vsix) { Write-Host ("VSIX: " + $vsix.FullName) } else { Write-Host "VSIX: not found" }
if ($nupkgs) { $nupkgs | ForEach-Object { Write-Host ("NuGet: " + $_.FullName) } } else { Write-Host "NuGet: not found" }

exit 0
