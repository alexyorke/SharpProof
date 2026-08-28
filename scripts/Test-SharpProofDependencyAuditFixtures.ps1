[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-dependency-audit-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path `
    (Join-Path $fixture 'scripts'),
    (Join-Path $fixture 'eng/acceptance'),
    (Join-Path $fixture 'artifacts') -Force | Out-Null

try {
    foreach ($name in @(
            'Test-SharpProofDependencyAudit.ps1',
            'SharpProof.ReleaseConfigurationEvidence.psm1')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/$name") `
            -Destination (Join-Path $fixture "scripts/$name")
    }
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Invoke-SharpProofContainer.ps1') `
        -Destination (Join-Path $fixture 'scripts/Invoke-SharpProofContainer.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') `
        -Destination (Join-Path $fixture 'scripts/SharpProof.ContainerExecution.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Assert-SharpProofUniqueJsonProperties.ps1') `
        -Destination (Join-Path $fixture 'scripts/Assert-SharpProofUniqueJsonProperties.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/acceptance/contract.json') `
        -Destination (Join-Path $fixture 'eng/acceptance/contract.json')

    $solutionPath = Join-Path $fixture 'SharpProof.sln'
    $projectPath = Join-Path $fixture 'Project.csproj'
    $configurationPath = Join-Path $fixture 'NuGet.Config'
    $reportPath = Join-Path $fixture 'audit-report.json'
    @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{00000000-0000-0000-0000-000000000000}") = "Project", "Project.csproj", "{11111111-1111-1111-1111-111111111111}"
EndProject
'@ | Set-Content -LiteralPath $solutionPath -Encoding utf8NoBOM
    [IO.File]::WriteAllText($projectPath, '<Project />', [Text.UTF8Encoding]::new($false))
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <auditSources>
    <clear />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </auditSources>
</configuration>
'@ | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
    $absoluteProject = $projectPath.Replace('\', '/')
    ([ordered]@{
            version = 1
            parameters = '--vulnerable --include-transitive'
            sources = @('https://api.nuget.org/v3/index.json')
            projects = @([ordered]@{
                    path = $absoluteProject
                    frameworks = @([ordered]@{
                            framework = 'net9.0'
                            topLevelPackages = @()
                            transitivePackages = @()
                        })
                })
        } | ConvertTo-Json -Depth 8) |
        Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM

    $outputPath = Join-Path $fixture 'artifacts/dependency-audit.json'
    $outerOutputPath = Join-Path $fixture 'artifacts/dependency-audit/dependency-audit.json'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outerOutputPath)) | Out-Null
    [IO.File]::WriteAllText(
        $outerOutputPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $output = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Test-SharpProofDependencyAudit.ps1') `
        -SolutionPath $solutionPath `
        -NuGetConfigurationPath $configurationPath `
        -OutputPath $outputPath `
        -ReportPath $reportPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency audit report fixture failed: $output"
    }
    $evidence = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
    if ([string]$evidence.commit -notmatch '^(?:[0-9a-f]{40}|local-[0-9a-f]{32})$' -or
        [string]::IsNullOrWhiteSpace([string]$evidence.checkedAtUtc) -or
        [string]$evidence.attemptId -notmatch '^(?:[0-9]+/[0-9]+|local-[0-9a-f]{32})$') {
        throw 'Dependency audit evidence did not bind commit/time/attempt identity.'
    }

    [IO.File]::WriteAllText(
        $outputPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $missing = & pwsh -NoLogo -NoProfile -File (
        Join-Path $fixture 'scripts/Test-SharpProofDependencyAudit.ps1') `
        -SolutionPath (Join-Path $fixture 'missing.sln') `
        -NuGetConfigurationPath $configurationPath `
        -OutputPath $outputPath 2>&1
    if ($LASTEXITCODE -eq 0 -or (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "Dependency audit stale-output fixture failed: $missing"
    }

    [IO.File]::WriteAllText(
        $outputPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    $oldContainer = $env:SHARPPROOF_CONTAINER
    try {
        $env:SHARPPROOF_CONTAINER = '1'
        $outer = & pwsh -NoLogo -NoProfile -File (
            Join-Path $fixture 'scripts/Invoke-SharpProofContainer.ps1') `
            -Command dependency-audit 2>&1
        if ($LASTEXITCODE -eq 0 -or (Test-Path -LiteralPath $outerOutputPath -PathType Leaf)) {
            throw "Dependency audit outer stale-output fixture failed: $outer"
        }
    }
    finally {
        $env:SHARPPROOF_CONTAINER = $oldContainer
    }
    Write-Host 'Dependency audit evidence fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
