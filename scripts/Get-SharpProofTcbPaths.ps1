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

        $path = ([string]$Value).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not $seen.Add($path)) {
            throw "Trusted-computing-base path is blank or duplicated: '$path'."
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
