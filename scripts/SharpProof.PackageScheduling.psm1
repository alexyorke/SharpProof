Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofPackageShardSchedule {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Shards
    )

    return @($Shards | Sort-Object `
        @{ Expression = {
                if ($null -eq $_.EstimatedMilliseconds -or
                    [long]$_.EstimatedMilliseconds -lt 1) {
                    1L
                }
                else {
                    [long]$_.EstimatedMilliseconds
                }
            }; Descending = $true }, `
        @{ Expression = { [string]$_.Name }; Descending = $false })
}

Export-ModuleMember -Function 'Get-SharpProofPackageShardSchedule'
