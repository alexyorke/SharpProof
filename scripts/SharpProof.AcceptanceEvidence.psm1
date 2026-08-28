Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SharpProofAcceptanceEvidencePhases {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Evidence,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedPhaseNames
    )

    if ($ExpectedPhaseNames.Count -eq 0) {
        throw 'Acceptance evidence requires a non-empty phase contract.'
    }
    $phasesProperty = $Evidence.PSObject.Properties['phases']
    if ($null -eq $phasesProperty) {
        throw 'Acceptance evidence is missing its required phases.'
    }
    $phases = @($phasesProperty.Value)
    if ($phases.Count -ne $ExpectedPhaseNames.Count) {
        throw (
            'Acceptance evidence must contain exactly the contracted phases; ' +
            "expected $($ExpectedPhaseNames.Count), found $($phases.Count).")
    }

    for ($index = 0; $index -lt $ExpectedPhaseNames.Count; $index++) {
        $phase = $phases[$index]
        if ([string]$phase.name -cne $ExpectedPhaseNames[$index] -or
            [string]$phase.status -cne 'passed') {
            throw (
                "Acceptance phase '$($ExpectedPhaseNames[$index])' must be " +
                'present in order and have status passed.')
        }
    }

    return $true
}

Export-ModuleMember -Function 'Assert-SharpProofAcceptanceEvidencePhases'
