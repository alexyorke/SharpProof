[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CorePackageSource,
    [Parameter(Mandatory)][string]$ToolPackageSource,
    [Parameter(Mandatory)][string]$DeveloperPackageSource,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$QualificationDirectory,
    [Parameter(Mandatory)][string]$ImageArchivePath,
    [Parameter(Mandatory)][string]$ImageReceiptPath,
    [Parameter(Mandatory)][ValidatePattern('\Aghcr\.io/alexyorke/sharpproof-preview@sha256:[a-f0-9]{64}\z')][string]$ConsumerImage
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath($env:SHARPPROOF_REPO_ROOT)
Import-Module (Join-Path $repository 'eng/scripts/SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $repository 'eng/scripts/SharpProof.PackageIdentity.psm1') -Force
Import-Module (Join-Path $repository 'eng/scripts/SharpProof.ReleaseConfiguration.psm1') -Force
Assert-SharpProofContainer 'Private preview evidence must be built in the canonical container.'
$source = $env:SHARPPROOF_SOURCE_COMMIT
$workflow = $env:SHARPPROOF_WORKFLOW_COMMIT
if ($env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
    $source -cnotmatch '\A[a-f0-9]{40}\z' -or $workflow -cnotmatch '\A[a-f0-9]{40}\z' -or
    (Get-SharpProofEffectivePackageVersion $repository) -cne '1.0.0-preview.1' -or
    (Get-SharpProofExpectedRepositoryUrl) -cne 'https://github.com/alexyorke/SharpProof-preview') {
    throw 'Official private preview evidence requires the fixed release repository, version, and both exact commit identities.'
}
$head = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -cne $source) { throw 'The build checkout is not the approved source commit.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Private release output must be empty.' }
$sourceReceipt = Get-Content -LiteralPath (Join-Path $QualificationDirectory 'private-source-qualification.json') -Raw | ConvertFrom-Json
if ($sourceReceipt.sourceRepository -cne 'alexyorke/SharpProof' -or $sourceReceipt.sourceCommit -cne $source -or
    $sourceReceipt.workflowCommit -cne $workflow -or $sourceReceipt.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    @($sourceReceipt.gates).Count -ne 6 -or @($sourceReceipt.gates | Where-Object conclusion -CNE 'success').Count -ne 0) {
    throw 'The exact public source qualification receipt is missing or inconsistent.'
}
foreach ($requiredWorkflow in @('ci.yml', 'coverage.yml', 'toolchain-security.yml', 'package-consumers.yml', 'docs-maintenance.yml', 'nightly.yml')) {
    $gate = @($sourceReceipt.gates | Where-Object workflow -CEQ $requiredWorkflow)
    $allowedEvents = if ($requiredWorkflow -ceq 'nightly.yml') { @('schedule', 'workflow_dispatch') } else { @('push') }
    if ($gate.Count -ne 1 -or $gate[0].event -cnotin $allowedEvents -or [string]$gate[0].runId -cnotmatch '\A[1-9][0-9]*\z') { throw "Missing exact source workflow receipt: $requiredWorkflow" }
}
$packageReceiptPath = Join-Path $QualificationDirectory 'private-package-qualification.json'
$packageReceipt = Get-Content -LiteralPath $packageReceiptPath -Raw | ConvertFrom-Json -AsHashtable
$requiredGates = @('release-context', 'package', 'container-verifier', 'native-linux-verifier', 'native-windows-arm64-verifier', 'portable-consumers', 'visual-studio-consumers', 'optional-tool')
if ($packageReceipt.Count -ne $requiredGates.Count) { throw 'Unexpected private package qualification gate count.' }
foreach ($gate in $requiredGates) {
    if (-not $packageReceipt.ContainsKey($gate) -or $packageReceipt[$gate].result -cne 'success') { throw "Required qualification did not pass: $gate" }
}
$developerRun = Split-Path -Parent ([IO.Path]::GetFullPath($DeveloperPackageSource))
$dx = Get-Content -LiteralPath (Join-Path $developerRun 'run.json') -Raw | ConvertFrom-Json
$composeFiles = @(Get-ChildItem -LiteralPath (Join-Path $developerRun 'compose-acceptance') -Filter 'compose-run.json' -Recurse -File)
if ($composeFiles.Count -ne 2) { throw 'Both starter and workspace actual generated Compose acceptance receipts are required.' }
if ($dx.status -cne 'passed' -or $dx.qualificationMode -cne 'AnalyzerEnabled' -or -not $dx.repositoryAnalyzersEnabled -or $dx.sourceCommit -cne $source -or $dx.packageVersion -cne '1.0.0-preview.1' -or
    $dx.consumerImage -cne $ConsumerImage -or $dx.toolPackageMode -cne 'qualified-artifact') {
    throw 'Actual analyzer-enabled developer qualification must pass for these exact inputs.'
}
$composeConsumers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($composeFile in $composeFiles) {
    $compose = Get-Content -LiteralPath $composeFile.FullName -Raw | ConvertFrom-Json
    if ($compose.consumerDirectory -cnotin @('consumer', 'workspace-consumer') -or -not $composeConsumers.Add($compose.consumerDirectory) -or
        $compose.status -cne 'passed' -or $compose.sourceCommit -cne $source -or $compose.packageVersion -cne '1.0.0-preview.1' -or
        $compose.consumerImage -cne $ConsumerImage -or $compose.cleanupExitCode -ne 0 -or @($compose.commands | Where-Object exitCode -NE 0).Count -ne 0) {
        throw 'Both distinct generated Compose consumers must pass for these exact inputs.'
    }
    foreach ($phase in @('compose-tool-restore', 'compose-restore', 'compose-build', 'compose-doctor', 'compose-cleanup')) {
        if (@($compose.commands | Where-Object phase -CEQ $phase).Count -ne 1) { throw "Missing actual Compose phase: $phase" }
    }
}
$identities = [Collections.Generic.List[object]]::new()
$archives = [Collections.Generic.List[object]]::new()
$packageSpecifications = [ordered]@{
    'SharpProof' = $CorePackageSource; 'SharpProof.Attributes' = $CorePackageSource; 'SharpProof.Verifier' = $CorePackageSource
    'SharpProof.Tool' = $ToolPackageSource; 'SharpProof.CodeFixes' = $DeveloperPackageSource; 'SharpProof.Templates' = $DeveloperPackageSource
}
foreach ($id in $packageSpecifications.Keys) {
    $path = Join-Path $packageSpecifications[$id] "$id.1.0.0-preview.1.nupkg"
    $identity = Get-SharpProofPackageIdentity -Path $path -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne $id -or $identity.Version -cne '1.0.0-preview.1' -or $identity.RepositoryCommit -cne $source -or
        $identity.RepositoryUrl -cne 'https://github.com/alexyorke/SharpProof-preview') { throw "Private archive identity mismatch: $id" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $recorded = @($dx.packages | Where-Object id -CEQ $id)
    if ($recorded.Count -ne 1 -or $recorded[0].sha256 -cne $hash) { throw "Archive differs from actual developer qualification: $id" }
    $identities.Add([ordered]@{ id = $id; version = $identity.Version; fileName = [IO.Path]::GetFileName($path); sha256 = $hash; repositoryCommit = $source })
    foreach ($extension in @('.nupkg', '.snupkg')) {
        $archivePath = [IO.Path]::ChangeExtension($path, $extension)
        if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            if ($extension -ceq '.snupkg' -and $id -cin @('SharpProof.Attributes', 'SharpProof.Verifier')) { throw "Required exact symbol archive is missing: $id" }
            continue
        }
        $archiveIdentity = Get-SharpProofPackageIdentity -Path $archivePath -RequireRepository -RequireSingleIdentity
        if ($archiveIdentity.Id -cne $id -or $archiveIdentity.Version -cne '1.0.0-preview.1' -or $archiveIdentity.RepositoryCommit -cne $source) { throw 'Symbol archive identity differs from the qualified package.' }
        Copy-Item -LiteralPath $archivePath -Destination $output
        $archives.Add([ordered]@{ fileName = [IO.Path]::GetFileName($archivePath); id = $id; symbol = $extension -ceq '.snupkg'; bytes = (Get-Item -LiteralPath $archivePath).Length; sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
}
# Preserve the established reviewed core-component/license evidence separately.
& (Join-Path $repository 'eng/scripts/New-SharpProofReleaseEvidence.ps1') -PackageSource $CorePackageSource -OutputDirectory (Join-Path $output 'core-evidence') -RepositoryCommit $source
$spdxPackages = [Collections.Generic.List[object]]::new()
$spdxFiles = [Collections.Generic.List[object]]::new()
$relationships = [Collections.Generic.List[object]]::new()
foreach ($package in $identities) {
    $packageId = 'SPDXRef-' + $package.id.Replace('.', '-')
    $spdxPackage = [ordered]@{ SPDXID = $packageId; name = $package.id; versionInfo = $package.version; downloadLocation = 'NOASSERTION'; filesAnalyzed = $true; licenseConcluded = 'NOASSERTION'; licenseDeclared = 'MIT'; licenseInfoFromFiles = @('NOASSERTION'); copyrightText = 'NOASSERTION'; checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $package.sha256 }) }
    $verificationHashes = [Collections.Generic.List[string]]::new()
    $relationships.Add([ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = $packageId })
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $output $package.fileName))
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @($zip.Entries | Sort-Object FullName -CaseSensitive)) {
            if ($entry.FullName.EndsWith('/')) { continue }
            if ($entry.FullName.StartsWith('/') -or $entry.FullName.Contains('\') -or '..' -cin $entry.FullName.Split('/') -or -not $seen.Add($entry.FullName)) { throw 'Unsafe or duplicate archive payload path.' }
            $stream = $entry.Open()
            try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            $verificationStream = $entry.Open()
            try { $verificationHashes.Add([Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($verificationStream)).ToLowerInvariant()) }
            finally { $verificationStream.Dispose() }
            $fileId = "SPDXRef-File-$($spdxFiles.Count)"
            $spdxFiles.Add([ordered]@{ SPDXID = $fileId; fileName = "./$($package.id)/$($entry.FullName)"; checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $digest }); licenseConcluded = 'NOASSERTION'; licenseInfoInFiles = @('NOASSERTION'); copyrightText = 'NOASSERTION' })
            $relationships.Add([ordered]@{ spdxElementId = $packageId; relationshipType = 'CONTAINS'; relatedSpdxElement = $fileId })
        }
    }
    finally { $zip.Dispose() }
    # SPDX 2.3 defines this compatibility field with SHA1; release authority
    # remains the signed SHA256 archive and payload inventory.
    $verificationInput = [Text.Encoding]::UTF8.GetBytes((@($verificationHashes | Sort-Object) -join ''))
    $spdxPackage.packageVerificationCode = [ordered]@{ packageVerificationCodeValue = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($verificationInput)).ToLowerInvariant() }
    $spdxPackages.Add($spdxPackage)
}
$coreSbomPath = Join-Path $output 'core-evidence/sbom.spdx.json'
$coreSbom = Get-Content -LiteralPath $coreSbomPath -Raw | ConvertFrom-Json
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'; dataLicense = 'CC0-1.0'; SPDXID = 'SPDXRef-DOCUMENT'; name = 'SharpProof private preview six-package archive payload inventory'
    documentNamespace    = "https://github.com/alexyorke/SharpProof-preview/sbom/$source/$workflow"
    creationInfo         = [ordered]@{ created = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators = @('Tool: SharpProof private preview exporter') }
    comment              = 'All six exact archive payloads are inventoried by byte hash. Reviewed core third-party component/license evidence is retained in core-evidence/sbom.spdx.json. Optional bundled component licenses are not inferred from their containing package license.'
    packages = @($spdxPackages.ToArray()); files = @($spdxFiles.ToArray()); relationships = @($relationships.ToArray())
    externalDocumentRefs = @([ordered]@{ externalDocumentId = 'DocumentRef-ReviewedCore'; spdxDocument = $coreSbom.documentNamespace; checksum = [ordered]@{ algorithm = 'SHA256'; checksumValue = (Get-FileHash -LiteralPath $coreSbomPath -Algorithm SHA256).Hash.ToLowerInvariant() } })
}
[IO.File]::WriteAllText((Join-Path $output 'packages.spdx.json'), ($sbom | ConvertTo-Json -Depth 15) + "`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $QualificationDirectory 'private-source-qualification.json') -Destination $output
Copy-Item -LiteralPath $packageReceiptPath -Destination $output
Copy-Item -LiteralPath (Join-Path $developerRun 'run.json') -Destination (Join-Path $output 'developer-run.json')
foreach ($composeFile in $composeFiles) {
    $compose = Get-Content -LiteralPath $composeFile.FullName -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $composeFile.FullName -Destination (Join-Path $output "compose-$($compose.consumerDirectory)-run.json")
}
Copy-Item -LiteralPath (Join-Path $repository 'consumer-image.json') -Destination $output
$imageReceipt = Get-Content -LiteralPath $ImageReceiptPath -Raw | ConvertFrom-Json
if ($imageReceipt.sourceCommit -cne $source -or $imageReceipt.registryDigest -cne $ConsumerImage.Split('@')[1] -or
    $imageReceipt.imageReference -cne "ghcr.io/alexyorke/sharpproof-preview:sha-$source" -or
    $imageReceipt.archiveSha256 -cne (Get-FileHash -LiteralPath $ImageArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -or
    $imageReceipt.bytes -ne (Get-Item -LiteralPath $ImageArchivePath).Length) { throw 'Frozen image archive differs from its verified OCI receipt.' }
Copy-Item -LiteralPath $ImageArchivePath -Destination (Join-Path $output 'consumer-image.tar')
Copy-Item -LiteralPath $ImageReceiptPath -Destination (Join-Path $output 'image-receipt.json')
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ fileName = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
$manifest = [ordered]@{
    schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; sourceRepository = 'alexyorke/SharpProof'; sourceCommit = $source; workflowCommit = $workflow
    workflowPath = '.github/workflows/private-preview.yml'; packageVersion = '1.0.0-preview.1'; consumerImage = $ConsumerImage
    signaturePolicy = 'RSA3072-SHA256-PKCS1-v1.5; pinned SPKI; offline; no transparency-log publication'
    packages = @($identities.ToArray()); archives = @($archives.ToArray()); files = $files
}
[IO.File]::WriteAllText((Join-Path $output 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 15) + "`n", [Text.UTF8Encoding]::new($false))
$checksums = @($files | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) + "$((Get-FileHash -LiteralPath (Join-Path $output 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Host 'Private release evidence binds six exact identities, retained symbols, complete archive payload hashes, core SBOM, successful gates, source/workflow commits, and exact image.'
