<#
.SYNOPSIS
Reports raw SMT migration hotspots.

.DESCRIPTION
This is a read-only migration inventory. Analyzer hotspots are intentionally
narrower than a generic text search: using SmtAnalysisService or
SmtAnalysisOptions is not a hotspot. Symbolic public surfaces identify current
API debt where backend SmtFormula types still leak through the public .NET API.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

$categories = @(
    [pscustomobject]@{
        name = 'condition-translator'
        needles = @('CSharpConditionToFormula.')
    },
    [pscustomobject]@{
        name = 'formula-construction'
        needles = @(
            'new SmtBinaryFormula',
            'new SmtUnaryFormula',
            'new SmtIntegerConstant',
            'new SmtNullConstant',
            'new SmtBooleanConstant',
            'new SmtVariable',
            'new SmtIntegerBinaryTerm',
            'new SmtIntegerUnaryTerm',
            'new SmtStringLengthTerm',
            'new SmtStringConcatTerm',
            'new SmtStringContainsFormula',
            'new SmtStringStartsWithFormula',
            'new SmtStringEndsWithFormula',
            'new SmtRegexMatchFormula',
            'new SmtRuntimeTypeTestFormula',
            'new SmtConditionalFormula'
        )
    }
)

function Get-AnalyzerHotspots
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'PurelySharp.Analyzer') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/'
        } |
        Sort-Object FullName

    $hotspots = @()
    foreach ($file in $files)
    {
        $source = Get-Content -LiteralPath $file.FullName -Raw
        $matchedCategories = @()
        $matchCount = 0

        foreach ($category in $categories)
        {
            $categoryCount = 0
            foreach ($needle in $category.needles)
            {
                $index = 0
                while ($index -lt $source.Length)
                {
                    $found = $source.IndexOf($needle, $index, [System.StringComparison]::Ordinal)
                    if ($found -lt 0)
                    {
                        break
                    }

                    $categoryCount++
                    $index = $found + $needle.Length
                }
            }

            if ($categoryCount -gt 0)
            {
                $matchedCategories += $category.name
                $matchCount += $categoryCount
            }
        }

        if ($matchCount -gt 0)
        {
            $hotspots += [pscustomobject]@{
                path = Convert-ToRepoPath $file.FullName
                matchCount = $matchCount
                categories = @($matchedCategories)
            }
        }
    }

    return [pscustomobject]@{
        totalFiles = $files.Count
        hotspots = @($hotspots)
    }
}

function Get-SymbolicPublicFormulaSurfaces
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'PurelySharp.Symbolic') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/'
        } |
        Sort-Object FullName

    $surfaces = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('public', [System.StringComparison]::Ordinal) -ge 0 -and
                $line.IndexOf('SmtFormula', [System.StringComparison]::Ordinal) -ge 0)
            {
                $surfaces += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    text = $line.Trim()
                }
            }
        }
    }

    return @($surfaces)
}

Push-Location $repoRoot
try
{
    $analyzer = Get-AnalyzerHotspots
    $publicFormulaSurfaces = Get-SymbolicPublicFormulaSurfaces

    $report = [ordered]@{
        schemaVersion = 1
        module = 'Analyzer'
        totalFiles = $analyzer.totalFiles
        hotspotCount = $analyzer.hotspots.Count
        hotspots = @($analyzer.hotspots)
        symbolicPublicFormulaSurfaceCount = $publicFormulaSurfaces.Count
        symbolicPublicFormulaSurfaces = @($publicFormulaSurfaces)
    }

    if ($Json)
    {
        $report | ConvertTo-Json -Depth 5
        exit 0
    }

    "Analyzer raw SMT hotspots: $($report.hotspotCount) files"
    "Symbolic public SmtFormula surfaces: $($report.symbolicPublicFormulaSurfaceCount) lines"
    ''
    $analyzer.hotspots | Format-Table -AutoSize | Out-String
    $publicFormulaSurfaces | Format-Table -AutoSize | Out-String
}
finally
{
    Pop-Location
}
