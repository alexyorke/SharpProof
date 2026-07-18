<#
.SYNOPSIS
Reports maintained production LOC against the 20,000-line reduction baseline.

.DESCRIPTION
The metric counts tracked handwritten production C#, tracked PowerShell build
and generator scripts, and the explicit maintained production specifications
recorded by the baseline. Tests, generated C#, documentation, fixtures, and
build output are excluded.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Json,

    [Parameter()]
    [ValidateRange(0, 1000000)]
    [int]$RequiredReductionLines = 0,

    [Parameter()]
    [switch]$EnforceTarget
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repoPrefix = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$baselinePath = Join-Path $PSScriptRoot 'production-reduction-baseline.json'
$productionMetricsPath = Join-Path $PSScriptRoot 'Get-SharpProofProductionMetrics.ps1'
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path is outside the repository root: $fullPath"
    }

    return $fullPath.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-TrackedLineCount
{
    param(
        [Parameter(Mandatory = $true)][string[]]$Paths,
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.HashSet[string]]$TrackedPaths
    )

    $lines = 0
    $files = 0
    foreach ($path in $Paths)
    {
        $repoPath = $path.Replace('\', '/')
        if (-not $TrackedPaths.Contains($repoPath))
        {
            throw "Maintained production path is not tracked: $repoPath"
        }

        $fullPath = Join-Path $repoRoot $repoPath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf))
        {
            throw "Maintained production path does not exist: $repoPath"
        }

        $lines += (Get-Content -LiteralPath $fullPath | Measure-Object -Line).Lines
        $files++
    }

    return [pscustomobject]@{ files = $files; lines = $lines }
}

Push-Location $repoRoot
try
{
    $trackedPathValues = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }

    $trackedPaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $trackedPathValues)
    {
        [void]$trackedPaths.Add($path.Replace('\', '/'))
    }

    $production = & $productionMetricsPath -Json | ConvertFrom-Json
    $productionCSharp = [pscustomobject]@{
        files = [int]$production.handwrittenFiles
        lines = [int]$production.handwrittenLines
    }

    $scriptRoot = ([string]$baseline.scriptRoot).TrimEnd('/') + '/'
    $scriptPaths = @($trackedPathValues | Where-Object {
        $normalized = $_.Replace('\', '/')
        $normalized.StartsWith($scriptRoot, [System.StringComparison]::Ordinal) -and
            $normalized.EndsWith('.ps1', [System.StringComparison]::OrdinalIgnoreCase)
    })
    $scripts = Get-TrackedLineCount -Paths $scriptPaths -TrackedPaths $trackedPaths
    $specifications = Get-TrackedLineCount `
        -Paths @($baseline.specificationPaths | ForEach-Object { [string]$_ }) `
        -TrackedPaths $trackedPaths

    $currentLines = $productionCSharp.lines + $scripts.lines + $specifications.lines
    $baselineLines = [int]$baseline.maintainedProductionLines
    $targetReduction = [int]$baseline.targetReductionLines
    $maximumLines = [int]$baseline.maximumMaintainedProductionLines
    if ($maximumLines -ne $baselineLines - $targetReduction)
    {
        throw 'Production reduction baseline has an inconsistent maximum line count.'
    }

    $reduction = $baselineLines - $currentLines
    $requiredReduction = if ($EnforceTarget) { $targetReduction } else { $RequiredReductionLines }
    $report = [ordered]@{
        schemaVersion = 1
        baseline = Convert-ToRepoPath $baselinePath
        baselineCommit = [string]$baseline.baselineCommit
        baselineLines = $baselineLines
        targetReductionLines = $targetReduction
        maximumMaintainedProductionLines = $maximumLines
        current = [ordered]@{
            files = $productionCSharp.files + $scripts.files + $specifications.files
            lines = $currentLines
            productionCSharp = $productionCSharp
            scripts = $scripts
            specifications = $specifications
        }
        reductionLines = $reduction
        remainingLines = [Math]::Max(0, $targetReduction - $reduction)
        requiredReductionLines = $requiredReduction
        meetsRequiredReduction = $reduction -ge $requiredReduction
        meetsTarget = $currentLines -le $maximumLines
    }

    if ($Json)
    {
        $report | ConvertTo-Json -Depth 5
    }
    else
    {
        "Maintained production LOC: $currentLines"
        "Reduction from baseline: $reduction"
        "Remaining to 20,000-line target: $($report.remainingLines)"
        "C#: $($productionCSharp.lines); scripts: $($scripts.lines); specifications: $($specifications.lines)"
    }

    if (-not $report.meetsRequiredReduction)
    {
        Write-Error "Required production reduction is $requiredReduction lines; current reduction is $reduction."
        exit 1
    }
}
finally
{
    Pop-Location
}
