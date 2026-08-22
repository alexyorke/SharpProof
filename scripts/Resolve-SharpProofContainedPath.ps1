function Resolve-SharpProofPhysicalPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrEmpty($pathRoot)) {
        throw "Path has no filesystem root: $fullPath"
    }
    $relativePath = $fullPath.Substring($pathRoot.Length)
    $components = @($relativePath.Split(
            [char[]]@(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries))
    $current = $pathRoot
    for ($index = 0; $index -lt $components.Count; $index++) {
        $next = Join-Path $current $components[$index]
        try {
            $item = Get-Item -LiteralPath $next -Force -ErrorAction Stop
        }
        catch [Management.Automation.ItemNotFoundException] {
            for ($remainder = $index;
                $remainder -lt $components.Count;
                $remainder++) {
                $current = Join-Path $current $components[$remainder]
            }
            return [IO.Path]::GetFullPath($current)
        }

        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $target = $item.ResolveLinkTarget($true)
            if ($null -eq $target -or -not $target.Exists) {
                throw "Path contains an unresolved link: $next"
            }
            $current = [IO.Path]::GetFullPath($target.FullName)
        }
        else {
            $current = [IO.Path]::GetFullPath($item.FullName)
        }
        if ($index -lt $components.Count - 1 -and
            -not [IO.Directory]::Exists($current)) {
            throw "Path traverses a non-directory component: $current"
        }
    }
    return [IO.Path]::GetFullPath($current)
}

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
    if (-not [IO.Directory]::Exists($canonicalRoot)) {
        throw "Containment root does not exist: $canonicalRoot"
    }
    $physicalRoot = Resolve-SharpProofPhysicalPath -Path $canonicalRoot
    $physicalPath = Resolve-SharpProofPhysicalPath -Path $canonicalPath
    $physicalPrefix = $physicalRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith(
            $physicalPrefix,
            [StringComparison]::Ordinal)) {
        throw "$ParameterName must resolve to a child of '$physicalRoot': $physicalPath"
    }
    return $physicalPath
}
