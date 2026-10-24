[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Promotion receipt regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewPromotionReceipts.psm1') -Force

# Deliberately synthetic schema/hash fixtures. These files are not editor sessions,
# private ACL observations, authenticated Actions artifacts, or release acceptance.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-promotion-receipts-' + [Guid]::NewGuid().ToString('N'))
$candidate = Join-Path $scratch 'candidate'
$receipts = Join-Path $scratch 'receipts'
$attachments = Join-Path $receipts 'evidence'
[void][IO.Directory]::CreateDirectory($candidate)
[void][IO.Directory]::CreateDirectory($attachments)
$controls = 0
$contract = Get-SharpProofPrivatePreviewPromotionReceiptContract
$source = '1' * 40
$workflow = '2' * 40
$artifactDigest = '3' * 64
$image = 'ghcr.io/alexyorke/sharpproof-preview@sha256:' + ('4' * 64)
$timestamp = '2026-10-22T12:00:00Z'
$version = '1.0.0-preview.1'
$editorPath = Join-Path $receipts 'editor-acceptance.json'
$accessPath = Join-Path $receipts 'access-acceptance.json'
$observationPath = Join-Path $attachments 'synthetic-observation.txt'
$fixturePath = Join-Path $attachments 'synthetic-fixture.txt'
$globalPath = Join-Path $attachments 'global.json'

function Write-ReceiptJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 32) + "`n", [Text.UTF8Encoding]::new($false))
}
function Get-ReceiptAttachment([string]$Path) {
    return [ordered]@{ path = [IO.Path]::GetRelativePath($receipts, $Path).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function New-Observation([string]$Id, [string]$Method = 'interactive-ui') {
    return [ordered]@{ id = $Id; status = 'passed'; performed = $true; method = $Method; observedAtUtc = $timestamp; evidence = @((Get-ReceiptAttachment $observationPath)) }
}
function New-Receipt([string]$Kind, [string]$Payload, $Value) {
    $receipt = [ordered]@{
        schemaVersion = 1; kind = $Kind; status = 'passed'; sourceRepository = 'alexyorke/SharpProof'; releaseRepository = 'alexyorke/SharpProof-preview'
        sourceCommit = $source; workflowCommit = $workflow; candidateManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $candidate 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        candidateArtifactId = 101; candidateArtifactDigest = $artifactDigest; candidateRunId = 202; consumerImage = $image; packageVersion = $version
        packageFeed = 'https://nuget.pkg.github.com/alexyorke/index.json'; reviewedBy = 'alexyorke'; reviewedAtUtc = $timestamp; packages = @($manifest.packages | ForEach-Object { [ordered]@{ id = $_.id; version = $_.version; sha256 = $_.sha256 } })
    }
    $receipt.Add($Payload, $Value)
    return $receipt
}
function Save-Fixture {
    Write-ReceiptJson $editorPath $script:editor
    Write-ReceiptJson $accessPath $script:access
}
function Reset-Fixture {
    $script:editor = $editorJson | ConvertFrom-Json -AsHashtable -Depth 32
    $script:access = $accessJson | ConvertFrom-Json -AsHashtable -Depth 32
    [IO.File]::WriteAllText($observationPath, 'SYNTHETIC REGRESSION OBSERVATION; not acceptance evidence.', [Text.UTF8Encoding]::new($false))
    Write-ReceiptJson $globalPath ([ordered]@{ sdk = [ordered]@{ version = '10.0.402'; rollForward = 'disable' } })
    Save-Fixture
}
function Test-Fixture([string]$ReceiptKind = 'All') {
    Assert-SharpProofPrivatePreviewPromotionReceipts -EvidenceDirectory $receipts -CandidateDirectory $candidate -Manifest $manifest `
        -CandidateArtifactId 101 -CandidateArtifactDigest $artifactDigest -CandidateRunId 202 -ReceiptKind $ReceiptKind
}
function Assert-ExpectedRejection([string]$Name, [scriptblock]$Action, [string]$Expected) {
    $rejected = $false
    try { & $Action } catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw "Unexpected rejection for ${Name}: $($_.Exception.Message)" }
        $rejected = $true
    }
    if (-not $rejected) { throw "Promotion receipt control accepted invalid fixture: $Name" }
    $script:controls++
}
function Assert-MutationRejected([string]$Name, [scriptblock]$Mutation, [string]$Expected) {
    Reset-Fixture
    & $Mutation
    Save-Fixture
    Assert-ExpectedRejection $Name { Test-Fixture } $Expected
}

function Invoke-PromotionIntegrationControls {
    $integration = Join-Path $scratch 'promotion-integration'
    $integrationScripts = Join-Path $integration 'eng/scripts'
    $integrationTrust = Join-Path $integration 'eng/release/private-preview'
    $integrationCandidate = Join-Path $integration 'candidate'
    $integrationAcceptance = Join-Path $integration 'acceptance'
    foreach ($directory in @($integrationScripts, $integrationTrust, $integrationCandidate, (Join-Path $integrationAcceptance 'evidence'))) { [void][IO.Directory]::CreateDirectory($directory) }
    foreach ($name in @('New-SharpProofPrivatePreviewPromotion.ps1', 'Test-SharpProofPrivatePreviewPromotion.ps1',
            'Test-SharpProofPrivatePreviewEvidence.ps1', 'SharpProof.PrivatePreviewPromotionReceipts.psm1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $integrationScripts
    }
    # The production verifier source is copied unchanged. Only its trust fixture is
    # replaced with an ephemeral synthetic key; no official signing secret is read.
    $integrationRsa = [Security.Cryptography.RSA]::Create(3072)
    $environment = @{}
    foreach ($name in @('GITHUB_REPOSITORY', 'SHARPPROOF_PRIVATE_PREVIEW', 'SHARPPROOF_SOURCE_COMMIT', 'SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT', 'SHARPPROOF_WORKFLOW_COMMIT')) {
        $environment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    function Invoke-IntegrationVerification([string]$Directory, [string]$Phase = 'finalize', [switch]$Signed) {
        & (Join-Path $integrationScripts 'Test-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $integrationCandidate -PromotionDirectory $Directory -Phase $Phase -RequireSignature:$Signed
    }
    function Write-IntegrationManifest($Record, [string]$Directory) {
        Write-ReceiptJson (Join-Path $Directory 'promotion-manifest.json') $Record
    }
    function Restore-IntegrationPromotion {
        Write-IntegrationManifest ($integrationPromotionJson | ConvertFrom-Json -AsHashtable -Depth 32) $integrationFinal
        Copy-Item -LiteralPath $integrationCandidateAuthentication -Destination (Join-Path $integrationFinal 'candidate-authentication.json') -Force
        Copy-Item -LiteralPath $integrationAcceptanceAuthentication -Destination (Join-Path $integrationFinal 'acceptance-authentication.json') -Force
        Copy-Item -LiteralPath (Join-Path $integrationAcceptance 'editor-acceptance.json') -Destination (Join-Path $integrationFinal 'acceptance/editor-acceptance.json') -Force
        Copy-Item -LiteralPath (Join-Path $integrationAcceptance 'access-acceptance.json') -Destination (Join-Path $integrationFinal 'acceptance/access-acceptance.json') -Force
    }
    function Update-IntegrationFileHashes($Record) {
        foreach ($file in $Record.files) {
            $path = Join-Path $integrationFinal $file.fileName
            $file.bytes = (Get-Item -LiteralPath $path).Length
            $file.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        Write-IntegrationManifest $Record $integrationFinal
    }
    try {
        $env:GITHUB_REPOSITORY = 'alexyorke/SharpProof-preview'
        $env:SHARPPROOF_PRIVATE_PREVIEW = 'true'
        $env:SHARPPROOF_SOURCE_COMMIT = $source
        $env:SHARPPROOF_CANDIDATE_WORKFLOW_COMMIT = $workflow
        $env:SHARPPROOF_WORKFLOW_COMMIT = '5' * 40
        [IO.File]::WriteAllText((Join-Path $integrationTrust 'signing-public-key.pem'), $integrationRsa.ExportSubjectPublicKeyInfoPem() + "`n", [Text.UTF8Encoding]::new($false))
        $keyHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($integrationRsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
        Write-ReceiptJson (Join-Path $integrationTrust 'policy.json') ([ordered]@{ schemaVersion = 1; algorithm = 'RSA3072-SHA256-PKCS1-v1.5'; publicKeySpkiSha256 = $keyHash })
        $integrationFiles = [Collections.Generic.List[object]]::new()
        $integrationArchives = [Collections.Generic.List[object]]::new()
        $integrationPackages = [Collections.Generic.List[object]]::new()
        foreach ($id in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.CodeFixes', 'SharpProof.Templates', 'SharpProof.Tool', 'SharpProof.Verifier')) {
            $archiveExtensions = if ($id -cin @('SharpProof.Attributes', 'SharpProof.Verifier')) { @('.nupkg', '.snupkg') } else { @('.nupkg') }
            foreach ($extension in $archiveExtensions) {
                $name = "$id.$version$extension"
                $path = Join-Path $integrationCandidate $name
                $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
                try {
                    $entry = $zip.CreateEntry("$id.nuspec")
                    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                    try { $writer.Write("<package><metadata><id>$id</id><version>$version</version><repository type=`"git`" url=`"https://github.com/alexyorke/SharpProof-preview`" commit=`"$source`"/></metadata></package>") }
                    finally { $writer.Dispose() }
                }
                finally { $zip.Dispose() }
                $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
                $length = (Get-Item -LiteralPath $path).Length
                $integrationFiles.Add([ordered]@{ fileName = $name; bytes = $length; sha256 = $hash })
                $integrationArchives.Add([ordered]@{ id = $id; fileName = $name; symbol = $extension -ceq '.snupkg'; bytes = $length; sha256 = $hash })
                if ($extension -ceq '.nupkg') { $integrationPackages.Add([ordered]@{ id = $id; version = $version; fileName = $name; repositoryCommit = $source; sha256 = $hash }) }
            }
        }
        $integrationManifest = [ordered]@{ schemaVersion = 1; sourceRepository = 'alexyorke/SharpProof'; releaseRepository = 'alexyorke/SharpProof-preview'
            sourceCommit = $source; workflowCommit = $workflow; workflowPath = '.github/workflows/private-preview.yml'; packageVersion = $version; consumerImage = $image
            packages = @($integrationPackages.ToArray()); archives = @($integrationArchives.ToArray()); files = @($integrationFiles.ToArray())
        }
        $integrationManifestPath = Join-Path $integrationCandidate 'release-manifest.json'
        Write-ReceiptJson $integrationManifestPath $integrationManifest
        $integrationManifestHash = (Get-FileHash -LiteralPath $integrationManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllLines((Join-Path $integrationCandidate 'SHA256SUMS'), (@($integrationFiles | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) + "$integrationManifestHash  release-manifest.json"), [Text.UTF8Encoding]::new($false))
        $integrationCandidateAuthentication = Join-Path $integration 'candidate-authentication.json'
        $integrationAcceptanceAuthentication = Join-Path $integration 'acceptance-authentication.json'
        $candidateAuthenticationRecord = [ordered]@{ schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; artifactId = 101; artifactDigest = $artifactDigest
            runId = '202'; workflowCommit = $workflow; workflowPath = '.github/workflows/private-preview.yml'; kind = 'Candidate'; status = 'authenticated-extracted'; candidateManifestSha256 = $integrationManifestHash
        }
        $acceptanceAuthenticationRecord = [ordered]@{ schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; artifactId = 303; artifactDigest = ('6' * 64)
            runId = '404'; workflowCommit = $env:SHARPPROOF_WORKFLOW_COMMIT; workflowPath = '.github/workflows/private-preview-receipts.yml'; kind = 'Acceptance'; status = 'authenticated-extracted'; candidateManifestSha256 = $null
        }
        Write-ReceiptJson $integrationCandidateAuthentication $candidateAuthenticationRecord
        Write-ReceiptJson $integrationAcceptanceAuthentication $acceptanceAuthenticationRecord
        Reset-Fixture
        foreach ($name in @('synthetic-observation.txt', 'synthetic-fixture.txt', 'global.json')) {
            Copy-Item -LiteralPath (Join-Path $attachments $name) -Destination (Join-Path $integrationAcceptance "evidence/$name")
        }
        foreach ($record in @($editor, $access)) {
            $record.candidateManifestSha256 = $integrationManifestHash
            $record.packages = @($integrationPackages | ForEach-Object { [ordered]@{ id = $_.id; version = $_.version; sha256 = $_.sha256 } })
        }
        Write-ReceiptJson (Join-Path $integrationAcceptance 'editor-acceptance.json') $editor
        Write-ReceiptJson (Join-Path $integrationAcceptance 'access-acceptance.json') $access
        [IO.File]::WriteAllText((Join-Path $integrationAcceptance 'unreferenced-private-input.txt'), 'SYNTHETIC UNREFERENCED INPUT - MUST NOT BE RETAINED', [Text.UTF8Encoding]::new($false))
        $integrationStage = Join-Path $integration 'stage-promotion'
        $integrationFinal = Join-Path $integration 'final-promotion'
        foreach ($phase in @('stage-registry', 'finalize')) {
            $output = if ($phase -ceq 'stage-registry') { $integrationStage } else { $integrationFinal }
            & (Join-Path $integrationScripts 'New-SharpProofPrivatePreviewPromotion.ps1') -CandidateDirectory $integrationCandidate -AcceptanceDirectory $integrationAcceptance `
                -CandidateAuthenticationPath $integrationCandidateAuthentication -AcceptanceAuthenticationPath $integrationAcceptanceAuthentication -OutputDirectory $output -Phase $phase
            $script:controls++
            Invoke-IntegrationVerification $output $phase
            $script:controls++
            if (Test-Path -LiteralPath (Join-Path $output 'acceptance/unreferenced-private-input.txt')) { throw 'Promotion retained an unreferenced private input.' }
            if ((Test-Path -LiteralPath (Join-Path $output 'acceptance/access-acceptance.json')) -ne ($phase -ceq 'finalize')) { throw 'Promotion retained the wrong receipt set for its phase.' }
            $script:controls++
        }
        $integrationPromotionJson = [IO.File]::ReadAllText((Join-Path $integrationFinal 'promotion-manifest.json'))
        foreach ($field in @('candidateManifestSha256', 'sourceCommit', 'candidateWorkflowCommit', 'promotionWorkflowCommit', 'workflowPath', 'consumerImage')) {
            Restore-IntegrationPromotion
            $record = $integrationPromotionJson | ConvertFrom-Json -AsHashtable -Depth 32
            $record[$field] = 'different'
            Write-IntegrationManifest $record $integrationFinal
            Assert-ExpectedRejection "promotion wrong $field" { Invoke-IntegrationVerification $integrationFinal } 'exact original candidate'
        }
        Restore-IntegrationPromotion
        Assert-ExpectedRejection 'promotion phase mismatch' { Invoke-IntegrationVerification $integrationFinal 'stage-registry' } 'exact original candidate'
        [IO.File]::AppendAllText((Join-Path $integrationFinal 'acceptance/editor-acceptance.json'), ' ', [Text.UTF8Encoding]::new($false))
        Assert-ExpectedRejection 'retained receipt bytes altered' { Invoke-IntegrationVerification $integrationFinal } 'evidence bytes changed'
        Restore-IntegrationPromotion
        [IO.File]::WriteAllText((Join-Path $integrationFinal '.unbound'), 'SYNTHETIC UNBOUND FILE', [Text.UTF8Encoding]::new($false))
        Assert-ExpectedRejection 'unbound promotion file' { Invoke-IntegrationVerification $integrationFinal } 'missing or unbound files'
        Remove-Item -LiteralPath (Join-Path $integrationFinal '.unbound') -Force
        $candidateSignature = $integrationRsa.SignData([IO.File]::ReadAllBytes($integrationManifestPath), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [IO.File]::WriteAllBytes((Join-Path $integrationCandidate 'release-manifest.sig'), $candidateSignature)
        Assert-ExpectedRejection 'required promotion signature missing' { Invoke-IntegrationVerification $integrationFinal -Signed } 'promotion-manifest.sig'
        $signature = $integrationRsa.SignData([IO.File]::ReadAllBytes((Join-Path $integrationFinal 'promotion-manifest.json')), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [IO.File]::WriteAllBytes((Join-Path $integrationFinal 'promotion-manifest.sig'), $signature)
        Invoke-IntegrationVerification $integrationFinal -Signed
        $script:controls++
        $signature[0] = $signature[0] -bxor 1
        [IO.File]::WriteAllBytes((Join-Path $integrationFinal 'promotion-manifest.sig'), $signature)
        Assert-ExpectedRejection 'promotion signature altered' { Invoke-IntegrationVerification $integrationFinal -Signed } 'Pinned promotion signature is invalid'
        Remove-Item -LiteralPath (Join-Path $integrationFinal 'promotion-manifest.sig')
        Remove-Item -LiteralPath (Join-Path $integrationCandidate 'release-manifest.sig')
        Restore-IntegrationPromotion
        $alteredReceipt = Get-Content -LiteralPath (Join-Path $integrationFinal 'acceptance/editor-acceptance.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 32
        $alteredReceipt.sessions[0].scenarios[0].status = 'pending'
        Write-ReceiptJson (Join-Path $integrationFinal 'acceptance/editor-acceptance.json') $alteredReceipt
        Update-IntegrationFileHashes ($integrationPromotionJson | ConvertFrom-Json -AsHashtable -Depth 32)
        Assert-ExpectedRejection 'rehashed pending editor receipt' { Invoke-IntegrationVerification $integrationFinal } 'must actually pass'
        Restore-IntegrationPromotion
        $candidateArchive = Join-Path $integrationCandidate $integrationManifest.packages[0].fileName
        $candidateArchiveBytes = [IO.File]::ReadAllBytes($candidateArchive)
        [IO.File]::WriteAllText($candidateArchive, 'SYNTHETIC ALTERED ARCHIVE', [Text.UTF8Encoding]::new($false))
        Assert-ExpectedRejection 'promotion rereads actual candidate archive bytes' { Invoke-IntegrationVerification $integrationFinal } 'Signed evidence bytes changed'
        [IO.File]::WriteAllBytes($candidateArchive, $candidateArchiveBytes)
        foreach ($subject in @('candidateArtifact', 'acceptanceArtifact')) {
            Restore-IntegrationPromotion
            $record = $integrationPromotionJson | ConvertFrom-Json -AsHashtable -Depth 32
            $alteredAuthentication = $record[$subject]
            $alteredAuthentication.workflowCommit = '7' * 40
            $fileName = if ($subject -ceq 'candidateArtifact') { 'candidate-authentication.json' } else { 'acceptance-authentication.json' }
            Write-ReceiptJson (Join-Path $integrationFinal $fileName) $alteredAuthentication
            Update-IntegrationFileHashes $record
            Assert-ExpectedRejection 'coherent authenticated workflow identity substitution' { Invoke-IntegrationVerification $integrationFinal } 'both approved private workflow identities'
        }
        Restore-IntegrationPromotion
        $record = $integrationPromotionJson | ConvertFrom-Json -AsHashtable -Depth 32
        $alteredAuthentication = $record.candidateArtifact
        $alteredAuthentication.candidateManifestSha256 = 'f' * 64
        Write-ReceiptJson (Join-Path $integrationFinal 'candidate-authentication.json') $alteredAuthentication
        Update-IntegrationFileHashes $record
        Assert-ExpectedRejection 'coherent candidate authentication manifest substitution' { Invoke-IntegrationVerification $integrationFinal } 'authenticated immutable private Actions artifact provenance'
    }
    finally {
        foreach ($entry in $environment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
        $integrationRsa.Dispose()
    }
}
try {
    [IO.File]::WriteAllText($observationPath, 'SYNTHETIC REGRESSION OBSERVATION; not acceptance evidence.', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($fixturePath, 'SYNTHETIC CLEAN SOURCE SNAPSHOT; not an editor fixture.', [Text.UTF8Encoding]::new($false))
    Write-ReceiptJson $globalPath ([ordered]@{ sdk = [ordered]@{ version = '10.0.402'; rollForward = 'disable' } })
    $packages = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.CodeFixes', 'SharpProof.Templates', 'SharpProof.Tool', 'SharpProof.Verifier') | ForEach-Object {
        $name = "$($_).$version.nupkg"
        $path = Join-Path $candidate $name
        [IO.File]::WriteAllText($path, "SYNTHETIC ARCHIVE HASH FIXTURE: $_", [Text.UTF8Encoding]::new($false))
        [ordered]@{ id = $_; version = $version; fileName = $name; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $manifest = [ordered]@{ schemaVersion = 1; sourceRepository = 'alexyorke/SharpProof'; releaseRepository = 'alexyorke/SharpProof-preview'; sourceCommit = $source
        workflowCommit = $workflow; workflowPath = '.github/workflows/private-preview.yml'; packageVersion = $version; consumerImage = $image; packages = @($packages)
    }
    Write-ReceiptJson (Join-Path $candidate 'release-manifest.json') $manifest
    $sessions = @($contract.editors | ForEach-Object {
            $editorId = $_
            $scenarios = @($contract.scenarios | ForEach-Object {
                    $method = if ($_ -cin @('strict-claims-review', 'strict-assumptions-vacuity-review', 'regression-fails-before', 'regression-passes-after', 'fresh-strict-proof-passes')) { 'canonical-container' } else { 'interactive-ui' }
                    New-Observation $_ $method
                })
            [ordered]@{ editor = $editorId; status = 'passed'; interactive = $true; os = 'windows'; windowsEdition = 'Windows 11 Pro'; windowsBuild = '10.0.26100.1234'
                processArchitecture = 'x64'; volumeFileSystem = 'NTFS'; volumeKind = 'fixed-local'; channel = 'stable'; editorVersion = '1.2.3'; compilerVersion = '5.0.0'
                extensions = @([ordered]@{ id = $(if ($editorId -ceq 'visual-studio-code') { 'ms-dotnettools.csharp' } else { 'synthetic.csharp' }); version = '1.2.3'; role = 'csharp' })
                selectedSdkVersion = '10.0.402'; installedSdkVersions = @('10.0.402'); runtimeVersions = @('10.0.4'); freshWorkspace = $true; candidateRestoreVerified = $true
                analyzersEnabled = $true; analyzerLoadClean = $true; fixture = (Get-ReceiptAttachment $fixturePath); globalJson = (Get-ReceiptAttachment $globalPath)
                candidateAuthentication = (New-Observation 'candidate-authentication' 'private-actions-artifact'); scenarios = $scenarios
            }
        })
    $subjects = @($contract.accessSubjects | ForEach-Object {
            $subjectId = $_
            $checks = @($contract.accessChecks | ForEach-Object {
                    $check = New-Observation $_ 'settings-ui'
                    switch ($_) {
                        'visibility' { $check.method = 'metadata-api'; $check.Add('value', 'private') }
                        'direct-users' { $check.Add('value', @('alexyorke')) }
                        'teams' { $check.Add('value', @()) }
                        'inheritance' { $check.Add('value', 'private-owner-only-verified') }
                        'actions-repositories' { $check.Add('value', @('alexyorke/SharpProof-preview')) }
                        'anonymous-access' { $check.method = 'anonymous-access-test'; $check.Add('value', 'denied'); $check.Add('principal', 'anonymous'); $check.Add('httpStatus', 401) }
                        'non-owner-access' { $check.method = 'non-owner-access-test'; $check.Add('value', 'denied'); $check.Add('principal', 'synthetic-non-owner'); $check.Add('principalVerified', $true); $check.Add('httpStatus', 404) }
                    }
                    $check
                })
            [ordered]@{ id = $subjectId; linkedRepository = 'alexyorke/SharpProof-preview'; checks = $checks }
        })
    $editor = New-Receipt 'SharpProof.PrivatePreviewEditorAcceptance' 'sessions' $sessions
    $access = New-Receipt 'SharpProof.PrivatePreviewAccessAcceptance' 'subjects' $subjects
    $editorJson = $editor | ConvertTo-Json -Depth 32
    $accessJson = $access | ConvertTo-Json -Depth 32
    Save-Fixture
    Test-Fixture
    $controls++

    # Staging must never require fabricated access observations for unpublished subjects.
    Remove-Item -LiteralPath $accessPath
    Test-Fixture 'Editor'
    $controls++
    Assert-ExpectedRejection 'final gate requires access receipt' { Test-Fixture } 'access-acceptance.json'
    Assert-ExpectedRejection 'lowercase All cannot bypass receipt validation' { Test-Fixture 'all' } 'access-acceptance.json'
    Reset-Fixture
    Remove-Item -LiteralPath $editorPath
    Test-Fixture 'Access'
    $controls++
    Assert-ExpectedRejection 'final gate requires editor receipt' { Test-Fixture } 'editor-acceptance.json'
    Reset-Fixture
    $editor.sessions[2].scenarios[7].status = 'not-applicable'
    $editor.sessions[2].scenarios[7].Add('reason', 'Separate refactoring menu was inspected and absent; the diagnostic action passed.')
    $editor.sessions[2].scenarios[7].Add('observedActionAvailable', $false)
    Save-Fixture
    Test-Fixture
    $controls++

    Reset-Fixture
    foreach ($session in $editor.sessions) {
        foreach ($scenario in $session.scenarios) {
            if ($scenario.method -ceq 'canonical-container') { $scenario.method = 'native-consumer-windows-x64' }
        }
    }
    Save-Fixture
    Test-Fixture
    $controls++

    foreach ($field in @('sourceCommit', 'workflowCommit', 'candidateManifestSha256', 'candidateArtifactDigest', 'consumerImage', 'packageVersion', 'packageFeed', 'releaseRepository', 'reviewedBy')) {
        Assert-MutationRejected "wrong editor $field" { $editor[$field] = 'different' } 'binding differs'
        Assert-MutationRejected "wrong access $field" { $access[$field] = 'different' } 'binding differs'
    }
    foreach ($field in @('candidateArtifactId', 'candidateRunId')) {
        Assert-MutationRejected "wrong numeric $field" { $editor[$field] = 999 } 'identity differs'
        Assert-MutationRejected "string numeric $field" { $editor[$field] = '101' } 'must be integers'
    }
    Assert-MutationRejected 'pending overall editor receipt' { $editor.status = 'pending' } 'binding differs'
    Assert-MutationRejected 'wrong schema kind' { $access.kind = 'SharpProof.PrivatePreviewEditorAcceptance' } 'binding differs'
    Assert-MutationRejected 'schema version as string' { $editor.schemaVersion = '1' } 'must be an integer'
    Assert-MutationRejected 'unknown root field' { $editor.Add('assumedAccepted', $true) } 'Unexpected receipt field'
    Assert-MutationRejected 'wrong package hash' { $editor.packages[0].sha256 = 'f' * 64 } 'archive hash differs'
    Assert-MutationRejected 'duplicate package' { $editor.packages[1] = $editor.packages[0] } 'unexpected or duplicated'
    Assert-MutationRejected 'missing package' { $access.packages = @($access.packages | Select-Object -First 5) } 'all six exact'
    Assert-MutationRejected 'missing host' { $editor.sessions = @($editor.sessions | Select-Object -First 2) } 'All three'
    Assert-MutationRejected 'duplicate host' { $editor.sessions[2].editor = $editor.sessions[0].editor } 'host is missing or incompatible'
    foreach ($field in @('os', 'processArchitecture', 'channel', 'volumeFileSystem', 'volumeKind')) {
        Assert-MutationRejected "incompatible $field" { $editor.sessions[0][$field] = 'incompatible' } 'host is missing or incompatible'
    }
    foreach ($field in @('interactive', 'freshWorkspace', 'candidateRestoreVerified', 'analyzersEnabled', 'analyzerLoadClean')) {
        Assert-MutationRejected "unperformed $field" { $editor.sessions[0][$field] = $false } 'not actually performed'
    }
    Assert-MutationRejected 'string interactive flag' { $editor.sessions[0].interactive = 'true' } 'not actually performed'
    Assert-MutationRejected 'unrecorded compiler' { $editor.sessions[0].compilerVersion = 'unknown' } 'not a placeholder'
    Assert-MutationRejected 'unversioned editor' { $editor.sessions[0].editorVersion = 'stable' } 'must be exact versions'
    Assert-MutationRejected 'missing C# extension' { $editor.sessions[1].extensions[0].id = 'unrelated.extension' } 'Microsoft C# extension'
    Assert-MutationRejected 'missing plugin versions' { $editor.sessions[0].extensions = @() } 'Exact C# extension'
    Assert-MutationRejected 'wrong effective SDK' { $editor.sessions[1].selectedSdkVersion = '10.0.100' } 'exact .NET 10 SDK'
    Assert-MutationRejected 'installed SDK mismatch' { $editor.sessions[1].installedSdkVersions = @('10.0.100') } 'exact .NET 10 SDK'
    Reset-Fixture
    Write-ReceiptJson $globalPath ([ordered]@{ sdk = [ordered]@{ version = '10.0.402'; rollForward = 'latestPatch' } })
    foreach ($session in $editor.sessions) { $session.globalJson = Get-ReceiptAttachment $globalPath }
    Save-Fixture
    Assert-ExpectedRejection 'SDK roll-forward weakens the exact pin' { Test-Fixture } 'exact .NET 10 SDK'
    Reset-Fixture
    $alternate = Join-Path $attachments 'different-fixture.txt'
    [IO.File]::WriteAllText($alternate, 'A DIFFERENT SYNTHETIC CLEAN SOURCE SNAPSHOT', [Text.UTF8Encoding]::new($false))
    $editor.sessions[1].fixture = Get-ReceiptAttachment $alternate
    Save-Fixture
    Assert-ExpectedRejection 'different clean source fixture' { Test-Fixture } 'same pinned fixture'
    Assert-MutationRejected 'strict execution substituted by UI observation' { $editor.sessions[0].scenarios[19].method = 'interactive-ui' } 'actual installed-consumer build/test evidence'
    Assert-MutationRejected 'wrong extension role' { $editor.sessions[0].extensions[0].role = 'unrelated' } 'extension/plugin identity'
    Assert-MutationRejected 'authentication substituted by unit tests' { $editor.sessions[0].candidateAuthentication.method = 'unit-tests' } 'authentication observation'
    Assert-MutationRejected 'missing scenario' { $editor.sessions[0].scenarios = @($editor.sessions[0].scenarios | Select-Object -Skip 1) } 'Every required interactive'
    Assert-MutationRejected 'duplicate scenario' { $editor.sessions[0].scenarios[1] = $editor.sessions[0].scenarios[0] } 'unexpected or duplicated'
    foreach ($status in @('pending', 'blocked', 'unavailable', 'failed', 'not-applicable')) {
        Assert-MutationRejected "unpassed scenario $status" { $editor.sessions[0].scenarios[0].status = $status } 'must actually pass'
    }
    Assert-MutationRejected 'CLI test substituted for interactive observation' { $editor.sessions[0].scenarios[0].method = 'canonical-container' } 'cannot be substituted'
    Assert-MutationRejected 'package installation was not observed' { $editor.sessions[0].scenarios[24].performed = $false } 'not actually performed'
    Assert-MutationRejected 'undo and source restoration were not observed' { $editor.sessions[0].scenarios[25].status = 'pending' } 'must actually pass'
    Assert-MutationRejected 'performed flag false' { $editor.sessions[0].scenarios[0].performed = $false } 'not actually performed'
    Assert-MutationRejected 'future observation' { $editor.sessions[0].scenarios[0].observedAtUtc = '9999-01-01T00:00:00Z' } 'outside its valid range'
    Assert-MutationRejected 'empty evidence list' { $editor.sessions[0].scenarios[0].evidence = @() } 'requires bounded private evidence'
    Assert-MutationRejected 'path traversal' { $editor.sessions[0].scenarios[0].evidence[0].path = 'evidence/../candidate/file.txt' } 'path is unsafe'
    Assert-MutationRejected 'bad attachment hash' { $editor.sessions[0].scenarios[0].evidence[0].sha256 = 'f' * 64 } 'attachment hash differs'
    Assert-MutationRejected 'duplicate attachment' { $editor.sessions[0].scenarios[0].evidence += $editor.sessions[0].scenarios[0].evidence[0] } 'path must be unique'
    Assert-MutationRejected 'uninspected optional refactoring' { $editor.sessions[0].scenarios[7].status = 'not-applicable' } 'must be actually inspected'

    Assert-MutationRejected 'missing access subject' { $access.subjects = @($access.subjects | Select-Object -Skip 1) } 'every repository, package'
    Assert-MutationRejected 'duplicate access subject' { $access.subjects[1] = $access.subjects[0] } 'linkage differs'
    Assert-MutationRejected 'public package' { $access.subjects[1].checks[0].value = 'public' } 'visibility must actually be private'
    Assert-MutationRejected 'wrong linked repository' { $access.subjects[1].linkedRepository = 'alexyorke/SharpProof' } 'linkage differs'
    Assert-MutationRejected 'extra direct user' { $access.subjects[1].checks[1].value += 'other-user' } 'only the owner'
    Assert-MutationRejected 'extra team' { $access.subjects[1].checks[2].value = @('other-team') } 'team grant'
    Assert-MutationRejected 'unverified inheritance' { $access.subjects[1].checks[3].value = 'enabled' } 'explicitly verified or disabled'
    Assert-MutationRejected 'public Actions repository' { $access.subjects[1].checks[4].value = @('alexyorke/SharpProof') } 'extraneous Actions repository'
    foreach ($index in @(1, 2, 3, 4)) {
        Assert-MutationRejected 'metadata-only granular access' { $access.subjects[1].checks[$index].method = 'metadata-api' } 'cannot be inferred'
    }
    Assert-MutationRejected 'missing check' { $access.subjects[1].checks = @($access.subjects[1].checks | Select-Object -Skip 1) } 'Every granular access check'
    Assert-MutationRejected 'pending check' { $access.subjects[1].checks[0].status = 'pending' } 'pending, or unsuccessful'
    Assert-MutationRejected 'anonymous access granted' { $access.subjects[1].checks[5].value = 'allowed' } 'unauthenticated access denial'
    Assert-MutationRejected 'owner used for non-owner check' { $access.subjects[1].checks[6].principal = 'alexyorke' } 'authenticated non-owner access denial'
    Assert-MutationRejected 'unverified test credential identity' { $access.subjects[1].checks[6].principalVerified = $false } 'not actually performed'
    Assert-MutationRejected 'unauthenticated non-owner request' { $access.subjects[1].checks[6].httpStatus = 401 } 'authenticated non-owner access denial'
    Assert-MutationRejected 'HTTP status as string' { $access.subjects[1].checks[6].httpStatus = '404' } 'authenticated non-owner access denial'

    Reset-Fixture
    [IO.File]::WriteAllText($observationPath, 'altered retained observation', [Text.UTF8Encoding]::new($false))
    Assert-ExpectedRejection 'altered observation bytes' { Test-Fixture } 'attachment hash differs'
    Reset-Fixture
    $archivePath = Join-Path $candidate $manifest.packages[0].fileName
    $archiveBytes = [IO.File]::ReadAllBytes($archivePath)
    [IO.File]::WriteAllText($archivePath, 'altered frozen candidate', [Text.UTF8Encoding]::new($false))
    Assert-ExpectedRejection 'actual archive substitution' { Test-Fixture } 'Frozen candidate archive bytes differ'
    [IO.File]::WriteAllBytes($archivePath, $archiveBytes)
    $manifest.packages[0].sha256 = 'f' * 64
    Assert-ExpectedRejection 'authenticated manifest substitution' { Test-Fixture } 'Authenticated candidate package hashes differ'
    $manifest.packages[0].sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Reset-Fixture
    [IO.File]::WriteAllText($editorPath, $editorJson.Replace('"schemaVersion": 1,', '"schemaVersion": 1, "SchemaVersion": 1,'), [Text.UTF8Encoding]::new($false))
    Assert-ExpectedRejection 'case ambiguous duplicate JSON field' { Test-Fixture } 'Duplicate or case-ambiguous'
    Reset-Fixture
    [IO.File]::WriteAllText($editorPath, '{"oversized":"' + ('x' * 4MB) + '"}', [Text.UTF8Encoding]::new($false))
    Assert-ExpectedRejection 'oversized JSON' { Test-Fixture } 'nonempty bounded regular file'
    Reset-Fixture
    [void](New-Item -ItemType SymbolicLink -Path (Join-Path $attachments 'linked.txt') -Target $observationPath)
    $editor.sessions[0].scenarios[0].evidence[0].path = 'evidence/linked.txt'
    Save-Fixture
    Assert-ExpectedRejection 'evidence link' { Test-Fixture } 'must not contain filesystem links'
    Invoke-PromotionIntegrationControls
    Write-Host "Promotion receipt regressions passed: $controls synthetic fixture controls. No interactive editor acceptance, real access verification, publication, or signing is asserted."
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-promotion-receipts-[a-f0-9]{32}\z') { throw 'Refusing unsafe promotion receipt fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
