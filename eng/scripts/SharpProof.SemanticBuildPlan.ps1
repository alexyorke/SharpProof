Set-StrictMode -Version Latest

# Derive build roots from the selected tasks, after TaskGroup filtering. Keep
# project-reference closure evaluation in MSBuild and test ownership in the
# existing semantic task plan, rather than maintaining a second group map.
function Get-SharpProofSemanticBuildProjects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.Generic.IReadOnlyList[object]]$Tasks,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SemanticSolutionFilter,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.Generic.IReadOnlyList[string]]$SemanticProjects
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    $solutionFilter = [IO.Path]::GetFullPath(
        $SemanticSolutionFilter.Replace('\', '/'), $root)
    $projects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    foreach ($task in $Tasks) {
        $target = [string]$task.Target
        if ([string]::IsNullOrWhiteSpace($target)) {
            throw 'Semantic build task has no target.'
        }
        $absoluteTarget = [IO.Path]::GetFullPath($target.Replace('\', '/'), $root)
        $candidates = if ($absoluteTarget -ceq $solutionFilter) {
            $SemanticProjects
        }
        elseif ($absoluteTarget.EndsWith('.csproj', [StringComparison]::Ordinal)) {
            , $absoluteTarget
        }
        else {
            throw "Semantic build task has an unsupported target: '$target'."
        }
        foreach ($candidate in $candidates) {
            if ([string]::IsNullOrWhiteSpace($candidate)) {
                throw 'Semantic build plan has an empty project.'
            }
            $absoluteProject = [IO.Path]::GetFullPath(
                ([string]$candidate).Replace('\', '/'), $root)
            if (-not $absoluteProject.StartsWith($prefix, [StringComparison]::Ordinal) -or
                -not $absoluteProject.EndsWith('.csproj', [StringComparison]::Ordinal)) {
                throw "Semantic build project is outside the repository or is not a project: '$candidate'."
            }
            [void]$projects.Add([IO.Path]::GetRelativePath($root, $absoluteProject).Replace('\', '/'))
        }
    }
    if ($projects.Count -eq 0) {
        throw 'Semantic build plan selected no projects.'
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string](
        [string[]]@($projects.ToImmutable() | Sort-Object -CaseSensitive))
}
