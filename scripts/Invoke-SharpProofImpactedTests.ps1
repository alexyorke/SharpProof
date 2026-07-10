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
    [string]$ModuleImpactManifestPath = '',

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

function Resolve-TestImpactInventoryPath
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$RequestedPath
    )

    if ([string]::IsNullOrWhiteSpace($RequestedPath))
    {
        return Join-Path $RepoRoot 'scripts\test-impact-inventory.json'
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

function Resolve-TestImpactModuleManifestPath
{
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$RequestedPath
    )

    if ([string]::IsNullOrWhiteSpace($RequestedPath))
    {
        return Join-Path $RepoRoot 'scripts\test-impact-modules.json'
    }

    if ([System.IO.Path]::IsPathRooted($RequestedPath))
    {
        return $RequestedPath
    }

    return Join-Path $RepoRoot $RequestedPath
}

function Get-TestImpactModuleManifest
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowNull()]$Inventory
    )

    $loaded = Test-Path -LiteralPath $Path
    $schemaVersion = 0
    if (-not $loaded)
    {
        return [pscustomobject][ordered]@{
            loaded = $false
            valid = $false
            schemaVersion = 0
            modules = @()
            sourceOwners = @{}
            error = 'Module impact manifest was not found.'
        }
    }

    try
    {
        $json = Get-Content -LiteralPath $Path -Raw
        if ([string]::IsNullOrWhiteSpace($json))
        {
            throw 'Module impact manifest is empty.'
        }

        $document = $json | ConvertFrom-Json
        $schemaProperty = $document.PSObject.Properties['schemaVersion']
        if ($null -eq $schemaProperty)
        {
            throw 'Module impact manifest is missing schemaVersion.'
        }

        $schemaVersion = [int]$schemaProperty.Value
        if ($schemaVersion -ne 1)
        {
            throw "Unsupported module impact manifest schema: $schemaVersion"
        }

        $modulesProperty = $document.PSObject.Properties['modules']
        $moduleEntries = if ($null -eq $modulesProperty) { @() } else { @($modulesProperty.Value) }
        if ($moduleEntries.Count -eq 0)
        {
            throw 'Module impact manifest must define at least one module.'
        }

        if ($null -eq $Inventory -or $null -eq $Inventory.testFixtures)
        {
            throw 'Generated test-impact inventory is unavailable for fixture validation.'
        }

        $knownFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
        foreach ($fixture in @($Inventory.testFixtures))
        {
            if ($null -ne $fixture -and $null -ne $fixture.PSObject.Properties['name'])
            {
                [void]$knownFixtures.Add([string]$fixture.name)
            }
        }

        $moduleNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
        $rawModulesByName = @{}
        $orderedNames = New-Object System.Collections.Generic.List[string]
        foreach ($entry in $moduleEntries)
        {
            if ($null -eq $entry)
            {
                throw 'Module impact manifest contains a null module.'
            }

            $nameProperty = $entry.PSObject.Properties['name']
            $name = if ($null -eq $nameProperty) { '' } else { ([string]$nameProperty.Value).Trim() }
            if ([string]::IsNullOrWhiteSpace($name))
            {
                throw 'Every module impact entry must have a nonempty name.'
            }

            if (-not $moduleNames.Add($name))
            {
                throw "Duplicate module impact name: $name"
            }

            $rawModulesByName[$name] = $entry
            $orderedNames.Add($name)
        }

        $sourceOwners = @{}
        $normalizedModules = New-Object System.Collections.Generic.List[object]
        foreach ($name in $orderedNames)
        {
            $entry = $rawModulesByName[$name]
            $sourcePathsProperty = $entry.PSObject.Properties['sourcePaths']
            $dependsOnProperty = $entry.PSObject.Properties['dependsOn']
            $testFixturesProperty = $entry.PSObject.Properties['testFixtures']
            if ($null -eq $sourcePathsProperty -or $null -eq $dependsOnProperty -or $null -eq $testFixturesProperty)
            {
                throw "Module $name must define sourcePaths, dependsOn, and testFixtures arrays."
            }

            $fullProperty = $entry.PSObject.Properties['fullSuiteOnDirectChange']
            $fullSuiteOnDirectChange = $false
            if ($null -ne $fullProperty)
            {
                if ($fullProperty.Value -isnot [bool])
                {
                    throw "Module $name fullSuiteOnDirectChange must be a JSON boolean."
                }

                $fullSuiteOnDirectChange = [bool]$fullProperty.Value
            }

            $sourcePaths = New-Object System.Collections.Generic.List[string]
            foreach ($rawSourcePath in @($sourcePathsProperty.Value))
            {
                $sourcePath = ([string]$rawSourcePath).Trim().Replace('\', '/')
                if ([string]::IsNullOrWhiteSpace($sourcePath) -or
                    [System.IO.Path]::IsPathRooted($sourcePath) -or
                    $sourcePath.StartsWith('./', [StringComparison]::Ordinal) -or
                    $sourcePath -match '(^|/)\.\.(/|$)')
                {
                    throw "Module $name has an invalid repo-relative source path: $rawSourcePath"
                }

                if ($sourceOwners.ContainsKey($sourcePath))
                {
                    throw "Source path $sourcePath is owned by more than one module."
                }

                $sourceOwners[$sourcePath] = $name
                $sourcePaths.Add($sourcePath)
            }

            if ($sourcePaths.Count -eq 0)
            {
                throw "Module $name must own at least one source path."
            }

            $dependencies = New-Object System.Collections.Generic.List[string]
            $dependencyNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
            foreach ($rawDependency in @($dependsOnProperty.Value))
            {
                $dependency = ([string]$rawDependency).Trim()
                if ([string]::IsNullOrWhiteSpace($dependency) -or -not $moduleNames.Contains($dependency))
                {
                    throw "Module $name has an unknown dependency: $rawDependency"
                }

                if ([string]::Equals($name, $dependency, [StringComparison]::OrdinalIgnoreCase))
                {
                    throw "Module $name cannot depend on itself."
                }

                if (-not $dependencyNames.Add($dependency))
                {
                    throw "Module $name contains duplicate dependency $dependency."
                }

                $dependencies.Add($dependency)
            }

            $fixtures = New-Object System.Collections.Generic.List[string]
            $fixtureNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
            foreach ($rawFixture in @($testFixturesProperty.Value))
            {
                $fixture = ([string]$rawFixture).Trim()
                if ([string]::IsNullOrWhiteSpace($fixture) -or -not $knownFixtures.Contains($fixture))
                {
                    throw "Module $name references unknown test fixture: $rawFixture"
                }

                if (-not $fixtureNames.Add($fixture))
                {
                    throw "Module $name contains duplicate test fixture $fixture."
                }

                $fixtures.Add($fixture)
            }

            if (-not $fullSuiteOnDirectChange -and $fixtures.Count -eq 0)
            {
                throw "Module $name must select at least one test fixture."
            }

            $normalizedModules.Add([pscustomobject][ordered]@{
                name = $name
                sourcePaths = @($sourcePaths)
                dependsOn = @($dependencies)
                testFixtures = @($fixtures)
                fullSuiteOnDirectChange = $fullSuiteOnDirectChange
            })
        }

        $indegree = @{}
        $dependents = @{}
        foreach ($module in $normalizedModules)
        {
            $indegree[$module.name] = @($module.dependsOn).Count
            $dependents[$module.name] = New-Object System.Collections.Generic.List[string]
        }

        foreach ($module in $normalizedModules)
        {
            foreach ($dependency in @($module.dependsOn))
            {
                $dependents[$dependency].Add([string]$module.name)
            }
        }

        $queue = New-Object System.Collections.Generic.Queue[string]
        foreach ($name in $orderedNames)
        {
            if ([int]$indegree[$name] -eq 0)
            {
                $queue.Enqueue($name)
            }
        }

        $visitedCount = 0
        while ($queue.Count -gt 0)
        {
            $current = $queue.Dequeue()
            $visitedCount++
            foreach ($dependent in $dependents[$current])
            {
                $indegree[$dependent] = [int]$indegree[$dependent] - 1
                if ([int]$indegree[$dependent] -eq 0)
                {
                    $queue.Enqueue($dependent)
                }
            }
        }

        if ($visitedCount -ne $normalizedModules.Count)
        {
            throw 'Module impact manifest dependencies contain a cycle.'
        }

        return [pscustomobject][ordered]@{
            loaded = $true
            valid = $true
            schemaVersion = $schemaVersion
            modules = @($normalizedModules.ToArray())
            sourceOwners = $sourceOwners
            error = ''
        }
    }
    catch
    {
        return [pscustomobject][ordered]@{
            loaded = $loaded
            valid = $false
            schemaVersion = $schemaVersion
            modules = @()
            sourceOwners = @{}
            error = $_.Exception.Message
        }
    }
}

function Get-DirectImpactModule
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Manifest
    )

    if (-not $Manifest.valid -or -not $Manifest.sourceOwners.ContainsKey($Path))
    {
        return $null
    }

    $moduleName = [string]$Manifest.sourceOwners[$Path]
    return @($Manifest.modules | Where-Object {
        [string]::Equals([string]$_.name, $moduleName, [StringComparison]::OrdinalIgnoreCase)
    })[0]
}

function Get-ReverseModuleClosure
{
    param(
        [Parameter(Mandatory = $true)][string]$DirectModuleName,
        [Parameter(Mandatory = $true)][object[]]$Modules
    )

    $impacted = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    [void]$impacted.Add($DirectModuleName)
    $changed = $true
    while ($changed)
    {
        $changed = $false
        foreach ($module in $Modules)
        {
            if ($impacted.Contains([string]$module.name))
            {
                continue
            }

            foreach ($dependency in @($module.dependsOn))
            {
                if ($impacted.Contains([string]$dependency))
                {
                    [void]$impacted.Add([string]$module.name)
                    $changed = $true
                    break
                }
            }
        }
    }

    return @($impacted | Sort-Object)
}

function Add-ModuleManifestMappedTests
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,

        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Manifest,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$FullSuiteReasons,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence
    )

    $directModule = Get-DirectImpactModule -Path $Path -Manifest $Manifest
    if ($null -eq $directModule)
    {
        return $false
    }

    $closure = @(Get-ReverseModuleClosure -DirectModuleName ([string]$directModule.name) -Modules @($Manifest.modules))
    $fixtures = @($Manifest.modules |
        Where-Object { $closure -contains [string]$_.name } |
        ForEach-Object { @($_.testFixtures) } |
        Sort-Object -Unique)
    Add-TestClasses $Set $fixtures
    Add-SelectionEvidence `
        -Evidence $Evidence `
        -Path $Path `
        -Source 'module-manifest' `
        -Reason "Explicit module $($directModule.name) impacts modules: $($closure -join ', ')" `
        -SelectedTestFixtures $fixtures `
        -Module ([string]$directModule.name)

    if ([bool]$directModule.fullSuiteOnDirectChange)
    {
        Add-FullSuiteFallbackReason `
            -Reasons $FullSuiteReasons `
            -Evidence $Evidence `
            -Path $Path `
            -Reason "$Path directly changes the $($directModule.name) module, which requires full-suite validation"
    }

    return $true
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
        [System.Collections.Generic.List[object]]$Evidence = $null
    )

    $dependency = Get-InventoryDependency -Inventory $Inventory -Path $Path
    if ($null -eq $dependency)
    {
        return $false
    }

    $fixtures = @($dependency.selectedTestFixtures | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($fixtures.Count -eq 0)
    {
        return $false
    }

    Add-TestClasses $Set $fixtures
    Add-SelectionEvidence `
        -Evidence $Evidence `
        -Path $Path `
        -Source 'inventory-symbol-reference' `
        -Reason 'Generated inventory maps changed source symbols to referencing test fixtures' `
        -SelectedTestFixtures $fixtures `
        -Tokens @($dependency.tokens | ForEach-Object { [string]$_ }) `
        -Module ([string]$dependency.module)

    return $true
}

function Add-SearchLibSmtTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-TestClasses $Set @(
        'SearchLibZ3SmokeTests',
        'SearchLibPurityProofTests',
        'SearchLibRoslynLoweringTests',
        'SearchLibBackedPurityFlowTests',
        'SmtAnalysisServiceTests',
        'SemanticOracleSmtTests',
        'ExpressionSmtTranslationTests',
        'ExpressionAtomSmtTests',
        'StringLengthSmtTests')
}

function Add-RegexSmtTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-SearchLibSmtTestClasses $Set
    Add-TestClasses $Set @(
        'RegexTests')
}

function Add-SearchLibFormulaEncoderTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-RegexSmtTestClasses $Set
    Add-TestClasses $Set @(
        'ExceptionReachabilitySmtTests',
        'SymbolicRuntimeHazardQueryTests')
}

function Add-SymbolicSmtTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-SearchLibSmtTestClasses $Set
    Add-TestClasses $Set @(
        'ElementAccessSmtTests',
        'ForeachSmtInvariantTests',
        'LoopExitSmtInvariantTests',
        'PathFactExpressionReachabilityTests',
        'PathSensitiveSmtInvariantTests',
        'PatternSmtInvariantTests',
        'ReferenceReachabilitySmtTests',
        'SymbolicProgramPointFactTests',
        'SymbolicRuntimeHazardQueryTests',
        'SymbolicSourceQueryLineTests')
}

function Add-RuntimeHazardSmtTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-TestClasses $Set @(
        'SmtAnalysisServiceTests',
        'SemanticOracleSmtTests',
        'SymbolicRuntimeHazardQueryTests',
        'SymbolicSourceQueryLineTests',
        'DiagnosticEvidenceTests')
}

function Add-ExceptionReachabilityRuntimeHazardTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-TestClasses $Set @(
        'AuthoringRuntimeHazardDiagnosticTests',
        'DiagnosticEvidenceTests',
        'ExceptionFlowPathFactStressTests',
        'ExceptionFlowPropagationRegressionTests',
        'ExceptionHandlingTests',
        'ExceptionReachabilitySmtTests',
        'RecursiveExceptionFlowTests',
        'SemanticOracleSmtTests',
        'SymbolicRuntimeHazardQueryTests',
        'SymbolicSourceQueryLineTests',
        'ThrowExpressionTests')
}

function Add-AnalyzerSmtTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-SymbolicSmtTestClasses $Set
    Add-TestClasses $Set @(
        'ExceptionReachabilitySmtTests',
        'ExceptionFlowPathFactStressTests',
        'ExceptionFlowPropagationRegressionTests',
        'ExceptionHandlingTests',
        'RecursiveExceptionFlowTests')
}

function Add-RuntimeHazardAnalyzerTestClasses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set
    )

    Add-AnalyzerSmtTestClasses $Set
    Add-TestClasses $Set @(
        'DiagnosticEvidenceTests')
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

    $ripgrep = Get-Command -Name 'rg' -ErrorAction SilentlyContinue

    foreach ($token in $Tokens)
    {
        if ($null -ne $ripgrep)
        {
            $matches = @(& $ripgrep.Source -l -F $token SharpProof.Test SharpProof.ToolingTest -g '*.cs' 2>$null)
        }
        else
        {
            $matches = @(
                Get-ChildItem -Path (Join-Path $PSScriptRoot '..\SharpProof.Test'), (Join-Path $PSScriptRoot '..\SharpProof.ToolingTest') -Recurse -Filter '*.cs' -File |
                    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
                    Select-String -SimpleMatch -Pattern $token -List |
                    ForEach-Object { $_.Path }
            )
        }

        foreach ($match in $matches)
        {
            $repoPath = Convert-ToRepoPath $match
            if ($repoPath -eq 'SharpProof.Test/ImpactedTestSelectionScriptTests.cs')
            {
                continue
            }

            $className = Get-TestClassFromFile $repoPath
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
        [string]$Path,

        [Parameter()]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Evidence = $null
    )

    $before = @($Set | Sort-Object)

    switch -Regex ($Path)
    {
        '^SearchLib/(SmtFormula|Z3FormulaEncoder)\.cs$' {
            Add-SearchLibFormulaEncoderTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'SearchLib SMT formula or encoder change' $before $Set
            break
        }
        '^SearchLib/SmtSolver\.cs$' {
            Add-RegexSmtTestClasses $Set
            Add-RuntimeHazardSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'SearchLib SMT solver change' $before $Set
            break
        }
        '^SearchLib/' {
            Add-SearchLibSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'SearchLib SMT solver and proof-search change' $before $Set
            break
        }
        '^SharpProof\.Symbolic/SymbolicRuntimeHazardQueryService\.cs$' {
            Add-RuntimeHazardSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Symbolic runtime-hazard query change' $before $Set
            break
        }
        '^SharpProof\.Symbolic/SymbolicProgramPointFacts\.cs$' {
            Add-SymbolicSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Symbolic program-point fact extraction change' $before $Set
            break
        }
        '^SharpProof\.Symbolic/Smt/(CSharpConditionToFormula|SmtAnalysisService|SwitchPathConditionBuilder)\.cs$' {
            Add-SymbolicSmtTestClasses $Set
            Add-TestClasses $Set @('RegexTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Symbolic SMT string-length and regex translation change' $before $Set
            break
        }
        '^SharpProof\.Symbolic/' {
            Add-SymbolicSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Symbolic SMT query surface change' $before $Set
            break
        }
        '^Tools/SharpProof\.SymbolicCli/' {
            Add-TestClasses $Set @('SymbolicSourceQueryLineTests', 'SymbolicRuntimeHazardQueryTests', 'AnalyzerPackagingTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Symbolic CLI query surface change' $before $Set
            break
        }
        '^Tools/SharpProof\.Fuzz(\.Core)?/' {
            Add-TestClasses $Set @('FuzzToolTests', 'RoslynShapeManifestCoverageTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Fuzz tool change' $before $Set
            break
        }
        '^Tools/SharpProof\.CorpusReport(\.Core)?/' {
            Add-TestClasses $Set @('CorpusReportTests', 'RoslynConstructCoverageTests', 'RoslynShapeManifestCoverageTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Corpus report tool change' $before $Set
            break
        }
        '^Tools/SharpProof\.EffectSummary/' {
            Add-TestClasses $Set @('EffectSummaryToolTests', 'ExceptionSummaryCatalogValidationTests', 'AnalyzerPackagingTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Effect summary tool change' $before $Set
            break
        }
        '^SharpProof\.Package/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer package change' $before $Set
            break
        }
        '^SharpProof\.Vsix/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests', 'AssemblyLoadingTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'VSIX packaging change' $before $Set
            break
        }
        '^SharpProof\.CodeFixes/' {
            Add-TestClasses $Set @('SharpProofCodeFixTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Code fix change' $before $Set
            break
        }
        '^SharpProof\.Attributes/' {
            Add-TestClasses $Set @('AttributeResolutionTests', 'AttributePlacementPurityTests', 'BoundaryAttributeTests', 'BasicPurityTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Public analyzer attributes change' $before $Set
            break
        }
        '^Shared/' {
            Add-TestClasses $Set @('AnalyzerPackagingTests', 'EffectSummaryToolTests', 'ExceptionSummaryCatalogValidationTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Shared build/runtime support change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Configuration/(AnalyzerConfiguration|AnalyzerConfigurationOptionRegistry|ConfigKeys)\.cs$' {
            Add-TestClasses $Set @('DiagnosticEvidenceTests', 'SemanticOracleSmtTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer runtime-hazard configuration change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/ExceptionFlowQuery\.cs$' {
            Add-ExceptionReachabilityRuntimeHazardTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Exception flow query reachability and runtime-hazard change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/ExceptionFlowAnalyzer\.ExceptionSites\.cs$' {
            Add-ExceptionReachabilityRuntimeHazardTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Exception site reachability and runtime-hazard analyzer change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/ExceptionFlowAnalyzer(\.(ExceptionSites|PathFacts|PropertyFlow|ResourceCallSites|SpecialCases))?\.cs$' {
            Add-RuntimeHazardAnalyzerTestClasses $Set
            Add-TestClasses $Set @('ExceptionSummaryCatalogValidationTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Exception flow and runtime-hazard analyzer change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Engine/PurityAnalysisEngine\.StateMerge\.cs$' {
            Add-SymbolicSmtTestClasses $Set
            Add-TestClasses $Set @('DiagnosticEvidenceTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer symbolic state-merge and path-fact change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Engine/ExecutionVisibility\.cs$' {
            Add-AnalyzerSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'SMT execution-visibility change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Engine/Rules/(BinaryOperationPurityRule|CoalesceOperationPurityRule|ConditionalAccessPurityRule|ConditionalOperationPurityRule)\.cs$' {
            Add-SymbolicSmtTestClasses $Set
            Add-TestClasses $Set @('DiagnosticEvidenceTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'SMT path-fact analyzer rule change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Engine/Rules/FieldOrPropertyInitializerOperationHelper\.cs$' {
            Add-TestClasses $Set @('ObjectEqualsDispatchTests', 'ComparisonDispatchTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Field/property initializer receiver analysis change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/Engine/Rules/MethodInvocationPurityRule\.cs$' {
            Add-SymbolicSmtTestClasses $Set
            Add-RuntimeHazardSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer as-conversion and runtime type-test SMT change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/.*Hazard.*\.cs$' {
            Add-RuntimeHazardSmtTestClasses $Set
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer runtime-hazard SMT change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/.*(Exception|Throw|Catch|Finally)' {
            Add-AnalyzerSmtTestClasses $Set
            Add-TestClasses $Set @('ExceptionSummaryCatalogValidationTests', 'DiagnosticEvidenceTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Exception or runtime-hazard analyzer change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/.*(Smt|SemanticOracle|PathFact|Regex|String|Invariant)' {
            Add-SymbolicSmtTestClasses $Set
            Add-TestClasses $Set @('RegexTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Analyzer SMT/path-fact/string/regex change' $before $Set
            break
        }
        '^SharpProof\.Analyzer/.*(EffectSummary|GeneratedPurity|Catalog|Summary)' {
            Add-TestClasses $Set @(
                'AnalyzerPackagingTests',
                'EffectSummaryToolTests',
                'ExceptionSummaryCatalogValidationTests',
                'DiagnosticEvidenceTests')
            Add-SelectionEvidenceForAddedTests $Evidence $Path 'path-map' 'Generated effect-summary or catalog change' $before $Set
            break
        }
    }
}

function Join-TestFilter
{
    param([Parameter(Mandatory = $true)][string[]]$ClassNames)

    return ($ClassNames |
        Sort-Object -Unique |
        ForEach-Object { "FullyQualifiedName~SharpProof.Test.$_" }) -join '|'
}

function Get-TestLaneForFixtures
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$ClassNames
    )

    if ($ClassNames.Count -eq 0)
    {
        return 'All'
    }

    $toolingFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'AnalyzerPackagingTests',
        'CorpusReportTests',
        'EffectSummaryToolTests',
        'ExceptionSummaryCatalogValidationTests',
        'FuzzToolTests',
        'ImpactedTestSelectionScriptTests',
        'SharpProofCodeFixTests',
        'RoslynConstructCoverageTests',
        'RoslynShapeManifestCoverageTests',
        'SymbolicRuntimeHazardQueryTests',
        'SymbolicSourceQueryLineTests'))
    {
        [void]$toolingFixtures.Add($fixture)
    }

    $hasToolingFixture = $false
    $hasMainFixture = $false
    foreach ($className in $ClassNames)
    {
        if ($toolingFixtures.Contains($className))
        {
            $hasToolingFixture = $true
        }
        else
        {
            $hasMainFixture = $true
        }
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
    $resolvedImpactInventoryPath = Resolve-TestImpactInventoryPath -RepoRoot $script:RepoRoot -RequestedPath $ImpactInventoryPath
    $impactInventory = Get-TestImpactInventory -Path $resolvedImpactInventoryPath
    $inventorySummary = [ordered]@{
        loaded = $null -ne $impactInventory
        path = Convert-ToRepoPath $resolvedImpactInventoryPath
        schemaVersion = if ($null -ne $impactInventory) { [int]$impactInventory.schemaVersion } else { 0 }
        modules = if ($null -ne $impactInventory -and $null -ne $impactInventory.modules) { @($impactInventory.modules | ForEach-Object { [string]$_.name }) } else { @() }
    }

    $resolvedModuleImpactManifestPath = Resolve-TestImpactModuleManifestPath `
        -RepoRoot $script:RepoRoot `
        -RequestedPath $ModuleImpactManifestPath
    $moduleImpactManifest = Get-TestImpactModuleManifest `
        -Path $resolvedModuleImpactManifestPath `
        -Inventory $impactInventory
    $moduleManifestSummary = [ordered]@{
        loaded = [bool]$moduleImpactManifest.loaded
        valid = [bool]$moduleImpactManifest.valid
        path = Convert-ToRepoPath $resolvedModuleImpactManifestPath
        schemaVersion = [int]$moduleImpactManifest.schemaVersion
        modules = @($moduleImpactManifest.modules | ForEach-Object { [string]$_.name })
        error = [string]$moduleImpactManifest.error
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
                moduleManifest = $moduleManifestSummary
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
            Add-TestClasses $testClasses @('SearchLibZ3SmokeTests')
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

        if ($path -match '^scripts/(Get-SharpProofTestImpactInventory\.ps1|test-impact-(inventory|modules)\.json)$')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('ImpactedTestSelectionScriptTests')
            Add-SelectionEvidenceForAddedTests $selectionEvidence $path 'path-map' 'Impacted-test metadata change' $before $testClasses
            continue
        }

        if ($path -match '^\.github/workflows/')
        {
            $before = @($testClasses | Sort-Object)
            Add-TestClasses $testClasses @('SearchLibZ3SmokeTests')
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
            $beforeCount = $testClasses.Count
            Add-PathMappedTests $testClasses $path $selectionEvidence
            if ($testClasses.Count -eq $beforeCount)
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path changes project references or package graph"
            }

            continue
        }

        if ($path -match '^SharpProof\.Analyzer/.*\.cs$')
        {
            if (-not $moduleImpactManifest.valid)
            {
                Add-FullSuiteFallbackReason `
                    -Reasons $fullReasons `
                    -Evidence $selectionEvidence `
                    -Path $path `
                    -Reason "$path cannot use the invalid module impact manifest: $($moduleImpactManifest.error)"
                continue
            }

            $hasModuleManifestMapping = Add-ModuleManifestMappedTests `
                -Set $testClasses `
                -Path $path `
                -Manifest $moduleImpactManifest `
                -FullSuiteReasons $fullReasons `
                -Evidence $selectionEvidence
            if ($hasModuleManifestMapping)
            {
                continue
            }
        }

        $beforeMappedCount = $testClasses.Count
        $beforeMappedEvidenceCount = $selectionEvidence.Count
        Add-PathMappedTests $testClasses $path $selectionEvidence
        $hasPathMapEvidence = $selectionEvidence.Count -gt $beforeMappedEvidenceCount
        $hasInventoryEvidence = Add-InventoryMappedTests $testClasses $path $impactInventory $selectionEvidence

        if ($path -match '^SharpProof\.Analyzer/')
        {
            $tokens = @(Get-TypeSearchTokens $path)
            $beforeTokenReferences = @($testClasses | Sort-Object)
            Add-TestFilesReferencingTokens $testClasses $tokens
            $tokenSelected = @(Get-AddedTestClasses -Set $testClasses -Before $beforeTokenReferences)
            if ($tokenSelected.Count -gt 0)
            {
                Add-SelectionEvidence `
                    -Evidence $selectionEvidence `
                    -Path $path `
                    -Source 'token-reference' `
                    -Reason 'Test files reference production type tokens from the changed file' `
                    -SelectedTestFixtures $tokenSelected `
                    -Tokens $tokens
            }

            $inventoryHighFanoutReason = Get-InventoryHighFanoutReason -Inventory $impactInventory -Path $path
            if (-not [string]::IsNullOrWhiteSpace($inventoryHighFanoutReason))
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path is $inventoryHighFanoutReason"
            }
            elseif ($path -match '^SharpProof\.Analyzer/Engine/(PurityAnalysisEngine|CompilationPurityService|Rules/RuleRegistry)\.cs$')
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path is high-fanout analyzer core"
            }
            elseif ($path -match '\.cs$' -and -not $hasPathMapEvidence -and -not $hasInventoryEvidence -and $testClasses.Count -eq $beforeMappedCount)
            {
                Add-FullSuiteFallbackReason $fullReasons $selectionEvidence $path "$path has no impacted-test mapping"
            }
        }
        elseif ($path -match '^(SharpProof\.Symbolic|SearchLib|Tools|SharpProof\.CodeFixes|SharpProof\.Attributes|SharpProof\.Package|SharpProof\.Vsix|Shared)/')
        {
            if ($path -match '^SearchLib/(SmtFormula|Z3FormulaEncoder)\.cs$')
            {
                continue
            }

            $tokens = @(Get-TypeSearchTokens $path)
            $beforeTokenReferences = @($testClasses | Sort-Object)
            Add-TestFilesReferencingTokens $testClasses $tokens
            $tokenSelected = @(Get-AddedTestClasses -Set $testClasses -Before $beforeTokenReferences)
            if ($tokenSelected.Count -gt 0)
            {
                Add-SelectionEvidence `
                    -Evidence $selectionEvidence `
                    -Path $path `
                    -Source 'token-reference' `
                    -Reason 'Test files reference production type tokens from the changed file' `
                    -SelectedTestFixtures $tokenSelected `
                    -Tokens $tokens
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
    $testLane = if ($requiresFull) { 'All' } else { Get-TestLaneForFixtures $classNames }
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
        moduleManifest = $moduleManifestSummary
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
        Write-Host "  Module manifest loaded: $($moduleManifestSummary.loaded); valid: $($moduleManifestSummary.valid) ($($moduleManifestSummary.path))"
        if (-not $moduleManifestSummary.valid)
        {
            Write-Host "  Module manifest error: $($moduleManifestSummary.error)"
        }
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
