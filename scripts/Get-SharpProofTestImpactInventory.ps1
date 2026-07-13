<#
.SYNOPSIS
Builds the SharpProof impacted-test dependency inventory.

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
. (Join-Path $PSScriptRoot 'TestImpactPolicy.ps1')

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repoFullPath = [System.IO.Path]::GetFullPath($script:RepoRoot)
    $repoPrefix = $repoFullPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path is outside the repository root: $fullPath"
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
    [ordered]@{ name = 'Analyzer'; sourceRoots = @('SharpProof.Analyzer/'); allowedProjectReferences = @('SharpProof.Attributes', 'SharpProof.Symbolic', 'SharpProof.ProofCore') },
    [ordered]@{ name = 'Symbolic'; sourceRoots = @('SharpProof.Symbolic/'); allowedProjectReferences = @('SharpProof.ProofCore') },
    [ordered]@{ name = 'ProofCore'; sourceRoots = @('SharpProof.ProofCore/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'CodeFixes'; sourceRoots = @('SharpProof.CodeFixes/'); allowedProjectReferences = @('SharpProof.Analyzer', 'SharpProof.Attributes') },
    [ordered]@{ name = 'Attributes'; sourceRoots = @('SharpProof.Attributes/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'Shared'; sourceRoots = @('Shared/'); allowedProjectReferences = @() },
    [ordered]@{ name = 'Packaging'; sourceRoots = @('SharpProof.Package/'); allowedProjectReferences = @('SharpProof.CodeFixes') },
    [ordered]@{ name = 'VSIX'; sourceRoots = @('SharpProof.Vsix/', 'Tools/VsixHarness/'); allowedProjectReferences = @('SharpProof.CodeFixes', 'SharpProof.Analyzer', 'SharpProof.Symbolic') },
    [ordered]@{ name = 'Tools'; sourceRoots = @('Tools/SharpProof.Baseline.Core/', 'Tools/SharpProof.Baseline/', 'Tools/SharpProof.CorpusReport.Core/', 'Tools/SharpProof.CorpusReport/', 'Tools/SharpProof.EffectSummary/', 'Tools/SharpProof.Fuzz.Core/', 'Tools/SharpProof.Fuzz/', 'Tools/SharpProof.SymbolicCli/'); allowedProjectReferences = @('SharpProof.Analyzer', 'SharpProof.Attributes', 'SharpProof.Symbolic', 'SharpProof.Baseline.Core', 'SharpProof.CorpusReport.Core', 'SharpProof.Fuzz.Core') },
    [ordered]@{ name = 'TestInfrastructure'; sourceRoots = @('SharpProof.Test/', 'SharpProof.ToolingTest/'); allowedProjectReferences = @('SharpProof.CodeFixes', 'SharpProof.Attributes', 'SharpProof.Analyzer', 'SharpProof.Symbolic', 'SharpProof.ProofCore', 'SharpProof.CorpusReport.Core', 'SharpProof.Fuzz.Core', 'SharpProof.SymbolicCli') }
)
$script:IgnoredTypeTokens = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
foreach ($token in Get-SharpProofIgnoredImpactTypeTokens)
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

    $testFiles = @(
        foreach ($testRoot in @('SharpProof.Test', 'SharpProof.ToolingTest'))
        {
            $rootPath = Join-Path $script:RepoRoot $testRoot
            if (-not (Test-Path -LiteralPath $rootPath))
            {
                continue
            }

            Get-ChildItem -Path $rootPath -Recurse -Filter '*.cs' |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        }
    ) | Sort-Object FullName

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
            (Convert-ToRepoPath $_.FullName) -notmatch '^SharpProof\.(Demo|Smoke\.Net472)/' -and
            (Convert-ToRepoPath $_.FullName) -notmatch '^SharpProof\.(Test|ToolingTest)/' -and
            (Get-ModuleName -Path (Convert-ToRepoPath $_.FullName)) -ne 'Unknown'
        } |
        Sort-Object FullName)

    $sourceFiles = New-Object System.Collections.Generic.List[object]
    $fixtureDependencies = New-Object System.Collections.Generic.List[object]
    $highFanoutFiles = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @(
        [ordered]@{ path = 'SharpProof.Analyzer/Engine/PurityAnalysisEngine.cs'; reason = 'high-fanout analyzer core' },
        [ordered]@{ path = 'SharpProof.Analyzer/Engine/CompilationPurityService.cs'; reason = 'high-fanout analyzer core' },
        [ordered]@{ path = 'SharpProof.Analyzer/Engine/Rules/RuleRegistry.cs'; reason = 'high-fanout analyzer core' }
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
                if ([string]::Equals([string]$testEntry.path, 'SharpProof.Test/ImpactedTestSelectionScriptTests.cs', [StringComparison]::OrdinalIgnoreCase))
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
        generatedBy = 'scripts/Get-SharpProofTestImpactInventory.ps1'
        modules = @($script:Modules)
        maxInventoryFixtureDependencies = $maxInventoryFixtureDependencies
        highFanoutFiles = @($highFanoutFiles | Sort-Object { $_['path'] })
        projects = @($projects)
        testFixtures = @($testFixtures)
        sourceFiles = @($sourceFiles | Sort-Object { $_['path'] })
        fixtureDependencies = @($fixtureDependencies | Sort-Object { $_['path'] })
    }

    $json = ($inventory | ConvertTo-Json -Depth 8).Replace("`r`n", "`n")
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

        [System.IO.File]::WriteAllText($resolvedOutputPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))
    }
}
finally
{
    Pop-Location
}
