[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -ne '1') { throw 'Run doctor regressions in the canonical container.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-doctor-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
try {
    $project = Join-Path $fixture 'Consumer.csproj'
    [IO.File]::WriteAllText($project, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <SharpProofProfile>advisory</SharpProofProfile>
    <SharpProofVerify>false</SharpProofVerify>
    <SharpProofVerifyPolicy>advisory</SharpProofVerifyPolicy>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpProof" Version="1.2.3" />
    <PackageReference Include="SharpProof.Attributes" Version="1.2.4" />
  </ItemGroup>
</Project>
'@)
    $doctor = Join-Path $PSScriptRoot 'Invoke-SharpProofDoctor.ps1'
    $json = & pwsh -NoLogo -NoProfile -File $doctor -Target $project -Format Json
    if ($LASTEXITCODE -ne 0) { throw 'Advisory project doctor should succeed.' }
    $report = ($json | Out-String) | ConvertFrom-Json
    if ($report.schemaVersion -ne 1 -or $report.packages.Count -ne 2) { throw 'Doctor JSON schema/package discovery failed.' }
    if ($report.configuration.SharpProofVerify -ne 'false') { throw 'Doctor did not evaluate effective configuration.' }
    if (-not @($report.checks | Where-Object { $_.name -eq 'package-versions' -and $_.status -eq 'warning' }).Count) { throw 'Expected mismatched-version warning.' }
    if (Test-Path -LiteralPath (Join-Path $fixture 'obj')) { throw 'Doctor created build/restore output.' }
    $validSource = [IO.File]::ReadAllText($project)
    [IO.File]::WriteAllText($project, $validSource.Replace('<SharpProofProfile>advisory</SharpProofProfile>', '<SharpProofProfile>default</SharpProofProfile>'))
    $json = & pwsh -NoLogo -NoProfile -File $doctor -Target $project -Format Json
    if ($LASTEXITCODE -ne 1) { throw 'Invalid profile must fail preflight.' }
    $report = ($json | Out-String) | ConvertFrom-Json
    if (-not @($report.checks | Where-Object { $_.name -eq 'SharpProofProfile' -and $_.status -eq 'error' }).Count) { throw 'Expected invalid-profile diagnostic.' }
    [IO.File]::WriteAllText($project, $validSource)
    $source = [IO.File]::ReadAllText($project).Replace('<SharpProofVerify>false</SharpProofVerify>', '<SharpProofVerify>true</SharpProofVerify>')
    [IO.File]::WriteAllText($project, $source)
    $json = & pwsh -NoLogo -NoProfile -File $doctor -Target $project -Format Json
    if ($LASTEXITCODE -ne 1) { throw 'Enabled verification without a payload must fail preflight.' }
    $report = ($json | Out-String) | ConvertFrom-Json
    if (-not @($report.checks | Where-Object { $_.name -eq 'worker' -and $_.status -eq 'error' }).Count) { throw 'Expected actionable missing-worker error.' }
    $centralSource = $validSource.Replace(' Version="1.2.3"', '').Replace(' Version="1.2.4"', '')
    $centralSource = $centralSource.Replace('<TargetFramework>net8.0</TargetFramework>', '<TargetFramework>net8.0</TargetFramework><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally><ProjectAssetsFile>$(MSBuildProjectDirectory)/assets.fixture.json</ProjectAssetsFile>')
    $centralSource = $centralSource.Replace('</ItemGroup>', '<PackageReference Include="SharpProof.Unrelated" /></ItemGroup>')
    [IO.File]::WriteAllText($project, $centralSource)
    $assetsPath = Join-Path $fixture 'assets.fixture.json'
    $assetsSource = '{"libraries":{"SharpProof/1.2.3":{"type":"package"},"src/SharpProof.Attributes/1.2.3":{"type":"package"},"src/SharpProof.Verifier/1.2.3":{"type":"package"},"SharpProof.Unrelated/7.0.0":{"type":"package"}},"packageFolders":{"do-not-emit-private-package-path":{}}}'
    [IO.File]::WriteAllText($assetsPath, $assetsSource)
    $json = & pwsh -NoLogo -NoProfile -File $doctor -Target $project -Format Json
    if ($LASTEXITCODE -ne 0) { throw 'Versionless centrally managed references should succeed.' }
    $report = ($json | Out-String) | ConvertFrom-Json
    if ($report.packages.Count -ne 2 -or $report.resolvedPackages.Count -ne 3) { throw 'Exact package IDs or transitive resolved discovery failed.' }
    if (@($report.packages | Where-Object { $_.version }).Count) { throw 'Expected versionless evaluated direct references.' }
    if (@($report.checks | Where-Object { $_.name -eq 'package-versions' }).Count) { throw 'Aligned resolved versions should not warn.' }
    if (($json | Out-String).Contains('do-not-emit-private-package-path')) { throw 'Doctor leaked unrelated assets metadata.' }
    [IO.File]::WriteAllText($assetsPath, $assetsSource.Replace('src/SharpProof.Verifier/1.2.3', 'src/SharpProof.Verifier/1.2.4'))
    $json = & pwsh -NoLogo -NoProfile -File $doctor -Target $project -Format Json
    if ($LASTEXITCODE -ne 0) { throw 'Resolved version mismatch should be a warning.' }
    $report = ($json | Out-String) | ConvertFrom-Json
    if (-not @($report.checks | Where-Object { $_.name -eq 'package-versions' -and $_.status -eq 'warning' }).Count) { throw 'Expected transitive resolved-version mismatch warning.' }
    $text = & pwsh -NoLogo -NoProfile -File $doctor
    if ($LASTEXITCODE -ne 0 -or -not ($text -match 'No consumer project selected')) { throw 'Doctor text discovery failed.' }
    Write-Output 'Doctor regressions passed: JSON/text, configuration, direct/central/transitive packages, invalid profile, missing worker, no build outputs.'
}
finally {
    # The fixture is a freshly created child of the canonical temp directory.
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
