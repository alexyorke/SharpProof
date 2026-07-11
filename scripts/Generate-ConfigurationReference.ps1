[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configKeysPath = Join-Path $repositoryRoot "SharpProof.Analyzer/Configuration/ConfigKeys.cs"
$registryPath = Join-Path $repositoryRoot "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs"
$outputPath = Join-Path $repositoryRoot "docs/configuration-reference.md"

function Normalize-Text {
    param([string]$Text)

    $normalized = $Text.Replace("`r`n", "`n")
    return $normalized.TrimEnd("`r`n".ToCharArray()) + "`n"
}

function Get-ConstantValues {
    param([string]$Source)

    $values = @{}
    $pattern = 'public\s+const\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"(?<value>[^"]*)"\s*;'
    foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($Source, $pattern)) {
        $values[$match.Groups["name"].Value] = $match.Groups["value"].Value
    }

    return $values
}

function Get-BalancedArguments {
    param(
        [string]$Source,
        [int]$OpenIndex
    )

    $depth = 0
    $inString = $false
    $escaped = $false
    $closeIndex = -1
    for ($index = $OpenIndex; $index -lt $Source.Length; $index++) {
        $character = $Source[$index]
        if ($inString) {
            if ($escaped) {
                $escaped = $false
            }
            elseif ($character -eq '\') {
                $escaped = $true
            }
            elseif ($character -eq '"') {
                $inString = $false
            }

            continue
        }

        if ($character -eq '"') {
            $inString = $true
        }
        elseif ($character -eq '(') {
            $depth++
        }
        elseif ($character -eq ')') {
            $depth--
            if ($depth -eq 0) {
                $closeIndex = $index
                break
            }
        }
    }

    if ($closeIndex -lt 0) {
        throw "Could not find the end of a registry option constructor."
    }

    $body = $Source.Substring($OpenIndex + 1, $closeIndex - $OpenIndex - 1)
    $arguments = New-Object System.Collections.Generic.List[string]
    $start = 0
    $nestedDepth = 0
    $inString = $false
    $escaped = $false
    for ($index = 0; $index -lt $body.Length; $index++) {
        $character = $body[$index]
        if ($inString) {
            if ($escaped) {
                $escaped = $false
            }
            elseif ($character -eq '\') {
                $escaped = $true
            }
            elseif ($character -eq '"') {
                $inString = $false
            }

            continue
        }

        if ($character -eq '"') {
            $inString = $true
        }
        elseif ($character -eq '(') {
            $nestedDepth++
        }
        elseif ($character -eq ')') {
            $nestedDepth--
        }
        elseif ($character -eq ',' -and $nestedDepth -eq 0) {
            $arguments.Add($body.Substring($start, $index - $start).Trim())
            $start = $index + 1
        }
    }

    $arguments.Add($body.Substring($start).Trim())
    return @($arguments)
}

function Convert-CSharpString {
    param([string]$Expression)

    $trimmed = $Expression.Trim()
    if ($trimmed -eq "string.Empty") {
        return ""
    }

    if ($trimmed -match '^"(?<value>[^"]*)"$') {
        return $Matches["value"]
    }

    throw "Expected a string expression, found '$Expression'."
}

function Get-RegistryOptions {
    param(
        [string]$ConfigKeysSource,
        [string]$RegistrySource
    )

    $constants = Get-ConstantValues -Source $ConfigKeysSource
    $needle = "new AnalyzerConfigurationOption("
    $options = New-Object System.Collections.Generic.List[object]
    $searchIndex = 0
    while (($startIndex = $RegistrySource.IndexOf($needle, $searchIndex, [System.StringComparison]::Ordinal)) -ge 0) {
        $openIndex = $startIndex + $needle.Length - 1
        $arguments = Get-BalancedArguments -Source $RegistrySource -OpenIndex $openIndex
        if ($arguments.Count -lt 5) {
            throw "AnalyzerConfigurationOption at offset $startIndex has too few arguments."
        }

        $constantExpression = $arguments[0].Trim()
        if ($constantExpression -notmatch '^ConfigKeys\.(?<name>[A-Za-z_][A-Za-z0-9_]*)$') {
            throw "Unexpected ConfigKeys expression '$constantExpression'."
        }

        $constantName = $Matches["name"]
        if (-not $constants.ContainsKey($constantName)) {
            throw "Registry references missing ConfigKeys member '$constantName'."
        }

        $allowedValues = @()
        if ($arguments.Count -ge 6) {
            $allowedValues = @(
                [System.Text.RegularExpressions.Regex]::Matches($arguments[5], '"(?<value>[^"]*)"') |
                    ForEach-Object { $_.Groups["value"].Value })
        }

        $scope = $arguments[1].Trim() -replace '^AnalyzerConfigurationScope\.', ''
        $valueKind = $arguments[2].Trim() -replace '^AnalyzerConfigurationValueKind\.', ''
        $defaultValue = Convert-CSharpString -Expression $arguments[3]
        $description = Convert-CSharpString -Expression $arguments[4]

        $options.Add([pscustomobject]@{
                Name = $constantName
                Key = $constants[$constantName]
                Scope = $scope
                ValueKind = $valueKind
                DefaultValue = $defaultValue
                Description = $description
                AllowedValues = $allowedValues
            })

        $searchIndex = $startIndex + $needle.Length
    }

    if ($options.Count -eq 0) {
        throw "No analyzer configuration options were found."
    }

    return @($options | Sort-Object Key)
}

function Get-ValueDescription {
    param($Option)

    switch ($Option.ValueKind) {
        "Bool" { return 'boolean (`true` or `false`)' }
        "StringList" { return '`;`, `,`, or newline-delimited values' }
        "NonNegativeInteger" { return "non-negative integer" }
        "PositiveInteger" { return "positive integer" }
        default {
            if ($Option.AllowedValues.Count -ne 0) {
                return ($Option.AllowedValues | ForEach-Object { [char]96 + $_ + [char]96 }) -join ", "
            }

            return "value accepted by the analyzer parser"
        }
    }
}

function Get-DefaultDescription {
    param($Option)

    if ($Option.DefaultValue -ne "mode default") {
        if ($Option.Name -eq "RuntimeHazardMode" -and $Option.DefaultValue -eq "off") {
            return "none"
        }

        return $Option.DefaultValue
    }

    switch ($Option.Name) {
        "SmtTimeoutMs" { return "mode default: 750 ms (bounded/off), 2000 ms (deep)" }
        "SmtMethodBudgetMs" { return "mode default: 5000 ms (bounded/off), 15000 ms (deep)" }
        "SmtMaxPathConditions" { return "mode default: 192 (bounded/off), 512 (deep)" }
        "SmtMaxExpressionNodes" { return "mode default: 2048 (bounded/off), 8192 (deep)" }
        default { return "mode default" }
    }
}

function Get-RelatedDiagnostics {
    param($Option)

    $feature = switch ($Option.Name) {
        { $_ -in @("KnownImpureMethods", "KnownPureMethods", "KnownImpureNamespaces", "KnownImpureTypes", "PurityProfile") } { "SP0002"; break }
        { $_ -like "SuggestMissingEnforcePure*" } { "SP0004"; break }
        "EmitExplanations" { "SP0009"; break }
        "ReportBclFallbackGuesses" { "SP0012"; break }
        "RuntimeHazardMode" { "SP0010, SP0011, SP0033"; break }
        { $_ -in @("SuppressProvenDiagnostics", "SuppressionDiagnosticIds") } { "SPS0001-SPS0018"; break }
        "ReportExceptions" { "SP0010"; break }
        "CheckedExceptions" { "SP0011"; break }
        "EnableEffectSummaryJson" { "SP0002, SP0010, SP0011"; break }
        { $_ -like "Smt*" } { "SMT-backed proof results"; break }
        default { "configuration consumers"; break }
    }

    return "$feature; SP0025 for invalid values"
}

function Get-SampleValue {
    param($Option)

    switch ($Option.ValueKind) {
        "Bool" { return $Option.DefaultValue }
        "StringList" {
            if ($Option.Name -eq "AttributeStubNamespaces") {
                return "SharpProof.Attributes; My.Contracts"
            }

            return "Demo.Namespace.Member"
        }
        "NonNegativeInteger" { return "3" }
        "PositiveInteger" { return "1000" }
        "PurityProfile" { return "balanced" }
        "MissingPuritySuggestionScope" { return "public" }
        "RuntimeHazardMode" { return "all" }
        "SmtMode" { return "deep" }
        default { return $Option.DefaultValue }
    }
}

function Get-ScopeDescription {
    param([string]$Scope)

    switch ($Scope) {
        "GlobalOnly" { return "Global-only" }
        "GlobalAndTree" { return "Global and per-tree" }
        "TreeOnly" { return "Per-tree" }
        default { throw "Unknown configuration scope '$Scope'." }
    }
}

function Build-Reference {
    param($Options)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine("# Analyzer configuration reference")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("<!-- Generated from ConfigKeys.cs and AnalyzerConfigurationOptionRegistry.cs by scripts/Generate-ConfigurationReference.ps1. -->")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('SharpProof reads these `sharpproof_*` analyzer options from global AnalyzerConfig and, where noted, per-tree `.editorconfig` sections. Invalid values are reported as `SP0025`; they do not silently change the effective configuration.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('Options that alter purity classification policy, plus non-configuration trust sources and precedence, are audited in [Purity Classification Policy](purity-policy.md).')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("## Option reference")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("| Key | Scope | Valid values | Default | Related diagnostics | Description |")
    [void]$builder.AppendLine("| --- | --- | --- | --- | --- | --- |")
    foreach ($option in $Options) {
        $description = $option.Description.Replace("|", "\\|")
        $key = [char]96 + $option.Key + [char]96
        $default = [char]96 + (Get-DefaultDescription -Option $option) + [char]96
        [void]$builder.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f
                $key,
                (Get-ScopeDescription -Scope $option.Scope),
                (Get-ValueDescription -Option $option),
                $default,
                (Get-RelatedDiagnostics -Option $option),
                $description))
    }

    [void]$builder.AppendLine()
    [void]$builder.AppendLine("## Global AnalyzerConfig example")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('Global-only options must be set in a global AnalyzerConfig file. Global-and-tree options can also be set here as defaults before a matching `.editorconfig` override.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('```ini')
    [void]$builder.AppendLine("is_global = true")
    foreach ($option in $Options | Where-Object { $_.Scope -in @("GlobalOnly", "GlobalAndTree") }) {
        [void]$builder.AppendLine(("{0} = {1}" -f $option.Key, (Get-SampleValue -Option $option)))
    }
    [void]$builder.AppendLine('```')

    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## Per-tree `.editorconfig` example')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('Only global-and-tree options can be overridden in a per-tree section. Global-only options placed in such a section are invalid and produce `SP0025`.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('```ini')
    [void]$builder.AppendLine("root = true")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("[src/**/*.cs]")
    foreach ($option in $Options | Where-Object { $_.Scope -eq "GlobalAndTree" }) {
        [void]$builder.AppendLine(("{0} = {1}" -f $option.Key, (Get-SampleValue -Option $option)))
    }
    [void]$builder.AppendLine('```')

    return Normalize-Text -Text $builder.ToString()
}

$options = Get-RegistryOptions `
    -ConfigKeysSource (Get-Content -LiteralPath $configKeysPath -Raw) `
    -RegistrySource (Get-Content -LiteralPath $registryPath -Raw)
$generated = Build-Reference -Options $options

if ($Verify) {
    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "$($outputPath) is missing. Run .\scripts\Generate-ConfigurationReference.ps1."
    }

    $existing = Normalize-Text -Text (Get-Content -LiteralPath $outputPath -Raw)
    if (-not [string]::Equals($existing, $generated, [System.StringComparison]::Ordinal)) {
        throw "$($outputPath) is stale. Run .\scripts\Generate-ConfigurationReference.ps1."
    }

    Write-Host "Generated configuration reference is up to date."
    return
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outputPath, $generated, $utf8NoBom)
Write-Host "Regenerated docs/configuration-reference.md."
