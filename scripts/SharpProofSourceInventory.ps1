function ConvertTo-SharpProofRepoPath
{
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { return '' }

    $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path is outside the repository root: $fullPath"
    }

    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

function Test-SharpProofProductionSourcePath
{
    param([Parameter(Mandatory = $true)][string]$RepoPath)

    return $RepoPath -notmatch '(^|/)(bin|obj)/' -and
        $RepoPath -notmatch '^artifacts/' -and
        $RepoPath -notmatch '^docs/readme-examples/' -and
        $RepoPath -notmatch '(^|/)\.[^/]+/' -and
        $RepoPath -notmatch '^SharpProof\.(Test|ToolingTest)/' -and
        $RepoPath -notmatch '^SharpProof\.(Demo|Smoke\.Net472)/'
}

function Get-SharpProofProductionSourceFiles
{
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$SearchRoot = $RepositoryRoot
    )

    $searchPath = [IO.Path]::GetFullPath($SearchRoot)
    [void](ConvertTo-SharpProofRepoPath -RepositoryRoot $RepositoryRoot -Path $searchPath)
    return @(Get-ChildItem -LiteralPath $searchPath -Recurse -File -Filter '*.cs' |
        ForEach-Object {
            $repoPath = ConvertTo-SharpProofRepoPath -RepositoryRoot $RepositoryRoot -Path $_.FullName
            if (Test-SharpProofProductionSourcePath $repoPath)
            {
                [pscustomobject]@{
                    FullName = $_.FullName
                    RepoPath = $repoPath
                }
            }
        } |
        Sort-Object RepoPath)
}
