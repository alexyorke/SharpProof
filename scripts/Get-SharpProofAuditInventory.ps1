[CmdletBinding()]
param(
    [switch]$Json,
    [int]$Top = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot

$productionRoots = @(
    'SharpProof.Analyzer',
    'SharpProof.Symbolic',
    'SharpProof.ProofCore',
    'SharpProof.CodeFixes',
    'SharpProof.Attributes',
    'Shared',
    'Tools\SharpProof.SymbolicCli',
    'Tools\SharpProof.EffectSummary',
    'Tools\SharpProof.Fuzz.Core'
) | ForEach-Object { Join-Path $repositoryRoot $_ }

$markerPatterns = @(
    'formula-fallback',
    'unknown_external_call',
    'BclFallbackGuess',
    'unsupported',
    'conservative',
    'NotSupportedException',
    'probably_pure',
    'probably_impure'
)

function Get-RiskClass {
    param(
        [string]$RelativePath,
        [string[]]$Markers
    )

    if ($RelativePath -like 'SharpProof.Symbolic/*') {
        $symbolicPath = $RelativePath.Substring('SharpProof.Symbolic/'.Length)
        if ($symbolicPath -match 'QueryService|QueryResult|Capability|Complexity|RuntimeHazardQuery') {
            return 'public-result-cli'
        }

        if ($symbolicPath -match 'RuntimeHazard|Proof|Reachability|ProgramPoint|CSharpConditionToFormula|Smt') {
            return 'proof-fallback'
        }
    }

    if ($RelativePath -like 'SharpProof.Analyzer/*') {
        if ($RelativePath -match 'Engine|Method|Rule|ExceptionFlow|Analyzer') {
            return 'analyzer-verdict'
        }
    }

    if ($RelativePath -like 'Tools/SharpProof.SymbolicCli/*') {
        return 'public-result-cli'
    }

    if ($RelativePath -like 'SharpProof.Package/*' -or
        $RelativePath -like 'SharpProof.Vsix/*' -or
        $RelativePath -like 'SharpProof.Attributes/*' -or
        $RelativePath -like 'SharpProof.CodeFixes/*' -or
        $RelativePath -like 'Tools/SharpProof.EffectSummary/*') {
        return 'packaging-consumer'
    }

    if ($Markers.Count -gt 0) {
        return 'proof-fallback'
    }

    return 'low-risk-utility'
}

$files = foreach ($root in $productionRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
        continue
    }

    Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        ForEach-Object {
        $fullPath = $_.FullName
        $repoPrefix = $repositoryRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Path is outside the repository root: $fullPath"
        }

        $relativePath = $fullPath.Substring($repositoryRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $content = Get-Content -LiteralPath $fullPath -Raw
        $markers = @(
            foreach ($pattern in $markerPatterns) {
                if ($content -match [regex]::Escape($pattern)) {
                    $pattern
                }
            }
        )

        [pscustomobject]@{
            Path = $relativePath
            Module = ($relativePath -split '/')[0]
            Lines = (Get-Content -LiteralPath $fullPath | Measure-Object -Line).Lines
            RiskClass = Get-RiskClass -RelativePath $relativePath -Markers $markers
            MarkerHits = $markers
            MarkerCount = $markers.Count
        }
    }
}

$ordered = $files |
    Sort-Object @{ Expression = 'MarkerCount'; Descending = $true }, @{ Expression = 'Lines'; Descending = $true }, Path

$summary = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    totalFiles = @($ordered).Count
    riskClasses = $ordered |
        Group-Object RiskClass |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                riskClass = $_.Name
                files = $_.Count
                lines = ($_.Group | Measure-Object -Property Lines -Sum).Sum
            }
        }
    markerTotals = $markerPatterns | ForEach-Object {
        $pattern = $_
        [pscustomobject]@{
            marker = $pattern
            files = @($ordered | Where-Object { $_.MarkerHits -contains $pattern }).Count
        }
    }
    files = @($ordered | Select-Object -First $Top)
}

if ($Json) {
    $summary | ConvertTo-Json -Depth 6
    return
}

Write-Host "SharpProof audit inventory"
Write-Host "  Total files: $($summary.totalFiles)"
Write-Host "  Top entries: $Top"
Write-Host ""
Write-Host "Risk classes:"
$summary.riskClasses | Format-Table -AutoSize
Write-Host ""
Write-Host "Marker totals:"
$summary.markerTotals | Format-Table -AutoSize
Write-Host ""
Write-Host "Top files:"
$summary.files | Select-Object Path, RiskClass, Lines, MarkerCount, MarkerHits | Format-Table -AutoSize
