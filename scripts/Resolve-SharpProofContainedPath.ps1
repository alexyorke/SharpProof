function Resolve-SharpProofContainedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ParameterName
    )

    $canonicalRoot = [IO.Path]::GetFullPath($Root)
    $rootPath = [IO.Path]::GetPathRoot($canonicalRoot)
    if (-not [string]::Equals(
            $canonicalRoot,
            $rootPath,
            [StringComparison]::Ordinal)) {
        $canonicalRoot = $canonicalRoot.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
    }
    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $canonicalRoot $Path
    }
    $canonicalPath = [IO.Path]::GetFullPath($candidate)
    $prefix = $canonicalRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $canonicalPath.StartsWith(
            $prefix,
            [StringComparison]::Ordinal)) {
        throw "$ParameterName must be a child of '$canonicalRoot': $canonicalPath"
    }
    return $canonicalPath
}
