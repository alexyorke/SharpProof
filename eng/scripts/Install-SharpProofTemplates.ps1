[CmdletBinding()]
param(
    [switch]$Verify,
    [string]$PackageSource
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Assert-SharpProofContainer 'Install and validate repository templates in the canonical Linux amd64 container.'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
[xml]$release = Get-Content -LiteralPath (Join-Path $repository 'eng/Versions.props') -Raw
$version = [string]$release.Project.PropertyGroup.SharpProofPackageVersion
$version = $version.Replace('$(SharpProofVersionPrefix)', [string]$release.Project.PropertyGroup.SharpProofVersionPrefix)
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Unexpected release package version.' }
if ($Verify -and -not $PackageSource) { throw '-Verify requires the exact packed -PackageSource.' }
if ($PackageSource) { $PackageSource = (Resolve-Path -LiteralPath $PackageSource).Path }
if ($Verify) {
    $packageFiles = @(Get-ChildItem -LiteralPath $PackageSource -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') })
    $identity = Get-SharpProofPackageIdentitySet -Files $packageFiles -RequireRepository
    if ($identity.Version -ne $version) { throw "Packed feed version '$($identity.Version)' does not match checkout '$version'." }
}

# Keep template installation metadata and consumers out of the repository and normal user template hive.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-templates-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$hive = Join-Path $scratch 'hive'
$templateRoot = Join-Path $scratch 'templates'
[void][IO.Directory]::CreateDirectory($templateRoot)
foreach ($kind in @('portable', 'verified')) {
    $target = Join-Path $templateRoot $kind
    Copy-Item -LiteralPath (Join-Path $repository "templates/$kind") -Destination $target -Recurse -Force
    $configPath = Join-Path $target '.template.config/template.json'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.symbols.packageVersion.isRequired = $false
    $config.symbols.packageVersion | Add-Member -NotePropertyName defaultValue -NotePropertyValue $version
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}

Invoke-DotNet -Arguments @('new', 'install', $templateRoot, '--debug:custom-hive', $hive)
Write-Host "Template hive: $hive"
Write-Host "Package version: $version"
Write-Host "Create: dotnet new sp-portable -n MyLibrary --debug:custom-hive $hive"
Write-Host "Create: dotnet new sp-verified -n MyLibrary --debug:custom-hive $hive"

if ($Verify) {
    $configFile = Join-Path $scratch 'NuGet.config'
    $escapedFeed = [Security.SecurityElement]::Escape($PackageSource)
    $nugetConfig = @"
<configuration>
  <packageSources><clear /><add key="exact" value="$escapedFeed" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="exact"><package pattern="SharpProof*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@
    [IO.File]::WriteAllText($configFile, $nugetConfig, [Text.UTF8Encoding]::new($false))
    foreach ($kind in @('portable', 'verified')) {
        $consumer = Join-Path $scratch "consumer-$kind"
        Invoke-DotNet -Arguments @('new', "sp-$kind", '-n', 'TemplateConsumer', '-o', $consumer, '--debug:custom-hive', $hive)
        $project = Join-Path $consumer 'TemplateConsumer.csproj'
        Invoke-DotNet -Arguments @('restore', $project, '--configfile', $configFile, '--packages', (Join-Path $scratch 'packages'))
        Invoke-DotNet -Arguments @('build', $project, '-c', 'Release', '--no-restore')
        if ($kind -eq 'portable') {
            $source = Join-Path $consumer 'Calculator.cs'
            [IO.File]::WriteAllText($source, ([IO.File]::ReadAllText($source).Replace('Positive(1)', 'Positive(0)')), [Text.UTF8Encoding]::new($false))
            # The negative control must fail because the analyzer is present and SP0027 fires.
            $negativeOutput = @(& dotnet build $project -c Release --no-restore '-warnaserror:SP0027' 2>&1)
            $negativeExit = $LASTEXITCODE
            $negativeOutput | ForEach-Object { Write-Host $_ }
            if ($negativeExit -eq 0 -or ($negativeOutput -join "`n") -notmatch 'error SP0027') {
                throw 'Portable template negative control did not reject the invalid call with SP0027.'
            }
        }
        else {
            $resultPath = Join-Path $consumer 'obj/Release/net8.0/SharpProof/result.json'
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            if ($result.runStatus -ne 'Complete' -or $result.failureReason -ne 'None' -or @($result.claimResults).Count -ne 1 -or
                $result.claimResults[0].outcome -ne 'Proven' -or $result.claimResults[0].vacuity -ne 'None' -or
                @($result.callableResults | Where-Object coverage -ne 'Complete').Count -gt 0) {
                throw 'Verified template did not produce one complete Proven claim.'
            }
        }
    }
    Write-Host "Template smoke checks passed against exact package version $version. Local scratch: $scratch"
}
