[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-changed-tests-' + [Guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $fixture 'scripts'
$fixtureTests = Join-Path $fixture 'SharpProof.New.Test'
New-Item -ItemType Directory -Path $fixtureScripts, $fixtureTests -Force |
    Out-Null

try {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Invoke-SharpProofChangedTests.ps1') `
        -Destination (Join-Path $fixtureScripts 'Invoke-SharpProofChangedTests.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') `
        -Destination (Join-Path $fixtureScripts 'SharpProof.ContainerExecution.psm1')
    New-Item -ItemType Directory -Path (Join-Path $fixture 'eng/acceptance') -Force |
        Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $fixture 'eng/acceptance/contract.json'),
        '{"automation":{"testProjectCpuDivisor":1}}' + "`n")
    [IO.File]::WriteAllText(
        (Join-Path $fixture 'SharpProof.sln'),
        "baseline solution`n")
    New-Item -ItemType Directory -Path (
        Join-Path $fixture 'SharpProof.Core.Test') -Force | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $fixture 'SharpProof.Core.Test/SharpProof.Core.Test.csproj'),
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
    & git -C $fixture init --quiet
    & git -C $fixture config user.email 'fixture@example.invalid'
    & git -C $fixture config user.name 'SharpProof fixture'
    & git -C $fixture add --all
    & git -C $fixture commit --quiet -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the changed-test fixture.' }

    [IO.File]::AppendAllText(
        (Join-Path $fixture 'SharpProof.sln'),
        "solution changed`n")
    [IO.File]::WriteAllText(
        (Join-Path $fixtureTests 'SharpProof.New.Test.csproj'),
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')

    $oldContainer = $env:SHARPPROOF_CONTAINER
    try {
        $env:SHARPPROOF_CONTAINER = '1'
        $output = @(& pwsh -NoLogo -NoProfile -File (
                Join-Path $fixtureScripts 'Invoke-SharpProofChangedTests.ps1') `
                -ComparisonRef HEAD -PlanOnly 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $env:SHARPPROOF_CONTAINER = $oldContainer
    }
    $newProject = 'SharpProof.New.Test\SharpProof.New.Test.csproj'
    if ($exitCode -ne 0 -or
        @($output | Where-Object { $_.ToString().Contains($newProject) }).Count -eq 0) {
        throw "Untracked test project was omitted from changed-test planning: $($output -join [Environment]::NewLine)"
    }

    Write-Host 'Changed-test untracked-project fixture passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
