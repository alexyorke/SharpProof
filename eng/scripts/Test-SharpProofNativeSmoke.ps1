[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageSource
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1')

# Proves one contract and refutes its broken twin with the packed packages on
# the host's own SDK. Release runs this on Windows and macOS, where no other
# job loads the native verifier payloads.
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$version = Get-SharpProofEffectivePackageVersion -RepositoryRoot $repository
$PackageSource = (Resolve-Path -LiteralPath $PackageSource).Path
if (-not (Test-Path -LiteralPath (Join-Path $PackageSource "SharpProof.$version.nupkg") -PathType Leaf)) {
    throw "The package source has no SharpProof $version package."
}

# Outside the checkout, so its global.json does not select the SDK.
$consumer = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-native-smoke-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($consumer)
$feed = [Security.SecurityElement]::Escape($PackageSource)
Set-Content -LiteralPath (Join-Path $consumer 'NuGet.config') -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="packed" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="packed"><package pattern="SharpProof*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@
Set-Content -LiteralPath (Join-Path $consumer 'Smoke.csproj') -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpProof" Version="$version" />
  </ItemGroup>
</Project>
"@
$counter = Join-Path $consumer 'Counter.cs'
$program = @'
using SharpProof.Attributes;

public static class Counter
{
    public static int Next(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}
'@
$result = Join-Path $consumer 'obj/Release/net10.0/SharpProof/result.json'

# Builds the consumer and requires one claim with the expected outcome.
function Assert-SmokeBuild {
    param([string]$Text, [string]$Outcome, [bool]$Succeeds)
    Set-Content -LiteralPath $counter -Value $Text
    Remove-Item -LiteralPath $result -ErrorAction SilentlyContinue
    # The SDK is chosen from the working directory, so build from the consumer
    # to use the host SDK rather than the checkout's pinned global.json.
    Push-Location -LiteralPath $consumer
    try { & dotnet build -c Release -nologo | Out-Host }
    finally { Pop-Location }
    $built = $LASTEXITCODE -eq 0
    if (-not (Test-Path -LiteralPath $result -PathType Leaf)) { throw "The build published no verification result." }
    $response = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $outcomes = @($response.claimResults | ForEach-Object { [string]$_.outcome })
    if ([string]$response.runStatus -cne 'Complete') { throw "The verifier run was '$($response.runStatus)', not Complete." }
    if ($outcomes.Count -ne 1 -or $outcomes[0] -cne $Outcome) { throw "Expected one $Outcome claim; got '$($outcomes -join ', ')'." }
    if ($built -ne $Succeeds) { throw "The $Outcome contract build succeeded: $built." }
}

Assert-SmokeBuild -Text $program -Outcome 'Proven' -Succeeds $true
Assert-SmokeBuild -Text $program.Replace('Contract.Old(value) + 1', 'Contract.Old(value) + 2') -Outcome 'Refuted' -Succeeds $false
Write-Output "Native smoke passed for SharpProof $version."
# The expected failing build leaves $LASTEXITCODE nonzero, and the Actions
# pwsh wrapper exits with it, so succeed explicitly once every check passes.
exit 0
