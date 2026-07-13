<#
.SYNOPSIS
Reports raw SMT migration hotspots.

.DESCRIPTION
This is a read-only migration inventory. Analyzer hotspots are intentionally
narrower than a generic text search: using SmtAnalysisService or
SmtAnalysisOptions is not a hotspot. Symbolic public surfaces identify current
API debt where backend SmtFormula types or legacy formula-shaped result
metadata still leak through the public .NET API.
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
    $repoPrefix = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path is outside the repository root: $fullPath"
    }

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
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Analyzer') -Recurse -Filter '*.cs' |
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

function Get-AnalyzerTranslatorShimUsage
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Analyzer') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/'
        } |
        Sort-Object FullName

    $usages = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('CSharpSmtFormulaTranslator.', [System.StringComparison]::Ordinal) -ge 0)
            {
                $usages += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    text = $line.Trim()
                }
            }
        }
    }

    return @($usages)
}

function Get-SymbolicPublicFormulaSurfaces
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic') -Recurse -Filter '*.cs' |
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

function Get-SymbolicCompatibilitySurfaces
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/' -and
                $repoPath -notmatch '^SharpProof\.Symbolic/Ir/'
        } |
        Sort-Object FullName

    $patterns = @(
        [pscustomobject]@{
            category = 'formula-metadata'
            regex = '\b(HasSmtFormula|FormulaKind|FormulaText)\b'
        }
    )

    $surfaces = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('public', [System.StringComparison]::Ordinal) -lt 0)
            {
                continue
            }

            foreach ($pattern in $patterns)
            {
                if ($line -match $pattern.regex)
                {
                    $surfaces += [pscustomobject]@{
                        path = Convert-ToRepoPath $file.FullName
                        line = $lineNumber
                        category = $pattern.category
                        text = $line.Trim()
                    }
                }
            }
        }
    }

    return @($surfaces)
}

function Get-SymbolicDirectTranslatorUsage
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/' -and
                $repoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpConditionToFormula' -and
                $repoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpSmtFormulaTranslator\.cs$'
        } |
        Sort-Object FullName

    $usages = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('CSharpConditionToFormula.', [System.StringComparison]::Ordinal) -ge 0)
            {
                $usages += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    text = $line.Trim()
                }
            }
        }
    }

    return @($usages)
}

function Get-SymbolicTranslatorShimUsage
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/' -and
                $repoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpConditionToFormula' -and
                $repoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpSmtFormulaTranslator\.cs$'
        } |
        Sort-Object FullName

    $usages = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('CSharpSmtFormulaTranslator.', [System.StringComparison]::Ordinal) -ge 0)
            {
                $usages += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    text = $line.Trim()
                }
            }
        }
    }

    return @($usages)
}

function Get-SymbolicTranslatorShimFamilies
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Usages
    )

    function Get-FamilyName
    {
        param([Parameter(Mandatory = $true)][string]$Text)

        if ($Text.IndexOf('TryCollectDomainFacts(', [System.StringComparison]::Ordinal) -ge 0 -or
            $Text.IndexOf('TryCollectBranchAssumptions(', [System.StringComparison]::Ordinal) -ge 0)
        {
            return 'branch-facts'
        }

        if ($Text.IndexOf('TryCollectPatternBindingFacts(', [System.StringComparison]::Ordinal) -ge 0 -or
            $Text.IndexOf('TryTranslatePattern(', [System.StringComparison]::Ordinal) -ge 0)
        {
            return 'pattern'
        }

        if ($Text.IndexOf('TryTranslateValueWithPathFacts(', [System.StringComparison]::Ordinal) -ge 0)
        {
            return 'path-fact-value'
        }

        if ($Text.IndexOf('TryTranslateValue(', [System.StringComparison]::Ordinal) -ge 0)
        {
            return 'value'
        }

        if ($Text.IndexOf('TryTranslate(', [System.StringComparison]::Ordinal) -ge 0)
        {
            return 'condition'
        }

        return 'other'
    }

    return @(
        $Usages |
            Group-Object -Property { Get-FamilyName $_.Text } |
            Sort-Object Name |
            ForEach-Object {
                [pscustomobject]@{
                    family = $_.Name
                    count = $_.Count
                    paths = @(
                        $_.Group |
                            ForEach-Object { $_.path } |
                            Sort-Object -Unique
                    )
                }
            })
}

function Get-IrKnownApiLoweringLocations
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic\Ir') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/'
        } |
        Sort-Object FullName

    $locations = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            $descriptorKind = $null
            if ($line.IndexOf(
                    'new KnownApiLoweringDescriptor<SymbolicCondition>',
                    [System.StringComparison]::Ordinal) -ge 0)
            {
                $descriptorKind = 'condition'
            }
            elseif ($line.IndexOf(
                    'new KnownApiLoweringDescriptor<SymbolicTerm>',
                    [System.StringComparison]::Ordinal) -ge 0)
            {
                $descriptorKind = 'term'
            }

            if ($descriptorKind)
            {
                $locations += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    kind = $descriptorKind
                    text = $line.Trim()
                }
            }
        }
    }

    return @($locations)
}

function Get-RuntimeHazardFormulaFallbackLocations
{
    $files = Get-ChildItem -Path (Join-Path $repoRoot 'SharpProof.Symbolic') -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/' -and
                ($repoPath -match '^SharpProof\.Symbolic/SymbolicRuntimeHazard' -or
                    $repoPath -eq 'SharpProof.Symbolic/SymbolicReachabilityService.cs')
        } |
        Sort-Object FullName

    $locations = @()
    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            if ($line.IndexOf('ir.runtime-hazard.', [System.StringComparison]::Ordinal) -ge 0 -and
                $line.IndexOf('formula-fallback', [System.StringComparison]::Ordinal) -ge 0)
            {
                $locations += [pscustomobject]@{
                    path = Convert-ToRepoPath $file.FullName
                    line = $lineNumber
                    text = $line.Trim()
                }
            }
        }
    }

    return @($locations)
}

Push-Location $repoRoot
try
{
    $analyzer = Get-AnalyzerHotspots
    $analyzerTranslatorShimUsages = @(Get-AnalyzerTranslatorShimUsage)
    $publicFormulaSurfaces = @(Get-SymbolicPublicFormulaSurfaces)
    $compatibilitySurfaces = @(Get-SymbolicCompatibilitySurfaces)
    $symbolicDirectTranslatorUsages = @(Get-SymbolicDirectTranslatorUsage)
    $symbolicTranslatorShimUsages = @(Get-SymbolicTranslatorShimUsage)
    $symbolicTranslatorShimFamilies = @(Get-SymbolicTranslatorShimFamilies -Usages $symbolicTranslatorShimUsages)
    $irKnownApiLoweringLocations = @(Get-IrKnownApiLoweringLocations)
    $irKnownApiConditionLoweringLocations = @($irKnownApiLoweringLocations | Where-Object { $_.kind -eq 'condition' })
    $irKnownApiTermLoweringLocations = @($irKnownApiLoweringLocations | Where-Object { $_.kind -eq 'term' })
    $runtimeHazardFormulaFallbackLocations = @(Get-RuntimeHazardFormulaFallbackLocations)

    $report = [ordered]@{
        schemaVersion = 1
        module = 'Analyzer'
        totalFiles = $analyzer.totalFiles
        hotspotCount = $analyzer.hotspots.Count
        hotspots = @($analyzer.hotspots)
        analyzerTranslatorShimUsageCount = $analyzerTranslatorShimUsages.Count
        analyzerTranslatorShimUsages = @($analyzerTranslatorShimUsages)
        symbolicPublicFormulaSurfaceCount = $publicFormulaSurfaces.Count
        symbolicPublicFormulaSurfaces = @($publicFormulaSurfaces)
        symbolicCompatibilitySurfaceCount = $compatibilitySurfaces.Count
        symbolicCompatibilitySurfaces = @($compatibilitySurfaces)
        symbolicDirectTranslatorUsageCount = $symbolicDirectTranslatorUsages.Count
        symbolicDirectTranslatorUsages = @($symbolicDirectTranslatorUsages)
        symbolicTranslatorShimUsageCount = $symbolicTranslatorShimUsages.Count
        symbolicTranslatorShimUsages = @($symbolicTranslatorShimUsages)
        symbolicTranslatorShimFamilyCount = $symbolicTranslatorShimFamilies.Count
        symbolicTranslatorShimFamilies = @($symbolicTranslatorShimFamilies)
        irKnownApiLoweringCount = $irKnownApiLoweringLocations.Count
        irKnownApiConditionLoweringCount = $irKnownApiConditionLoweringLocations.Count
        irKnownApiTermLoweringCount = $irKnownApiTermLoweringLocations.Count
        irKnownApiLoweringLocations = @($irKnownApiLoweringLocations)
        runtimeHazardFormulaFallbackCount = $runtimeHazardFormulaFallbackLocations.Count
        runtimeHazardFormulaFallbackLocations = @($runtimeHazardFormulaFallbackLocations)
    }

    if ($Json)
    {
        $report | ConvertTo-Json -Depth 5
        exit 0
    }

    "Analyzer raw SMT hotspots: $($report.hotspotCount) files"
    "Analyzer CSharpSmtFormulaTranslator shim usages: $($report.analyzerTranslatorShimUsageCount) lines"
    "Symbolic public SmtFormula surfaces: $($report.symbolicPublicFormulaSurfaceCount) lines"
    "Symbolic formula-shaped compatibility surfaces: $($report.symbolicCompatibilitySurfaceCount) lines"
    "Symbolic direct CSharpConditionToFormula usages: $($report.symbolicDirectTranslatorUsageCount) lines"
    "Symbolic CSharpSmtFormulaTranslator shim usages: $($report.symbolicTranslatorShimUsageCount) lines"
    "Symbolic CSharpSmtFormulaTranslator shim families: $($report.symbolicTranslatorShimFamilyCount)"
    "IR known API lowering descriptors: $($report.irKnownApiLoweringCount) entries ($($report.irKnownApiConditionLoweringCount) condition, $($report.irKnownApiTermLoweringCount) term)"
    "Runtime-hazard formula fallback provenances: $($report.runtimeHazardFormulaFallbackCount) lines"
    ''
    $analyzer.hotspots | Format-Table -AutoSize | Out-String
    $analyzerTranslatorShimUsages | Format-Table -AutoSize | Out-String
    $publicFormulaSurfaces | Format-Table -AutoSize | Out-String
    $compatibilitySurfaces | Format-Table -AutoSize | Out-String
    $symbolicDirectTranslatorUsages | Format-Table -AutoSize | Out-String
    $symbolicTranslatorShimUsages | Format-Table -AutoSize | Out-String
    $symbolicTranslatorShimFamilies | Format-Table -AutoSize | Out-String
    $irKnownApiLoweringLocations | Format-Table -AutoSize | Out-String
    $runtimeHazardFormulaFallbackLocations | Format-Table -AutoSize | Out-String
}
finally
{
    Pop-Location
}
