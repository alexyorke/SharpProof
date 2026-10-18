[CmdletBinding()]
param(
    [switch]$VerifyNoChanges,
    [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Script linting and formatting require the canonical Linux container.'
$configurationRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$catalog = Get-Content (Join-Path $configurationRoot 'eng/container/toolchain.json') -Raw | ConvertFrom-Json
Import-Module PSScriptAnalyzer -RequiredVersion $catalog.scriptTools.psscriptAnalyzer.version -Force
$shellcheckVersion = & shellcheck --version
if ($LASTEXITCODE -ne 0 -or $shellcheckVersion -notcontains "version: $($catalog.scriptTools.shellcheck.version)") {
    throw 'ShellCheck does not match the pinned toolchain version.'
}
$shfmtVersion = & shfmt --version
if ($LASTEXITCODE -ne 0 -or $shfmtVersion -cne "v$($catalog.scriptTools.shfmt.version)") {
    throw 'shfmt does not match the pinned toolchain version.'
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$git = [Diagnostics.ProcessStartInfo]::new('git')
$git.WorkingDirectory = $RepositoryRoot
$git.UseShellExecute = $false
$git.RedirectStandardOutput = $true
foreach ($argument in @('ls-files', '-z')) { [void]$git.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($git)
try {
    $tracked = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or [string]::IsNullOrEmpty($tracked)) {
        throw 'Script formatting requires a Git checkout with tracked sources.'
    }
}
finally { $process.Dispose() }

# Arcade requires this pinned upstream tree to remain byte-for-byte intact.
# Check provenance before excluding vendor code from repository style rules.
$arcadeManifest = Get-Content (Join-Path $configurationRoot 'eng/arcade-source.json') -Raw | ConvertFrom-Json
$arcadeFileBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
foreach ($file in $arcadeManifest.commonFiles) {
    if ($arcadeFileBuilder.ContainsKey([string]$file.path)) { throw [ArgumentException]::new('An item with the same key has already been added. Key: ' + $file.path) }
    $arcadeFileBuilder.Add([string]$file.path, [string]$file.sha256)
}
$arcadeFiles = $arcadeFileBuilder.ToImmutable()

$powershellPathBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$shellPathBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($relative in $tracked.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
    if ($relative.StartsWith('eng/common/', [StringComparison]::Ordinal)) {
        if (-not $arcadeFiles.ContainsKey($relative)) { throw "Unpinned Arcade bootstrap file: $relative" }
        $digest = (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($digest -cne $arcadeFiles[$relative]) { throw "Modified Arcade bootstrap file: $relative" }
        continue
    }
    if ([IO.Path]::GetExtension($relative) -in @('.ps1', '.psm1', '.psd1')) {
        $powershellPathBuilder.Add($relative)
        continue
    }
    if ([IO.Path]::GetExtension($relative) -in @('.sh', '.bash')) {
        $shellPathBuilder.Add($relative)
        continue
    }
    # Include shell scripts with no extension (or an unusual one) by shebang.
    $stream = [IO.File]::OpenRead((Join-Path $RepositoryRoot $relative))
    try {
        $prefix = [byte[]]::new(256)
        $length = $stream.Read($prefix, 0, $prefix.Length)
        if ([Text.Encoding]::ASCII.GetString($prefix, 0, $length) -match '^#![^\r\n]*\b(bash|sh|dash|ksh)\b') {
            $shellPathBuilder.Add($relative)
        }
    }
    finally { $stream.Dispose() }
}

$powershellPaths = $powershellPathBuilder.ToImmutable()
$shellPaths = $shellPathBuilder.ToImmutable()
$encoding = [Text.UTF8Encoding]::new($false, $true)
$formatSettings = Join-Path $configurationRoot 'eng/PowerShellFormatting.psd1'
$lintSettings = Join-Path $configurationRoot 'eng/PSScriptAnalyzerSettings.psd1'
$failureBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$changed = 0

function Format-PowerShellSource {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Source)
    if ($Source.Length -eq 0) { return "`n" }
    $formatted = (Invoke-Formatter -ScriptDefinition $Source -Settings $formatSettings).TrimEnd("`r", "`n") + "`n"
    $formatTokens = $null
    $formatErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput($formatted, [ref]$formatTokens, [ref]$formatErrors)
    if ($formatErrors.Count -gt 0) { throw 'PowerShell formatting produced invalid syntax.' }
    # PSScriptAnalyzer can leave spaces after closing braces. Remove them
    # without trimming data inside strings or here-strings.
    $literals = @($formatTokens | Where-Object { $_.Kind -in @('StringLiteral', 'StringExpandable', 'HereStringLiteral', 'HereStringExpandable') })
    $trailingWhitespace = [regex]::Matches($formatted, '(?m)[ \t]+$')
    $builder = [Text.StringBuilder]::new($formatted)
    for ($index = $trailingWhitespace.Count - 1; $index -ge 0; $index--) {
        $whitespace = $trailingWhitespace[$index]
        $insideLiteral = @($literals | Where-Object {
                $_.Extent.StartOffset -le $whitespace.Index -and $_.Extent.EndOffset -gt $whitespace.Index
            }).Count -gt 0
        if (-not $insideLiteral) { [void]$builder.Remove($whitespace.Index, $whitespace.Length) }
    }
    return $builder.ToString()
}

Push-Location -LiteralPath $RepositoryRoot
try {
    foreach ($relative in $powershellPaths) {
        $path = Join-Path $RepositoryRoot $relative
        $original = $encoding.GetString([IO.File]::ReadAllBytes($path))
        $inputText = $original.TrimStart([char]0xFEFF).Replace("`r`n", "`n")
        $parseTokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseInput($inputText, [ref]$parseTokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) {
            foreach ($parseError in $parseErrors) {
                $failureBuilder.Add("${relative}:$($parseError.Extent.StartLineNumber): PowerShell parse error: $($parseError.Message)")
            }
            continue
        }
        $formatted = Format-PowerShellSource -Source $inputText
        if ($original -cne $formatted) {
            if ($VerifyNoChanges) { $failureBuilder.Add("Formatting differs: $relative") }
            else { [IO.File]::WriteAllText($path, $formatted, $encoding); $changed++ }
        }
        foreach ($finding in @(Invoke-ScriptAnalyzer -Path $path -Settings $lintSettings)) {
            $failureBuilder.Add("${relative}:$($finding.Line): $($finding.RuleName): $($finding.Message)")
        }
    }
    if ($shellPaths.Count -gt 0) {
        $shellArguments = [string[]]@($shellPaths)
        & shellcheck --format=gcc -- @shellArguments
        if ($LASTEXITCODE -ne 0) { $failureBuilder.Add('ShellCheck failed.') }
        $shellFormatMode = if ($VerifyNoChanges) { '-d' } else { '-w' }
        & shfmt -i 2 -ci -sr $shellFormatMode -- @shellArguments
        if ($LASTEXITCODE -ne 0) { $failureBuilder.Add('Shell formatting failed.') }
    }
}
finally { Pop-Location }
$failures = $failureBuilder.ToImmutable()
Write-Host "Checked $($powershellPaths.Count) PowerShell files and $($shellPaths.Count) shell scripts; formatted $changed PowerShell files."
if ($failures.Count -gt 0) {
    $failures | Select-Object -First 60 | ForEach-Object { Write-Host $_ }
    throw "Script checks failed with $($failures.Count) findings."
}
