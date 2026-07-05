param(
    [string]$Configuration = "Release"
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

. (Join-Path $root "scripts\JobObjectHelpers.ps1")

function Invoke-DotnetInRepo([string[]]$Arguments, [int]$MemoryLimitMb = 0) {
    $exitCode = Invoke-ProcessUnderJobObject -FilePath "dotnet" -ArgumentList $Arguments -MemoryLimitMb $MemoryLimitMb -WorkingDirectory $root
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode"
    }
}

Write-Section "Changing directory to repo root: $root"
Set-Location -Path $root -ErrorAction Stop

Write-Section "Restoring packages"
Invoke-DotnetInRepo @("restore")

Write-Section "Building non-VSIX projects ($Configuration)"
Invoke-DotnetInRepo @("build", ".\SharpProof.Attributes\SharpProof.Attributes.csproj", "-c", $Configuration)
Invoke-DotnetInRepo @("build", ".\SharpProof.Analyzer\SharpProof.Analyzer.csproj", "-c", $Configuration)
Invoke-DotnetInRepo @("build", ".\SharpProof.CodeFixes\SharpProof.CodeFixes.csproj", "-c", $Configuration)

$vsixDir = Join-Path $root "SharpProof.Vsix\bin\$Configuration"
$nugetOutputDir = Join-Path $root "artifacts\nuget"
New-Item -ItemType Directory -Force -Path $nugetOutputDir | Out-Null

$vsix = Get-ChildItem -Path $vsixDir -Filter *.vsix -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $vsix) {
    Write-Section "Building VSIX using MSBuild.exe"

    $candidateMsBuildPaths = @()

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\\Installer\\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\\**\\Bin\\MSBuild.exe" 2>$null | Select-Object -First 1
        if ($path) { $candidateMsBuildPaths += $path }
    }

    $candidateMsBuildPaths += @(
        "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe",
        "C:\\Program Files\\Microsoft Visual Studio\\2022\\Professional\\MSBuild\\Current\\Bin\\MSBuild.exe",
        "C:\\Program Files\\Microsoft Visual Studio\\2022\\Enterprise\\MSBuild\\Current\\Bin\\MSBuild.exe",
        "C:\\Program Files (x86)\\Microsoft Visual Studio\\2022\\BuildTools\\MSBuild\\Current\\Bin\\MSBuild.exe",
        "C:\\Program Files (x86)\\Microsoft Visual Studio\\2019\\BuildTools\\MSBuild\\Current\\Bin\\MSBuild.exe"
    )

    $msbuildPath = $candidateMsBuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $msbuildPath) {
        $searchRoots = @("C:\\Program Files\\Microsoft Visual Studio", "C:\\Program Files (x86)\\Microsoft Visual Studio")
        foreach ($rootPath in $searchRoots) {
            $found = Get-ChildItem -Path $rootPath -Filter MSBuild.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*\\MSBuild\\Current\\Bin\\MSBuild.exe" } |
                Select-Object -First 1
            if ($found) { $msbuildPath = $found.FullName; break }
        }
    }

    if (-not $msbuildPath) { throw "Could not locate MSBuild.exe. Please install Visual Studio 2022 (any edition) or MSBuild Build Tools with the VS extension workload." }

    & $msbuildPath ".\SharpProof.Vsix\SharpProof.Vsix.csproj" /t:Build /p:Configuration=$Configuration /p:EnableVsixPackaging=true
    if ($LASTEXITCODE -ne 0) { throw "MSBuild VSIX build failed with exit code $LASTEXITCODE" }

    $vsix = Get-ChildItem -Path $vsixDir -Filter *.vsix -ErrorAction Stop | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

Write-Section "Packing NuGet packages"
Get-ChildItem -Path $nugetOutputDir -Filter *.nupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force

Invoke-DotnetInRepo @("pack", ".\SharpProof.Package\SharpProof.Package.csproj", "-c", $Configuration, "-o", $nugetOutputDir)
Invoke-DotnetInRepo @("pack", ".\SharpProof.Attributes\SharpProof.Attributes.csproj", "-c", $Configuration, "-o", $nugetOutputDir)

$nupkgs = Get-ChildItem -Path $nugetOutputDir -Filter *.nupkg -File -ErrorAction Stop | Sort-Object Name

Write-Host ""
Write-Section "Artifacts"
if ($vsix) { Write-Host ("VSIX: " + $vsix.FullName) } else { Write-Host "VSIX: not found" }
if ($nupkgs) { $nupkgs | ForEach-Object { Write-Host ("NuGet: " + $_.FullName) } } else { Write-Host "NuGet: not found" }

exit 0


