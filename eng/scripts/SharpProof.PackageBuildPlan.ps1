Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

# The package catalog owns the roots; MSBuild owns their complete build-only,
# analyzer, generator, and ordinary project-reference closure.
function Get-SharpProofPackageBuildProjects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$RepositoryRoot,

        [switch]$IncludeConsumerHarness
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $catalogPath = Resolve-SharpProofContainedPath -Root $root `
        -Path 'eng/scripts/package-projects.json' -ParameterName 'Package catalog'
    $json = [IO.File]::ReadAllText($catalogPath)
    $document = [Text.Json.JsonDocument]::Parse($json)
    try { Assert-UniqueJsonProperties -Value $document.RootElement -Context 'Package project catalog' }
    finally { $document.Dispose() }
    $catalog = ConvertFrom-Json -InputObject $json -AsHashtable
    Assert-SharpProofExactJsonProperties -Actual @($catalog.Keys) `
        -Expected @('schemaVersion', 'projects') -Description 'Package project catalog'
    if (($catalog.schemaVersion -isnot [int] -and $catalog.schemaVersion -isnot [long]) -or
        $catalog.schemaVersion -ne 1 -or $catalog.projects -isnot [array] -or
        $catalog.projects.Count -eq 0) {
        throw 'Package project catalog requires schema 1 and a nonempty project array.'
    }
    $solutionPath = Resolve-SharpProofContainedPath -Root $root `
        -Path 'SharpProof.slnx' -ParameterName 'Package build solution'
    [xml]$solution = [IO.File]::ReadAllText($solutionPath)
    $solutionProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $solution.SelectNodes('/Solution//Project')) {
        [void]$solutionProjects.Add($project.GetAttribute('Path').Replace('\', '/'))
    }
    $roots = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $candidates = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $candidates.AddRange([object[]]$catalog.projects)
    if ($IncludeConsumerHarness) {
        $candidates.Add('src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj')
    }
    foreach ($candidate in $candidates) {
        if ($candidate -isnot [string] -or $candidate -cnotmatch '^src/[^\\:\x00-\x1f]+\.csproj$' -or
            @($candidate.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) {
            throw 'Package build roots must be repository-relative projects under src.'
        }
        $absolute = Resolve-SharpProofContainedPath -Root $root -Path $candidate `
            -ParameterName 'Package build project'
        if (-not [IO.File]::Exists($absolute) -or -not $solutionProjects.Contains($candidate)) {
            throw "Package build project is missing from the source solution: '$candidate'."
        }
        if (-not $roots.Add($candidate)) {
            throw "Package build project is duplicated: '$candidate'."
        }
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string](
        [string[]]@($roots.ToImmutable() | Sort-Object -CaseSensitive))
}
