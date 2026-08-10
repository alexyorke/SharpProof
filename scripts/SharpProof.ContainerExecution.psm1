Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofTestProjectParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $override = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_TEST_PROJECT_PARALLELISM',
        [EnvironmentVariableTarget]::Process)
    $visibleProcessors = [Environment]::ProcessorCount
    if ($visibleProcessors -lt 1) {
        throw 'The container did not expose a positive processor count.'
    }

    if (-not [string]::IsNullOrWhiteSpace($override)) {
        $value = 0
        if (-not [int]::TryParse(
                $override,
                [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$value) -or
            $value -lt 1 -or
            $value -gt $visibleProcessors) {
            throw (
                'SHARPPROOF_TEST_PROJECT_PARALLELISM must be an integer ' +
                "between 1 and the container-visible CPU count " +
                "($visibleProcessors).")
        }
        return $value
    }

    $contract = Get-Content -LiteralPath (Join-Path `
        $RepositoryRoot 'eng/acceptance/contract.json') -Raw |
        ConvertFrom-Json
    $divisor = [int]$contract.automation.testProjectCpuDivisor
    if ($divisor -lt 1) {
        throw 'The test-project CPU divisor must be positive.'
    }

    return [Math]::Max(1, [Math]::Floor($visibleProcessors / $divisor))
}

Export-ModuleMember -Function 'Get-SharpProofTestProjectParallelism'
