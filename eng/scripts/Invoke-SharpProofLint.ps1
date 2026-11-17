[CmdletBinding()]
param(
    [ValidateSet('All', 'Workflows', 'Markdown', 'Spelling', 'Links', 'ToolDependencies', 'Compatibility')]
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
# The repository lane runs `tooling layout` itself, and `tooling format-check`
# (build lane) owns PSScriptAnalyzer and ShellCheck.
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

Invoke-LintCheck 'Workflows' {
    Invoke-LintNative 'actionlint' @('-oneline')
}
Invoke-LintCheck 'WorkflowSecurity' {
    # Plain output preserves finding exit codes; SARIF mode does not.
    Invoke-LintNative 'zizmor' @('--offline', '--format', 'plain', '--config', 'eng/lint/zizmor.yml', '.github')
}
Invoke-LintCheck 'Markdown' { Invoke-LintNative 'node' @('eng/lint/markdownlint.mjs') }
Invoke-LintCheck 'Spelling' { Invoke-LintNative 'cspell' @('lint', '--config', 'cspell.json', '--no-progress', '--no-cache') }
Invoke-LintCheck 'Links' {
    Invoke-LintNative 'sh' @('eng/scripts/Test-SharpProofLinks.sh', (Join-Path $reportRoot 'links.md'))
}
Invoke-LintCheck 'ToolDependencies' {
    # Every advisory fails unless eng/lint/audit-exceptions.json lists it with a
    # reason and an expiry date. An expired or unused exception fails too.
    $report = & node '/usr/local/lib/node_modules/npm/bin/npm-cli.js' 'audit' `
        '--prefix' '/opt/sharpproof/lint' '--omit=dev' '--audit-level=low' '--json' `
        '--cache' (Join-Path ([IO.Path]::GetTempPath()) 'sharpproof-npm-cache')
    $audit = ($report -join "`n") | ConvertFrom-Json -AsHashtable
    if ($null -eq $audit -or -not $audit.ContainsKey('vulnerabilities')) { throw 'npm audit produced no report.' }
    $found = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($vulnerability in $audit['vulnerabilities'].Values) {
        foreach ($via in $vulnerability['via']) {
            if ($via -is [Collections.IDictionary]) {
                if ([string]$via['url'] -notmatch '/advisories/(GHSA-[a-z0-9-]+)$') { throw "Unrecognized advisory: $($via['url'])" }
                $found.Add("$($Matches[1])|$($via['name'])") | Out-Null
            }
        }
    }
    $today = [DateTime]::UtcNow.Date
    $accepted = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($exception in @(Get-Content -Raw 'eng/lint/audit-exceptions.json' | ConvertFrom-Json)) {
        if ([string]::IsNullOrWhiteSpace($exception.reason)) { throw "Audit exception $($exception.advisory) has no reason." }
        $expires = [DateTime]::ParseExact($exception.expires, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        if ($expires -lt $today) { throw "Audit exception $($exception.advisory) expired on $($exception.expires)." }
        $accepted.Add("$($exception.advisory)|$($exception.package)") | Out-Null
    }
    $unexpected = @($found | Where-Object { -not $accepted.Contains($_) })
    $unused = @($accepted | Where-Object { -not $found.Contains($_) })
    foreach ($entry in $found) { Write-Output "Advisory: $entry$(if ($accepted.Contains($entry)) { ' (reviewed exception)' })" }
    if ($unexpected.Count -gt 0) { throw "Unreviewed tool dependency advisories: $($unexpected -join ', ')" }
    if ($unused.Count -gt 0) { throw "Audit exceptions no longer needed: $($unused -join ', ')" }
}
Invoke-LintCheck 'Compatibility' { & (Join-Path $PSScriptRoot 'Test-SharpProofPackageCompatibility.ps1') }

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
