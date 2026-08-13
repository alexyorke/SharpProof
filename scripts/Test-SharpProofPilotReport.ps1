function Test-SharpProofPilotReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit
    )

    if ([int]$Report.schemaVersion -ne 2 -or
        [string]$Report.runId -cnotmatch '^[0-9a-f]{32}$' -or
        [string]$Report.commit -cne $ExpectedCommit -or
        [int]$Report.pilotCount -ne 5 -or @($Report.pilots).Count -ne 5 -or
        @($Report.pilots.id | Select-Object -Unique).Count -ne 5 -or
        @($Report.packageArtifacts).Count -ne 6) { return $false }
    $packageNames = @($Report.packageArtifacts | ForEach-Object {
            if ([string]$_.fileName -cne [IO.Path]::GetFileName([string]$_.fileName) -or
                @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier') -cnotcontains [string]$_.packageId -or
                [string]$_.version -cne [string]$Report.packageVersion -or
                [string]$_.repositoryCommit -cne $ExpectedCommit -or
                [int64]$_.bytes -le 0 -or [string]$_.sha256 -cnotmatch '^[0-9a-f]{64}$') {
                return $null
            }
            [string]$_.fileName
        })
    if ($packageNames.Count -ne 6 -or @($packageNames | Select-Object -Unique).Count -ne 6) {
        return $false
    }
    $expectedNames = @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier') |
        ForEach-Object { "$_." + [string]$Report.packageVersion + '.nupkg'; "$_." + [string]$Report.packageVersion + '.snupkg' }
    if (@($packageNames | Where-Object { $expectedNames -cnotcontains $_ }).Count -ne 0) {
        return $false
    }
    foreach ($pilot in @($Report.pilots)) {
        if ([string]$pilot.runStatus -cne 'Complete' -or -not [bool]$pilot.sarifProduced -or
            @($pilot.evidence).Count -ne 4 -or
            @($pilot.evidence.kind | Sort-Object) -join '|' -cne
                'compilerManifest|request|result|sarif') { return $false }
        foreach ($evidence in @($pilot.evidence)) {
            $path = [string]$evidence.path
            if ([string]::IsNullOrWhiteSpace($path) -or [IO.Path]::IsPathRooted($path) -or
                $path.Contains('..') -or [int64]$evidence.bytes -le 0 -or
                [string]$evidence.sha256 -cnotmatch '^[0-9a-f]{64}$') { return $false }
        }
    }
    return $true
}
