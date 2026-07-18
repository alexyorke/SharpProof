<#
.SYNOPSIS
Recommends or runs a conservative SharpProof test filter for changed files.

.DESCRIPTION
This helper maps changed repository files to likely impacted NUnit fixtures and
passes the generated VSTest filter to Invoke-SharpProofTests.ps1. It is a local
iteration aid only; it intentionally falls back to the full suite for shared
test infrastructure, build graph changes, high-fanout analyzer core files,
unmapped files, or generated filters that are too large.

.EXAMPLE
.\scripts\Invoke-SharpProofImpactedTests.ps1 -ListOnly

.EXAMPLE
.\scripts\Invoke-SharpProofImpactedTests.ps1 -ListOnly -Json -ChangedFile SharpProof.Test\SemanticOracleSmtTests.cs

.EXAMPLE
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -Workers 20 -FailFast
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
    [switch]$Explain,

    [Parameter()]
    [string]$ImpactInventoryPath = '',

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
    [switch]$NoExit,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$Top = 30,

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotnetTestArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestImpactPolicy.ps1')

if ($Json -and -not $ListOnly)
{
    throw '-Json is only supported with -ListOnly because normal mode streams test output.'
}

function Complete-ImpactedSelector
{
    param([int]$Code)

    if ($NoExit)
    {
        return
    }

    exit $Code
}

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Replace('\', '/')
    if (-not [string]::IsNullOrWhiteSpace($script:RepoRoot))
    {
        $repoRootNormalized = $script:RepoRoot.Replace('\', '/').TrimEnd('/')
        if ($normalized.StartsWith($repoRootNormalized + '/', [StringComparison]::OrdinalIgnoreCase))
        {
            $normalized = $normalized.Substring($repoRootNormalized.Length + 1)
        }
    }

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
        return $ExplicitChangedFiles |
            ForEach-Object { $_ -split ',' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Convert-ToRepoPath $_ } |
            Sort-Object -Unique
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue))
    {
        throw 'git is required to discover changed files; pass -ChangedFile for an explicit selection.'
    }

    $base = Resolve-BaseRef $RequestedBaseRef
    $mergeBase = (& git merge-base HEAD $base 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mergeBase))
    {
        throw "git merge-base failed for HEAD and '$base'."
    }

    $files = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $committedFiles = @(& git diff --name-only $mergeBase --)
    if ($LASTEXITCODE -ne 0) { throw "git diff failed for merge base '$mergeBase'." }
    foreach ($file in $committedFiles)
    {
        if (-not [string]::IsNullOrWhiteSpace($file))
        {
            [void]$files.Add((Convert-ToRepoPath $file))
        }
    }

    if ($IncludeUncommitted)
    {
        $cachedFiles = @(& git diff --name-only --cached --)
        if ($LASTEXITCODE -ne 0) { throw 'git diff --cached failed.' }
        foreach ($file in $cachedFiles)
        {
            if (-not [string]::IsNullOrWhiteSpace($file))
            {
                [void]$files.Add((Convert-ToRepoPath $file))
            }
        }

        $workingFiles = @(& git diff --name-only --)
        if ($LASTEXITCODE -ne 0) { throw 'git diff for the working tree failed.' }
        foreach ($file in $workingFiles)
        {
            if (-not [string]::IsNullOrWhiteSpace($file))
            {
                [void]$files.Add((Convert-ToRepoPath $file))
            }
        }

        $untrackedFiles = @(& git ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
        foreach ($file in $untrackedFiles)
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

function Get-AddedTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Before
    )

    $beforeSet = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($className in $Before)
    {
        [void]$beforeSet.Add($className)
    }

    return @($Set | Where-Object { -not $beforeSet.Contains($_) } | Sort-Object)
}

function Add-SelectionEvidence
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$SelectedTestFixtures = @(),

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$Tokens = @(),

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$FullSuiteFallbackReasons = @(),

        [Parameter()]
        [string]$Module = ''
    )

    if ($null -eq $Evidence)
    {
        return
    }

    $entry = [ordered]@{
        changedFile = $Path
        source = $Source
        reason = $Reason
        selectedTestFixtures = @($SelectedTestFixtures | Sort-Object -Unique)
        tokens = @($Tokens | Sort-Object -Unique)
        module = $Module
        fullSuiteFallbackReasons = @($FullSuiteFallbackReasons)
    }

    [void]$Evidence.Add($entry)
}

function Add-SelectionEvidenceForAddedTests
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Before,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$Tokens = @(),

        [Parameter()]
        [string]$Module = ''
    )

    $added = @(Get-AddedTestClasses -Set $Set -Before $Before)
    Add-SelectionEvidence `
        -Evidence $Evidence `
        -Path $Path `
        -Source $Source `
        -Reason $Reason `
        -SelectedTestFixtures $added `
        -Tokens $Tokens `
        -Module $Module
}

function Add-FullSuiteFallbackReason
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Reasons,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    $Reasons.Add($Reason)
    Add-SelectionEvidence `
        -Evidence $Evidence `
        -Path $Path `
        -Source 'full-suite-fallback' `
        -Reason $Reason `
        -FullSuiteFallbackReasons @($Reason)
}

function Resolve-RepoRelativePath
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$RequestedPath,
        [Parameter(Mandatory = $true)][string]$DefaultRelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RequestedPath))
    {
        return Join-Path $RepoRoot $DefaultRelativePath
    }

    if ([System.IO.Path]::IsPathRooted($RequestedPath))
    {
        return $RequestedPath
    }

    return Join-Path $RepoRoot $RequestedPath
}

function Get-TestImpactInventory
{
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path))
    {
        return $null
    }

    $json = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($json))
    {
        return $null
    }

    $inventory = $json | ConvertFrom-Json
    if ($null -eq $inventory.schemaVersion -or [int]$inventory.schemaVersion -ne 1)
    {
        throw "Unsupported impacted-test inventory schema in $Path"
    }

    return $inventory
}

function Get-InventoryDependency
{
    param(
        [AllowNull()]
        $Inventory,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ($null -eq $Inventory -or $null -eq $Inventory.fixtureDependencies)
    {
        return $null
    }

    foreach ($entry in @($Inventory.fixtureDependencies))
    {
        if ([string]::Equals([string]$entry.path, $Path, [StringComparison]::OrdinalIgnoreCase))
        {
            return $entry
        }
    }

    return $null
}

function Get-InventoryHighFanoutReason
{
    param(
        [AllowNull()]
        $Inventory,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ($null -eq $Inventory -or $null -eq $Inventory.highFanoutFiles)
    {
        return ''
    }

    foreach ($entry in @($Inventory.highFanoutFiles))
    {
        if ([string]::Equals([string]$entry.path, $Path, [StringComparison]::OrdinalIgnoreCase))
        {
            return [string]$entry.reason
        }
    }

    return ''
}

function Get-InventoryModule
{
    param(
        [AllowNull()]$Inventory,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($null -eq $Inventory -or $null -eq $Inventory.modules)
    {
        return $null
    }

    return $Inventory.modules | Where-Object {
        @($_.sourceRoots | Where-Object {
            $Path.StartsWith([string]$_, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
    } | Select-Object -First 1
}

function Get-InventoryReverseModuleClosure
{
    param(
        [Parameter(Mandatory = $true)]$DirectModule,
        [Parameter(Mandatory = $true)][object[]]$Modules
    )

    $impacted = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    [void]$impacted.Add([string]$DirectModule.name)
    $changed = $true
    while ($changed)
    {
        $changed = $false
        $impactedProjects = @($Modules |
            Where-Object { $impacted.Contains([string]$_.name) } |
            ForEach-Object { @($_.sourceRoots) } |
            ForEach-Object { [System.IO.Path]::GetFileName(([string]$_).TrimEnd('/')) })
        foreach ($module in $Modules)
        {
            if ($impacted.Contains([string]$module.name) -or
                [string]::Equals([string]$module.name, 'TestInfrastructure', [StringComparison]::OrdinalIgnoreCase))
            {
                continue
            }

            if (@($module.allowedProjectReferences | Where-Object { $impactedProjects -contains [string]$_ }).Count -gt 0)
            {
                [void]$impacted.Add([string]$module.name)
                $changed = $true
            }
        }
    }

    return @($impacted | Sort-Object)
}

function Add-InventoryMappedTests
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [AllowNull()]
        $Inventory,

        [Parameter()]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence = $null,

        [Parameter()]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$FullSuiteReasons = $null
    )

    $dependency = Get-InventoryDependency -Inventory $Inventory -Path $Path
    if ($null -ne $dependency)
    {
        $fixtures = @($dependency.selectedTestFixtures | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($fixtures.Count -gt 0)
        {
            Add-TestClasses $Set $fixtures
            Add-SelectionEvidence `
                -Evidence $Evidence `
                -Path $Path `
                -Source 'inventory-symbol-reference' `
                -Reason 'Generated inventory maps changed source symbols to referencing test fixtures' `
                -SelectedTestFixtures $fixtures `
                -Tokens @($dependency.tokens | ForEach-Object { [string]$_ }) `
                -Module ([string]$dependency.module)
        }
    }

    $directModule = Get-InventoryModule -Inventory $Inventory -Path $Path
    if ($null -eq $directModule)
    {
        return $null -ne $dependency
    }

    $closure = @(Get-InventoryReverseModuleClosure -DirectModule $directModule -Modules @($Inventory.modules))
    $fixtures = @($Inventory.fixtureDependencies |
        Where-Object { $closure -contains [string]$_.module } |
        ForEach-Object { @($_.selectedTestFixtures) } |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    Add-TestClasses $Set $fixtures
    Add-SelectionEvidence `
        -Evidence $Evidence `
        -Path $Path `
        -Source 'inventory-module-closure' `
        -Reason "Generated module dependency closure impacts modules: $($closure -join ', ')" `
        -SelectedTestFixtures $fixtures `
        -Module ([string]$directModule.name)

    if ($null -ne $FullSuiteReasons)
    {
        Add-FullSuiteFallbackReason `
            -Reasons $FullSuiteReasons `
            -Evidence $Evidence `
            -Path $Path `
            -Reason "$Path uses inferred module-closure selection, which requires full-suite validation"
    }

    return $true
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

function Join-TestFilter
{
    param([Parameter(Mandatory = $true)][string[]]$ClassNames)

    return ($ClassNames |
        Sort-Object -Unique |
        ForEach-Object { "FullyQualifiedName~$_." }) -join '|'
}

function Get-TestLaneForFixtures
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$ClassNames,

        [AllowNull()]$Inventory
    )

    if ($ClassNames.Count -eq 0 -or $null -eq $Inventory) { return 'All' }
    $hasToolingFixture = $false
    $hasMainFixture = $false
    foreach ($className in $ClassNames)
    {
        $paths = @($Inventory.testFixtures |
            Where-Object { [string]::Equals([string]$_.name, $className, [StringComparison]::Ordinal) } |
            ForEach-Object { [string]$_.path })
        if (@($paths | Where-Object { $_.StartsWith('SharpProof.ToolingTest/', [StringComparison]::Ordinal) }).Count -gt 0)
        {
            $hasToolingFixture = $true
        }
        if (@($paths | Where-Object { $_.StartsWith('SharpProof.Test/', [StringComparison]::Ordinal) }).Count -gt 0)
        {
            $hasMainFixture = $true
        }
        if ($paths.Count -eq 0) { return 'All' }
    }

    if ($hasToolingFixture -and -not $hasMainFixture)
    {
        return 'Tooling'
    }

    if ($hasMainFixture -and -not $hasToolingFixture)
    {
        return 'Main'
    }

    return 'All'
}

function Format-TestWrapperCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('RunFullSuite', 'RunPartial', 'RunPartialForced', 'Skip')]
        [string]$SuggestedAction,

        [string]$Filter,

        [ValidateSet('All', 'Main', 'Tooling')]
        [string]$TestLane,

        [string]$Configuration,

        [bool]$NoBuild,

        [bool]$FailFast,

        [int]$Workers,

        [bool]$Profile,

        [int]$Top,

        [int]$MemoryLimitMb,

        [int]$TimeoutSeconds
    )

    if ($SuggestedAction -eq 'Skip')
    {
        return ''
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('.\scripts\Invoke-SharpProofTests.ps1')
    $parts.Add('-Configuration')
    $parts.Add($Configuration)

    $parts.Add('-TestLane')
    $parts.Add($TestLane)

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

    if ($TimeoutSeconds -gt 0)
    {
        $parts.Add('-TimeoutSeconds')
        $parts.Add([string]$TimeoutSeconds)
    }

    if ($SuggestedAction -ne 'RunFullSuite' -and -not [string]::IsNullOrWhiteSpace($Filter))
    {
        $escapedFilter = $Filter.Replace("'", "''")
        $parts.Add('-Filter')
        $parts.Add("'$escapedFilter'")
    }

    return ($parts -join ' ')
}

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $script:RepoRoot
try
{
    $resolvedImpactInventoryPath = Resolve-RepoRelativePath `
        -RepoRoot $script:RepoRoot `
        -RequestedPath $ImpactInventoryPath `
        -DefaultRelativePath 'scripts\test-impact-inventory.json'
    $impactInventory = Get-TestImpactInventory -Path $resolvedImpactInventoryPath
    $inventorySummary = [ordered]@{
        loaded = $null -ne $impactInventory
        path = Convert-ToRepoPath $resolvedImpactInventoryPath
        schemaVersion = if ($null -ne $impactInventory) { [int]$impactInventory.schemaVersion } else { 0 }
        modules = if ($null -ne $impactInventory -and $null -ne $impactInventory.modules) { @($impactInventory.modules | ForEach-Object { [string]$_.name }) } else { @() }
    }

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
                selectionEvidence = @()
                inventory = $inventorySummary
                suggestedAction = 'Skip'
                suggestedCommand = ''
                note = 'No changed files detected. No impacted tests to run.'
            } | ConvertTo-Json -Depth 4
        }
        else
        {
            Write-Host 'No changed files detected. No impacted tests to run.'
        }

        Complete-ImpactedSelector 0
        return
    }

    $testClasses = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    $fullReasons = New-Object System.Collections.Generic.List[string]
    $ignoredFiles = New-Object System.Collections.Generic.List[string]
    $selectionEvidence = New-Object System.Collections.Generic.List[object]

    foreach ($path in $changedFiles)
    {
        if ($path -match '^config/profiles/')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ConfigurationProfileTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'SharpProof adoption profile change' $before $testClasses
            continue
        }

        if ($path -match '^(README\.md|REMAINING_ANALYZER_BACKLOG\.md)$|^docs/')
        {
            $ignoredFiles.Add($path)
            Add-SelectionEvidence `
                -Evidence $selectionEvidence `
                -Path $path `
                -Source 'ignored' `
                -Reason 'Documentation-only change'
            continue
        }

        if ($path -match '^(Directory\.Build\.props|global\.json|SharpProof\.sln|build.*\.ps1)$')
        {
            Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path changes build or solution shape"
            continue
        }

        if ($path -match '^scripts/Invoke-SharpProof(Tests|Dotnet|ImpactedTests)\.ps1$')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ProofCoreZ3SmokeTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'Test wrapper or impacted-test script change' $before $testClasses
            continue
        }

        if ($path -match '^scripts/Get-SharpProofProductionMetrics\.ps1$')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ArchitectureReductionTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'Production metrics script change' $before $testClasses
            continue
        }

        if ($path -match '^scripts/(Get-SharpProofTestImpactInventory\.ps1|TestImpactPolicy\.ps1|test-impact-inventory\.json)$')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ImpactedTestSelectionScriptTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'Impacted-test metadata change' $before $testClasses
            continue
        }

        if ($path -match '^\.github/workflows/')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ProofCoreZ3SmokeTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'CI workflow smoke-test change' $before $testClasses
            continue
        }

        if ($path -match '^SharpProof\.(Test|ToolingTest)/(Verifiers/|AnalyzerTestHost\.cs|AssemblyInfo\.cs|SharpProof\.(Test|ToolingTest)\.csproj)')
        {
            Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path changes shared test infrastructure"
            continue
        }

        if ($path -match '^SharpProof\.(Test|ToolingTest)/.*\.cs$')
        {
            $className = Get-TestClassFromFile $path
            if ($path -match '(Throw|Hazard)')
            {
                $before = @($testClasses | Sort-Object)
                Add-TestClass $testClasses $className
                Add-ExceptionReachabilityRuntimeHazardTestClasses $testClasses
                Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'test-name-map' 'Throw/hazard-named test change maps to exception reachability and runtime-hazard fixtures' $before $testClasses
            }
            elseif ([string]::IsNullOrWhiteSpace($className))
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path is a test helper without a single owning fixture"
            }
            else
            {
                $before = @($testClasses | Sort-Object)
                Add-TestClass $testClasses $className
                Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'changed-test-file' 'Changed test file maps to its owning fixture' $before $testClasses
            }

            continue
        }

        if ($path -match '\.csproj$')
        {
            $hasInventoryMapping = Add-InventoryMappedTests `
                -Set $testClasses `
                -Path $path `
                -Inventory $impactInventory `
                -Evidence $selectionEvidence `
                -FullSuiteReasons $fullReasons
            if (-not $hasInventoryMapping)
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path changes project references or package graph"
            }

            continue
        }

        $hasInventoryEvidence = Add-InventoryMappedTests `
            -Set $testClasses `
            -Path $path `
            -Inventory $impactInventory `
            -Evidence $selectionEvidence `
            -FullSuiteReasons $fullReasons

        if ($path -match '^SharpProof\.Analyzer/')
        {
            $inventoryHighFanoutReason = Get-InventoryHighFanoutReason -Inventory $impactInventory -Path $path
            if (-not [string]::IsNullOrWhiteSpace($inventoryHighFanoutReason))
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path is $inventoryHighFanoutReason"
            }
            elseif ($path -match '\.cs$' -and -not $hasInventoryEvidence)
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path has no impacted-test mapping"
            }
        }
        elseif ($path -match '^(SharpProof\.Symbolic|SharpProof\.ProofCore|SharpProof\.Contracts|SharpProof\.Tooling\.Core|Tools|SharpProof\.CodeFixes|SharpProof\.Attributes|SharpProof\.Package|SharpProof\.Vsix)/')
        {
            if (-not $hasInventoryEvidence)
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path has no impacted-test mapping"
            }
        }
        elseif (-not ($path -match '^(SharpProof\.Demo|SharpProof\.Smoke\.Net472)/'))
        {
            Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path has no impacted-test mapping"
        }
    }

    $classNames = @($testClasses | Sort-Object)
    $filter = if ($classNames.Count -gt 0) { Join-TestFilter $classNames } else { '' }
    $filterTooLong = $filter.Length -gt 7000
    $requiresFull = $fullReasons.Count -gt 0 -or $filterTooLong
    $testLane = if ($requiresFull) { 'All' } else { Get-TestLaneForFixtures $classNames $impactInventory }
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
        -TestLane $testLane `
        -Configuration $Configuration `
        -NoBuild ([bool]$NoBuild) `
        -FailFast ([bool]$FailFast) `
        -Workers $Workers `
        -Profile ([bool]$Profile) `
        -Top $Top `
        -MemoryLimitMb $MemoryLimitMb `
        -TimeoutSeconds $TimeoutSeconds

    $recommendation = [ordered]@{
        changedFiles = @($changedFiles)
        ignoredFiles = @($ignoredFiles)
        selectedTestFixtures = @($classNames)
        testFilter = $filter
        testLane = $testLane
        requiresFullSuite = $requiresFull
        fullSuiteFallbackReasons = @($fullReasons)
        selectionEvidence = @($selectionEvidence.ToArray())
        inventory = $inventorySummary
        filterTooLong = $filterTooLong
        forcePartial = [bool]$ForcePartial
        suggestedAction = $suggestedAction
        suggestedCommand = $suggestedCommand
    }

    if ($Json)
    {
        $recommendation | ConvertTo-Json -Depth 4
        Complete-ImpactedSelector 0
        return
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

    if ($Explain)
    {
        Write-Host ''
        Write-Host 'Impact-selection evidence:'
        Write-Host "  Inventory loaded: $($inventorySummary.loaded) ($($inventorySummary.path))"
        foreach ($entry in $selectionEvidence)
        {
            $fixtures = @($entry.selectedTestFixtures) -join ', '
            $tokens = @($entry.tokens) -join ', '
            $module = [string]$entry.module
            if ([string]::IsNullOrWhiteSpace($fixtures))
            {
                $fixtures = '<none>'
            }

            if ([string]::IsNullOrWhiteSpace($tokens))
            {
                $tokens = '<none>'
            }

            if ([string]::IsNullOrWhiteSpace($module))
            {
                $module = '<unknown>'
            }

            Write-Host "  $($entry.changedFile): $($entry.source); module=$module; fixtures=$fixtures; tokens=$tokens; reason=$($entry.reason)"
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

        Complete-ImpactedSelector 0
        return
    }

    $wrapperPath = Join-Path $PSScriptRoot 'Invoke-SharpProofTests.ps1'
    $wrapperParams = @{
        Configuration = $Configuration
        MemoryLimitMb = $MemoryLimitMb
        TimeoutSeconds = $TimeoutSeconds
        Top = $Top
    }

    if ($NoBuild) { $wrapperParams.NoBuild = $true }
    if ($FailFast) { $wrapperParams.FailFast = $true }
    if ($Workers -gt 0) { $wrapperParams.Workers = $Workers }
    if ($Profile) { $wrapperParams.Profile = $true }
    $wrapperParams.TestLane = $testLane

    if ($requiresFull -and -not $ForcePartial)
    {
        Write-Host ''
        Write-Host 'Running full suite because impact selection is unsafe for these changes.'
        & $wrapperPath @wrapperParams @DotnetTestArgs
        Complete-ImpactedSelector $LASTEXITCODE
        return
    }

    if ([string]::IsNullOrWhiteSpace($filter))
    {
        Write-Host ''
        Write-Host 'No test-impacting changes detected. Skipping test run.'
        Complete-ImpactedSelector 0
        return
    }

    $wrapperParams.Filter = $filter
    Write-Host ''
    Write-Host "Running impacted tests with filter: $filter"
    & $wrapperPath @wrapperParams @DotnetTestArgs
    Complete-ImpactedSelector $LASTEXITCODE
    return
}
finally
{
    Pop-Location
}
