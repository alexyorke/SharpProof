[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configKeysPath = Join-Path $repositoryRoot "SharpProof.Analyzer/Configuration/ConfigKeys.cs"
$registryPath = Join-Path $repositoryRoot "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs"
$outputPath = Join-Path $repositoryRoot "docs/configuration-reference.md"

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

function Convert-ConfigurationDefault {
    param(
        [string]$Expression,
        [string[]]$AllowedValues = @()
    )

    $trimmed = $Expression.Trim()
    if ($trimmed -match '^AnalyzerConfigurationDefault\.ForSmtModes\(\s*(?<bounded>[0-9]+)\s*,\s*(?<deep>[0-9]+)\s*(?:,\s*"(?<unit>[^"]*)"\s*)?\)$') {
        $unit = $Matches["unit"]
        $suffix = if ([string]::IsNullOrEmpty($unit)) { "" } else { " $unit" }
        return "$($Matches['bounded'])$suffix (disabled/bounded), $($Matches['deep'])$suffix (deep)"
    }

    if ($trimmed -match '^string\.Join\("[,] ",\s*ProvenDiagnosticSuppressionOptions\.AllSupportedDiagnosticIds\.OrderBy\(') {
        return @(
            $AllowedValues |
                Where-Object { $_ -ne 'none' } |
                ForEach-Object { $_.ToUpperInvariant() } |
                Sort-Object) -join ', '
    }

    return Convert-CSharpString -Expression $trimmed
}

function Get-RegistryOptions {
    param(
        [string]$ConfigKeysSource,
        [string]$RegistrySource
    )

    $constants = Get-ConstantValues -Source $ConfigKeysSource
    $initializerNeedle = "public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = ImmutableArray.Create("
    $initializerStart = $RegistrySource.IndexOf($initializerNeedle, [System.StringComparison]::Ordinal)
    if ($initializerStart -lt 0) {
        throw "Could not find the analyzer configuration option registry initializer."
    }

    $initializerOpen = $initializerStart + $initializerNeedle.Length - 1
    $registrations = Get-BalancedArguments -Source $RegistrySource -OpenIndex $initializerOpen
    $options = New-Object System.Collections.Generic.List[object]
    foreach ($registration in $registrations) {
        if ($registration -notmatch '^(?<factory>GlobalOption|TreeOption|TreeSuggestionScope|TreeBool|GlobalBool|GlobalPositiveInteger)\s*\(') {
            throw "Unexpected registry option expression '$registration'."
        }

        $factory = $Matches["factory"]
        $openIndex = $registration.IndexOf('(')
        $arguments = Get-BalancedArguments -Source $registration -OpenIndex $openIndex
        $constantExpression = $arguments[0].Trim()
        if ($constantExpression -notmatch '^ConfigKeys\.(?<name>[A-Za-z_][A-Za-z0-9_]*)$') {
            throw "Unexpected ConfigKeys expression '$constantExpression'."
        }

        $constantName = $Matches["name"]
        if (-not $constants.ContainsKey($constantName)) {
            throw "Registry references missing ConfigKeys member '$constantName'."
        }

        $scope = if ($factory.StartsWith("Tree", [System.StringComparison]::Ordinal)) {
            "GlobalAndTree"
        }
        else {
            "GlobalOnly"
        }

        $allowedExpression = $null
        switch ($factory) {
            { $_ -in @("GlobalOption", "TreeOption") } {
                if ($arguments.Count -lt 4) { throw "$factory requires at least four arguments." }
                $valueKind = $arguments[1].Trim() -replace '^AnalyzerConfigurationValueKind\.', ''
                $defaultExpression = $arguments[2]
                $descriptionExpression = $arguments[3]
                if ($arguments.Count -ge 5 -and $arguments[4].Trim().StartsWith("ImmutableArray.Create(",
                        [System.StringComparison]::Ordinal)) {
                    $allowedExpression = $arguments[4]
                }
                break
            }
            "TreeSuggestionScope" {
                $valueKind = "MissingPuritySuggestionScope"
                $defaultExpression = '"all"'
                $descriptionExpression = $arguments[1]
                $allowedExpression = 'ImmutableArray.Create("all", "public", "internal", "off")'
                break
            }
            { $_ -in @("TreeBool", "GlobalBool") } {
                $valueKind = "Bool"
                $defaultExpression = $arguments[1]
                $descriptionExpression = $arguments[2]
                break
            }
            "GlobalPositiveInteger" {
                $valueKind = "PositiveInteger"
                $defaultExpression = $arguments[1]
                $descriptionExpression = $arguments[2]
                break
            }
            default { throw "Unsupported registry option factory '$factory'." }
        }

        $allowedValues = if ($null -eq $allowedExpression) {
            @()
        }
        else {
            @([System.Text.RegularExpressions.Regex]::Matches($allowedExpression, '"(?<value>[^"]*)"') |
                ForEach-Object { $_.Groups["value"].Value })
        }
        $defaultValue = Convert-ConfigurationDefault -Expression $defaultExpression -AllowedValues $allowedValues
        $description = Convert-CSharpString -Expression $descriptionExpression

        $options.Add([pscustomobject]@{
                Name = $constantName
                Key = $constants[$constantName]
                Scope = $scope
                ValueKind = $valueKind
                DefaultValue = $defaultValue
                Description = $description
                AllowedValues = $allowedValues
            })
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
        "StructuralMemberKeyList" { return 'canonical `spm1\|...` keys delimited by `;`, `,`, or newlines; property keys end in `.get` or `.set`' }
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

    return $Option.DefaultValue
}

function Get-RelatedDiagnostics {
    param($Option)

    $feature = switch ($Option.Name) {
        { $_ -in @("KnownImpureMethods", "KnownPureMethods", "KnownImpureNamespaces", "KnownImpureTypes", "PurityProfile") } { "SP0002"; break }
        "TrustedBoundaryReviewMode" { "SP0040"; break }
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
        "StructuralMemberKeyList" {
            return "spm1|RGVtby5OYW1lc3BhY2UuVHlwZQ==|b3JkaW5hcnk=|TWVtYmVy|0|0|bm9uZQ==|bmFtZWQ6U3lzdGVtLlZvaWQ="
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

    return ConvertTo-SharpProofGeneratedText -Text $builder.ToString()
}

$options = Get-RegistryOptions `
    -ConfigKeysSource (Get-Content -LiteralPath $configKeysPath -Raw) `
    -RegistrySource (Get-Content -LiteralPath $registryPath -Raw)
$generated = Build-Reference -Options $options

Update-SharpProofGeneratedFile `
    -Path $outputPath `
    -Content $generated `
    -DisplayPath 'docs/configuration-reference.md' `
    -GeneratorCommand '.\scripts\Generate-ConfigurationReference.ps1' `
    -Verify:$Verify

if ($Verify) {
    Write-Host "Generated configuration reference is up to date."
    return
}

Write-Host "Regenerated docs/configuration-reference.md."
