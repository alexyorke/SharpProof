param(
    [string]$Configuration = 'Release',
    [string]$Framework = 'net8.0',
    [string]$DemoDir = 'tmp-sharpproof-demo/DemoApp',
    [string]$LocalFeed = 'artifacts/nuget',
    [ValidateSet('Vsix','NuGet')]
    [string]$Mode = 'Vsix',
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if ($Clean -and (Test-Path $DemoDir)) {
        Write-Host "Cleaning demo directory: $DemoDir" -ForegroundColor Yellow
        Remove-Item -Recurse -Force $DemoDir
    }

    # 1) Build local NuGet packages
    Write-Host "Building local NuGet packages..." -ForegroundColor Cyan
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build-nuget.ps1 -Configuration $Configuration -OutputDir (Join-Path $repoRoot $LocalFeed) | Out-Host

    $feedPath = Resolve-Path (Join-Path $repoRoot $LocalFeed)
    Write-Host "Local feed: $feedPath" -ForegroundColor Green

    # 2) Create demo console app
    $demoPath = Join-Path $repoRoot $DemoDir
    New-Item -ItemType Directory -Force -Path $demoPath | Out-Null
    $demoPath = Resolve-Path $demoPath
    Write-Host "Creating demo app in: $demoPath" -ForegroundColor Cyan
    dotnet new console -f $Framework --force --output $demoPath | Out-Host

    $proj = Join-Path $demoPath 'DemoApp.csproj'
    if (!(Test-Path $proj)) {
        # dotnet new may name project after folder
        $proj = Get-ChildItem $demoPath -Filter *.csproj | Select-Object -ExpandProperty FullName -First 1
    }
    if (-not $proj) { throw "Could not locate project file in $demoPath" }
    Write-Host "Project: $proj" -ForegroundColor Green

    # 3) Install packages from local feed
    if ($Mode -eq 'NuGet') {
        Write-Host "Installing SharpProof (analyzer) + SharpProof.Attributes from local feed..." -ForegroundColor Cyan
        dotnet add "$proj" package SharpProof --source "$feedPath" | Out-Host
        dotnet add "$proj" package SharpProof.Attributes --source "$feedPath" | Out-Host
    }
    else {
        Write-Host "Installing only SharpProof.Attributes from local feed (use VSIX for analyzer)..." -ForegroundColor Cyan
        dotnet add "$proj" package SharpProof.Attributes --source "$feedPath" | Out-Host
    }

    # 4) Write sample source that should produce analyzer diagnostics
    $programPath = Join-Path $demoPath 'Program.cs'
    Write-Host "Writing sample code to: $programPath" -ForegroundColor Cyan
    $code = @'
using System;
using SharpProof.Attributes;

// SP0003: Misplaced attribute on class
[EnforcePure]
public class Demo
{
    private int _counter = 0;

    // SP0002: Marked pure but mutates instance state
    [EnforcePure]
    public int AddImpure(int a, int b)
    {
        _counter++;
        return a + b + _counter;
    }

    // SP0004: Pure method missing [EnforcePure]
    public static int PureAdd(int a, int b) => a + b;

    // SP0003: Misplaced attribute on field
    [EnforcePure]
    private int _misplaced = 0;
}

class Program
{
    static void Main()
    {
        Console.WriteLine(Demo.PureAdd(1, 2));
        Console.WriteLine(new Demo().AddImpure(3, 4));
    }
}
'@
    Set-Content -Path $programPath -Value $code -Encoding UTF8

    # 5) Build and show diagnostics (CLI shows analyzer diagnostics only in NuGet mode; VSIX diagnostics show in VS)
    Write-Host "Building demo project..." -ForegroundColor Cyan
    dotnet build "$proj" -c $Configuration -v m | Out-Host

    Write-Host "Done." -ForegroundColor Green
}
finally {
    Pop-Location
}


