[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,
    [Parameter(Mandatory = $true)]
    [ValidateSet('arm64', 'x64')]
    [string]$Architecture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$actualHost = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$actualProcess = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if (-not $IsMacOS -or $actualHost -cne $Architecture -or $actualProcess -cne $Architecture) {
    throw "Native macOS consumer requires matching $Architecture host and process architecture."
}

$source = (Resolve-Path -LiteralPath $PackageSource).Path
$packages = @(Get-ChildItem -LiteralPath $source -Filter '*.nupkg' -File)
if ($packages.Count -ne 3) {
    throw 'The native macOS consumer requires exactly three package artifacts.'
}
$verifier = @($packages | Where-Object Name -Match '^SharpProof\.Verifier\.(.+)\.nupkg$')
if ($verifier.Count -ne 1) {
    throw 'The native macOS consumer requires one SharpProof.Verifier package.'
}
$version = [regex]::Match($verifier[0].Name, '^SharpProof\.Verifier\.(.+)\.nupkg$').Groups[1].Value
foreach ($id in @('SharpProof', 'SharpProof.Attributes')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source "$id.$version.nupkg") -PathType Leaf)) {
        throw "The exact $id package is missing."
    }
}

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$parent = Join-Path ([IO.Path]::GetTempPath()) 'SharpProof.MacOsVerifierConsumer'
$root = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $repository 'global.json') -Destination (Join-Path $root 'global.json')
    $escapedSource = [Security.SecurityElement]::Escape($source)
    $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="$escapedSource" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@
    [IO.File]::WriteAllText((Join-Path $root 'NuGet.config'), $config, [Text.UTF8Encoding]::new($false))
    $project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <SharpProofProfile>strict</SharpProofProfile>
    <SharpProofVerify>true</SharpProofVerify>
    <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
    <SharpProofVerifyResultFile>$(MSBuildProjectDirectory)/result.json</SharpProofVerifyResultFile>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpProof.Attributes" Version="VERSION" />
    <PackageReference Include="SharpProof" Version="VERSION" PrivateAssets="all" />
    <PackageReference Include="SharpProof.Verifier" Version="VERSION" PrivateAssets="all" />
  </ItemGroup>
</Project>
'@.Replace('VERSION', [Security.SecurityElement]::Escape($version))
    [IO.File]::WriteAllText((Join-Path $root 'Consumer.csproj'), $project, [Text.UTF8Encoding]::new($false))
    $sourceCode = @'
using SharpProof.Attributes;

public static class Subject
{
    public static int Increment(int value)
    {
        Contract.Requires(value >= 0 && value < 10);
        Contract.Ensures(Contract.Result<int>() == value + 1);
        return value + 1;
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $root 'Subject.cs'), $sourceCode, [Text.UTF8Encoding]::new($false))

    Push-Location $root
    try {
        & dotnet restore Consumer.csproj --configfile NuGet.config --packages (Join-Path $root 'packages') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Native macOS consumer restore failed.' }
        & dotnet build Consumer.csproj --configuration Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Native macOS verifier build failed.' }
        $resultPath = Join-Path $root 'result.json'
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw 'The native macOS verifier did not publish a worker result.'
        }
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $claims = @($result.claimResults)
        if ($claims.Count -lt 1 -or @($claims | Where-Object outcome -CEQ 'Proven').Count -lt 1) {
            throw 'The native macOS verifier did not prove a claim.'
        }

        $nativePath = Join-Path $root "packages/sharpproof.verifier/$version/tools/native/osx-$Architecture/libz3.dylib"
        if (-not (Test-Path -LiteralPath $nativePath -PathType Leaf)) {
            throw 'The architecture-specific native payload is missing from the restored package.'
        }
        $stream = [IO.File]::Open($nativePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $original = $stream.ReadByte()
            $stream.Position = 0
            $stream.WriteByte([byte]($original -bxor 1))
        }
        finally { $stream.Dispose() }
        $failure = & dotnet build Consumer.csproj --configuration Release --no-restore --nologo -t:Rebuild 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $failure -notmatch 'pinned digest') {
            throw 'The verifier did not reject a modified macOS Z3 payload.'
        }
        Write-Host 'Native macOS verifier proof and payload-tamper rejection passed.'
    }
    finally { Pop-Location }
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    $resolvedParent = [IO.Path]::GetFullPath($parent)
    if (-not $resolvedRoot.StartsWith($resolvedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Refusing to remove an unexpected native macOS consumer root.'
    }
    if ([IO.Directory]::Exists($resolvedRoot)) {
        [IO.Directory]::Delete($resolvedRoot, $true)
    }
}
