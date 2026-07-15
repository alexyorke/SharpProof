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
. (Join-Path $PSScriptRoot 'SharpProofSourceInventory.ps1')

function Get-ProductionSourceFiles
{
    param([Parameter(Mandatory = $true)][string]$RelativeRoot)

    return Get-SharpProofProductionSourceFiles `
        -RepositoryRoot $repoRoot `
        -SearchRoot (Join-Path $repoRoot $RelativeRoot)
}

function Find-SourceLineMatches
{
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Files,
        [string[]]$AllNeedles = @(),
        [scriptblock]$Classify
    )

    $matches = @()
    foreach ($file in $Files)
    {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName)
        {
            $lineNumber++
            $matched = $true
            foreach ($needle in $AllNeedles)
            {
                if ($line.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { continue }

                $matched = $false
                break
            }
            if (-not $matched) { continue }

            $extraProperties = if ($Classify) { & $Classify $line } else { [ordered]@{} }
            if ($null -eq $extraProperties) { continue }

            $properties = [ordered]@{
                path = $file.RepoPath
                line = $lineNumber
            }
            foreach ($entry in $extraProperties.GetEnumerator())
            {
                $properties[$entry.Key] = $entry.Value
            }
            $properties.text = $line.Trim()
            $matches += [pscustomobject]$properties
        }
    }

    return @($matches)
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
    $files = Get-ProductionSourceFiles 'SharpProof.Analyzer'

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
                path = $file.RepoPath
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
    return @(Find-SourceLineMatches `
        -Files (Get-ProductionSourceFiles 'SharpProof.Analyzer') `
        -AllNeedles 'CSharpSmtFormulaTranslator.')
}

function Get-SymbolicPublicFormulaSurfaces
{
    return @(Find-SourceLineMatches `
        -Files (Get-ProductionSourceFiles 'SharpProof.Symbolic') `
        -AllNeedles @('public', 'SmtFormula'))
}

function Get-SymbolicCompatibilitySurfaces
{
    $files = Get-ProductionSourceFiles 'SharpProof.Symbolic' |
        Where-Object {
            $_.RepoPath -notmatch '^SharpProof\.Symbolic/Ir/'
        }

    return @(Find-SourceLineMatches -Files $files -AllNeedles 'public' -Classify {
        param($line)

        if ($line -notmatch '\b(HasSmtFormula|FormulaKind|FormulaText)\b') { return $null }

        return [ordered]@{ category = 'formula-metadata' }
    })
}

function Get-SymbolicDirectTranslatorUsage
{
    $files = Get-ProductionSourceFiles 'SharpProof.Symbolic' |
        Where-Object {
            $_.RepoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpConditionToFormula' -and
                $_.RepoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpSmtFormulaTranslator\.cs$'
        }

    return @(Find-SourceLineMatches -Files $files -AllNeedles 'CSharpConditionToFormula.')
}

function Get-SymbolicTranslatorShimUsage
{
    $files = Get-ProductionSourceFiles 'SharpProof.Symbolic' |
        Where-Object {
            $_.RepoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpConditionToFormula' -and
                $_.RepoPath -notmatch '^SharpProof\.Symbolic/Smt/CSharpSmtFormulaTranslator\.cs$'
        }

    return @(Find-SourceLineMatches -Files $files -AllNeedles 'CSharpSmtFormulaTranslator.')
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
    return @(Find-SourceLineMatches `
        -Files (Get-ProductionSourceFiles 'SharpProof.Symbolic\Ir') `
        -Classify {
            param($line)

            if ($line -notmatch
                '(?:new|private static readonly) KnownApiLoweringDescriptor<(SymbolicCondition|SymbolicTerm)>')
            {
                return $null
            }

            return [ordered]@{
                kind = if ($Matches[1] -eq 'SymbolicCondition') { 'condition' } else { 'term' }
            }
        })
}

function Get-RuntimeHazardFormulaFallbackLocations
{
    $files = Get-ProductionSourceFiles 'SharpProof.Symbolic' |
        Where-Object {
            $_.RepoPath -match '^SharpProof\.Symbolic/SymbolicRuntimeHazard' -or
                $_.RepoPath -eq 'SharpProof.Symbolic/SymbolicReachabilityService.cs'
        }

    return @(Find-SourceLineMatches `
        -Files $files `
        -AllNeedles @('ir.runtime-hazard.', 'formula-fallback'))
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
