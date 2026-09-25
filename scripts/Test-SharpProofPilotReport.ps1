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

function Get-SharpProofPilotDiagnosticsFromSarif {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Sarif)

    if ($Sarif -isnot [pscustomobject] -or
        [string]$Sarif.version -cne '2.1.0' -or
        @($Sarif.runs).Count -eq 0) {
        throw 'Pilot SARIF is not a nonempty SARIF 2.1.0 document.'
    }

    $counts = [Collections.Generic.SortedDictionary[string, int]]::new(
        [StringComparer]::Ordinal)
    foreach ($run in @($Sarif.runs)) {
        if ($run -isnot [pscustomobject]) {
            throw 'Pilot SARIF contains an invalid run.'
        }
        $resultsProperty = $run.PSObject.Properties['results']
        if ($null -eq $resultsProperty) { continue }
        $tool = $run.PSObject.Properties['tool']
        $driver = if ($null -ne $tool -and $null -ne $tool.Value) {
            $tool.Value.PSObject.Properties['driver']
        } else { $null }
        $rulesProperty = if ($null -ne $driver -and $null -ne $driver.Value) {
            $driver.Value.PSObject.Properties['rules']
        } else { $null }
        $rules = if ($null -ne $rulesProperty) { @($rulesProperty.Value) } else { @() }
        foreach ($result in @($resultsProperty.Value | Where-Object { $null -ne $_ })) {
            if ($result -isnot [pscustomobject]) {
                throw 'Pilot SARIF contains an invalid result.'
            }
            $ruleIdProperty = $result.PSObject.Properties['ruleId']
            $ruleId = if ($null -ne $ruleIdProperty) {
                [string]$ruleIdProperty.Value
            } else { '' }
            if ([string]::IsNullOrWhiteSpace($ruleId)) {
                $ruleIndexProperty = $result.PSObject.Properties['ruleIndex']
                $ruleIndex = -1
                if ($null -eq $ruleIndexProperty -or
                    -not [int]::TryParse([string]$ruleIndexProperty.Value, [ref]$ruleIndex) -or
                    $ruleIndex -lt 0 -or $ruleIndex -ge $rules.Count -or
                    $null -eq $rules[$ruleIndex] -or
                    $null -eq $rules[$ruleIndex].PSObject.Properties['id']) {
                    throw 'Pilot SARIF result has no resolvable rule ID.'
                }
                $ruleId = [string]$rules[$ruleIndex].id
            }
            if ($ruleId.StartsWith('SP', [StringComparison]::Ordinal) -and
                $ruleId -cnotmatch '^SP[0-9]{4}$') {
                throw 'Pilot SARIF contains a malformed SharpProof diagnostic ID.'
            }
            if ($ruleId -cnotmatch '^SP[0-9]{4}$') { continue }
            if (-not $counts.ContainsKey($ruleId)) { $counts.Add($ruleId, 0) }
            $counts[$ruleId] = [int]$counts[$ruleId] + 1
        }
    }

    return @($counts.GetEnumerator() | ForEach-Object {
        [pscustomobject]@{ id = [string]$_.Key; count = [int]$_.Value }
    })
}

function Get-SharpProofPilotReviewLedgerSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)]$Ledger
    )

    function Require-ExactProperties($Value, [string[]]$Names, [string]$Label) {
        if ($null -eq $Value) { throw "$Label is missing." }
        $actual = @($Value.PSObject.Properties.Name | Sort-Object)
        $expected = @($Names | Sort-Object)
        if (($actual -join '|') -cne ($expected -join '|')) {
            throw "$Label has an invalid property set."
        }
    }

    Require-ExactProperties $Ledger `
        @('schemaVersion','commit','packageArtifacts','reviews') 'Review ledger'
    if ([int]$Ledger.schemaVersion -ne 2 -or
        [string]$Ledger.commit -cne [string]$Report.commit) {
        throw 'The review ledger is stale or has the wrong identity.'
    }

    $sourcePackages = @($Report.packageArtifacts | Sort-Object fileName)
    $ledgerPackages = @($Ledger.packageArtifacts | Sort-Object fileName)
    if ($sourcePackages.Count -ne 6 -or $ledgerPackages.Count -ne 6) {
        throw 'The review ledger must bind the exact six packages.'
    }
    for ($index = 0; $index -lt 6; $index++) {
        Require-ExactProperties $ledgerPackages[$index] `
            @('bytes','fileName','packageId','repositoryCommit','sha256','version') `
            'Review ledger package'
        foreach ($name in @('fileName','packageId','version','repositoryCommit','bytes','sha256')) {
            if ([string]$sourcePackages[$index].$name -cne
                [string]$ledgerPackages[$index].$name) {
                throw 'The review ledger package identities do not match the report.'
            }
        }
    }

    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $diagnosticCounts = @{}
    foreach ($pilot in @($Report.pilots)) {
        $pilotId = [string]$pilot.id
        foreach ($claim in @($pilot.claimEvidence | Where-Object { $null -ne $_ })) {
            $claimId = [string]$claim.claimId
            if ([string]::IsNullOrWhiteSpace($pilotId) -or
                [string]::IsNullOrWhiteSpace($claimId) -or
                $pilotId.Contains('|') -or $claimId.Contains('|')) {
                throw 'The report contains an invalid review identity.'
            }
            if (-not $expected.Add("$pilotId|Claim|$claimId")) {
                throw 'The report contains a duplicate claim identity.'
            }
        }
        foreach ($diagnostic in @($pilot.diagnostics | Where-Object { $null -ne $_ })) {
            $id = [string]$diagnostic.id
            $key = "$pilotId|Diagnostic|$id"
            if ($id -cnotmatch '^SP[0-9]{4}$' -or
                [int]$diagnostic.count -le 0 -or
                -not $expected.Add($key)) {
                throw 'The report contains an invalid or duplicate diagnostic identity.'
            }
            $diagnosticCounts[$key] = [int]$diagnostic.count
        }
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $falsePositives = @{}
    foreach ($review in @($Ledger.reviews | Where-Object { $null -ne $_ })) {
        Require-ExactProperties $review @('disposition','id','kind','pilotId') 'Review row'
        $pilotId = [string]$review.pilotId
        $kind = [string]$review.kind
        $id = [string]$review.id
        $key = "$pilotId|$kind|$id"
        $disposition = [string]$review.disposition
        if (-not $expected.Contains($key) -or -not $seen.Add($key) -or
            @('TruePositive','FalsePositive') -cnotcontains $disposition) {
            throw 'The review ledger contains an unknown, duplicate, or contradictory row.'
        }
        if ($disposition -ceq 'FalsePositive') {
            $count = if ($kind -ceq 'Diagnostic') {
                [int]$diagnosticCounts[$key]
            } else { 1 }
            $falsePositives[$pilotId] = $count + [int]($falsePositives[$pilotId] ?? 0)
        }
    }
    if ($seen.Count -ne $expected.Count) {
        throw 'The review ledger is incomplete.'
    }

    return [pscustomobject]@{ falsePositiveCounts = $falsePositives }
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

    $ledgerHashProperty = $Report.PSObject.Properties['reviewLedgerSha256']
    if ([int]$Report.schemaVersion -ne 6 -or
        @('Unreviewed', 'Reviewed') -cnotcontains [string]$Report.reviewStatus -or
        ([string]$Report.reviewStatus -ceq 'Reviewed' -and
            ($null -eq $ledgerHashProperty -or
                [string]$ledgerHashProperty.Value -cnotmatch '^[0-9a-f]{64}$')) -or
        ([string]$Report.reviewStatus -ceq 'Unreviewed' -and
            $null -ne $ledgerHashProperty -and $null -ne $ledgerHashProperty.Value) -or
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
                [string]$_.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
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
            [string]$pilot.resultPath -cne
                "artifacts/pilots/runs/$([string]$Report.runId)/$([string]$pilot.id)/evidence/result.json" -or
            @($pilot.evidence).Count -ne 4 -or
            @($pilot.evidence.kind | Sort-Object) -join '|' -cne
                'compilerManifest|request|result|sarif') { return $false }
        $expectedEvidenceNames = @{
            request = 'request.json'
            result = 'result.json'
            compilerManifest = 'compiler-manifest.json'
            sarif = 'result.sarif'
        }
        $pathComparison = if ([IO.Path]::DirectorySeparatorChar -eq [char]'\') {
            [StringComparison]::OrdinalIgnoreCase
        } else {
            [StringComparison]::Ordinal
        }
        $root = [IO.Path]::GetFullPath($RepositoryRoot)
        $rootPath = [IO.Path]::GetPathRoot($root)
        if (-not [string]::Equals($root, $rootPath, $pathComparison)) {
            $root = $root.TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar)
        }
        $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
        $evidenceByKind = @{}
        foreach ($evidence in @($pilot.evidence)) {
            if ((@($evidence.PSObject.Properties.Name | Sort-Object) -join '|') -cne
                'bytes|kind|path|sha256') { return $false }
            $kind = [string]$evidence.kind
            $path = [string]$evidence.path
            $expectedPath = "artifacts/pilots/runs/$([string]$Report.runId)/" +
                "$([string]$pilot.id)/evidence/$($expectedEvidenceNames[$kind])"
            if ([string]::IsNullOrWhiteSpace($path) -or
                [IO.Path]::IsPathRooted($path) -or
                $path -cne $expectedPath -or
                [int64]$evidence.bytes -le 0 -or
                [string]$evidence.sha256 -cnotmatch '^[0-9a-f]{64}$') { return $false }
            try {
                $resolvedPath = [IO.Path]::GetFullPath((Join-Path $root $path))
                if (-not $resolvedPath.StartsWith($rootPrefix, $pathComparison) -or
                    -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
                    return $false
                }
                $file = Get-Item -LiteralPath $resolvedPath
                if ($file.Length -ne [int64]$evidence.bytes -or $file.Length -le 0) {
                    return $false
                }
                $actualSha256 = (Get-FileHash -LiteralPath $resolvedPath `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            } catch { return $false }
            if ($actualSha256 -cne [string]$evidence.sha256 -or
                $evidenceByKind.ContainsKey($kind)) { return $false }
            $evidenceByKind[$kind] = [pscustomobject]@{
                path = $resolvedPath
                sha256 = $actualSha256
            }
        }
        if ($evidenceByKind.Count -ne 4) { return $false }
        try {
            $request = Get-Content -LiteralPath $evidenceByKind['request'].path -Raw |
                ConvertFrom-Json -ErrorAction Stop
            $compilerManifest = Get-Content -LiteralPath $evidenceByKind['compilerManifest'].path -Raw |
                ConvertFrom-Json -ErrorAction Stop
            $response = Get-Content -LiteralPath $evidenceByKind['result'].path -Raw |
                ConvertFrom-Json -ErrorAction Stop
            $sarif = Get-Content -LiteralPath $evidenceByKind['sarif'].path -Raw |
                ConvertFrom-Json -ErrorAction Stop
        } catch { return $false }
        if ($request -isnot [pscustomobject] -or
            $compilerManifest -isnot [pscustomobject] -or
            $response -isnot [pscustomobject] -or
            $sarif -isnot [pscustomobject] -or
            $request.PSObject.Properties.Name -cnotcontains 'compilerManifest' -or
            $compilerManifest.PSObject.Properties.Name -cnotcontains 'manifest' -or
            $compilerManifest.manifest -isnot [pscustomobject] -or
            $compilerManifest.manifest.PSObject.Properties.Name -cnotcontains 'claims' -or
            $response.PSObject.Properties.Name -cnotcontains 'requestHash' -or
            $response.PSObject.Properties.Name -cnotcontains 'runStatus' -or
            $response.PSObject.Properties.Name -cnotcontains 'manifest' -or
            $response.manifest -isnot [pscustomobject] -or
            $response.manifest.PSObject.Properties.Name -cnotcontains 'claims' -or
            $response.PSObject.Properties.Name -cnotcontains 'claimResults' -or
            $sarif.PSObject.Properties.Name -cnotcontains 'version' -or
            $sarif.PSObject.Properties.Name -cnotcontains 'runs') { return $false }
        if ($request.compilerManifest -isnot [pscustomobject] -or
            $request.compilerManifest.PSObject.Properties.Name -cnotcontains 'path' -or
            $request.compilerManifest.PSObject.Properties.Name -cnotcontains 'sha256') {
            return $false
        }
        if (
            [string]::IsNullOrWhiteSpace([string]$request.compilerManifest.path) -or
            [IO.Path]::GetFileName([string]$request.compilerManifest.path) -cne 'compiler-manifest.json' -or
            [string]$request.compilerManifest.sha256 -cne $evidenceByKind['compilerManifest'].sha256 -or
            [string]$response.requestHash -cnotmatch '^[0-9a-f]{64}$' -or
            [string]$response.runStatus -cne 'Complete' -or
            [string]$sarif.version -cne '2.1.0' -or
            @($sarif.runs).Count -eq 0) { return $false }
        try {
            $actualDiagnostics = @(Get-SharpProofPilotDiagnosticsFromSarif $sarif)
        } catch { return $false }
        $reportedDiagnostics = @($pilot.diagnostics | Where-Object { $null -ne $_ })
        if ($actualDiagnostics.Count -ne $reportedDiagnostics.Count) { return $false }
        for ($index = 0; $index -lt $actualDiagnostics.Count; $index++) {
            $diagnostic = $reportedDiagnostics[$index]
            if ((@($diagnostic.PSObject.Properties.Name | Sort-Object) -join '|') -cne 'count|id' -or
                [string]$diagnostic.id -cne [string]$actualDiagnostics[$index].id -or
                [int]$diagnostic.count -ne [int]$actualDiagnostics[$index].count) {
                return $false
            }
        }
        $manifestClaims = @($compilerManifest.manifest.claims)
        $resultManifestClaims = @($response.manifest.claims)
        $claimResults = @($response.claimResults)
        $actual = @(ConvertTo-SharpProofPilotClaimEvidence `
            -ManifestClaims $manifestClaims `
            -ClaimResults $claimResults)
        $resultManifestEvidence = @(ConvertTo-SharpProofPilotClaimEvidence `
            -ManifestClaims $resultManifestClaims `
            -ClaimResults $claimResults)
        $reported = @($pilot.claimEvidence | Sort-Object claimId)
        if ($actual.Count -eq 0 -or $actual.Count -ne $reported.Count -or
            $resultManifestEvidence.Count -ne $actual.Count -or
            @($actual.claimId | Select-Object -Unique).Count -ne $actual.Count) { return $false }
        for ($index = 0; $index -lt $actual.Count; $index++) {
            if ([string]$actual[$index].claimId -cne [string]$resultManifestEvidence[$index].claimId -or
                [string]$actual[$index].kind -cne [string]$resultManifestEvidence[$index].kind -or
                [string]$reported[$index].claimId -cne [string]$actual[$index].claimId -or
                [string]$reported[$index].kind -cne [string]$actual[$index].kind -or
                [string]$reported[$index].outcome -cne [string]$actual[$index].outcome) { return $false }
        }
        $kinds = @($actual.kind | Select-Object -Unique)
        if ($expectedPilot.category -eq 'mixed-strict' -and
            @($actual | Where-Object { [string]$_.outcome -cne 'Proven' }).Count -ne 0) {
            return $false
        }
        if (($expectedPilot.category -eq 'effect-heavy' -and $kinds -cnotcontains 'Effect') -or
            ($expectedPilot.category -eq 'contract-heavy' -and $kinds -cnotcontains 'Postcondition') -or
            ($expectedPilot.category -eq 'mixed-strict' -and
                ($kinds -cnotcontains 'Effect' -or $kinds -cnotcontains 'Postcondition'))) { return $false }
    }
    return $true
}
