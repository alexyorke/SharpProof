[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$RepositoryCommit,
    [string]$InventoryPath = (Join-Path $PSScriptRoot '../eng/release/third-party-components.json'),
    [DateTimeOffset]$Timestamp = [DateTimeOffset]::UtcNow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
& (Join-Path $PSScriptRoot 'Test-SharpProofThirdPartyInventory.ps1') -PackageSource $PackageSource -InventoryPath $InventoryPath
$files = @(Get-ChildItem -LiteralPath $PackageSource -File | Where-Object Extension -in @('.nupkg', '.snupkg') | Sort-Object Name -CaseSensitive)
if ($files.Count -ne 5) { throw 'Release evidence requires exactly three main and two symbol packages.' }
$identitySet = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
if (@($identitySet.RepositoryCommits).Count -ne 1 -or $identitySet.RepositoryCommits[0] -cne $RepositoryCommit) {
    throw 'Release package repository commit differs from the qualified source commit.'
}
$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json -AsHashtable
$inventoryDigest = (Get-FileHash -LiteralPath $InventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$created = $Timestamp.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
$scope = 'Shipped package payload only: reviewed third-party components, archive hashes, and packaged binary hashes. Excludes SDK/build tools and dependencies not redistributed in these archives; no claim of a complete source/dependency SBOM.'
$artifacts = @($identitySet.Identities | ForEach-Object {
    if ($_.File.Name -cnotmatch '^[A-Za-z0-9_.+-]+\.(?:nupkg|snupkg)$') { throw 'Unsafe release package filename.' }
    [ordered]@{
        fileName = $_.File.Name; packageId = $_.Identity.Id; version = $_.Identity.Version
        kind = $_.File.Extension.TrimStart('.'); bytes = $_.File.Length
        sha256 = (Get-FileHash -LiteralPath $_.File.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$namespaceInput = $RepositoryCommit + $inventoryDigest + $created + (($artifacts | ForEach-Object sha256) -join '')
$namespaceDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($namespaceInput))).ToLowerInvariant()
$packages = [Collections.Generic.List[object]]::new()
$binaryFiles = [Collections.Generic.List[object]]::new()
$relationships = [Collections.Generic.List[object]]::new()
$extractedLicenses = [ordered]@{}
$archiveIds = @{}
for ($index = 0; $index -lt $artifacts.Count; $index++) {
    $artifact = $artifacts[$index]
    $id = "SPDXRef-Artifact-$index"
    $packages.Add([ordered]@{
        SPDXID = $id; name = $artifact.fileName; versionInfo = $artifact.version
        downloadLocation = 'NOASSERTION'; filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'; licenseDeclared = 'NOASSERTION'; copyrightText = 'NOASSERTION'
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $artifact.sha256 })
    })
    $relationships.Add([ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = $id })
    if ($artifact.kind -ceq 'nupkg') { $archiveIds[$artifact.packageId] = $id }
}
$componentIndex = 0
foreach ($packageId in @($inventory.packages.Keys | Sort-Object -CaseSensitive)) {
    $artifact = @($artifacts | Where-Object { $_.packageId -ceq $packageId -and $_.kind -ceq 'nupkg' })[0]
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $PackageSource $artifact.fileName))
    try {
        $entryIds = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        foreach ($component in @($inventory.packages[$packageId])) {
            $componentId = "SPDXRef-Component-$componentIndex"
            $componentIndex++
            $license = [string]$component.license
            $packages.Add([ordered]@{
                SPDXID = $componentId; name = [string]$component.id; versionInfo = [string]$component.version
                downloadLocation = 'NOASSERTION'; filesAnalyzed = $false
                licenseConcluded = 'NOASSERTION'; licenseDeclared = $license; copyrightText = 'NOASSERTION'
                sourceInfo = if ($component.Contains('source')) { [string]$component.source } else { 'Source and version as recorded in the reviewed third-party inventory.' }
            })
            if ($license.StartsWith('LicenseRef-', [StringComparison]::Ordinal)) {
                $extractedLicenses[$license] = [ordered]@{
                    licenseId = $license; name = 'Complete distributed third-party notice bundle'
                    extractedText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.txt'))
                }
            }
            $relationships.Add([ordered]@{ spdxElementId = $archiveIds[$packageId]; relationshipType = 'CONTAINS'; relatedSpdxElement = $componentId })
            foreach ($entryPath in @($component.entries | Sort-Object -CaseSensitive)) {
                if (-not $entryIds.ContainsKey($entryPath)) {
                    $stream = $archive.GetEntry($entryPath).Open()
                    try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                    finally { $stream.Dispose() }
                    $entryId = "SPDXRef-File-$($binaryFiles.Count)"
                    $entryIds[$entryPath] = $entryId
                    $binaryFiles.Add([ordered]@{
                        SPDXID = $entryId; fileName = "./$packageId/$entryPath"
                        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $digest })
                        licenseConcluded = 'NOASSERTION'; copyrightText = 'NOASSERTION'
                    })
                    $relationships.Add([ordered]@{ spdxElementId = $archiveIds[$packageId]; relationshipType = 'CONTAINS'; relatedSpdxElement = $entryId })
                }
                $relationships.Add([ordered]@{ spdxElementId = $componentId; relationshipType = 'CONTAINS'; relatedSpdxElement = $entryIds[$entryPath] })
                if ($component.Contains('linkage') -and $component.linkage -ceq 'static') {
                    $relationships.Add([ordered]@{ spdxElementId = $entryIds[$entryPath]; relationshipType = 'STATIC_LINK'; relatedSpdxElement = $componentId })
                }
            }
        }
    }
    finally { $archive.Dispose() }
}
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'; dataLicense = 'CC0-1.0'; SPDXID = 'SPDXRef-DOCUMENT'
    name = "SharpProof $($identitySet.Version) shipped payload"
    documentNamespace = "https://github.com/alexyorke/SharpProof/sbom/$RepositoryCommit/$namespaceDigest"
    creationInfo = [ordered]@{ created = $created; creators = @('Tool: SharpProof release evidence exporter') }
    comment = $scope; packages = @($packages.ToArray()); files = @($binaryFiles.ToArray())
    relationships = @($relationships.ToArray())
}
if ($extractedLicenses.Count -gt 0) { $sbom.hasExtractedLicensingInfos = @($extractedLicenses.Values) }
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$sbomPath = Join-Path $OutputDirectory 'sbom.spdx.json'
$sbom | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $sbomPath -Encoding utf8NoBOM
$manifest = [ordered]@{
    schemaVersion = 1; repository = 'alexyorke/SharpProof'; repositoryCommit = $RepositoryCommit
    packageVersion = $identitySet.Version; createdUtc = $created; scope = $scope
    inventorySha256 = $inventoryDigest; artifacts = $artifacts
    sbom = [ordered]@{ fileName = 'sbom.spdx.json'; sha256 = (Get-FileHash -LiteralPath $sbomPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifestPath = Join-Path $OutputDirectory 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
$checksums = @($artifacts | ForEach-Object { "$($_.sha256)  $($_.fileName)" })
$checksums += "$($manifest.sbom.sha256)  sbom.spdx.json"
$checksums += "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
$checksums | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS') -Encoding utf8NoBOM
Write-Host "Release evidence generated: five archives, $componentIndex reviewed components, $($binaryFiles.Count) distinct packaged third-party binaries."
