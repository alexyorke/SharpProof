[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Gate regressions require the canonical lint container.'
}
$scratchRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-lint-probes-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratchRoot) | Out-Null
$previousDotnetHome = $env:DOTNET_CLI_HOME
$env:DOTNET_CLI_HOME = Join-Path $scratchRoot 'dotnet-home'

function Write-Probe([string]$RelativePath, [string]$Content) {
    $path = Join-Path $scratchRoot $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content)
    return $path
}

function Assert-NativeRejection([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$Expected) {
    $output = @(& $Executable @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -or ($output -join "`n") -notmatch $Expected) {
        throw "$Name did not reject the intended defect (exit $exitCode): $($output -join ' ')"
    }
    Write-Host "$Name rejects its negative fixture."
}

Push-Location $scratchRoot
try {
    $workflow = Write-Probe 'invalid.yml' "on: push`njobs:`n  test:`n    runs-on: ubuntu-latest`n    steps:`n      - run: echo '`$`{{ invalid.context }}'`n"
    Assert-NativeRejection 'actionlint' 'actionlint' @('-oneline', $workflow) 'expression'
    $unsafeWorkflow = Write-Probe 'unsafe.yml' @'
name: Unsafe fixture
on: pull_request
permissions:
  contents: read
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - run: echo "${{ github.event.pull_request.title }}"
'@
    Assert-NativeRejection 'zizmor' 'zizmor' @('--offline', '--format', 'plain', $unsafeWorkflow) 'template-injection'
    $shell = Write-Probe 'invalid.sh' "#!/bin/sh`necho `$word`n"
    Assert-NativeRejection 'ShellCheck' 'shellcheck' @('--format=gcc', $shell) 'SC2086'
    $markdown = Write-Probe 'invalid.md' "#Missing heading space`n"
    Assert-NativeRejection 'markdownlint' 'node' @((Join-Path $repositoryRoot 'eng/lint/markdownlint.mjs'), $markdown) 'MD018'
    $validMarkdown = Write-Probe 'valid.md' @'
# Math

Inline $x$.

$$
x^2
$$
'@
    [IO.File]::AppendAllText($validMarkdown, "`n")
    $output = @(& node (Join-Path $repositoryRoot 'eng/lint/markdownlint.mjs') $validMarkdown 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Valid Markdown/math failed: $($output -join ' ')" }
    $spelling = Write-Probe 'spelling.md' "# Zzxqvspellmistakke`n"
    Assert-NativeRejection 'CSpell' 'cspell' @('lint', '--config', (Join-Path $repositoryRoot 'cspell.json'), '--no-progress', $spelling) 'Unknown word'
    $powershell = Write-Probe 'invalid.ps1' '$PID = 1'
    Import-Module PSScriptAnalyzer -RequiredVersion 1.25.0
    $findings = @(Invoke-ScriptAnalyzer -Path $powershell -Settings (Join-Path $repositoryRoot 'eng/lint/PSScriptAnalyzerSettings.psd1'))
    if (@($findings | Where-Object RuleName -eq 'PSAvoidAssignmentToAutomaticVariable').Count -eq 0) {
        throw 'PSScriptAnalyzer accepted assignment to an automatic variable.'
    }
    Write-Host 'PSScriptAnalyzer rejects its negative fixture.'

    # Exercise the production runner too: native findings must become a failed
    # structured result and a nonzero process exit, not just console output.
    $runnerRoot = Join-Path $scratchRoot 'runner'
    Write-Probe 'runner/invalid.sh' "#!/bin/sh`necho `$word`n" | Out-Null
    $runnerPath = Write-Probe 'runner/scripts/Invoke-SharpProofLint.ps1' (
        Get-Content (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofLint.ps1') -Raw)
    & git init --quiet $runnerRoot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize runner regression repository.' }
    Assert-NativeRejection 'Lint runner' 'pwsh' @('-NoLogo', '-NoProfile', '-File', $runnerPath, '-Category', 'Shell') 'Repository quality checks failed'
    $runnerResults = @(Get-Content (Join-Path $runnerRoot 'artifacts/lint/results.json') -Raw | ConvertFrom-Json)
    if ($runnerResults.Count -ne 1 -or $runnerResults[0].passed) { throw 'Runner did not preserve the failed result.' }

    $compose = Get-Content (Join-Path $repositoryRoot 'compose.yaml') -Raw
    $invalidCompose = Write-Probe 'invalid-compose.yaml' $compose.Replace(
        '        TOOLING_IMAGE: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}',
        '        TOOLING_IMAGE: unrelated:latest')
    Assert-NativeRejection 'Lint container authority' 'pwsh' @(
        '-NoLogo', '-NoProfile', '-File', (Join-Path $repositoryRoot 'scripts/Test-SharpProofContainerContract.ps1'),
        '-ComposePath', $invalidCompose, '-AuthorityOnly') 'Lint base'

    foreach ($version in @('baseline', 'current')) {
        $project = Write-Probe "$version/ApiProbe.csproj" '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnablePackageValidation>true</EnablePackageValidation></PropertyGroup></Project>'
        $code = if ($version -eq 'baseline') { 'public class Contract { public void Existing() {} }' } else { 'public class Contract {}' }
        Write-Probe "$version/Contract.cs" $code | Out-Null
        $output = @(& dotnet restore $project --configfile (Join-Path $repositoryRoot 'NuGet.Config') 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
        $output = @(& dotnet pack $project --configuration Release --no-restore --output (Join-Path $scratchRoot "$version-packages") 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    }
    $baseline = @(Get-ChildItem (Join-Path $scratchRoot 'baseline-packages') -Filter '*.nupkg')
    $current = @(Get-ChildItem (Join-Path $scratchRoot 'current-packages') -Filter '*.nupkg')
    if ($baseline.Count -ne 1 -or $current.Count -ne 1) { throw 'API fixtures did not produce exactly two packages.' }
    $output = @(& apicompat package $baseline[0].FullName --baseline-package $baseline[0].FullName 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Identical API packages failed: $($output -join ' ')" }
    Assert-NativeRejection 'ApiCompat' 'apicompat' @('package', $current[0].FullName, '--baseline-package', $baseline[0].FullName) 'CP0002'
}
finally {
    Pop-Location
    $env:DOTNET_CLI_HOME = $previousDotnetHome
}
