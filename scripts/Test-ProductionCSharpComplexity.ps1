[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CSharpSourceMetrics.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
$contract = Get-Content -LiteralPath $contractPath -Raw |
    ConvertFrom-Json
if ($null -eq $contract.productionComplexity) {
    throw 'The active acceptance contract must define productionComplexity.'
}
$maximumExpressionNodes =
    [int]$contract.productionComplexity.maximumExpressionNodes
$maximumDecisionPoints =
    [int]$contract.productionComplexity.maximumDecisionPoints
$maximumMembers =
    [int]$contract.productionComplexity.maximumMembers
$limits = @(
    $maximumExpressionNodes,
    $maximumDecisionPoints,
    $maximumMembers
)
if (@($limits | Where-Object { $_ -le 0 }).Count -ne 0) {
    throw 'Production-complexity ceilings must be positive.'
}
$ceilingRationale =
    [string]$contract.productionComplexity.ceilingRationale
if ([string]::IsNullOrWhiteSpace($ceilingRationale)) {
    throw 'Production-complexity ceilings require an architectural rationale.'
}
$ceilingBinding =
    "ceilings:$maximumExpressionNodes/$maximumDecisionPoints/$maximumMembers"
if ($ceilingRationale.IndexOf(
        $ceilingBinding,
        [StringComparison]::Ordinal) -lt 0) {
    throw (
        'Production-complexity rationale must bind the exact current ' +
        "limits with '$ceilingBinding'.")
}
$compactProbe = @'
class Probe { int Absolute(int value) { if (value >= 0) return value; return -value; } }
'@
$readableProbe = @'
class Probe
{
    int Absolute(int value)
    {
        if (value >= 0)
        {
            return value;
        }

        return -value;
    }
}
'@
$compactMetrics = Measure-CSharpSourceText `
    -Source $compactProbe `
    -Path '<compact-formatting-probe>'
$readableMetrics = Measure-CSharpSourceText `
    -Source $readableProbe `
    -Path '<readable-formatting-probe>'
foreach ($metric in @('expressionNodes', 'decisionPoints', 'members')) {
    if ($compactMetrics.$metric -ne $readableMetrics.$metric) {
        throw "Source-complexity metric '$metric' is formatting-sensitive."
    }
}

$inventoryScript = Join-Path $repositoryRoot 'scripts/Get-SharpProofProductionInventory.ps1'
$inventoryJson = & $inventoryScript -RepositoryRoot $repositoryRoot -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'Production inventory authority could not be derived for complexity.'
}
$inventory = ($inventoryJson -join [Environment]::NewLine) | ConvertFrom-Json
$projects = @($inventory.projects | Sort-Object name)
$roots = @($projects | ForEach-Object { [string]$_.name + '/' })
$fileOptions = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
$generatedByPath = [Collections.Generic.Dictionary[string, bool]]::new(
    [StringComparer]::Ordinal)
foreach ($project in $projects) {
    foreach ($file in @($project.compile)) {
        $path = [string]$file.path
        if (-not $path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($fileOptions.ContainsKey($path)) {
            $existing = $fileOptions[$path] | ConvertTo-Json -Compress
            $current = $project.parseOptions | ConvertTo-Json -Compress
            if ($existing -cne $current) {
                throw "Shared Compile source has conflicting parse options: '$path'."
            }
        }
        else {
            $fileOptions[$path] = $project.parseOptions
        }
        if ($generatedByPath.ContainsKey($path) -and
            $generatedByPath[$path] -ne [bool]$file.generated) {
            throw "Shared Compile source has conflicting generated classification: '$path'."
        }
        $generatedByPath[$path] = [bool]$file.generated
    }
}
$files = @($fileOptions.Keys | Sort-Object)
if ($files.Count -eq 0) {
    throw 'Production inventory has no evaluated C# Compile items.'
}
$approvedGeneratedFiles = @(
    $generatedByPath.Keys |
        Where-Object { $generatedByPath[$_] } |
        Sort-Object)
$generatedCandidates = @($approvedGeneratedFiles)
$unapprovedGeneratedFiles = @()
$missingGeneratedFiles = @()
Push-Location $repositoryRoot
try {
    $physicalLines = 0
    $nonblankLines = 0
    $syntaxTokens = 0
    $syntaxNodes = 0
    $expressionNodes = 0
    $decisionPoints = 0
    $members = 0
    $handwrittenFiles = 0
    foreach ($path in $files) {
        $source = Get-Content -LiteralPath $path -Raw
        if ($path.Replace('\', '/') -in $approvedGeneratedFiles) {
            continue
        }

        $parseOptions = New-SharpProofCSharpParseOptions -LanguageVersion ([string]$fileOptions[$path].languageVersion) -PreprocessorSymbols @($fileOptions[$path].preprocessorSymbols | ForEach-Object { [string]$_ })
        $metrics = Measure-CSharpSourceText -Source $source -Path $path -ParseOptions $parseOptions
        $lines = @(Get-Content -LiteralPath $path)
        $physicalLines += $lines.Count
        $nonblankLines += @($lines | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        }).Count
        $syntaxTokens += $metrics.syntaxTokens
        $syntaxNodes += $metrics.syntaxNodes
        $expressionNodes += $metrics.expressionNodes
        $decisionPoints += $metrics.decisionPoints
        $members += $metrics.members
        $handwrittenFiles++
    }

    $passed =
        $expressionNodes -le $maximumExpressionNodes -and
        $decisionPoints -le $maximumDecisionPoints -and
        $members -le $maximumMembers
    $result = [ordered]@{
        schemaVersion = 1
        contract = 'eng/acceptance/contract.json'
        measurement = 'Roslyn expressions, decisions, and members'
        roots = $roots
        authority = [ordered]@{
            commit = [string]$inventory.commit
            sourceUniverseSha256 = [string]$inventory.sourceUniverseSha256
            generatedManifestSha256 = [string]$inventory.generatedManifestSha256
        }
        exclusions = [ordered]@{
            generatedFiles = $approvedGeneratedFiles
        }
        files = $handwrittenFiles
        expressionNodes = $expressionNodes
        maximumExpressionNodes = $maximumExpressionNodes
        decisionPoints = $decisionPoints
        maximumDecisionPoints = $maximumDecisionPoints
        members = $members
        maximumMembers = $maximumMembers
        informationalPhysicalLines = $physicalLines
        informationalNonblankLines = $nonblankLines
        informationalSyntaxTokens = $syntaxTokens
        informationalSyntaxNodes = $syntaxNodes
        formattingInvariantProbe = $true
        passed = $passed
    }

    if ($Json) {
        $result | ConvertTo-Json -Depth 4
    }
    else {
        "Production C# files: $handwrittenFiles"
        "Expression nodes: $expressionNodes (maximum $maximumExpressionNodes)"
        "Decision points: $decisionPoints (maximum $maximumDecisionPoints)"
        "Members: $members (maximum $maximumMembers)"
        "Syntax tokens (informational only): $syntaxTokens"
        "Syntax nodes (informational only): $syntaxNodes"
        "Physical lines (informational only): $physicalLines"
        "Nonblank lines (informational only): $nonblankLines"
    }

    if (-not $passed) {
        throw (
            'Production C# structural-complexity limits were exceeded: ' +
            "expressions $expressionNodes/$maximumExpressionNodes; " +
            "decisions $decisionPoints/$maximumDecisionPoints; " +
            "members $members/$maximumMembers.")
    }
}
finally {
    Pop-Location
}
