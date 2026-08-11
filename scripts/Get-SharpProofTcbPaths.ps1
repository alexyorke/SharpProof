function Get-SharpProofTcbPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Contract,

        [Parameter()]
        [switch]$IncludeAcceptanceContract
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

    return $paths.ToArray()
}
