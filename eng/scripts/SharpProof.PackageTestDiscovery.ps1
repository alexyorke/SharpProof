Set-StrictMode -Version Latest

function Get-SharpProofPackageMethodsFromDiscovery {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$FullyQualifiedNames,
        [Parameter(Mandatory = $true)][string[]]$ClassNames,
        [Parameter(Mandatory = $true)][Collections.IDictionary]$MinimumCounts,
        [Parameter(Mandatory = $true)][Collections.IDictionary]$Descriptions
    )

    $nameSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[string]($FullyQualifiedNames)
    $classSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[string]($ClassNames)
    $methodsByClass = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
    foreach ($className in $classSnapshot) {
        $pattern = '^' + [regex]::Escape($className) + '\.(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|$)'
        $methods = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($nameSnapshot | ForEach-Object {
                    $match = [regex]::Match($_, $pattern)
                    if ($match.Success) { $match.Groups['method'].Value }
                } | Sort-Object -Unique -CaseSensitive))
        if ($methods.Length -lt [int]$MinimumCounts[$className]) {
            throw ([string]$Descriptions[$className] + ' discovery returned only ' +
                $methods.Length + ' test methods.')
        }
        $methodsByClass[$className] = $methods
    }
    return , $methodsByClass.ToImmutable()
}
