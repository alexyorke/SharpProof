<#
.SYNOPSIS
Reports analyzer files that still construct raw SMT formulas or call the
legacy C# condition translator directly.

.DESCRIPTION
This is a read-only migration inventory. It is intentionally narrower than a
generic text search: using SmtAnalysisService or SmtAnalysisOptions is not a
hotspot. The remaining hotspots are places that should eventually lower to
Symbolic IR facts and use shared proof services instead.
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

Push-Location $repoRoot
try
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

    $report = [ordered]@{
        schemaVersion = 1
        module = 'Analyzer'
        totalFiles = $files.Count
        hotspotCount = $hotspots.Count
        hotspots = @($hotspots)
    }

    if ($Json)
    {
        $report | ConvertTo-Json -Depth 5
        exit 0
    }

    "Analyzer raw SMT hotspots: $($report.hotspotCount) files"
    ''
    $hotspots | Format-Table -AutoSize | Out-String
}
finally
{
    Pop-Location
}
