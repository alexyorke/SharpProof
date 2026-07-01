<#
.SYNOPSIS
Builds the PurelySharp impacted-test dependency inventory.

.DESCRIPTION
The inventory is intentionally static and conservative. It records module
boundaries, project references, production type declarations, test fixtures,
and direct test references to production type names. The impacted-test wrapper
uses it as explainable evidence, while curated path maps and full-suite
fallbacks remain the safety net.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputPath = '',

    [Parameter()]
    [switch]$Validate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repoFullPath = [System.IO.Path]::GetFullPath($script:RepoRoot)
    if (-not $fullPath.StartsWith($repoFullPath, [StringComparison]::OrdinalIgnoreCase))
    {
        return $Path.Replace('\', '/').TrimStart('/')
    }

    return $fullPath.Substring($repoFullPath.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-ModuleName
{
    param([Parameter(Mandatory = $true)][string]$Path)

    foreach ($module in $script:Modules)
    {
        foreach ($root in $module.sourceRoots)
        {
            if ($Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase))
            {
                return $module.name
            }
        }
    }

    return 'Unknown'
}

function Get-ProjectNameFromPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFileNameWithoutExtension($Path)
}

function Get-TypeNames
{
    param([Parameter(Mandatory = $true)][string]$Text)

    $names = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    $pattern = '(?m)^\s*(?:\[[^\r\n]+\]\s*)*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|unsafe|new)\s+)*(?:class|struct|interface|enum|record(?:\s+struct|\s+class)?)\s+([A-Za-z_][A-Za-z0-9_]*)'
    foreach ($match in [regex]::Matches($Text, $pattern))
    {
        $name = $match.Groups[1].Value
        if ($name.Length -ge 5 -and -not $script:IgnoredTypeTokens.Contains($name))
        {
            [void]$names.Add($name)
        }
    }

    return @($names | Sort-Object)
}

function Get-TestFixtureNames
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $fixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($Text, '\bclass\s+([A-Za-z_][A-Za-z0-9_]*Tests)\b'))
    {
        [void]$fixtures.Add($match.Groups[1].Value)
    }

    if ($fixtures.Count -eq 0)
    {
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($Path)
        if ($stem.EndsWith('Tests', [StringComparison]::Ordinal))
        {
            [void]$fixtures.Add($stem)
        }
    }

    return @($fixtures | Sort-Object)
}

function Test-TokenReference
{
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Token
    )

    return [regex]::IsMatch($Text, "(?<![A-Za-z0-9_])$([regex]::Escape($Token))(?![A-Za-z0-9_])")
}

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Modules = @(
    [ordered]@{ name = 'Analyzer'; sourceRoots = @('PurelySharp.Analyzer/'); allowedProjectReferences = @('PurelySharp.Attributes', 'PurelySharp.Symbolic', 'SearchLib') },
    [ordered]@{ name = 'Symbolic'; sourceRoots = @('PurelySharp.Symbolic/'); allowedProjectReferences = @('SearchLib') },
    [ordered]@{ name = 'SearchLib'; sourceRoots = @('SearchLib/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'CodeFixes'; sourceRoots = @('PurelySharp.CodeFixes/'); allowedProjectReferences = @('PurelySharp.Analyzer', 'PurelySharp.Attributes') },
    [ordered]@{ name = 'Attributes'; sourceRoots = @('PurelySharp.Attributes/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'Shared'; sourceRoots = @('Shared/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'Packaging'; sourceRoots = @('PurelySharp.Package/'); allowedProjectReferences = @('PurelySharp.CodeFixes') },
    [ordered]@{ name = 'VSIX'; sourceRoots = @('PurelySharp.Vsix/', 'Tools/VsixHarness/'); allowedProjectReferences = @('PurelySharp.CodeFixes', 'PurelySharp.Analyzer', 'PurelySharp.Symbolic') },
    [ordered]@{ name = 'Tools'; sourceRoots = @('Tools/PurelySharp.CorpusReport/', 'Tools/PurelySharp.EffectSummary/', 'Tools/PurelySharp.Fuzz/', 'Tools/PurelySharp.SymbolicCli/'); allowedProjectReferences = @('PurelySharp.Analyzer', 'PurelySharp.Attributes', 'PurelySharp.Symbolic') },
    [ordered]@{ name = 'TestInfrastructure'; sourceRoots = @('PurelySharp.Test/'); allowedProjectReferences = @('PurelySharp.CodeFixes', 'PurelySharp.Attributes', 'PurelySharp.Analyzer', 'PurelySharp.Symbolic', 'SearchLib', 'PurelySharp.CorpusReport', 'PurelySharp.Fuzz', 'PurelySharp.SymbolicCli') }
)
$script:IgnoredTypeTokens = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
foreach ($token in @('Program', 'Options', 'Builder', 'Factory', 'Helper', 'Helpers', 'Extensions', 'Constants'))
{
    [void]$script:IgnoredTypeTokens.Add($token)
}
$maxInventoryFixtureDependencies = 40

Push-Location $script:RepoRoot
try
{
    $projectFiles = @(Get-ChildItem -Path $script:RepoRoot -Recurse -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and (Convert-ToRepoPath $_.FullName) -notmatch '(^|/)\.[^/]+/' } |
        Sort-Object FullName)

    $projects = foreach ($projectFile in $projectFiles)
    {
        [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
        $projectPath = Convert-ToRepoPath $projectFile.FullName
        $projectName = Get-ProjectNameFromPath $projectPath
        $references = foreach ($reference in $projectXml.SelectNodes("//*[local-name()='ProjectReference']"))
        {
            $include = [string]$reference.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include))
            {
                continue
            }

            $referencePath = [System.IO.Path]::GetFullPath((Join-Path $projectFile.DirectoryName $include))
            Get-ProjectNameFromPath $referencePath
        }

        [ordered]@{
            name = $projectName
            path = $projectPath
            module = Get-ModuleName $projectPath
            projectReferences = @($references | Sort-Object -Unique)
        }
    }

    $testFiles = @(Get-ChildItem -Path (Join-Path $script:RepoRoot 'PurelySharp.Test') -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object FullName)

    $testFileEntries = foreach ($testFile in $testFiles)
    {
        $text = Get-Content -LiteralPath $testFile.FullName -Raw
        $repoPath = Convert-ToRepoPath $testFile.FullName
        [ordered]@{
            path = $repoPath
            fixtures = @(Get-TestFixtureNames $repoPath $text)
            text = $text
        }
    }

    $testFixtures = @($testFileEntries |
        ForEach-Object {
            $entry = $_
            foreach ($fixture in $entry.fixtures)
            {
                [ordered]@{
                    name = $fixture
                    path = $entry.path
                }
            }
        } |
        Sort-Object { $_['name'] }, { $_['path'] })

    $productionFiles = @(Get-ChildItem -Path $script:RepoRoot -Recurse -Filter '*.cs' |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            (Convert-ToRepoPath $_.FullName) -notmatch '(^|/)\.[^/]+/' -and
            (Convert-ToRepoPath $_.FullName) -notmatch '^PurelySharp\.(Demo|Smoke\.Net472)/' -and
            (Convert-ToRepoPath $_.FullName) -notmatch '^PurelySharp\.Test/'
        } |
        Sort-Object FullName)

    $sourceFiles = New-Object System.Collections.Generic.List[object]
    $fixtureDependencies = New-Object System.Collections.Generic.List[object]
    $highFanoutFiles = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @(
        [ordered]@{ path = 'PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs'; reason = 'high-fanout analyzer core' },
        [ordered]@{ path = 'PurelySharp.Analyzer/Engine/CompilationPurityService.cs'; reason = 'high-fanout analyzer core' },
        [ordered]@{ path = 'PurelySharp.Analyzer/Engine/Rules/RuleRegistry.cs'; reason = 'high-fanout analyzer core' }
    ))
    {
        [void]$highFanoutFiles.Add($entry)
    }

    foreach ($productionFile in $productionFiles)
    {
        $repoPath = Convert-ToRepoPath $productionFile.FullName
        $text = Get-Content -LiteralPath $productionFile.FullName -Raw
        $typeNames = @(Get-TypeNames $text)
        if ($typeNames.Count -eq 0)
        {
            continue
        }

        $fixtureMatches = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
        $tokenMatches = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
        foreach ($token in $typeNames)
        {
            foreach ($testEntry in $testFileEntries)
            {
                if ([string]::Equals([string]$testEntry.path, 'PurelySharp.Test/ImpactedTestSelectionScriptTests.cs', [StringComparison]::OrdinalIgnoreCase))
                {
                    continue
                }

                if (Test-TokenReference $testEntry.text $token)
                {
                    [void]$tokenMatches.Add($token)
                    foreach ($fixture in $testEntry.fixtures)
                    {
                        [void]$fixtureMatches.Add($fixture)
                    }
                }
            }
        }

        $sourceEntry = [ordered]@{
            path = $repoPath
            module = Get-ModuleName $repoPath
            declaredTypes = @($typeNames)
            referencingFixtureCount = $fixtureMatches.Count
            referencedTokens = @($tokenMatches | Sort-Object)
        }
        [void]$sourceFiles.Add($sourceEntry)

        if ($fixtureMatches.Count -gt $maxInventoryFixtureDependencies)
        {
            [void]$highFanoutFiles.Add([ordered]@{
                path = $repoPath
                reason = 'broad generated fixture dependency'
                module = $sourceEntry.module
                fixtureCount = $fixtureMatches.Count
                tokens = @($tokenMatches | Sort-Object)
            })
        }
        elseif ($fixtureMatches.Count -gt 0)
        {
            [void]$fixtureDependencies.Add([ordered]@{
                path = $repoPath
                module = $sourceEntry.module
                selectedTestFixtures = @($fixtureMatches | Sort-Object)
                tokens = @($tokenMatches | Sort-Object)
                fixtureCount = $fixtureMatches.Count
            })
        }
    }

    $inventory = [ordered]@{
        schemaVersion = 1
        generatedBy = 'scripts/Get-PurelySharpTestImpactInventory.ps1'
        modules = @($script:Modules)
        maxInventoryFixtureDependencies = $maxInventoryFixtureDependencies
        highFanoutFiles = @($highFanoutFiles | Sort-Object { $_['path'] })
        projects = @($projects)
        testFixtures = @($testFixtures)
        sourceFiles = @($sourceFiles | Sort-Object { $_['path'] })
        fixtureDependencies = @($fixtureDependencies | Sort-Object { $_['path'] })
    }

    $json = $inventory | ConvertTo-Json -Depth 8
    if ($Validate)
    {
        $parsed = $json | ConvertFrom-Json
        if ([int]$parsed.schemaVersion -ne 1)
        {
            throw 'Inventory schema version mismatch.'
        }

        if ($parsed.modules.Count -lt 9)
        {
            throw 'Inventory module count is unexpectedly low.'
        }

        if ($parsed.testFixtures.Count -lt 50)
        {
            throw 'Inventory discovered too few test fixtures.'
        }
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath))
    {
        $json
    }
    else
    {
        $resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath))
        {
            $OutputPath
        }
        else
        {
            Join-Path $script:RepoRoot $OutputPath
        }

        $outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory))
        {
            [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
        }

        [System.IO.File]::WriteAllText($resolvedOutputPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    }
}
finally
{
    Pop-Location
}
