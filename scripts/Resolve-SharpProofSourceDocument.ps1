. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

function Resolve-SharpProofSourceDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$DocumentPath,
        [string[]]$SourceRoots = @(),
        [bool]$RequireFile = $true
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path.
        TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    $physicalRoot = Resolve-SharpProofPhysicalPath -Path $root
    $physicalPrefix = $physicalRoot + [IO.Path]::DirectorySeparatorChar
    $normalized = $DocumentPath.Replace('\', '/')
    $candidates = [Collections.Generic.List[string]]::new()
    $candidates.Add($normalized)
    if (-not [IO.Path]::IsPathRooted($normalized)) {
        foreach ($sourceRoot in $SourceRoots) {
            if (-not [string]::IsNullOrWhiteSpace($sourceRoot)) {
                $candidates.Add($sourceRoot.Replace('\', '/').TrimEnd('/') + '/' + $normalized)
            }
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate.StartsWith('/_/', [StringComparison]::Ordinal)) {
            $suffix = $candidate.Substring(3)
            $components = $suffix.Split('/')
            if ([string]::IsNullOrWhiteSpace($suffix) -or
                [IO.Path]::IsPathRooted($suffix) -or $suffix -match '^[A-Za-z]:' -or
                @($components | Where-Object {
                    [string]::IsNullOrWhiteSpace($_) -or $_ -in @('.', '..')
                }).Count -ne 0) {
                throw "Mapped source document has an invalid repository suffix: '$DocumentPath'."
            }
            $candidate = Join-Path $root $suffix
        }
        $full = if ([IO.Path]::IsPathRooted($candidate)) {
            [IO.Path]::GetFullPath($candidate)
        }
        else {
            [IO.Path]::GetFullPath((Join-Path $root $candidate))
        }
        if (-not $full.StartsWith($prefix, [StringComparison]::Ordinal) -or
            ($RequireFile -and -not (Test-Path -LiteralPath $full -PathType Leaf))) {
            continue
        }
        $physical = Resolve-SharpProofPhysicalPath -Path $full `
            -BasePath $root -BasePhysicalPath $physicalRoot
        if (-not $physical.StartsWith($physicalPrefix, [StringComparison]::Ordinal)) {
            throw "Source document resolves outside the repository: '$DocumentPath'."
        }
        return $full.Substring($prefix.Length).Replace('\', '/')
    }
    throw "Source document is foreign or missing: '$DocumentPath'."
}
