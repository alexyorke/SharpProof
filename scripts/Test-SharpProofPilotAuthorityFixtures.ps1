[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-SharpProofPilotPackageAuthority.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sp-pilot-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $fixture 'packages'
$commit = ''
$version = '1.0.0-preview.1'

# Execute the producer's result projection with controlled build evidence.
# Hand-built report fixtures alone cannot detect a producer claiming a review
# happened before a reviewer supplied a disposition.
$producer = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Test-SharpProofPilots.ps1'),
    [ref]$null, [ref]$null)
$projections = @($producer.FindAll({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -ceq '$results' -and
        $node.Operator -eq [Management.Automation.Language.TokenKind]::PlusEquals
}, $true))
if ($projections.Count -ne 1) { throw 'Expected one pilot result projection.' }
$runRootAssignments = @($producer.FindAll({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -ceq '$runRoot'
}, $true))
$cachePathAssignments = @($producer.FindAll({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -ceq '$cachePath'
}, $true))
if ($runRootAssignments.Count -ne 1 -or
    $runRootAssignments[0].Right.Extent.Text -notmatch '\[IO\.Path\]::GetTempPath\(\)' -or
    $cachePathAssignments.Count -ne 1 -or
    $cachePathAssignments[0].Right.Extent.Text -notmatch '\$runRoot') {
    throw 'Pilot verifier caches must use a task-local run root.'
}
$projectionRoot = Join-Path ([IO.Path]::GetTempPath()) ('sp-pilot-projection-' + [Guid]::NewGuid().ToString('N'))
$projectionEvidenceDirectory = Join-Path $projectionRoot 'evidence'
[IO.Directory]::CreateDirectory($projectionEvidenceDirectory) | Out-Null
foreach ($fileName in @('request.json','result.json','compiler-manifest.json','result.sarif')) {
    [IO.File]::WriteAllText((Join-Path $projectionEvidenceDirectory $fileName), '{}')
}
& {
    $pilot = [pscustomobject]@{
        id='projection'; project='Projection.csproj'; category='contract-heavy'
        library='Projection'; libraryVersion='1.0.0'; setupFriction='none'
    }
    $response = [pscustomobject]@{ runStatus='Complete' }
    $claims = @([pscustomobject]@{ outcome='Proven' })
    $claimEvidence = @()
    $unknownReasons = @()
    $diagnosticIds = @()
    $build = [pscustomobject]@{
        elapsedMilliseconds=1; observedPeakWorkingSetBytes=0
    }
    $negativeProbePassed = $true
    $repositoryRoot = $projectionRoot
    $resultPath = Join-Path $projectionEvidenceDirectory 'result.json'
    $sarifPath = Join-Path $projectionEvidenceDirectory 'result.sarif'
    $evidenceFiles = @('request.json','result.json','compiler-manifest.json','result.sarif') |
        ForEach-Object { Join-Path $projectionEvidenceDirectory $_ }
    $results = @()
    . ([scriptblock]::Create($projections[0].Extent.Text))
    if ($results.Count -ne 1 -or $null -ne $results[0].falsePositiveReports) {
        throw 'A produced pilot result must leave false-positive review unreported.'
    }
}
Remove-Item -LiteralPath $projectionRoot -Recurse -Force

function Write-Package([string]$Id, [string]$Extension, [string]$Commit = $commit,
    [string]$PackageVersion = $version) {
    $path = Join-Path $packages "$Id.$version$Extension"
    $archive = [IO.Compression.ZipFile]::Open($path, 'Create')
    try {
        $entry = $archive.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open())
        try {
            $writer.Write("<package><metadata><id>$Id</id><version>$PackageVersion</version><repository commit=`"$Commit`" /></metadata></package>")
        } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
}

function Reset-Packages {
    if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
    [IO.Directory]::CreateDirectory($packages) | Out-Null
    foreach ($id in @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier')) {
        Write-Package $id '.nupkg'; Write-Package $id '.snupkg'
    }
}

function Require-Failure([scriptblock]$Action, [string]$Name) {
    try { & $Action; throw "Fixture '$Name' was accepted." }
    catch { if ($_.Exception.Message -eq "Fixture '$Name' was accepted.") { throw } }
}

function Initialize-FixtureRepository {
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize the receipt fixture repository.' }
    & git -C $fixture config user.email 'fixture@example.invalid'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the receipt fixture repository.' }
    & git -C $fixture config user.name 'Fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the receipt fixture repository.' }
    [IO.File]::WriteAllText((Join-Path $fixture 'tracked.txt'), "fixture`n")
    & git -C $fixture add -- tracked.txt
    if ($LASTEXITCODE -ne 0) { throw 'Failed to stage the receipt fixture repository.' }
    & git -C $fixture commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Failed to commit the receipt fixture repository.' }
    $script:commit = (& git -C $fixture rev-parse HEAD).Trim()
    if ($script:commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Receipt fixture repository has no full commit identity.'
    }
}

try {
    Initialize-FixtureRepository
    Reset-Packages
    $valid = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    if ($valid.Count -ne 6) { throw 'Canonical package authority failed.' }
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.snupkg")
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } missing-package
    Reset-Packages; Copy-Item (Join-Path $packages "SharpProof.$version.nupkg") (Join-Path $packages 'extra.nupkg')
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } extra-package
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package SharpProof '.nupkg' ('2' * 40)
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } stale-commit
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package Wrong '.nupkg'
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } wrong-id
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package SharpProof '.nupkg' $commit '9.9.9'
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } wrong-version

    # Restore canonical after the wrong-version case.
    Reset-Packages; $artifacts = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    $pilotRoot = Join-Path $fixture 'eng/pilots'
    [IO.Directory]::CreateDirectory($pilotRoot) | Out-Null
    $catalogRows = @(
        [ordered]@{ id='effect-one'; category='effect-heavy'; project='EffectOne/EffectOne.csproj'; library='Library.One'; libraryVersion='1.0.0'; setupFriction='none' },
        [ordered]@{ id='effect-two'; category='effect-heavy'; project='EffectTwo/EffectTwo.csproj'; library='Library.Two'; libraryVersion='2.0.0'; setupFriction='none' },
        [ordered]@{ id='contract-one'; category='contract-heavy'; project='ContractOne/ContractOne.csproj'; library='Library.Three'; libraryVersion='3.0.0'; setupFriction='none' },
        [ordered]@{ id='contract-two'; category='contract-heavy'; project='ContractTwo/ContractTwo.csproj'; library='Library.Four'; libraryVersion='4.0.0'; setupFriction='none' },
        [ordered]@{ id='mixed-one'; category='mixed-strict'; project='MixedOne/MixedOne.csproj'; library='Library.Five'; libraryVersion='5.0.0'; setupFriction='none' }
    )
    $catalogPath = Join-Path $pilotRoot 'catalog.json'
    [IO.File]::WriteAllText($catalogPath, ([ordered]@{ schemaVersion=1; pilots=$catalogRows } |
            ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    foreach ($row in $catalogRows) {
        $project = Join-Path $pilotRoot $row.project
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($project)) | Out-Null
        [IO.File]::WriteAllText($project,
            "<Project><ItemGroup><PackageReference Include=`"$($row.library)`" Version=`"$($row.libraryVersion)`" /><PackageReference Include=`"SharpProof`" Version=`"$version`" /></ItemGroup></Project>",
            [Text.UTF8Encoding]::new($false))
    }
    $reportPilots = @($catalogRows | ForEach-Object {
        $row = $_
        $kinds = if ($row.category -eq 'mixed-strict') { @('Effect','Postcondition') }
            elseif ($row.category -eq 'effect-heavy') { @('Effect') }
            else { @('Postcondition') }
        $manifestClaims = @($kinds | ForEach-Object -Begin { $ordinal = 0 } -Process {
                $ordinal++; [pscustomobject]@{ claimId="$($row.id)-$ordinal"; kind=$_ }
            })
        $claimResults = @($manifestClaims | ForEach-Object {
                [pscustomobject]@{ claimId=$_.claimId; outcome='Proven' }
            })
        $evidenceDirectory = Join-Path $fixture "artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence"
        [IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
        $manifestPath = Join-Path $evidenceDirectory 'compiler-manifest.json'
        [IO.File]::WriteAllText($manifestPath,
            ([ordered]@{ manifest=[ordered]@{ claims=$manifestClaims } } | ConvertTo-Json -Depth 6),
            [Text.UTF8Encoding]::new($false))
        $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $requestPath = Join-Path $evidenceDirectory 'request.json'
        [IO.File]::WriteAllText($requestPath,
            ([ordered]@{ protocolVersion='12'; compilerManifest=[ordered]@{
                    path='compiler-manifest.json'; sha256=$manifestHash
                } } | ConvertTo-Json -Depth 6),
            [Text.UTF8Encoding]::new($false))
        $resultPath = Join-Path $evidenceDirectory 'result.json'
        [IO.File]::WriteAllText($resultPath,
            ([ordered]@{ requestHash=('0' * 64); runStatus='Complete'; manifest=[ordered]@{ claims=$manifestClaims }; claimResults=$claimResults } |
                ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        $sarifPath = Join-Path $evidenceDirectory 'result.sarif'
        [IO.File]::WriteAllText($sarifPath,
            '{"version":"2.1.0","runs":[{}]}',
            [Text.UTF8Encoding]::new($false))
        $evidence = @(
            [pscustomobject]@{ kind='request'; path="artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence/request.json" },
            [pscustomobject]@{ kind='result'; path="artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence/result.json" },
            [pscustomobject]@{ kind='compilerManifest'; path="artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence/compiler-manifest.json" },
            [pscustomobject]@{ kind='sarif'; path="artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence/result.sarif" }
        ) | ForEach-Object {
            $path = Join-Path $fixture $_.path
            [pscustomobject]@{
                kind=$_.kind; path=$_.path
                bytes=[int64](Get-Item -LiteralPath $path).Length
                sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        [pscustomobject]@{
            id=$row.id; project=$row.project; category=$row.category; library=$row.library
            libraryVersion=$row.libraryVersion; runStatus='Complete'; sarifProduced=$true
            resultPath="artifacts/pilots/runs/$('1' * 32)/$($row.id)/evidence/result.json"
            claimEvidence=@($manifestClaims | ForEach-Object {
                    [pscustomobject]@{ claimId=$_.claimId; kind=$_.kind; outcome='Proven' }
                })
            diagnostics=@()
            falsePositiveReports=$null
            evidence=$evidence
        }
    })
    $report = [pscustomobject]@{
        schemaVersion=5; reviewStatus='Unreviewed'; runId=('1' * 32); commit=$commit; packageVersion=$version; pilotCount=5
        packageArtifacts=$artifacts
        pilots=$reportPilots
    }
    function Test-Report($Value) {
        Test-SharpProofPilotReport $Value $commit -RepositoryRoot $fixture -CatalogPath $catalogPath
    }
    function Copy-Json($Value) { $Value | ConvertTo-Json -Depth 10 | ConvertFrom-Json }
    function Set-EvidenceContentIdentity($Evidence, [string]$Path) {
        $Evidence.bytes = [int64](Get-Item -LiteralPath $Path).Length
        $Evidence.sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if (-not (Test-Report $report)) { throw 'Canonical pilot report failed.' }
    $canonicalReport = Copy-Json $report
    foreach ($kind in @('request','result','compilerManifest','sarif')) {
        $pilot = $canonicalReport.pilots[0]
        $evidence = @($pilot.evidence | Where-Object kind -CEQ $kind)[0]
        $path = Join-Path $fixture $evidence.path
        $original = [IO.File]::ReadAllBytes($path)
        try {
            Remove-Item -LiteralPath $path -Force
            if (Test-Report $canonicalReport) {
                throw "Missing '$kind' publication evidence was accepted."
            }
            [IO.File]::WriteAllBytes($path, [byte[]]::new(0))
            if (Test-Report $canonicalReport) {
                throw "Empty '$kind' publication evidence was accepted."
            }
            [IO.File]::WriteAllBytes($path, $original)
            $altered = [byte[]]$original.Clone()
            $altered[0] = [byte]($altered[0] -bxor 1)
            [IO.File]::WriteAllBytes($path, $altered)
            if (Test-Report $canonicalReport) {
                throw "Altered '$kind' publication evidence was accepted."
            }
        } finally {
            [IO.File]::WriteAllBytes($path, $original)
        }
    }
    $changed = Copy-Json $canonicalReport
    $changed.pilots[0].resultPath = 'results/other-run/result.json'
    if (Test-Report $changed) { throw 'A result path from another run was accepted.' }
    $requestEvidence = @($canonicalReport.pilots[0].evidence | Where-Object kind -CEQ 'request')[0]
    $requestPath = Join-Path $fixture $requestEvidence.path
    $originalRequest = [IO.File]::ReadAllBytes($requestPath)
    try {
        $request = [Text.Encoding]::UTF8.GetString($originalRequest) | ConvertFrom-Json
        $request.compilerManifest.sha256 = 'f' * 64
        [IO.File]::WriteAllText(
            $requestPath,
            ($request | ConvertTo-Json -Depth 10),
            [Text.UTF8Encoding]::new($false))
        $changed = Copy-Json $canonicalReport
        $changedRequestEvidence = @($changed.pilots[0].evidence | Where-Object kind -CEQ 'request')[0]
        Set-EvidenceContentIdentity $changedRequestEvidence $requestPath
        if (Test-Report $changed) { throw 'A request bound to a different compiler manifest was accepted.' }
    } finally {
        [IO.File]::WriteAllBytes($requestPath, $originalRequest)
    }
    $canonicalReport.pilots[0].diagnostics = @(
        [pscustomobject]@{ id='SP0001'; count=2 }
    )
    $sourcePath = Join-Path $fixture 'report.json'
    [IO.File]::WriteAllText(
        $sourcePath,
        ($canonicalReport | ConvertTo-Json -Depth 20) + "`n",
        [Text.UTF8Encoding]::new($false))
    $templatePath = Join-Path $fixture 'review-ledger.template.json'
    & (Join-Path $PSScriptRoot 'New-SharpProofPilotReviewLedger.ps1') `
        -SourceReportPath $sourcePath `
        -OutputPath $templatePath `
        -RepositoryRoot $fixture `
        -CatalogPath $catalogPath
    $template = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
    $expectedReviewRows = @(
        foreach ($pilot in $canonicalReport.pilots) {
            foreach ($claim in $pilot.claimEvidence) {
                [pscustomobject]@{
                    pilotId = [string]$pilot.id
                    kind = 'Claim'
                    id = [string]$claim.claimId
                }
            }
            foreach ($diagnostic in $pilot.diagnostics) {
                [pscustomobject]@{
                    pilotId = [string]$pilot.id
                    kind = 'Diagnostic'
                    id = [string]$diagnostic.id
                }
            }
        }
    )
    $actualReviewRows = @($template.reviews | ForEach-Object {
            [pscustomobject]@{
                pilotId = [string]$_.pilotId
                kind = [string]$_.kind
                id = [string]$_.id
            }
        })
    if ([int]$template.schemaVersion -ne 2 -or
        [string]$template.commit -cne $commit -or
        $template.packageArtifacts.Count -ne 6 -or
        ($actualReviewRows | ConvertTo-Json -Compress) -cne
            ($expectedReviewRows | ConvertTo-Json -Compress) -or
        @($template.reviews | Where-Object {
                [string]::IsNullOrWhiteSpace([string]$_.disposition)
            }).Count -ne $expectedReviewRows.Count) {
        throw 'Review ledger template omitted, changed, or pre-approved a finding.'
    }
    $report.pilots[0].evidence = @($report.pilots[0].evidence | Select-Object -Skip 1)
    if (Test-Report $report) { throw 'Stale/incomplete outputs were accepted.' }
    $report = Copy-Json $canonicalReport
    $changed = Copy-Json $canonicalReport; $changed.pilots[1].id = $changed.pilots[0].id
    if (Test-Report $changed) { throw 'Duplicate pilot IDs were accepted.' }
    $changed = Copy-Json $canonicalReport; $changed.pilots[1].project = $changed.pilots[0].project
    if (Test-Report $changed) { throw 'Duplicate pilot projects were accepted.' }
    $changed = Copy-Json $canonicalReport; $changed.pilots[1].library = $changed.pilots[0].library
    if (Test-Report $changed) { throw 'Duplicate library identities were accepted.' }
    $changed = Copy-Json $canonicalReport; $changed.pilots[0].category = 'contract-heavy'
    if (Test-Report $changed) { throw 'Mislabeled pilot category was accepted.' }
    $changed = Copy-Json $canonicalReport; $changed.pilots[0].claimEvidence = @()
    if (Test-Report $changed) { throw 'Zero claim evidence was accepted.' }
    $firstResultEvidence = $canonicalReport.pilots[0].evidence.Where({$_.kind -eq 'result'})[0]
    $firstResultPath = Join-Path $fixture $firstResultEvidence.path
    $originalResult = [IO.File]::ReadAllText($firstResultPath)
    [IO.File]::WriteAllText($firstResultPath,
        '{"manifest":{"claims":[]},"claimResults":[]}')
    $changed = Copy-Json $canonicalReport
    $changed.pilots[0].claimEvidence = @()
    $changedResultEvidence = $changed.pilots[0].evidence.Where({$_.kind -eq 'result'})[0]
    Set-EvidenceContentIdentity $changedResultEvidence $firstResultPath
    if (Test-Report $changed) { throw 'Zero-claim result was accepted.' }
    [IO.File]::WriteAllText($firstResultPath, $originalResult)
    $contractResultEvidence = $canonicalReport.pilots[2].evidence.Where({$_.kind -eq 'result'})[0]
    $contractResultPath = Join-Path $fixture $contractResultEvidence.path
    $originalContractResult = [IO.File]::ReadAllText($contractResultPath)
    [IO.File]::WriteAllText($contractResultPath,
        '{"manifest":{"claims":[{"claimId":"wrong-kind","kind":"Effect"}]},"claimResults":[{"claimId":"wrong-kind","outcome":"Proven"}]}')
    $changed = Copy-Json $canonicalReport
    $changed.pilots[2].claimEvidence = @([pscustomobject]@{
            claimId='wrong-kind'; kind='Effect'; outcome='Proven'
        })
    $changedResultEvidence = $changed.pilots[2].evidence.Where({$_.kind -eq 'result'})[0]
    Set-EvidenceContentIdentity $changedResultEvidence $contractResultPath
    if (Test-Report $changed) { throw 'Contract pilot without a postcondition was accepted.' }
    [IO.File]::WriteAllText($contractResultPath, $originalContractResult)
    $canonicalCatalog = Get-Content $catalogPath -Raw | ConvertFrom-Json
    $changedCatalog = Copy-Json $canonicalCatalog
    $changedCatalog.pilots[1].id = $changedCatalog.pilots[0].id
    [IO.File]::WriteAllText($catalogPath, ($changedCatalog | ConvertTo-Json -Depth 5))
    if (Test-Report $canonicalReport) { throw 'Duplicate catalog IDs were accepted.' }
    $changedCatalog = Copy-Json $canonicalCatalog
    $changedCatalog.pilots[1].project = $changedCatalog.pilots[0].project
    [IO.File]::WriteAllText($catalogPath, ($changedCatalog | ConvertTo-Json -Depth 5))
    if (Test-Report $canonicalReport) { throw 'Duplicate catalog projects were accepted.' }
    $changedCatalog = Copy-Json $canonicalCatalog
    $changedCatalog.pilots[1].library = $changedCatalog.pilots[0].library
    [IO.File]::WriteAllText($catalogPath, ($changedCatalog | ConvertTo-Json -Depth 5))
    if (Test-Report $canonicalReport) { throw 'Duplicate catalog libraries were accepted.' }
    $changedCatalog = Copy-Json $canonicalCatalog
    $changedCatalog | Add-Member NoteProperty unexpected $true
    [IO.File]::WriteAllText($catalogPath, ($changedCatalog | ConvertTo-Json -Depth 5))
    if (Test-Report $canonicalReport) { throw 'Unknown catalog schema fields were accepted.' }
    [IO.File]::WriteAllText($catalogPath, ($canonicalCatalog | ConvertTo-Json -Depth 5))
    $firstProject = Join-Path $pilotRoot $catalogRows[0].project
    $originalProject = [IO.File]::ReadAllText($firstProject)
    [IO.File]::WriteAllText($firstProject, $originalProject.Replace('Library.One','Wrong.Library'))
    if (Test-Report $report) { throw 'Wrong external project reference was accepted.' }
    [IO.File]::WriteAllText($firstProject, $originalProject)
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'ambient/sharpproof/1.0.0-preview.1')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'ambient/sharpproof/1.0.0-preview.1/SharpProof.dll'), 'foreign')
    $sourcePath = Join-Path $fixture 'report.json'
    $ledgerPath = Join-Path $fixture 'review-ledger.json'
    $reviewedPath = Join-Path $fixture 'reviewed-report.json'
    [IO.File]::WriteAllText($sourcePath, ($canonicalReport | ConvertTo-Json -Depth 20) + "`n")
    $reviewRows = @($canonicalReport.pilots | ForEach-Object {
        $pilot = $_
        @(
            foreach ($claim in $pilot.claimEvidence) {
                [ordered]@{
                    pilotId=$pilot.id; kind='Claim'; id=$claim.claimId
                    disposition='TruePositive'
                }
            }
            foreach ($diagnostic in $pilot.diagnostics) {
                [ordered]@{
                    pilotId=$pilot.id; kind='Diagnostic'; id=$diagnostic.id
                    disposition='TruePositive'
                }
            }
        )
    })
    $ledger = [ordered]@{
        schemaVersion=2
        commit=$commit
        packageArtifacts=$canonicalReport.packageArtifacts
        reviews=$reviewRows
    }
    function Write-Ledger($Value) {
        [IO.File]::WriteAllText($ledgerPath, ($Value | ConvertTo-Json -Depth 20) + "`n")
    }
    function Complete-Review {
        & (Join-Path $PSScriptRoot 'Complete-SharpProofPilotReview.ps1') `
            -SourceReportPath $sourcePath -ReviewLedgerPath $ledgerPath `
            -OutputPath $reviewedPath -RepositoryRoot $fixture -CatalogPath $catalogPath
    }
    Write-Ledger $template
    Require-Failure { Complete-Review } blank-review-template
    Write-Ledger $ledger
    Complete-Review
    $reviewed = Get-Content $reviewedPath -Raw | ConvertFrom-Json
    if ([string]$reviewed.reviewStatus -cne 'Reviewed' -or
        @($reviewed.pilots | Where-Object falsePositiveReports -ne 0).Count -ne 0) {
        throw 'Honest zero false-positive review failed.'
    }
    $receiptScripts = Join-Path $fixture 'scripts'
    [IO.Directory]::CreateDirectory($receiptScripts) | Out-Null
    foreach ($scriptName in @(
            'Write-SharpProofQualificationReceipt.ps1',
            'SharpProof.ReleaseBundle.ps1',
            'SharpProof.ReleaseJson.ps1',
            'Test-SharpProofPilotReport.ps1',
            'SharpProof.PackageIdentity.psm1')) {
        Copy-Item (Join-Path $PSScriptRoot $scriptName) `
            (Join-Path $receiptScripts $scriptName)
    }
    $receiptWriter = Join-Path $receiptScripts 'Write-SharpProofQualificationReceipt.ps1'
    $receiptDirectory = Join-Path $fixture 'artifacts/release-qualification/qualification-receipts'
    $receiptPath = Join-Path $receiptDirectory 'pilots.json'
    function Write-PilotReceipt([string]$EvidencePath) {
        & $receiptWriter -Gate pilots -EvidencePath $EvidencePath `
            -ReceiptDirectory $receiptDirectory
    }
    Write-PilotReceipt $reviewedPath
    $receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
    $reviewedHash = (Get-FileHash -LiteralPath $reviewedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([string]$receipt.status -cne 'passed' -or
        [string]$receipt.commit -cne $commit -or
        [string]$receipt.evidence.sha256 -cne $reviewedHash -or
        @($receipt.pilotEvidence).Count -ne 5 -or
        (@($receipt.pilotEvidence.id | Sort-Object) -join '|') -cne
            (@($reviewed.pilots.id | Sort-Object) -join '|')) {
        throw 'Canonical reviewed pilot evidence did not produce an authoritative receipt.'
    }

    function Require-PilotReceiptRejection(
        [string]$Name,
        [string]$PilotId,
        [string]$Mutation) {
        $variant = Copy-Json $reviewed
        $pilot = @($variant.pilots | Where-Object { [string]$_.id -ceq $PilotId })[0]
        $resultEvidence = @($pilot.evidence | Where-Object kind -ceq 'result')[0]
        $resultPath = Join-Path $fixture $resultEvidence.path
        $originalResult = [IO.File]::ReadAllText($resultPath)
        try {
            $response = $originalResult | ConvertFrom-Json
            if ($Mutation -ceq 'Failed') {
                $response.runStatus = 'Failed'
            } else {
                $response.claimResults[0].outcome = $Mutation
                $pilot.claimEvidence[0].outcome = $Mutation
            }
            [IO.File]::WriteAllText(
                $resultPath,
                ($response | ConvertTo-Json -Depth 10),
                [Text.UTF8Encoding]::new($false))
            Set-EvidenceContentIdentity $resultEvidence $resultPath
            $invalidReportPath = Join-Path $fixture "$Name-report.json"
            [IO.File]::WriteAllText(
                $invalidReportPath,
                ($variant | ConvertTo-Json -Depth 20) + "`n",
                [Text.UTF8Encoding]::new($false))
            if (Test-Report $variant) {
                throw "Validator accepted '$Name' pilot evidence."
            }
            if (Test-Path -LiteralPath $receiptPath) {
                Remove-Item -LiteralPath $receiptPath -Force
            }
            Require-Failure { Write-PilotReceipt $invalidReportPath } $Name
            if (Test-Path -LiteralPath $receiptPath) {
                throw "Receipt writer published '$Name' pilot evidence."
            }
        } finally {
            [IO.File]::WriteAllText(
                $resultPath,
                $originalResult,
                [Text.UTF8Encoding]::new($false))
        }
    }

    $advisoryUnknown = Copy-Json $reviewed
    $advisoryPilot = @($advisoryUnknown.pilots | Where-Object id -ceq 'effect-one')[0]
    $advisoryResult = @($advisoryPilot.evidence | Where-Object kind -ceq 'result')[0]
    $advisoryResultPath = Join-Path $fixture $advisoryResult.path
    $advisoryOriginalResult = [IO.File]::ReadAllText($advisoryResultPath)
    try {
        $advisoryResponse = $advisoryOriginalResult | ConvertFrom-Json
        $advisoryResponse.claimResults[0].outcome = 'Unknown'
        $advisoryPilot.claimEvidence[0].outcome = 'Unknown'
        [IO.File]::WriteAllText(
            $advisoryResultPath,
            ($advisoryResponse | ConvertTo-Json -Depth 10),
            [Text.UTF8Encoding]::new($false))
        Set-EvidenceContentIdentity $advisoryResult $advisoryResultPath
        if (-not (Test-Report $advisoryUnknown)) {
            throw 'Documented advisory Unknown outcome was rejected.'
        }
    } finally {
        [IO.File]::WriteAllText(
            $advisoryResultPath,
            $advisoryOriginalResult,
            [Text.UTF8Encoding]::new($false))
    }
    Require-PilotReceiptRejection 'failed-response' 'effect-one' 'Failed'
    Require-PilotReceiptRejection 'strict-refuted' 'mixed-one' 'Refuted'
    Require-PilotReceiptRejection 'strict-unknown' 'mixed-one' 'Unknown'

    $ledger.reviews[0].disposition = 'FalsePositive'; Write-Ledger $ledger
    Complete-Review
    $reviewed = Get-Content $reviewedPath -Raw | ConvertFrom-Json
    if ([int]$reviewed.pilots[0].falsePositiveReports -ne 1) {
        throw 'False-positive disposition was not derived.'
    }
    $ledger.reviews = @($ledger.reviews | Select-Object -Skip 1); Write-Ledger $ledger
    Require-Failure { Complete-Review } incomplete-review
    $ledger.reviews = @($reviewRows + $reviewRows[0]); Write-Ledger $ledger
    Require-Failure { Complete-Review } duplicate-review
    $ledger.reviews = @($reviewRows); $ledger.reviews[0].id = 'unknown'; Write-Ledger $ledger
    Require-Failure { Complete-Review } unknown-review
    Write-Host 'Pilot package/output authority fixtures passed.'
}
finally { if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force } }
