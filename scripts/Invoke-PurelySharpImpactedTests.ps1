<#
.SYNOPSIS
Recommends or runs a conservative PurelySharp test filter for changed files.

.DESCRIPTION
This helper maps changed repository files to likely impacted NUnit fixtures and
passes the generated VSTest filter to Invoke-PurelySharpTests.ps1. It is a local
iteration aid only; it intentionally falls back to the full suite for shared
test infrastructure, build graph changes, high-fanout analyzer core files,
unmapped files, or generated filters that are too large.

.EXAMPLE
.\scripts\Invoke-PurelySharpImpactedTests.ps1 -ListOnly

.EXAMPLE
.\scripts\Invoke-PurelySharpImpactedTests.ps1 -ListOnly -Json -ChangedFile PurelySharp.Test\SemanticOracleSmtTests.cs

.EXAMPLE
.\scripts\Invoke-PurelySharpImpactedTests.ps1 -NoBuild -Workers 20 -FailFast
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$BaseRef = '',

    [Parameter()]
    [bool]$IncludeUncommitted = $true,

    [Parameter()]
    [string[]]$ChangedFile = @(),

    [Parameter()]
    [switch]$ForcePartial,

    [Parameter()]
    [switch]$ListOnly,

    [Parameter()]
    [switch]$Json,

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [switch]$NoBuild,

    [Parameter()]
    [switch]$FailFast,

    [Parameter()]
    [ValidateRange(0, 256)]
    [int]$Workers = 0,

    [Parameter()]
    [switch]$Profile,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$Top = 30,

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotnetTestArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Json -and -not $ListOnly)
{
    throw '-Json is only supported with -ListOnly because normal mode streams test output.'
}

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Replace('\', '/')
    while ($normalized.StartsWith('./', [StringComparison]::Ordinal))
    {
        $normalized = $normalized.Substring(2)
    }

    return $normalized.TrimStart('/')
}

function Resolve-BaseRef
{
    param([string]$RequestedBaseRef)

    if (-not [string]::IsNullOrWhiteSpace($RequestedBaseRef))
    {
        return $RequestedBaseRef
    }

    return 'HEAD'
}

function Get-ChangedRepoFiles
{
    param(
        [string]$RequestedBaseRef,
        [bool]$IncludeUncommitted,
        [string[]]$ExplicitChangedFiles
    )

    if ($ExplicitChangedFiles.Count -gt 0)
    {
        return $ExplicitChangedFiles | ForEach-Object { Convert-ToRepoPath $_ } | Sort-Object -Unique
    }

    $base = Resolve-BaseRef $RequestedBaseRef
    $mergeBase = (& git merge-base HEAD $base 2>$null)
    if ([string]::IsNullOrWhiteSpace($mergeBase))
    {
        $mergeBase = $base
    }

    $files = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in (& git diff --name-only $mergeBase --))
    {
        if (-not [string]::IsNullOrWhiteSpace($file))
        {
            [void]$files.Add((Convert-ToRepoPath $file))
        }
    }

    if ($IncludeUncommitted)
    {
        foreach ($file in (& git diff --name-only --cached --))
        {
            if (-not [string]::IsNullOrWhiteSpace($file))
            {
                [void]$files.Add((Convert-ToRepoPath $file))
            }
        }

        foreach ($file in (& git diff --name-only --))
        {
            if (-not [string]::IsNullOrWhiteSpace($file))
            {
                [void]$files.Add((Convert-ToRepoPath $file))
            }
        }

        foreach ($file in (& git ls-files --others --exclude-standard))
        {
            if (-not [string]::IsNullOrWhiteSpace($file))
            {
                [void]$files.Add((Convert-ToRepoPath $file))
            }
        }
    }

    return $files | Sort-Object
}

function Add-TestClass
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$ClassName
    )

    if (-not [string]::IsNullOrWhiteSpace($ClassName))
    {
        [void]$Set.Add($ClassName)
    }
}

function Add-TestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [string[]]$ClassNames
    )

    foreach ($className in $ClassNames)
    {
        Add-TestClass -Set $Set -ClassName $className
    }
}

function Get-TestClassFromFile
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $name = $name -replace '\.Helpers$', ''
    $name = $name -replace '\+Test$', ''
    if ($name.Contains('`'))
    {
        return ''
    }

    if ($name.EndsWith('Tests', [StringComparison]::Ordinal))
    {
        return $name
    }

    return ''
}

function Get-TypeSearchTokens
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $tokens = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ($stem.Length -ge 5)
    {
        [void]$tokens.Add($stem)
    }

    if (Test-Path -LiteralPath $Path)
    {
        $text = Get-Content -LiteralPath $Path -Raw
        foreach ($match in [regex]::Matches($text, '\b(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)'))
        {
            $token = $match.Groups[1].Value
            if ($token.Length -ge 5)
            {
                [void]$tokens.Add($token)
            }
        }
    }

    return $tokens | Where-Object {
        $_ -notin @(
            'Program',
            'Options',
            'Builder',
            'Factory',
            'Helper',
            'Helpers',
            'Extensions',
            'Constants')
    }
}

function Add-TestFilesReferencingTokens
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Tokens
    )

    foreach ($token in $Tokens)
    {
        $matches = @(& rg -l -F $token PurelySharp.Test -g '*.cs' 2>$null)
        foreach ($match in $matches)
        {
            $className = Get-TestClassFromFile (Convert-ToRepoPath $match)
            Add-TestClass -Set $Set -ClassName $className
        }
    }
}

function Add-PathMappedTests
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    switch -Regex ($Path)
    {
        '^SearchLib/' {
            Add-TestClasses $Set @(
                'SearchLibZ3SmokeTests',
                'SearchLibPurityProofTests',
                'SearchLibRoslynLoweringTests',
                'SearchLibBackedPurityFlowTests',
                'SmtAnalysisServiceTests',
                'SemanticOracleSmtTests')
            break
        }
        '^PurelySharp\.Symbolic/' {
            Add-TestClasses $Set @(
                'SymbolicSourceQueryLineTests',
                'SymbolicProgramPointFactTests',
                'SmtAnalysisServiceTests',
                'SearchLibZ3SmokeTests',
                'SemanticOracleSmtTests')
            break
        }
        '^Tools/PurelySharp\.SymbolicCli/' {
            Add-TestClasses $Set @('SymbolicSourceQueryLineTests', 'AnalyzerPackagingTests')
            break
        }
        '^Tools/PurelySharp\.Fuzz/' {
            Add-TestClasses $Set @('FuzzToolTests', 'RoslynShapeManifestCoverageTests')
            break
        }
        '^Tools/PurelySharp\.CorpusReport/' {
            Add-TestClasses $Set @('RoslynConstructCoverageTests', 'RoslynShapeManifestCoverageTests')
            break
        }
        '^Tools/PurelySharp\.EffectSummary/' {
            Add-TestClasses $Set @('EffectSummaryToolTests', 'ExceptionSummaryCatalogValidationTests', 'AnalyzerPackagingTests')
            break
        }
        '^PurelySharp\.Package/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests')
            break
        }
        '^PurelySharp\.Vsix/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests', 'AssemblyLoadingTests')
            break
        }
        '^PurelySharp\.CodeFixes/' {
            Add-TestClasses $Set @('PurelySharpCodeFixTests')
            break
        }
        '^PurelySharp\.Attributes/' {
            Add-TestClasses $Set @('AttributeResolutionTests', 'AttributePlacementPurityTests', 'BoundaryAttributeTests', 'BasicPurityTests')
            break
        }
        '^Shared/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests', 'EffectSummaryToolTests', 'ExceptionSummaryCatalogValidationTests')
            break
        }
        '^PurelySharp\.Analyzer/.*(Exception|Throw|Catch|Finally)' {
            Add-TestClasses $Set @(
                'ExceptionReachabilitySmtTests',
                'ExceptionFlowPathFactStressTests',
                'ExceptionFlowPropagationRegressionTests',
                'ExceptionHandlingTests',
                'ExceptionSummaryCatalogValidationTests',
                'RecursiveExceptionFlowTests',
                'SemanticOracleSmtTests')
            break
        }
        '^PurelySharp\.Analyzer/.*(Smt|SemanticOracle|PathFact|Regex|String|Invariant)' {
            Add-TestClasses $Set @(
                'SmtAnalysisServiceTests',
                'SemanticOracleSmtTests',
                'PathSensitiveSmtInvariantTests',
                'ExpressionSmtTranslationTests',
                'ExpressionAtomSmtTests',
                'StringLengthSmtTests',
                'SearchLibZ3SmokeTests')
            break
        }
        '^PurelySharp\.Analyzer/.*(EffectSummary|GeneratedPurity|Catalog|Summary)' {
            Add-TestClasses $Set @(
                'AnalyzerPackagingTests',
                'EffectSummaryToolTests',
                'ExceptionSummaryCatalogValidationTests',
                'DiagnosticEvidenceTests')
            break
        }
    }
}

function Join-TestFilter
{
    param([Parameter(Mandatory = $true)][string[]]$ClassNames)

    return ($ClassNames |
        Sort-Object -Unique |
        ForEach-Object { "FullyQualifiedName~PurelySharp.Test.$_" }) -join '|'
}

function Format-TestWrapperCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('RunFullSuite', 'RunPartial', 'RunPartialForced', 'Skip')]
        [string]$SuggestedAction,

        [string]$Filter,

        [string]$Configuration,

        [bool]$NoBuild,

        [bool]$FailFast,

        [int]$Workers,

        [bool]$Profile,

        [int]$Top,

        [int]$MemoryLimitMb
    )

    if ($SuggestedAction -eq 'Skip')
    {
        return ''
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('.\scripts\Invoke-PurelySharpTests.ps1')
    $parts.Add('-Configuration')
    $parts.Add($Configuration)

    if ($NoBuild)
    {
        $parts.Add('-NoBuild')
    }

    if ($FailFast)
    {
        $parts.Add('-FailFast')
    }

    if ($Workers -gt 0)
    {
        $parts.Add('-Workers')
        $parts.Add([string]$Workers)
    }

    if ($Profile)
    {
        $parts.Add('-Profile')
    }

    if ($Top -ne 30)
    {
        $parts.Add('-Top')
        $parts.Add([string]$Top)
    }

    if ($MemoryLimitMb -gt 0)
    {
        $parts.Add('-MemoryLimitMb')
        $parts.Add([string]$MemoryLimitMb)
    }

    if ($SuggestedAction -ne 'RunFullSuite' -and -not [string]::IsNullOrWhiteSpace($Filter))
    {
        $escapedFilter = $Filter.Replace("'", "''")
        $parts.Add('-Filter')
        $parts.Add("'$escapedFilter'")
    }

    return ($parts -join ' ')
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try
{
    $changedFiles = @(Get-ChangedRepoFiles -RequestedBaseRef $BaseRef -IncludeUncommitted $IncludeUncommitted -ExplicitChangedFiles $ChangedFile)
    if ($changedFiles.Count -eq 0)
    {
        if ($Json)
        {
            [ordered]@{
                changedFiles = @()
                ignoredFiles = @()
                selectedTestFixtures = @()
                testFilter = ''
                requiresFullSuite = $false
                fullSuiteFallbackReasons = @()
                suggestedAction = 'Skip'
                suggestedCommand = ''
                note = 'No changed files detected. No impacted tests to run.'
            } | ConvertTo-Json -Depth 4
        }
        else
        {
            Write-Host 'No changed files detected. No impacted tests to run.'
        }

        exit 0
    }

    $testClasses = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    $fullReasons = New-Object System.Collections.Generic.List[string]
    $ignoredFiles = New-Object System.Collections.Generic.List[string]

    foreach ($path in $changedFiles)
    {
        if ($path -match '^(README\.md|REMAINING_ANALYZER_BACKLOG\.md)$|^docs/')
        {
            $ignoredFiles.Add($path)
            continue
        }

        if ($path -match '^(Directory\.Build\.props|global\.json|PurelySharp\.sln|build.*\.ps1)$')
        {
            $fullReasons.Add("$path changes build or solution shape")
            continue
        }

        if ($path -match '^scripts/Invoke-PurelySharp(Tests|Dotnet|ImpactedTests)\.ps1$')
        {
            Add-TestClasses $testClasses @('SearchLibZ3SmokeTests')
            continue
        }

        if ($path -match '^\.github/workflows/')
        {
            Add-TestClasses $testClasses @('SearchLibZ3SmokeTests')
            continue
        }

        if ($path -match '^PurelySharp\.Test/(Verifiers/|AnalyzerTestHost\.cs|AssemblyInfo\.cs|PurelySharp\.Test\.csproj)')
        {
            $fullReasons.Add("$path changes shared test infrastructure")
            continue
        }

        if ($path -match '^PurelySharp\.Test/.*\.cs$')
        {
            $className = Get-TestClassFromFile $path
            if ([string]::IsNullOrWhiteSpace($className))
            {
                $fullReasons.Add("$path is a test helper without a single owning fixture")
            }
            else
            {
                Add-TestClass $testClasses $className
            }

            continue
        }

        if ($path -match '\.csproj$')
        {
            $beforeCount = $testClasses.Count
            Add-PathMappedTests $testClasses $path
            if ($testClasses.Count -eq $beforeCount)
            {
                $fullReasons.Add("$path changes project references or package graph")
            }

            continue
        }

        Add-PathMappedTests $testClasses $path

        if ($path -match '^PurelySharp\.Analyzer/')
        {
            Add-TestFilesReferencingTokens $testClasses @(Get-TypeSearchTokens $path)
            if ($path -match '^PurelySharp\.Analyzer/Engine/(PurityAnalysisEngine|CompilationPurityService|Rules/RuleRegistry)\.cs$')
            {
                $fullReasons.Add("$path is high-fanout analyzer core")
            }
        }
        elseif ($path -match '^(PurelySharp\.Symbolic|SearchLib|Tools|PurelySharp\.CodeFixes|PurelySharp\.Attributes|PurelySharp\.Package|PurelySharp\.Vsix|Shared)/')
        {
            Add-TestFilesReferencingTokens $testClasses @(Get-TypeSearchTokens $path)
        }
        elseif (-not ($path -match '^(PurelySharp\.Demo|PurelySharp\.Smoke\.Net472)/'))
        {
            $fullReasons.Add("$path has no impacted-test mapping")
        }
    }

    $classNames = @($testClasses | Sort-Object)
    $filter = if ($classNames.Count -gt 0) { Join-TestFilter $classNames } else { '' }
    $filterTooLong = $filter.Length -gt 7000
    $requiresFull = $fullReasons.Count -gt 0 -or $filterTooLong
    $suggestedAction = if ($requiresFull -and -not $ForcePartial)
    {
        'RunFullSuite'
    }
    elseif ($requiresFull)
    {
        'RunPartialForced'
    }
    elseif ([string]::IsNullOrWhiteSpace($filter))
    {
        'Skip'
    }
    else
    {
        'RunPartial'
    }

    $suggestedCommand = Format-TestWrapperCommand `
        -SuggestedAction $suggestedAction `
        -Filter $filter `
        -Configuration $Configuration `
        -NoBuild ([bool]$NoBuild) `
        -FailFast ([bool]$FailFast) `
        -Workers $Workers `
        -Profile ([bool]$Profile) `
        -Top $Top `
        -MemoryLimitMb $MemoryLimitMb

    $recommendation = [ordered]@{
        changedFiles = @($changedFiles)
        ignoredFiles = @($ignoredFiles)
        selectedTestFixtures = @($classNames)
        testFilter = $filter
        requiresFullSuite = $requiresFull
        fullSuiteFallbackReasons = @($fullReasons)
        filterTooLong = $filterTooLong
        forcePartial = [bool]$ForcePartial
        suggestedAction = $suggestedAction
        suggestedCommand = $suggestedCommand
    }

    if ($Json)
    {
        $recommendation | ConvertTo-Json -Depth 4
        exit 0
    }

    Write-Host 'Changed files considered:'
    foreach ($path in $changedFiles)
    {
        Write-Host "  $path"
    }

    if ($ignoredFiles.Count -gt 0)
    {
        Write-Host ''
        Write-Host 'Ignored non-test-impacting files:'
        foreach ($path in $ignoredFiles)
        {
            Write-Host "  $path"
        }
    }

    if ($classNames.Count -gt 0)
    {
        Write-Host ''
        Write-Host 'Selected impacted test fixtures:'
        foreach ($className in $classNames)
        {
            Write-Host "  $className"
        }
    }

    if ($fullReasons.Count -gt 0)
    {
        Write-Host ''
        Write-Host 'Full-suite fallback reasons:'
        foreach ($reason in $fullReasons)
        {
            Write-Host "  $reason"
        }
    }

    if ($filter.Length -gt 7000)
    {
        Write-Host ''
        Write-Host "Full-suite fallback reason: generated filter is $($filter.Length) characters."
    }

    Write-Host ''
    Write-Host "Suggested action: $suggestedAction"
    if (-not [string]::IsNullOrWhiteSpace($suggestedCommand))
    {
        Write-Host "Suggested command: $suggestedCommand"
    }

    if ($ListOnly)
    {
        Write-Host ''
        if (-not [string]::IsNullOrWhiteSpace($filter))
        {
            Write-Host "Filter: $filter"
        }
        elseif (-not $requiresFull)
        {
            Write-Host 'No tests selected.'
        }

        if ($requiresFull -and -not $ForcePartial)
        {
            Write-Host 'Would run the full suite.'
        }
        elseif ($requiresFull)
        {
            Write-Host 'Would run the partial filter because -ForcePartial was set.'
        }

        exit 0
    }

    $wrapperPath = Join-Path $PSScriptRoot 'Invoke-PurelySharpTests.ps1'
    $wrapperParams = @{
        Configuration = $Configuration
        MemoryLimitMb = $MemoryLimitMb
        Top = $Top
    }

    if ($NoBuild) { $wrapperParams.NoBuild = $true }
    if ($FailFast) { $wrapperParams.FailFast = $true }
    if ($Workers -gt 0) { $wrapperParams.Workers = $Workers }
    if ($Profile) { $wrapperParams.Profile = $true }

    if ($requiresFull -and -not $ForcePartial)
    {
        Write-Host ''
        Write-Host 'Running full suite because impact selection is unsafe for these changes.'
        & $wrapperPath @wrapperParams @DotnetTestArgs
        exit $LASTEXITCODE
    }

    if ([string]::IsNullOrWhiteSpace($filter))
    {
        Write-Host ''
        Write-Host 'No test-impacting changes detected. Skipping test run.'
        exit 0
    }

    $wrapperParams.Filter = $filter
    Write-Host ''
    Write-Host "Running impacted tests with filter: $filter"
    & $wrapperPath @wrapperParams @DotnetTestArgs
    exit $LASTEXITCODE
}
finally
{
    Pop-Location
}
