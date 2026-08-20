function Get-SharpProofTcbPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Contract,

        [Parameter()]
        [switch]$IncludeAcceptanceContract,

        [Parameter()]
        $ProductionInventory
    )

    if ($null -eq $Contract.trustedKernel -or
        $null -eq $Contract.trustedComputingBase) {
        throw 'The acceptance contract must declare the trusted computing base.'
    }

    $paths = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $addPath = {
        param(
            [Parameter(Mandatory = $true)]
            $Value
        )

        $path = [string]$Value
        if ([string]::IsNullOrWhiteSpace($path)) {
            throw 'Trusted-computing-base path is blank.'
        }
        if ($path.Contains('\') -or
            [IO.Path]::IsPathRooted($path) -or
            $path.StartsWith('/', [StringComparison]::Ordinal) -or
            $path.EndsWith('/', [StringComparison]::Ordinal) -or
            $path.Contains('//')) {
            throw "Trusted-computing-base path is not canonical: '$path'."
        }
        $segments = $path.Split('/')
        if ($segments.Where({ $_ -eq '.' -or $_ -eq '..' }).Count -ne 0) {
            throw "Trusted-computing-base path contains a dot segment: '$path'."
        }
        if (-not $seen.Add($path)) {
            throw "Trusted-computing-base path is duplicated: '$path'."
        }

        [void]$paths.Add($path)
    }

    if ($IncludeAcceptanceContract) {
        & $addPath 'eng/acceptance/contract.json'
    }

    foreach ($path in @($Contract.trustedKernel.paths)) {
        & $addPath $path
    }

    foreach ($component in @(
            $Contract.trustedComputingBase.components)) {
        foreach ($path in @($component.paths)) {
            & $addPath $path
        }
    }

    if ($null -ne $ProductionInventory) {
        if ($null -eq $ProductionInventory.projects) {
            throw 'The production inventory authority has no projects.'
        }
        $compilePaths = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($project in @($ProductionInventory.projects)) {
            foreach ($file in @($project.compile)) {
                [void]$compilePaths.Add([string]$file.path)
            }
        }
        foreach ($path in $paths) {
            if ($path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and
                -not $compilePaths.Contains($path)) {
                throw (
                    "Trusted-computing-base source is not an evaluated " +
                    "production Compile item: '$path'.")
            }
        }
    }

    return $paths.ToArray()
}
