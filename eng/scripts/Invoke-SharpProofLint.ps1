[CmdletBinding()]
param(
    [ValidateSet('All', 'Workflows', 'PowerShell', 'Shell', 'Markdown', 'Spelling', 'Links', 'ToolDependencies', 'Compatibility', 'GateRegressions')]
    [string]$Category = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$selectedCategory = $Category
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repositoryRoot
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Lint requires the canonical Linux amd64 lint container.'
}
$reportRoot = Join-Path $repositoryRoot 'artifacts/lint'
[IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$sourceFiles = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(& git ls-files --cached --others --exclude-standard))
if ($LASTEXITCODE -ne 0 -or $sourceFiles.Length -eq 0) { throw 'Lint requires a nonempty Git source inventory.' }
$sourceFiles = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($sourceFiles | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }))
if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'eng/arcade-source.json')) {
    & (Join-Path $PSScriptRoot 'Test-SharpProofRepositoryLayout.ps1')
}
$sourceFiles = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($sourceFiles | Where-Object { -not $_.StartsWith('eng/common/', [StringComparison]::Ordinal) }))
$checks = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()

function Invoke-LintNative([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

function Invoke-LintCheck([string]$Name, [scriptblock]$Check) {
    if ($selectedCategory -ne 'All' -and $selectedCategory -ne $Name -and
        -not ($selectedCategory -eq 'Workflows' -and $Name -eq 'WorkflowSecurity')) { return }
    $passed = $true
    try {
        & { & $Check; Write-Output "$Name passed." } *>&1 |
            Tee-Object -FilePath (Join-Path $reportRoot "$Name.log")
    }
    catch {
        $passed = $false
        $_.ToString() | Tee-Object -FilePath (Join-Path $reportRoot "$Name.log") -Append | Write-Host
    }
    $checks.Add(([ordered]@{ check = $Name; passed = $passed }).AsReadOnly())
}

Invoke-LintCheck 'Workflows' { Invoke-LintNative 'actionlint' @('-oneline') }
Invoke-LintCheck 'WorkflowSecurity' {
    # Plain output preserves finding exit codes; SARIF mode does not.
    Invoke-LintNative 'zizmor' @('--offline', '--format', 'plain', '--config', 'eng/lint/zizmor.yml', '.github')
}
Invoke-LintCheck 'PowerShell' {
    Import-Module PSScriptAnalyzer -RequiredVersion 1.25.0
    $paths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($sourceFiles | Where-Object { $_ -match '\.ps[md]?1$' }))
    if ($paths.Length -eq 0) { throw 'No PowerShell inputs found.' }
    $findings = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($paths | ForEach-Object {
                Invoke-ScriptAnalyzer -Path $_ -Settings eng/lint/PSScriptAnalyzerSettings.psd1
            }))
    $findings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $reportRoot 'powershell.json')
    if ($findings.Length -gt 0) {
        $findings | Format-Table ScriptName, Line, Severity, RuleName, Message -Wrap
        throw "PSScriptAnalyzer reported $($findings.Length) findings."
    }
}
Invoke-LintCheck 'Shell' {
    $paths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($sourceFiles | Where-Object { $_ -match '\.sh$' }))
    if ($paths.Length -eq 0) { throw 'No shell inputs found.' }
    Invoke-LintNative 'shellcheck' (@('--format=gcc') + [string[]]@($paths))
}
Invoke-LintCheck 'Markdown' { Invoke-LintNative 'node' @('eng/lint/markdownlint.mjs') }
Invoke-LintCheck 'Spelling' { Invoke-LintNative 'cspell' @('lint', '--config', 'cspell.json', '--no-progress', '--no-cache') }
Invoke-LintCheck 'Links' {
    Invoke-LintNative 'sh' @('eng/scripts/Test-SharpProofLinks.sh', (Join-Path $reportRoot 'links.md'))
}
Invoke-LintCheck 'ToolDependencies' {
    Invoke-LintNative 'node' @(
        '/usr/local/lib/node_modules/npm/bin/npm-cli.js', 'audit',
        '--prefix', '/opt/sharpproof/lint', '--omit=dev', '--audit-level=low',
        '--cache', (Join-Path ([IO.Path]::GetTempPath()) 'sharpproof-npm-cache'))
}
Invoke-LintCheck 'Compatibility' { & (Join-Path $PSScriptRoot 'Test-SharpProofPackageCompatibility.ps1') }
Invoke-LintCheck 'GateRegressions' { & (Join-Path $PSScriptRoot 'Test-SharpProofLint.ps1') }

$checkSnapshot = $checks.ToImmutable()
$checkSnapshot | ConvertTo-Json | Set-Content (Join-Path $reportRoot 'results.json')
$summary = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$summary.AddRange([string[]]@('# Repository quality checks', '', '| Check | Result |', '| --- | --- |'))
foreach ($check in $checkSnapshot) {
    $result = if ($check.passed) { 'PASS' } else { 'FAIL' }
    $summary.Add("| $($check.check) | $result |")
}
$summary.ToImmutable() | Set-Content (Join-Path $reportRoot 'summary.md')
if ($checkSnapshot.Length -eq 0 -or @($checkSnapshot | Where-Object { -not $_.passed }).Count -gt 0) {
    throw 'Repository quality checks failed. See artifacts/lint for individual reports.'
}
