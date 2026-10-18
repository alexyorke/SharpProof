Set-StrictMode -Version Latest

function Get-SharpProofPackageMethodsFromDiscovery {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$FullyQualifiedNames,
        [Parameter(Mandatory = $true)][string[]]$ClassNames,
        [Parameter(Mandatory = $true)][hashtable]$MinimumCounts,
        [Parameter(Mandatory = $true)][hashtable]$Descriptions
    )

    $methodsByClass = @{}
    foreach ($className in $ClassNames) {
        $pattern = '^' + [regex]::Escape($className) + '\.(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|$)'
        [string[]]$methods = @($FullyQualifiedNames | ForEach-Object {
            $match = [regex]::Match($_, $pattern)
            if ($match.Success) { $match.Groups['method'].Value }
        } | Sort-Object -Unique -CaseSensitive)
        if ($methods.Count -lt [int]$MinimumCounts[$className]) {
            throw ([string]$Descriptions[$className] + ' discovery returned only ' +
                $methods.Count + ' test methods.')
        }
        $methodsByClass[$className] = $methods
    }
    return $methodsByClass
}
