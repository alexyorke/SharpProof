[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-SharpProofPilotPackageAuthority.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sp-pilot-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $fixture 'packages'
$commit = '1111111111111111111111111111111111111111'
$version = '1.0.0-preview.1'

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

try {
    Reset-Packages
    $valid = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    if ($valid.Count -ne 6) { throw 'Canonical package authority failed.' }
    $target = Join-Path $packages "SharpProof.$version.nupkg"
    [IO.File]::AppendAllText($target, 'changed')
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
    $pilotRoot = Join-Path $fixture 'pilots'
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
        $resultPath = Join-Path $fixture "results/$($row.id).json"
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resultPath)) | Out-Null
        [IO.File]::WriteAllText($resultPath,
            ([ordered]@{ manifest=[ordered]@{ claims=$manifestClaims }; claimResults=$claimResults } |
                ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        $relativeResult = [IO.Path]::GetRelativePath($fixture, $resultPath).Replace('\','/')
        $evidence = @('request','compilerManifest','sarif') | ForEach-Object {
            [pscustomobject]@{ kind=$_; path="evidence/$($row.id)-$_.json"; bytes=1 }
        }
        $evidence += [pscustomobject]@{
            kind='result'; path=$relativeResult; bytes=[int64](Get-Item $resultPath).Length
        }
        [pscustomobject]@{
            id=$row.id; project=$row.project; category=$row.category; library=$row.library
            libraryVersion=$row.libraryVersion; runStatus='Complete'; sarifProduced=$true
            claimEvidence=@($manifestClaims | ForEach-Object {
                    [pscustomobject]@{ claimId=$_.claimId; kind=$_.kind; outcome='Proven' }
                })
            diagnostics=@()
            falsePositiveReports=$null
            evidence=$evidence
        }
    })
    $report = [pscustomobject]@{
        schemaVersion=3; reviewStatus='Unreviewed'; runId=('1' * 32); commit=$commit; packageVersion=$version; pilotCount=5
        packageArtifacts=$artifacts
        pilots=$reportPilots
    }
    function Test-Report($Value) {
        Test-SharpProofPilotReport $Value $commit -RepositoryRoot $fixture -CatalogPath $catalogPath
    }
    function Copy-Json($Value) { $Value | ConvertTo-Json -Depth 10 | ConvertFrom-Json }
    if (-not (Test-Report $report)) { throw 'Canonical pilot report failed.' }
    $canonicalReport = Copy-Json $report
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
    $firstResultPath = Join-Path $fixture $canonicalReport.pilots[0].evidence.Where({$_.kind -eq 'result'})[0].path
    $originalResult = [IO.File]::ReadAllText($firstResultPath)
    [IO.File]::WriteAllText($firstResultPath,
        '{"manifest":{"claims":[]},"claimResults":[]}')
    $changed = Copy-Json $canonicalReport
    $changed.pilots[0].claimEvidence = @()
    $changedResultEvidence = $changed.pilots[0].evidence.Where({$_.kind -eq 'result'})[0]
    $changedResultEvidence.bytes = [int64](Get-Item $firstResultPath).Length
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
    $changedResultEvidence.bytes = [int64](Get-Item $contractResultPath).Length
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
        @($pilot.claimEvidence | ForEach-Object {
            [ordered]@{ pilotId=$pilot.id; kind='Claim'; id=$_.claimId; disposition='TruePositive' }
        })
    })
    $ledger = [ordered]@{
        schemaVersion=1
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
    Write-Ledger $ledger
    Complete-Review
    $reviewed = Get-Content $reviewedPath -Raw | ConvertFrom-Json
    if ([string]$reviewed.reviewStatus -cne 'Reviewed' -or
        @($reviewed.pilots | Where-Object falsePositiveReports -ne 0).Count -ne 0) {
        throw 'Honest zero false-positive review failed.'
    }
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
