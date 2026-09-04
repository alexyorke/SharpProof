Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function ConvertTo-SharpProofPilotClaimEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ManifestClaims,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ClaimResults,
        [switch]$ThrowOnMismatch,
        [string]$MismatchMessage = 'Pilot manifest/result claim set is incoherent.'
    )

    $claimResultIndex = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    $duplicateClaimResultIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($claimResult in $ClaimResults) {
        $claimId = [string]$claimResult.claimId
        if ($claimResultIndex.ContainsKey($claimId)) {
            [void]$duplicateClaimResultIds.Add($claimId)
        } else {
            $claimResultIndex.Add($claimId, $claimResult)
        }
    }
    return @($ManifestClaims | ForEach-Object {
            $manifestClaim = $_
            $claimId = [string]$manifestClaim.claimId
            if ($duplicateClaimResultIds.Contains($claimId) -or
                -not $claimResultIndex.ContainsKey($claimId)) {
                if ($ThrowOnMismatch) { throw $MismatchMessage }
                return $null
            }
            [pscustomobject]@{
                claimId = $claimId
                kind = [string]$manifestClaim.kind
                outcome = [string]$claimResultIndex[$claimId].outcome
            }
        } | Sort-Object claimId)
}

function Test-SharpProofPilotReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit,
        [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
        [string]$CatalogPath = (Join-Path $PSScriptRoot '..\eng\pilots\catalog.json')
    )

    try {
        $catalog = Get-Content -LiteralPath $CatalogPath -Raw -ErrorAction Stop |
            ConvertFrom-Json -ErrorAction Stop
        if ((@($catalog.PSObject.Properties.Name | Sort-Object) -join '|') -cne
                'pilots|schemaVersion' -or
            [int]$catalog.schemaVersion -ne 1 -or @($catalog.pilots).Count -ne 5) {
            return $false
        }
        $catalogRows = @{}
        $catalogProjects = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($row in @($catalog.pilots)) {
            $names = @($row.PSObject.Properties.Name | Sort-Object)
            if (($names -join '|') -cne
                'category|id|library|libraryVersion|project|setupFriction' -or
                [string]$row.id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or
                @('effect-heavy','contract-heavy','mixed-strict') -cnotcontains [string]$row.category -or
                $catalogRows.ContainsKey([string]$row.id)) { return $false }
            $project = [IO.Path]::GetFullPath((Join-Path (Split-Path $CatalogPath) ([string]$row.project)))
            $pilotRoot = [IO.Path]::GetFullPath((Split-Path $CatalogPath))
            if (-not $project.StartsWith($pilotRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::Ordinal) -or
                -not (Test-Path -LiteralPath $project -PathType Leaf) -or
                -not $catalogProjects.Add($project)) { return $false }
            [xml]$projectXml = Get-Content -LiteralPath $project -Raw
            $external = @($projectXml.Project.ItemGroup.PackageReference | Where-Object {
                    $id = [string]$_.Include
                    -not [string]::IsNullOrWhiteSpace($id) -and
                    -not $id.StartsWith('SharpProof', [StringComparison]::Ordinal)
                })
            if ($external.Count -ne 1 -or
                [string]$external[0].Include -cne [string]$row.library -or
                [string]$external[0].Version -cne [string]$row.libraryVersion) { return $false }
            $catalogRows[[string]$row.id] = [pscustomobject]@{
                project = [string]$row.project
                category = [string]$row.category
                library = [string]$external[0].Include
                version = [string]$external[0].Version
            }
        }
        if (@($catalog.pilots.library | Select-Object -Unique).Count -ne 5 -or
            @($catalog.pilots | Where-Object category -eq 'effect-heavy').Count -ne 2 -or
            @($catalog.pilots | Where-Object category -eq 'contract-heavy').Count -ne 2 -or
            @($catalog.pilots | Where-Object category -eq 'mixed-strict').Count -ne 1) { return $false }
    }
    catch { return $false }

    if ([int]$Report.schemaVersion -ne 3 -or
        @('Unreviewed', 'Reviewed') -cnotcontains [string]$Report.reviewStatus -or
        [string]$Report.runId -cnotmatch '^[0-9a-f]{32}$' -or
        [string]$Report.commit -cne $ExpectedCommit -or
        [int]$Report.pilotCount -ne 5 -or @($Report.pilots).Count -ne 5 -or
        @($Report.pilots.id | Select-Object -Unique).Count -ne 5 -or
        @($Report.packageArtifacts).Count -ne 6) { return $false }
    $packageKeys = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $packageNames = @($Report.packageArtifacts | ForEach-Object {
            $fileName = [string]$_.fileName
            $packageId = [string]$_.packageId
            $extension = [IO.Path]::GetExtension($fileName)
            if ($fileName -cne [IO.Path]::GetFileName($fileName) -or
                $SharpProofPackageIds -cnotcontains $packageId -or
                $extension -notin @('.nupkg', '.snupkg') -or
                $fileName -cne "$packageId.$([string]$Report.packageVersion)$extension" -or
                [string]$_.version -cne [string]$Report.packageVersion -or
                [string]$_.repositoryCommit -cne $ExpectedCommit -or
                [int64]$_.bytes -le 0 -or
                -not $packageKeys.Add("$packageId|$extension")) {
                return $null
            }
            $fileName
        })
    if ($packageNames.Count -ne 6 -or $packageKeys.Count -ne 6) {
        return $false
    }
    foreach ($pilot in @($Report.pilots)) {
        if (@($pilot.PSObject.Properties.Name) -cnotcontains 'project' -or
            @($pilot.PSObject.Properties.Name) -cnotcontains 'claimEvidence' -or
            @($pilot.PSObject.Properties.Name) -cnotcontains 'falsePositiveReports') { return $false }
        if (([string]$Report.reviewStatus -ceq 'Unreviewed' -and
                $null -ne $pilot.falsePositiveReports) -or
            ([string]$Report.reviewStatus -ceq 'Reviewed' -and
                ([int]$pilot.falsePositiveReports -lt 0))) { return $false }
        if (-not $catalogRows.ContainsKey([string]$pilot.id)) { return $false }
        $expectedPilot = $catalogRows[[string]$pilot.id]
        if ([string]$pilot.project -cne $expectedPilot.project -or
            [string]$pilot.category -cne $expectedPilot.category -or
            [string]$pilot.library -cne $expectedPilot.library -or
            [string]$pilot.libraryVersion -cne $expectedPilot.version -or
            [string]$pilot.runStatus -cne 'Complete' -or -not [bool]$pilot.sarifProduced -or
            @($pilot.evidence).Count -ne 4 -or
            @($pilot.evidence.kind | Sort-Object) -join '|' -cne
                'compilerManifest|request|result|sarif') { return $false }
        foreach ($evidence in @($pilot.evidence)) {
            $path = [string]$evidence.path
            if ([string]::IsNullOrWhiteSpace($path) -or [IO.Path]::IsPathRooted($path) -or
                $path.Contains('..') -or [int64]$evidence.bytes -le 0) { return $false }
        }
        $resultEvidence = @($pilot.evidence | Where-Object kind -ceq 'result')
        if ($resultEvidence.Count -ne 1) { return $false }
        $resultPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string]$resultEvidence[0].path)))
        $root = [IO.Path]::GetFullPath($RepositoryRoot)
        if (-not $resultPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal) -or
            -not (Test-Path -LiteralPath $resultPath -PathType Leaf) -or
            [int64](Get-Item -LiteralPath $resultPath).Length -ne [int64]$resultEvidence[0].bytes) { return $false }
        try { $response = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -ErrorAction Stop }
        catch { return $false }
        $manifestClaims = @($response.manifest.claims)
        $claimResults = @($response.claimResults)
        $actual = @(ConvertTo-SharpProofPilotClaimEvidence `
            -ManifestClaims $manifestClaims `
            -ClaimResults $claimResults)
        $reported = @($pilot.claimEvidence | Sort-Object claimId)
        if ($actual.Count -eq 0 -or $actual.Count -ne $reported.Count -or
            @($actual.claimId | Select-Object -Unique).Count -ne $actual.Count) { return $false }
        for ($index = 0; $index -lt $actual.Count; $index++) {
            if ([string]$reported[$index].claimId -cne [string]$actual[$index].claimId -or
                [string]$reported[$index].kind -cne [string]$actual[$index].kind -or
                [string]$reported[$index].outcome -cne [string]$actual[$index].outcome) { return $false }
        }
        $kinds = @($actual.kind | Select-Object -Unique)
        if (($expectedPilot.category -eq 'effect-heavy' -and $kinds -cnotcontains 'Effect') -or
            ($expectedPilot.category -eq 'contract-heavy' -and $kinds -cnotcontains 'Postcondition') -or
            ($expectedPilot.category -eq 'mixed-strict' -and
                ($kinds -cnotcontains 'Effect' -or $kinds -cnotcontains 'Postcondition'))) { return $false }
    }
    return $true
}
